using System.Text.Json;
using Npgsql;
using NexaConnect.Services.PlatformDirectory.Application.ControlPlane;
using NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence;
using NexaConnect.Services.Restaurant.Application.Provisioning;
using NexaConnect.Services.Restaurant.Infrastructure.Persistence;
using NexaConnect.Services.Authorization.Application.Assignments;
using NexaConnect.Services.Authorization.Infrastructure.Persistence;
using NexaConnect.Services.Catalog.Infrastructure;
using NexaConnect.Services.Inventory.Infrastructure;
using NexaConnect.Services.POS.Infrastructure.Persistence;

// Invoked only by the locked local lifecycle script. Each owner supplies its own repository and least-privilege login.
string stage="guards";
try
{
    string run = Environment.GetEnvironmentVariable("NEXACONNECT_DRAFT_RUN") ?? "";
    string path = Environment.GetEnvironmentVariable("NEXACONNECT_DRAFT_REFERENCE") ?? "";
    if (args.Length != 1 || args[0] is not ("--confirm" or "--media-access") || !System.Text.RegularExpressions.Regex.IsMatch(run,"^[a-f0-9]{32}$") ||
        Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") != "Development" ||
        !Path.GetFullPath(path).EndsWith(Path.Combine(".runstate", "first-draft", run, "reference.json"), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException();
    var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    var state = File.Exists(path) ? JsonSerializer.Deserialize<Reference>(File.ReadAllText(path),json)! : new Reference { RunId = run };
    if (state.RunId != run) throw new InvalidOperationException();
    void Save() { string temporary=path+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(state,json));File.Move(temporary,path,true); }
    NpgsqlDataSource Source(string suffix)
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_DRAFT_DB_"+suffix) ?? "");
        string user = suffix == "platform" ? "platform_directory_app" : "nexaconnect_"+suffix+"_app";
        if (builder.Host != "127.0.0.1" || builder.Database != $"nexa_review_it_{run}_{suffix}" || builder.Username != user)
            throw new InvalidOperationException();
        return NpgsqlDataSource.Create(builder.ConnectionString);
    }
    if (args[0] == "--media-access")
    {
        if (!state.Completed || !state.Subjects.TryGetValue("store-manager", out var manager)) throw new InvalidOperationException();
        await using var authorization = Source("authorization");
        await new DraftMediaAccessProvisioner(authorization).EnsureAsync(state.OrganizationId, manager, CancellationToken.None);
        Console.WriteLine("Local manager Media access prepared; existing denials/revocations preserved."); return 0;
    }
    // Completed runs are deliberately a no-op: never replenish sold stock, reactivate access, or overwrite edited reference data.
    if (state.Completed) { Console.WriteLine("Draft reference already provisioned; no data changed."); return 0; }
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)); var ct = timeout.Token;
    await using var platformDb=Source("platform"); await using var restaurantDb=Source("restaurant");
    await using var authDb=Source("authorization"); await using var catalogDb=Source("catalog");
    await using var inventoryDb=Source("inventory"); await using var posDb=Source("pos");
    stage="hierarchy";
    var platform = new PlatformDirectoryManagementService(new PostgresPlatformDirectoryManagementRepository(platformDb));
    var restaurant = new RestaurantProvisioningService(new PostgresRestaurantProvisioningRepository(restaurantDb));
    string actor="draft-provisioner:"+run;
    if(state.OrganizationId==Guid.Empty)
    {
        var existing=(await platform.ListOrganizationsAsync(ct)).SingleOrDefault(o=>o.Code=="draft");
        state.OrganizationId=existing?.OrganizationId??(await platform.CreateOrganizationAsync(new("draft","Singapore draft","Asia/Singapore"),actor,ct)).OrganizationId; Save();
    }
    try { await platform.RegisterProductAsync(new("nexa_connect","NexaConnect"),actor,ct); }
    catch (PlatformDirectoryConflictException) { /* The fixed code may already exist after an interrupted reference setup. */ }
    if(!await platform.ChangeProductAccessAsync(state.OrganizationId,new("nexa_connect","enabled"),actor,ct))throw new InvalidOperationException();
    if(state.RestaurantId==Guid.Empty) { state.RestaurantId=(await restaurant.CreateRestaurantAsync(new(state.OrganizationId,"draft","Draft restaurant","THB","Asia/Singapore"),actor,ct)).RestaurantId;Save(); }
    if(state.BranchId==Guid.Empty) {state.BranchId=(await restaurant.CreateBranchAsync(state.RestaurantId,new("singapore","Singapore test branch","THB","Asia/Singapore"),actor,ct))!.BranchId;Save();}
    if(state.DeniedRestaurantId==Guid.Empty) {state.DeniedRestaurantId=(await restaurant.CreateRestaurantAsync(new(state.OrganizationId,"denied","Access boundary restaurant","THB","Asia/Singapore"),actor,ct)).RestaurantId;Save();}
    if(state.DeniedBranchId==Guid.Empty) {state.DeniedBranchId=(await restaurant.CreateBranchAsync(state.DeniedRestaurantId,new("denied","Access boundary fixture","THB","Asia/Singapore"),actor,ct))!.BranchId;Save();}
    if(state.OtherOrganizationId==Guid.Empty) {var existing=(await platform.ListOrganizationsAsync(ct)).SingleOrDefault(o=>o.Code=="denied");state.OtherOrganizationId=existing?.OrganizationId??(await platform.CreateOrganizationAsync(new("denied","Tenant boundary fixture","Asia/Singapore"),actor,ct)).OrganizationId;Save();}
    stage="assignments";
    var assignments=new AuthorizationAssignmentService(new PostgresAuthorizationAssignmentRepository(authDb));
    foreach(var role in new[]{"cashier","kitchen-operator","store-manager","accountant"})
    {
        string subject=Environment.GetEnvironmentVariable("NEXACONNECT_DRAFT_SUBJECT_"+role)??throw new InvalidOperationException();
        if(!Guid.TryParse(subject,out _))throw new InvalidOperationException();
        if(state.Subjects.TryGetValue(role,out string? savedSubject)&&savedSubject!=subject)throw new InvalidOperationException();
        state.Subjects[role]=subject;
        await platform.ChangeMembershipAsync(state.OrganizationId,subject,new(subject,"active"),actor,ct);
        await assignments.AssignAsync(new(subject,state.OrganizationId,state.RestaurantId,role=="store-manager"?null:state.BranchId,role),actor,ct);
    }
    stage="limits";
    await new DraftApprovalLimitProvisioner(authDb).EnsureAsync(state.RestaurantId,state.Subjects["store-manager"],ct);
    if(state.StoreId==Guid.Empty){state.StoreId=Guid.NewGuid();state.TerminalId=Guid.NewGuid();state.Products=[Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()];Save();}
    stage="store";
    await new DraftStoreProvisioner(posDb).EnsureAsync(state.RestaurantId,state.BranchId,state.StoreId,state.TerminalId,ct);
    var menu=new PostgresMenuCatalog(catalogDb);var stock=new PostgresInventoryReservations(inventoryDb);
    var names=new[]{"Draft rice bowl","Draft noodles","Draft iced tea"};var prices=new[]{80m,95m,30m};var stations=new[]{"kitchen","kitchen","bar"};
    stage="menu-stock";
    for(int i=0;i<state.Products.Length;i++)
    {
        if(!menu.GetForOrganizationBranch(state.OrganizationId,state.BranchId).Any(p=>p.ProductId==state.Products[i]))
            menu.AddForOrganizationBranch(state.OrganizationId,state.BranchId,new(state.Products[i],names[i],prices[i],"THB",stations[i]),new(actor,Guid.NewGuid()));
        if(!stock.GetStock(state.OrganizationId,state.BranchId).Any(p=>p.ProductId==state.Products[i]))
            stock.SetStock(state.OrganizationId,state.BranchId,state.Products[i],100,new(actor,Guid.NewGuid()));
    }
    state.Completed=true;Save();Console.WriteLine("Draft reference provisioned through service-owned repositories; no financial transactions seeded.");return 0;
}
catch(Exception error) { Console.Error.WriteLine(JsonSerializer.Serialize(new{service="nexaconnect-draft-provisioner",stage,outcome="failed",category=error.GetType().Name,sqlState=error is PostgresException pg?pg.SqlState:null}));return 1; }

sealed class Reference
{
 public string RunId {get;set;}="";public bool Completed{get;set;}
 public Guid OrganizationId{get;set;}public Guid OtherOrganizationId{get;set;}public Guid RestaurantId{get;set;}
 public Guid BranchId{get;set;}public Guid DeniedRestaurantId{get;set;}public Guid DeniedBranchId{get;set;}public Guid StoreId{get;set;}public Guid TerminalId{get;set;}
 public Guid[] Products{get;set;}=[];public Dictionary<string,string> Subjects{get;set;}=[];
}
