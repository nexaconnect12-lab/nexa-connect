extern alias ORDER;
extern alias PAYMENT;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexaConnect.Services.PlatformDirectory.Application.ControlPlane;
using NexaConnect.Services.PlatformDirectory.Infrastructure.Persistence;
using NexaConnect.Services.Restaurant.Application.Provisioning;
using NexaConnect.Services.Restaurant.Infrastructure.Persistence;
using NexaConnect.Services.Authorization.Application.Assignments;
using NexaConnect.Services.Authorization.Infrastructure.Persistence;
using NexaConnect.Infrastructure.Messaging;
using NexaConnect.FinancialPortalAcceptance.Infrastructure;
using Npgsql;
using RabbitMQ.Client;
using ORDER::NexaConnect.Services.Order.Domain;
using ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using PAYMENT::NexaConnect.Services.Payment.Application.Intents;
using PAYMENT::NexaConnect.Services.Payment.Application.Refunds;
using PAYMENT::NexaConnect.Services.Payment.Infrastructure;
using PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers;

string stage="options";
try
{
    if (args.Length > 0 && args[0] is "host-pos" or "host-order")
    {
        stage="command-host"; await CashierCommandHost.RunAsync(args[0]); return 0;
    }
    var options=FixtureOptions.Read();
    if (args.Length == 1 && args[0] == "cashier-proof")
    {
        if(!options.DayClose || Environment.GetEnvironmentVariable("NEXACONNECT_FINANCIAL_PORTAL_CASHIER_DAY_CLOSE")!="1") throw new ArgumentException();
        using var proofTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await CashierReferenceFixture.ProofAsync(options,proofTimeout.Token); return 0;
    }
    if(args.Length==1 && args[0]=="provision-cashier" && (!options.DayClose || Environment.GetEnvironmentVariable("NEXACONNECT_FINANCIAL_PORTAL_CASHIER_DAY_CLOSE")!="1")) throw new ArgumentException();
    if(args.Length!=1 || args[0] is not ("provision" or "provision-cashier" or "record" or "deliver" or "revoke" or "revoke-source" or "membership" or "resolve-day-close" or "same-total-evidence" or "late-cash" or "approve-cash" or "preparation-proof" or "stop-pos" or "start-pos" or "revoke-day-close-read" or "revoke-day-close-prepare" or "revoke-manager-source" or "restore-manager-source" or "membership-second")) return 2;
    if(args[0] is not("provision" or "provision-cashier" or "record" or "deliver" or "revoke" or "revoke-source" or "membership") && !options.DayClose)return 2;
    using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));var ct=timeout.Token;
    await using var platformDb=NpgsqlDataSource.Create(options.Connection("platform"));
    await using var restaurantDb=NpgsqlDataSource.Create(options.Connection("restaurant"));
    await using var authorizationDb=NpgsqlDataSource.Create(options.Connection("authorization"));
    await using var orderDb=NpgsqlDataSource.Create(options.Connection("order"));
    await using var paymentDb=NpgsqlDataSource.Create(options.Connection("payment"));
    await using var reportDb=NpgsqlDataSource.Create(options.Connection("reporting"));
    var store=new FixtureStore(authorizationDb,reportDb);
    var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    var platformRepository=new PostgresPlatformDirectoryManagementRepository(platformDb);
    var platform=new PlatformDirectoryManagementService(platformRepository);
    string actor="financial-fixture:"+options.RunId;
    if(args[0] is "provision" or "provision-cashier")
    {
        stage="empty-check";
        var restaurantRepository=new PostgresRestaurantProvisioningRepository(restaurantDb);
        var assignmentsRepository=new PostgresAuthorizationAssignmentRepository(authorizationDb);
        if(File.Exists(options.StatePath)||!await platformRepository.IsEmptyAsync(ct)||!await restaurantRepository.IsEmptyAsync(ct)||
            !await assignmentsRepository.IsEmptyAsync(ct)||!await store.SourcesEmptyAsync(orderDb,paymentDb,ct))throw new InvalidOperationException();
        stage="scope";
        var org=await platform.CreateOrganizationAsync(new("financial-"+options.RunId[..8],"Financial Acceptance","Etc/UTC"),actor,ct);
        var other=await platform.CreateOrganizationAsync(new("other-"+options.RunId[..8],"Other Tenant","Etc/UTC"),actor,ct);
        await platform.RegisterProductAsync(new("nexa_connect","NexaConnect"),actor,ct);
        foreach(Guid id in new[]{org.OrganizationId,other.OrganizationId})
        {
            if(!await platform.ChangeProductAccessAsync(id,new("nexa_connect","enabled"),actor,ct))throw new InvalidOperationException();
            foreach(string subject in options.Subjects)
                if(!await platform.ChangeMembershipAsync(id,subject,new(subject,"active"),actor,ct))throw new InvalidOperationException();
        }
        var restaurant=new RestaurantProvisioningService(restaurantRepository);
        var r=await restaurant.CreateRestaurantAsync(new(org.OrganizationId,"financial","Financial Restaurant","THB",options.EndOfDay ? "Asia/Bangkok" : "Etc/UTC"),actor,ct);
        var branch=await restaurant.CreateBranchAsync(r.RestaurantId,new("allowed","Allowed Branch","THB",options.EndOfDay ? "Asia/Bangkok" : "Etc/UTC"),actor,ct)??throw new InvalidOperationException();
        var denied=await restaurant.CreateBranchAsync(r.RestaurantId,new("denied","Denied Branch","THB","Etc/UTC"),actor,ct)??throw new InvalidOperationException();
        var assignments=new AuthorizationAssignmentService(assignmentsRepository);
        await assignments.AssignAsync(new(options.Reader,org.OrganizationId,r.RestaurantId,branch.BranchId,args[0]=="provision-cashier"?"cashier":"accountant"),actor,ct);
        await assignments.AssignAsync(new(options.Resolver,org.OrganizationId,r.RestaurantId,null,"store-manager"),actor,ct);
        if(options.DayClose)await assignments.AssignAsync(new(options.SecondManager!,org.OrganizationId,r.RestaurantId,null,"store-manager"),actor,ct);
        if(args[0]=="provision-cashier")
        {
            if(!options.DayClose || Environment.GetEnvironmentVariable("NEXACONNECT_FINANCIAL_PORTAL_CASHIER_DAY_CLOSE")!="1") throw new ArgumentException();
            var reference=await CashierReferenceFixture.CreateAsync(options,org.OrganizationId,r.RestaurantId,branch.BranchId,ct);
            var cashierZone=TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
            var date=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,cashierZone).DateTime).AddDays(-1);
            var start=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue),cashierZone));
            var cashierState=new FixtureState(options.RunId,org.OrganizationId,other.OrganizationId,r.RestaurantId,branch.BranchId,denied.BranchId,start,start.AddDays(1),date.ToString("yyyy-MM-dd"),Cashier:reference);
            await File.WriteAllTextAsync(options.StatePath,JsonSerializer.Serialize(cashierState,json),ct);
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(options.StatePath)!,"clock.json"),JsonSerializer.Serialize(new{runId=options.RunId,mode="historical",atUtc=start.AddHours(10)},json),ct);
            return 0;
        }
        stage="retained-sources";
        bool day=options.EndOfDay;
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
        var businessDate=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,zone).DateTime).AddDays(-1);
        var from=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(businessDate.ToDateTime(TimeOnly.MinValue),zone));
        var until=from.AddDays(1);
        TimeProvider clock=day ? new FixtureClock(from.AddHours(10)) : TimeProvider.System;
        var provider=Options.Create(new PaymentProviderOptions());
        var context=new PaymentMutationContext(actor,Guid.NewGuid());
        var intents=new PostgresPaymentIntents(paymentDb,provider,clock);
        var intent=intents.Create(org.OrganizationId,new(r.RestaurantId,branch.BranchId,Guid.NewGuid(),Guid.NewGuid().ToString("D"),100,"THB","card"),context);
        var authorization=intents.BeginAuthorization(org.OrganizationId,intent.Id,context);
        intents.CompleteAuthorization(org.OrganizationId,intent.Id,authorization.Intent.ConcurrencyVersion,ProviderAuthorizationOutcome.Authorized,"synthetic-charge",null,context);
        var capture=intents.BeginCapture(org.OrganizationId,intent.Id,context);
        intent=intents.CompleteCapture(org.OrganizationId,intent.Id,capture.Intent.ConcurrencyVersion,ProviderCaptureOutcome.Captured,"synthetic-capture",null,context);
        var value=OrderAggregate.Create(intent.OrderId,org.OrganizationId,branch.BranchId,[new OrderLine(Guid.NewGuid(),"Synthetic sale",100,1,"kitchen")],"THB",restaurantId:r.RestaurantId,workflowPaymentMethod:"card");
        value.Submit();value.MarkInventoryReserved();value.MarkKitchenAccepted();
        var orders=new PostgresOrderRepository(orderDb);await orders.SaveAsync(value,ct);
        if(day)await store.SetUnpaidOrderTimeAsync(orderDb,value.Id,from.AddHours(9),ct);
        value.MarkPaid(intent.Id);value.IssueReceipt(clock.GetUtcNow(),"card");await orders.SaveAsync(value,ct);
        var refunds=new PostgresPaymentRefunds(paymentDb,provider,clock);
        var lease=refunds.Begin(org.OrganizationId,intent.Id,new(Guid.NewGuid(),25,"THB","customer_request",Guid.NewGuid()),context);
        refunds.Complete(org.OrganizationId,lease.Refund.Id,lease.Refund.ConcurrencyVersion,ProviderRefundOutcome.Refunded,"synthetic-refund",null,context);
        // UI has minute precision: close the window only after the retained sources exist.
        if(day)
        {
            if(!options.DayClose){
            intents.Create(org.OrganizationId,new(r.RestaurantId,branch.BranchId,Guid.NewGuid(),Guid.NewGuid().ToString("D"),20,"THB","card"),context);
            var uncertain=refunds.Begin(org.OrganizationId,intent.Id,new(Guid.NewGuid(),10,"THB","customer_request",Guid.NewGuid()),context);
            refunds.Complete(org.OrganizationId,uncertain.Refund.Id,uncertain.Refund.ConcurrencyVersion,ProviderRefundOutcome.Unknown,null,null,context);
            var pending=OrderAggregate.Create(Guid.NewGuid(),org.OrganizationId,branch.BranchId,[new OrderLine(Guid.NewGuid(),"Unresolved fixture",20,1,"kitchen")],"THB",restaurantId:r.RestaurantId);
            pending.Submit();pending.MarkInventoryReserved();pending.MarkKitchenAccepted();await orders.SaveAsync(pending,ct);
            await store.SetUnpaidOrderTimeAsync(orderDb,pending.Id,from.AddDays(-1),ct);

            }
            await using var posDb=NpgsqlDataSource.Create(options.Connection("pos"));
            var posIds=await store.CreateHistoricalCashAsync(posDb,r.RestaurantId,branch.BranchId,options.Resolver,from,ct);
            var dayState=new FixtureState(options.RunId,org.OrganizationId,other.OrganizationId,r.RestaurantId,branch.BranchId,denied.BranchId,from,until,businessDate.ToString("yyyy-MM-dd"),options.DayClose?posIds:null);
            await File.WriteAllTextAsync(options.StatePath,JsonSerializer.Serialize(dayState,json),ct);
            return 0;
        }
        var to=DateTimeOffset.UtcNow;
        to=new DateTimeOffset(to.Year,to.Month,to.Day,to.Hour,to.Minute,0,TimeSpan.Zero);
        while(DateTimeOffset.UtcNow<to.AddMinutes(1))await Task.Delay(250,ct);
        var state=new FixtureState(options.RunId,org.OrganizationId,other.OrganizationId,r.RestaurantId,branch.BranchId,denied.BranchId,to.AddDays(-1),to.AddMinutes(1));
        await File.WriteAllTextAsync(options.StatePath,JsonSerializer.Serialize(state,json),ct);
    }
    else
    {
        var state=JsonSerializer.Deserialize<FixtureState>(await File.ReadAllTextAsync(options.StatePath,ct),json)??throw new InvalidOperationException();
        if(state.RunId!=options.RunId||state.OrganizationId==Guid.Empty||state.BranchId==Guid.Empty)throw new InvalidOperationException();
        stage=args[0];
        if(options.DayClose && await DayCloseFixture.ExecuteAsync(options,state,args[0],authorizationDb,platform,ct))return 0;
        if(args[0]=="revoke")await store.RevokeReadAsync(options.Reader,ct);
        else if(args[0]=="revoke-source")
        {
            if(!options.EndOfDay)throw new InvalidOperationException();
            await store.RevokeReadAsync(options.Reader,ct,"payment.refund.read");
        }
        else if(args[0]=="membership")
        {
            if(!await platform.ChangeMembershipAsync(state.OrganizationId,options.Resolver,new(options.Resolver,"suspended"),actor,ct))throw new InvalidOperationException();
        }
        else if(args[0]=="deliver")
        {
            var broker=options.Broker();
            await using var rabbit=await new ConnectionFactory{Uri=broker}.CreateConnectionAsync(ct);
            await using var channel=await rabbit.CreateChannelAsync(cancellationToken:ct);
            await channel.ExchangeDeclareAsync("nexaconnect.events",ExchangeType.Topic,true,cancellationToken:ct);
            string sink="financial-fixture-"+options.RunId;
            await channel.QueueDeclareAsync(sink,true,false,false,cancellationToken:ct);
            await channel.QueueBindAsync(sink,"nexaconnect.events","#",cancellationToken:ct);
            await store.WaitForBindingsAsync(channel,ct);
            foreach(var source in new[]{orderDb,paymentDb})
            {
                await using var transport=new RabbitMqOutboxTransport(rabbit,Options.Create(new OutboxOptions()));
                using var dispatcher=new OutboxDispatcher(new PostgresOutboxStore(source),transport,Options.Create(new OutboxOptions{PollInterval=TimeSpan.FromMilliseconds(50)}),NullLogger<OutboxDispatcher>.Instance);
                await dispatcher.StartAsync(ct);
                try{await store.WaitForOutboxAsync(source,ct);}finally{await dispatcher.StopAsync(ct);}
            }
            await store.WaitForFactsAsync(ct);
            await channel.QueueDeleteAsync(sink,cancellationToken:ct);
        }
        else
        {
            var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(options.RecoveryDll());
            foreach(string argument in new[]{state.OrganizationId.ToString(),state.BranchId.ToString(),state.FromUtc.ToString("O"),state.ToUtc.ToString("O"),"--record"})start.ArgumentList.Add(argument);
            foreach(string suffix in new[]{"order","payment","reporting"})start.Environment["NEXACONNECT_FINANCIAL_RECOVERY_"+suffix.ToUpperInvariant()+"_DB"]=options.Connection(suffix);
            start.Environment["NEXACONNECT_FINANCIAL_RECOVERY_ACTOR"]=actor;
            using var child=Process.Start(start)??throw new InvalidOperationException();
            var output=child.StandardOutput.ReadToEndAsync(ct);var errors=child.StandardError.ReadToEndAsync(ct);
            try{await child.WaitForExitAsync(ct);}finally{if(!child.HasExited){child.Kill(true);child.WaitForExit(10000);}}
            await errors; if(child.ExitCode is not (0 or 1))throw new InvalidOperationException();
            using var result=JsonDocument.Parse(await output);
            if(!result.RootElement.GetProperty("Recorded").GetBoolean())throw new InvalidOperationException();
            Console.WriteLine(result.RootElement.GetProperty("Observation").GetProperty("Status").GetString());
        }
    }
    return 0;
}
catch(Exception exception){Console.Error.WriteLine($"Financial portal fixture failed at {stage} ({exception.GetType().Name}, SQLSTATE={(exception as PostgresException)?.SqlState??"none"}); sensitive details suppressed.");return 1;}

internal sealed record FixtureState(string RunId,Guid OrganizationId,Guid OtherOrganizationId,Guid RestaurantId,Guid BranchId,Guid DeniedBranchId,DateTimeOffset FromUtc,DateTimeOffset ToUtc,string? BusinessDate=null,PosFixtureIds? Pos=null,CashierReference? Cashier=null);
internal sealed class FixtureClock(DateTimeOffset now):TimeProvider { public override DateTimeOffset GetUtcNow()=>now; }
internal sealed record FixtureOptions(string RunId,string Reader,string Resolver,string StatePath)
{
    private static string Required(string key)=>Environment.GetEnvironmentVariable("NEXACONNECT_FINANCIAL_PORTAL_"+key)??throw new ArgumentException();
    public bool DayClose=>Environment.GetEnvironmentVariable("NEXACONNECT_FINANCIAL_PORTAL_DAY_CLOSE")=="1";
    public string? SecondManager=>DayClose?Required("SECOND_MANAGER_SUBJECT"):null;
    public IEnumerable<string> Subjects=>DayClose?[Reader,Resolver,SecondManager!]:[Reader,Resolver];
    public bool EndOfDay=>Environment.GetEnvironmentVariable("NEXACONNECT_FINANCIAL_PORTAL_END_OF_DAY")=="1";
    public static FixtureOptions Read()
    {
        string run=Required("RUN_ID");
        if(Required("ENABLED")!="1"||Required("CONFIRM_DISPOSABLE")!="1"||!System.Text.RegularExpressions.Regex.IsMatch(run,"^[a-f0-9]{32}$"))throw new ArgumentException();
        string reader=Required("READER_SUBJECT"),resolver=Required("RESOLVER_SUBJECT");
        if(!Guid.TryParse(reader,out _)||!Guid.TryParse(resolver,out _)||reader==resolver)throw new ArgumentException();
        string path=Path.GetFullPath(Required("STATE_PATH"));
        if(Path.GetFileName(path)!="fixture.json"||new DirectoryInfo(Path.GetDirectoryName(path)!).Name!=run)throw new ArgumentException();
        var options=new FixtureOptions(run,reader,resolver,path);
        if(options.DayClose && (!options.EndOfDay || !Guid.TryParse(options.SecondManager,out var other) || other==Guid.Parse(reader) || other==Guid.Parse(resolver)))throw new ArgumentException();
        return options;
    }
    public string Connection(string suffix)
    {
        if(suffix is not ("platform" or "restaurant" or "authorization" or "order" or "payment" or "reporting") && !(suffix=="pos"&&EndOfDay))throw new ArgumentException();
        string value=Required("DB_"+suffix.ToUpperInvariant());var b=new NpgsqlConnectionStringBuilder(value);
        if(b.Host!="127.0.0.1"||b.Database!=$"nexa_review_it_{RunId}_{suffix}"||!string.IsNullOrEmpty(b.SearchPath))throw new ArgumentException();
        return value;
    }
    public Uri Broker()
    {
        var uri=new Uri(Required("BROKER"));
        if(uri.Scheme!="amqp"||uri.Host!="127.0.0.1")throw new ArgumentException();
        return uri;
    }
    public string RecoveryDll()
    {
        string path=Path.GetFullPath(Required("RECOVERY_DLL"));
        string expected=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../..","NexaConnect.FinancialReportingRecovery/bin/Debug/net10.0/NexaConnect.FinancialReportingRecovery.dll"));
        if(path!=expected)throw new ArgumentException();
        return path;
    }
}
