using System.Text.Json;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Contracts.Platform;
using NexaConnect.Services.Authorization.Application.Assignments;
using NexaConnect.Services.Authorization.Infrastructure.Persistence;
using NexaConnect.Services.Order.Domain;
using NexaConnect.Services.Order.Infrastructure.Persistence;
using NexaConnect.Services.PlatformDirectory.Application.ControlPlane;
using NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence;
using NexaConnect.Services.Restaurant.Application.Provisioning;
using NexaConnect.Services.Restaurant.Infrastructure.Persistence;
using Npgsql;

return await AcceptanceFixtureApplication.RunAsync(Environment.GetEnvironmentVariables()
    .Cast<System.Collections.DictionaryEntry>()
    .ToDictionary(entry=>(string)entry.Key,entry=>entry.Value?.ToString(),StringComparer.Ordinal));

public static class AcceptanceFixtureApplication
{
    public static async Task<int> RunAsync(IReadOnlyDictionary<string,string?> environment)
    {
        try
        {
            AcceptanceFixtureOptions options=AcceptanceFixtureOptions.Parse(environment);
            AcceptanceFixtureResult result=await new AcceptanceFixtureProvisioner().ProvisionAsync(options,default);
            Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
            return 0;
        }
        catch(Exception)
        {
            Console.Error.WriteLine("Payment Review fixture provisioning failed; credentials and connection details were suppressed.");
            return 1;
        }
    }
}

public sealed record AcceptanceFixtureOptions(string RunId,string ReaderSubjectId,string ResolverSubjectId,
    string PlatformDirectoryConnection,string RestaurantConnection,string AuthorizationConnection,string OrderConnection)
{
    private const string Prefix="NEXACONNECT_REVIEW_FIXTURE_";
    public static AcceptanceFixtureOptions Parse(IReadOnlyDictionary<string,string?> environment)
    {
        string Required(string name)=>environment.TryGetValue(Prefix+name,out string? value)&&!string.IsNullOrWhiteSpace(value)
            ?value:throw new ArgumentException($"Missing acceptance fixture setting: {Prefix+name}. Inject it without printing its value.");
        if(Required("ENABLED")!="1")throw new ArgumentException("Acceptance fixture provisioning requires explicit enablement.");
        string runId=Required("RUN_ID");
        if(!System.Text.RegularExpressions.Regex.IsMatch(runId,"^[a-f0-9]{32}$"))throw new ArgumentException("Acceptance fixture run ID is invalid.");
        string reader=Required("READER_SUBJECT_ID").Trim(),resolver=Required("RESOLVER_SUBJECT_ID").Trim();
        if(reader.Length>200||resolver.Length>200||reader==resolver)throw new ArgumentException("Acceptance fixture subjects must be distinct and bounded.");
        string Connection(string name,string suffix)
        {
            string value=Required(name);var builder=new NpgsqlConnectionStringBuilder(value);
            if(!string.Equals(builder.Database,$"nexa_review_it_{runId}_{suffix}",StringComparison.Ordinal))
                throw new ArgumentException($"{Prefix+name} does not target the run-scoped database.");
            if(builder.Host is not ("127.0.0.1" or "localhost"))throw new ArgumentException("Acceptance fixture databases must use a loopback host.");
            return value;
        }
        return new(runId,reader,resolver,Connection("PLATFORM_DIRECTORY_DB","platform"),Connection("RESTAURANT_DB","restaurant"),Connection("AUTHORIZATION_DB","authorization"),Connection("ORDER_DB","order"));
    }
}

public sealed record AcceptanceFixtureResult(string RunId,Guid OrganizationId,Guid OtherOrganizationId,Guid RestaurantId,
    Guid BranchId,Guid ConcurrencyOrderId,Guid ResumeOrderId,Guid VoidOrderId,Guid OutageOrderId,Guid LostResponseOrderId,
    Guid InventoryProcessOrderId,Guid KitchenProcessOrderId,Guid CombinedProcessOrderId);

public sealed class AcceptanceFixtureProvisioner
{
    public async Task<AcceptanceFixtureResult> ProvisionAsync(AcceptanceFixtureOptions options,CancellationToken cancellationToken)
    {
        await using var platformSource=NpgsqlDataSource.Create(options.PlatformDirectoryConnection);
        await using var restaurantSource=NpgsqlDataSource.Create(options.RestaurantConnection);
        await using var authorizationSource=NpgsqlDataSource.Create(options.AuthorizationConnection);
        await using var orderSource=NpgsqlDataSource.Create(options.OrderConnection);
        var platformRepository=new PostgresPlatformDirectoryManagementRepository(platformSource);
        var restaurantRepository=new PostgresRestaurantProvisioningRepository(restaurantSource);
        var authorizationRepository=new PostgresAuthorizationAssignmentRepository(authorizationSource);
        var orders=new PostgresOrderRepository(orderSource);
        bool[] empty=await Task.WhenAll(platformRepository.IsEmptyAsync(cancellationToken),restaurantRepository.IsEmptyAsync(cancellationToken),
            authorizationRepository.IsEmptyAsync(cancellationToken),orders.IsEmptyAsync(cancellationToken));
        if(empty.Any(value=>!value))throw new InvalidOperationException("One or more run-scoped service databases are not empty.");
        var platform=new PlatformDirectoryManagementService(platformRepository);
        string actor=$"fixture-provisioner:{options.RunId}";
        OrganizationSummary organization=await platform.CreateOrganizationAsync(new($"review-{options.RunId[..8]}","Payment Review Acceptance","Etc/UTC"),actor,cancellationToken);
        OrganizationSummary other=await platform.CreateOrganizationAsync(new($"review-other-{options.RunId[..8]}","Payment Review Other Tenant","Etc/UTC"),actor,cancellationToken);
        await platform.RegisterProductAsync(new("nexa_connect","NexaConnect"),actor,cancellationToken);
        foreach(Guid organizationId in new[]{organization.OrganizationId,other.OrganizationId})
        {
            if(!await platform.ChangeProductAccessAsync(organizationId,new("nexa_connect","enabled"),actor,cancellationToken))throw new InvalidOperationException("Product access was not provisioned.");
            if(!await platform.ChangeMembershipAsync(organizationId,options.ResolverSubjectId,new(options.ResolverSubjectId,"active"),actor,cancellationToken))throw new InvalidOperationException("Resolver membership was not provisioned.");
        }
        if(!await platform.ChangeMembershipAsync(organization.OrganizationId,options.ReaderSubjectId,new(options.ReaderSubjectId,"active"),actor,cancellationToken))throw new InvalidOperationException("Reader membership was not provisioned.");

        var restaurant=new RestaurantProvisioningService(restaurantRepository);
        RestaurantProvisioningResult restaurantResult=await restaurant.CreateRestaurantAsync(new(organization.OrganizationId,"review","Payment Review Restaurant","USD","Etc/UTC"),actor,cancellationToken);
        BranchProvisioningResult branch=await restaurant.CreateBranchAsync(restaurantResult.RestaurantId,new("review","Payment Review Branch","USD","Etc/UTC"),actor,cancellationToken)
            ?? throw new InvalidOperationException("Acceptance branch was not provisioned.");
        var assignments=new AuthorizationAssignmentService(authorizationRepository);
        await assignments.AssignAsync(new(options.ReaderSubjectId,organization.OrganizationId,restaurantResult.RestaurantId,branch.BranchId,"accountant"),actor,cancellationToken);
        await assignments.AssignAsync(new(options.ResolverSubjectId,organization.OrganizationId,restaurantResult.RestaurantId,null,"store-manager"),actor,cancellationToken);

        Guid[] orderIds=Enumerable.Range(0,8).Select(_=>Guid.NewGuid()).ToArray();
        foreach(Guid orderId in orderIds)
        {
            Guid paymentIntentId=Guid.NewGuid();
            var order=OrderAggregate.Create(orderId,organization.OrganizationId,branch.BranchId,[new(Guid.NewGuid(),"Synthetic acceptance item",1m,1,"acceptance")],"USD",restaurantResult.RestaurantId,"pos","takeaway",$"IT-{orderId:N}"[..15]);
            order.Submit();order.MarkInventoryReserved();order.MarkKitchenAccepted();order.MarkPaymentPending(paymentIntentId);order.MarkPaymentReview();
            var required=new OrderPaymentReviewRequiredV1(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,organization.OrganizationId,orderId,paymentIntentId,$"browser-acceptance:{options.RunId}");
            await orders.SaveWithEventAsync(order,required,cancellationToken);
        }
        return new(options.RunId,organization.OrganizationId,other.OrganizationId,restaurantResult.RestaurantId,branch.BranchId,
            orderIds[0],orderIds[1],orderIds[2],orderIds[3],orderIds[4],orderIds[5],orderIds[6],orderIds[7]);
    }
}
