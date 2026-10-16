extern alias ORDER;
extern alias PAYMENT;
extern alias REPORTING;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexaConnect.Contracts.IntegrationEvents;
using NexaConnect.Infrastructure.Messaging;
using Npgsql;
using RabbitMQ.Client;
using ORDER::NexaConnect.Services.Order.Domain;
using ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
using PAYMENT::NexaConnect.Services.Payment.Application.Intents;
using PAYMENT::NexaConnect.Services.Payment.Application.Refunds;
using PAYMENT::NexaConnect.Services.Payment.Infrastructure;
using PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers;
using REPORTING::NexaConnect.Services.Reporting.Application;
using REPORTING::NexaConnect.Services.Reporting.Infrastructure.Persistence;

namespace NexaConnect.IntegrationTests;

public sealed class FinancialCompletenessPipelineTests:IAsyncLifetime
{
    private NpgsqlDataSource? order,payment,reporting;
    private string? reportingHostConnection;
    private readonly Dictionary<string,string> scopedConnections=[];
    private readonly List<(NpgsqlDataSource Source,string Schema)> owned=[];
    private static readonly IOptions<PaymentProviderOptions> ProviderOptions=Options.Create(new PaymentProviderOptions());

    [ReportingDatabaseFact]
    public async Task Refund_publication_rollback_original_identity_replay_and_missing_history_are_safe()
    {
        var intent=Captured(); var refunds=new PostgresPaymentRefunds(payment!,ProviderOptions);
        var context=new PaymentMutationContext("manager",Guid.NewGuid());
        var lease=refunds.Begin(intent.OrganizationId,intent.Id,new(Guid.NewGuid(),25,"THB","customer_request",Guid.NewGuid()),context);
        await using var database=await payment!.OpenConnectionAsync();
        await Sql(database,"CREATE FUNCTION financial_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.event_type='payment.refunded.v1' THEN RAISE EXCEPTION 'test failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER financial_failure BEFORE INSERT ON outbox_messages FOR EACH ROW EXECUTE FUNCTION financial_failure()");
        Assert.Throws<PostgresException>(()=>refunds.Complete(intent.OrganizationId,lease.Refund.Id,lease.Refund.ConcurrencyVersion,ProviderRefundOutcome.Refunded,"refund-test",null,context));
        Assert.Equal(0L,await Count(database,"SELECT count(*) FROM refund_financial_publications"));
        Assert.Equal("processing",refunds.Get(intent.OrganizationId,lease.Refund.Id)!.Status);
        await Sql(database,"DROP TRIGGER financial_failure ON outbox_messages");
        var completed=refunds.Complete(intent.OrganizationId,lease.Refund.Id,lease.Refund.ConcurrencyVersion,ProviderRefundOutcome.Refunded,"refund-test",null,context);
        Assert.Equal(1L,await Count(database,"SELECT count(*) FROM refund_financial_publications"));
        var window=(From:DateTimeOffset.UtcNow.AddDays(-1),To:DateTimeOffset.UtcNow);
        var replay=new PostgresRefundFinancialReplay(payment);
        var before=await replay.RunAsync(intent.OrganizationId,intent.BranchId,window.From,window.To,false,null,default);
        Assert.Single(before.Events); Guid original=before.Events[0].EventId;
        await Sql(database,"DELETE FROM outbox_messages WHERE event_type='payment.refunded.v1'");
        var applied=await replay.RunAsync(intent.OrganizationId,intent.BranchId,window.From,window.To,true,"operator",default);
        Assert.Equal(original,applied.Events.Single().EventId); Assert.Equal(1,applied.Requeued);
        Assert.Equal(completed.Receipt!.RefundedAtUtc,applied.Events[0].OccurredAtUtc);
        Assert.Equal(1L,await Count(database,"SELECT count(*) FROM refund_financial_replay_audit"));
        Assert.Empty((await replay.RunAsync(Guid.NewGuid(),intent.BranchId,window.From,window.To,true,"operator",default)).Events);
        await Sql(database,"UPDATE outbox_messages SET payload='{}'::jsonb WHERE event_type='payment.refunded.v1'");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>replay.RunAsync(intent.OrganizationId,intent.BranchId,window.From,window.To,true,"operator",default));
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(database,"UPDATE refund_financial_publications SET payload=payload"));
        await Assert.ThrowsAsync<PostgresException>(async ()=>await Sql(database,await File.ReadAllTextAsync(Path.Combine(Scripts("Payment"),"0010_refund_financial_publications/down.sql"))));
    }

    [ReportingDatabaseFact]
    public async Task Legacy_refund_retains_original_outbox_event_or_reports_missing_evidence_without_invention()
    {
        var intent=Captured(); var repository=new PostgresPaymentRefunds(payment!,ProviderOptions);
        var context=new PaymentMutationContext("manager",Guid.NewGuid());
        async Task<PaymentRefundedV1> Legacy(decimal amount)
        {
            var lease=repository.Begin(intent.OrganizationId,intent.Id,new(Guid.NewGuid(),amount,"THB","customer_request",Guid.NewGuid()),context);
            var at=DateTimeOffset.UtcNow;
            var receipt=new PaymentRefundReceipt($"RF-{lease.Refund.Id:N}".ToUpperInvariant(),lease.Refund.Id,intent.Id,intent.OrderId,amount,"THB","customer_request",at,100,amount);
            await using var update=payment!.CreateCommand("UPDATE refunds SET status='completed',completed_at_utc=$2,receipt_snapshot=$3::jsonb WHERE id=$1");
            update.Parameters.AddWithValue(lease.Refund.Id);update.Parameters.AddWithValue(at);update.Parameters.AddWithValue(JsonSerializer.Serialize(receipt));await update.ExecuteNonQueryAsync();
            return new(Guid.NewGuid(),context.CorrelationId,at,intent.OrganizationId,intent.RestaurantId,intent.BranchId,intent.OrderId,intent.Id,lease.Refund.Id,amount,"THB","customer_request",amount,100,receipt.ReceiptNumber);
        }
        var original=await Legacy(25); await Legacy(10); // the second original event was lost before ledger deployment
        await new PostgresOutboxStore(payment!).EnqueueAsync(new(original.EventId,"payment.refunded.v1",1,"payment-refund",original.RefundId,
            JsonSerializer.Serialize(original),original.CorrelationId.ToString("D"),original.OccurredAtUtc),default);
        var replay=new PostgresRefundFinancialReplay(payment!); var from=DateTimeOffset.UtcNow.AddDays(-1);var to=DateTimeOffset.UtcNow;
        var dry=await replay.RunAsync(intent.OrganizationId,intent.BranchId,from,to,false,null,default);
        Assert.Equal(2,dry.Candidates);Assert.Equal(1,dry.EvidenceGaps);Assert.Equal(1,dry.Unretained);
        var apply=await replay.RunAsync(intent.OrganizationId,intent.BranchId,from,to,true,"operator",default);
        Assert.Equal(original.EventId,apply.Events.Single().EventId);Assert.Equal(1,apply.EvidenceGaps);Assert.Equal(0,apply.Unretained);
        await using var database=await payment!.OpenConnectionAsync();
        Assert.Equal(1L,await Count(database,"SELECT count(*) FROM refund_financial_publications"));
    }

    [ReportingDatabaseFact]
    public async Task Combined_inventory_detects_partial_drift_extra_rows_time_bases_and_immutable_scoped_observations()
    {
        var intent=Captured(); var sale=await Paid(intent); var refund=Refund(intent);
        var from=DateTimeOffset.UtcNow.AddDays(-1);var to=DateTimeOffset.UtcNow;
        var sales=await new PostgresFinancialSaleSource(order!).RunAsync(intent.OrganizationId,intent.BranchId,from,to,false,null,default);
        var refunds=await new PostgresRefundFinancialReplay(payment!).RunAsync(intent.OrganizationId,intent.BranchId,from,to,false,null,default);
        var source=Source(intent,from,to,sales,refunds);
        var checker=new FinancialCompleteness(new PostgresFinancialCompletenessRepository(reporting!));
        var missing=await checker.CheckAsync(source,default);Assert.Equal(1,missing.Sales.Missing);Assert.Equal(1,missing.Refunds.Missing);
        await new RefundFinancialReporting(new PostgresRefundFinancialFactRepository(reporting!)).ProjectAsync(refunds.Events.Single(),default);
        await new SaleFinancialReporting(new PostgresSaleFinancialFactRepository(reporting!)).ProjectAsync(sales.Events.Single(),default);
        var complete=await checker.CheckAsync(source,default);Assert.Equal("observed_complete",complete.Status);
        await checker.RecordAsync(complete,"operator",default);
        var stored=await checker.LatestAsync(intent.OrganizationId,intent.BranchId,from,to,default);Assert.Equal(complete.CheckId,stored!.CheckId);
        long bucket=from.UtcTicks/10*10;
        var differentWindow=new DateTimeOffset(bucket+(from.UtcTicks%10==1?2:1),TimeSpan.Zero);
        Assert.Null(await checker.LatestAsync(intent.OrganizationId,intent.BranchId,differentWindow,to,default));
        Assert.Null(await checker.LatestAsync(Guid.NewGuid(),intent.BranchId,from,to,default));
        await VerifyRecoveryCli(intent,from,to);
        await using var database=await reporting!.OpenConnectionAsync();
        await Sql(database,"UPDATE payment_facts SET paid_amount=1");
        var changed=await checker.CheckAsync(source,default);Assert.Equal(1,changed.Payments.Conflicting);
        await Sql(database,"UPDATE payment_facts SET paid_amount=100");
        var extra=sales.Events.Single() with { EventId=Guid.NewGuid(),OrderId=Guid.NewGuid(),PaymentId=Guid.NewGuid() };
        await new SaleFinancialReporting(new PostgresSaleFinancialFactRepository(reporting!)).ProjectAsync(extra,default);
        var unexpected=await checker.CheckAsync(source,default);Assert.Equal(1,unexpected.Sales.Unexpected);Assert.Equal(1,unexpected.Payments.Unexpected);
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(database,"DELETE FROM financial_completeness_checks"));
        await Assert.ThrowsAsync<PostgresException>(async ()=>await Sql(database,await File.ReadAllTextAsync(Path.Combine(Scripts("Reporting"),"0020_financial_completeness/down.sql"))));
        // Sale and payment periods differ: Order creation is independently checked even when Paid is outside the window.
        var paidAt=sales.Events.Single().PaidAtUtc;
        var orderedWindowTo=paidAt.AddTicks(-10);
        var saleOnly=await new PostgresFinancialSaleSource(order!).RunAsync(intent.OrganizationId,intent.BranchId,from,orderedWindowTo,false,null,default);
        Assert.Single(saleOnly.Events);
        Assert.True(saleOnly.Events[0].OrderedAtUtc<orderedWindowTo);
    }

    [FinancialHostedFact]
    public async Task Hosted_outboxes_survive_unreachable_broker_and_reporting_process_restart_with_refund_before_sale()
    {
        string uri=Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI")!;
        string exchange=$"financial_host_it_{Guid.NewGuid():N}",saleQueue=exchange+".sales",refundQueue=exchange+".refunds";
        await using var rabbit=await new ConnectionFactory{Uri=new Uri(uri)}.CreateConnectionAsync();
        await using var channel=await rabbit.CreateChannelAsync();
        string evidenceQueue=exchange+".evidence";
        await channel.ExchangeDeclareAsync(exchange,ExchangeType.Topic,true);
        await channel.QueueDeclareAsync(evidenceQueue,true,false,false);
        await channel.QueueBindAsync(evidenceQueue,exchange,"#");
        Process? host=null;
        try
        {
            host=await StartReporting(uri,exchange,saleQueue,refundQueue);
            var intent=Captured(); await Paid(intent); Refund(intent);
            // A publisher that cannot reach its broker leaves the real PostgreSQL outbox retryable.
            int unavailablePort=FreePort();
            var unavailable=new UriBuilder(uri){Host="127.0.0.1",Port=unavailablePort}.Uri;
            await using var failure=new RabbitMqOutboxTransport(Options.Create(new OutboxOptions{Exchange=exchange}),
                ct=>new ConnectionFactory{Uri=unavailable,RequestedConnectionTimeout=TimeSpan.FromSeconds(2)}.CreateConnectionAsync(ct));
            await using var paymentDb=await payment!.OpenConnectionAsync();
            using(var dispatcher=new OutboxDispatcher(new PostgresOutboxStore(payment!),failure,Options.Create(new OutboxOptions{Exchange=exchange,PollInterval=TimeSpan.FromMilliseconds(50)}),NullLogger<OutboxDispatcher>.Instance))
            {
                await dispatcher.StartAsync(default);await Wait(async ()=>await Count(paymentDb,"SELECT count(*) FROM outbox_messages WHERE last_error_category IS NOT NULL")>0);await dispatcher.StopAsync(default);
            }
            Assert.True(await Count(paymentDb,"SELECT count(*) FROM outbox_messages WHERE published_at_utc IS NULL AND retry_count>0")>0);
            await Sql(paymentDb,"UPDATE outbox_messages SET next_attempt_at_utc=NULL");
            await Dispatch(payment!,rabbit,exchange);
            await using var reportDb=await reporting!.OpenConnectionAsync();
            await Wait(async ()=>await Count(reportDb,"SELECT count(*) FROM refund_facts")==1);
            Assert.Equal(0L,await Count(reportDb,"SELECT count(*) FROM sales_facts"));
            Stop(host);host=null;
            await Dispatch(order!,rabbit,exchange); // durable sale queue receives while Reporting is down
            var from=DateTimeOffset.UtcNow.AddDays(-1);var to=DateTimeOffset.UtcNow;
            var replay=await new PostgresRefundFinancialReplay(payment!).RunAsync(intent.OrganizationId,intent.BranchId,from,to,true,"acceptance",default);
            await Dispatch(payment!,rabbit,exchange); // duplicate refund delivery queues before restart
            host=await StartReporting(uri,exchange,saleQueue,refundQueue);
            await Wait(async ()=>await Count(reportDb,"SELECT count(*) FROM sales_facts")==1 && await Count(reportDb,"SELECT count(*) FROM refund_facts")==1);
            var sales=await new PostgresFinancialSaleSource(order!).RunAsync(intent.OrganizationId,intent.BranchId,from,to,false,null,default);
            var result=await new FinancialCompleteness(new PostgresFinancialCompletenessRepository(reporting!)).CheckAsync(Source(intent,from,to,sales,replay),default);
            Assert.Equal("observed_complete",result.Status);
            Assert.Equal(1L,await Count(reportDb,"SELECT count(*) FROM refund_fact_event_receipts"));
            Assert.Equal(0u,(await channel.QueueDeclarePassiveAsync(refundQueue+".dead")).MessageCount);
        }
        finally
        {
            if(host is not null)Stop(host);
            await channel.QueueDeleteAsync(saleQueue);await channel.QueueDeleteAsync(saleQueue+".dead");
            await channel.QueueDeleteAsync(refundQueue);await channel.QueueDeleteAsync(refundQueue+".dead");
            await channel.QueueDeleteAsync(evidenceQueue);await channel.ExchangeDeleteAsync(exchange);
        }
    }

    private static async Task Dispatch(NpgsqlDataSource source,IConnection rabbit,string exchange)
    {
        await using var transport=new RabbitMqOutboxTransport(rabbit,Options.Create(new OutboxOptions{Exchange=exchange}));
        using var dispatcher=new OutboxDispatcher(new PostgresOutboxStore(source),transport,Options.Create(new OutboxOptions{PollInterval=TimeSpan.FromMilliseconds(50)}),NullLogger<OutboxDispatcher>.Instance);
        await dispatcher.StartAsync(default);
        await using var connection=await source.OpenConnectionAsync();await Wait(async ()=>await Count(connection,"SELECT count(*) FROM outbox_messages WHERE published_at_utc IS NULL")==0);
        await dispatcher.StopAsync(default);
    }
    private async Task<Process> StartReporting(string uri,string exchange,string saleQueue,string refundQueue)
    {
        int port=FreePort();
        string directory=Path.Combine(Root(),"src/Services/NexaConnect.Services.Reporting");
        var start=new ProcessStartInfo("dotnet"){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.Combine(directory,"bin/Debug/net10.0/NexaConnect.Services.Reporting.dll"));
        start.Environment["DOTNET_ENVIRONMENT"]="Testing";start.Environment["ASPNETCORE_ENVIRONMENT"]="Testing";
        start.Environment["ASPNETCORE_URLS"]=$"http://127.0.0.1:{port}";start.Environment["ConnectionStrings__Reporting"]=reportingHostConnection!;
        start.Environment["OrderSaleConsumer__Enabled"]="true";start.Environment["PaymentRefundConsumer__Enabled"]="true";
        start.Environment["ActivityConsumer__Enabled"]="false";start.Environment["CashCloseConsumer__Enabled"]="false";
        start.Environment["Observability__OtlpEnabled"]="false";
        start.Environment["Authentication__Authority"]="https://localhost/realms/financial-acceptance";
        foreach(var key in new[]{"OrderSaleConsumer","PaymentRefundConsumer"})
        {start.Environment[key+"__ConnectionString"]=uri;start.Environment[key+"__Exchange"]=exchange;start.Environment[key+"__Queue"]=key=="OrderSaleConsumer"?saleQueue:refundQueue;}
        var readySale=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var readyRefund=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var process=new Process{StartInfo=start,EnableRaisingEvents=true};
        process.Exited+=(_,_)=>{readySale.TrySetException(new InvalidOperationException("Owned Reporting host exited before consumer readiness."));readyRefund.TrySetException(new InvalidOperationException("Owned Reporting host exited before consumer readiness."));};
        process.OutputDataReceived+=(_,e)=>{if(e.Data?.Contains("Order sale financial consumer ready")==true)readySale.TrySetResult();if(e.Data?.Contains("Payment refund financial consumer ready")==true)readyRefund.TrySetResult();};
        process.ErrorDataReceived+=(_,_)=>{};
        try
        {
            process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await Task.WhenAll(readySale.Task,readyRefund.Task).WaitAsync(timeout.Token);return process;
        }
        catch{Stop(process);throw;}
    }
    private async Task VerifyRecoveryCli(PaymentIntent intent,DateTimeOffset from,DateTimeOffset to)
    {
        string directory=Path.Combine(Root(),"src/Tools/NexaConnect.FinancialReportingRecovery");
        var start=new ProcessStartInfo("dotnet"){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.Combine(directory,"bin/Debug/net10.0/NexaConnect.FinancialReportingRecovery.dll"));
        foreach(string argument in new[]{intent.OrganizationId.ToString(),intent.BranchId.ToString(),from.ToString("O"),to.ToString("O"),"--record"})start.ArgumentList.Add(argument);
        foreach(string service in new[]{"Order","Payment","Reporting"})start.Environment["NEXACONNECT_FINANCIAL_RECOVERY_"+service.ToUpperInvariant()+"_DB"]=scopedConnections[service];
        start.Environment["NEXACONNECT_FINANCIAL_RECOVERY_ACTOR"]="acceptance-cli";
        using var process=new Process{StartInfo=start};process.Start();
        Task<string> output=process.StandardOutput.ReadToEndAsync(),error=process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0,process.ExitCode);Assert.Empty(await error);
            using var json=JsonDocument.Parse(await output);
            Assert.True(json.RootElement.GetProperty("Recorded").GetBoolean());
            Assert.Equal("observed_complete",json.RootElement.GetProperty("Observation").GetProperty("Status").GetString());
            Assert.DoesNotContain("acceptance-cli",await output);
        }
        finally{if(!process.HasExited){process.Kill(entireProcessTree:true);process.WaitForExit(10000);}}
    }
    private static void Stop(Process process)
    {try{if(!process.HasExited){process.Kill(entireProcessTree:true);process.WaitForExit(10000);}}finally{process.Dispose();}}
    private static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    private PaymentIntent Captured()
    {
        var store=new PostgresPaymentIntents(payment!,ProviderOptions);var context=new PaymentMutationContext("acceptance",Guid.NewGuid());
        var intent=store.Create(Guid.NewGuid(),new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid().ToString("D"),100,"THB","card"),context);
        var authorization=store.BeginAuthorization(intent.OrganizationId,intent.Id,context);
        store.CompleteAuthorization(intent.OrganizationId,intent.Id,authorization.Intent.ConcurrencyVersion,ProviderAuthorizationOutcome.Authorized,"charge-"+intent.Id.ToString("N"),null,context);
        var capture=store.BeginCapture(intent.OrganizationId,intent.Id,context);
        return store.CompleteCapture(intent.OrganizationId,intent.Id,capture.Intent.ConcurrencyVersion,ProviderCaptureOutcome.Captured,"capture-"+intent.Id.ToString("N"),null,context);
    }
    private async Task<OrderAggregate> Paid(PaymentIntent intent)
    {
        var value=OrderAggregate.Create(intent.OrderId,intent.OrganizationId,intent.BranchId,[new OrderLine(Guid.NewGuid(),"Rice",100,1,"kitchen")],"THB",restaurantId:intent.RestaurantId,workflowPaymentMethod:"card");
        value.Submit();value.MarkInventoryReserved();value.MarkKitchenAccepted();var store=new PostgresOrderRepository(order!);await store.SaveAsync(value,default);
        value.MarkPaid(intent.Id);value.IssueReceipt(DateTimeOffset.UtcNow,"card");await store.SaveAsync(value,default);return value;
    }
    private PaymentRefund Refund(PaymentIntent intent)
    {
        var store=new PostgresPaymentRefunds(payment!,ProviderOptions);var context=new PaymentMutationContext("manager",Guid.NewGuid());
        var lease=store.Begin(intent.OrganizationId,intent.Id,new(Guid.NewGuid(),25,"THB","customer_request",Guid.NewGuid()),context);
        return store.Complete(intent.OrganizationId,lease.Refund.Id,lease.Refund.ConcurrencyVersion,ProviderRefundOutcome.Refunded,"refund-"+lease.Refund.Id.ToString("N"),null,context);
    }
    private static FinancialCompletenessSource Source(PaymentIntent intent,DateTimeOffset from,DateTimeOffset to,FinancialSaleSource sales,RefundFinancialSource refunds)=>
        new(new(intent.OrganizationId,intent.BranchId,from,to),sales.ObservedAtUtc,refunds.ObservedAtUtc,sales.Candidates,refunds.Candidates,sales.EvidenceGaps,refunds.EvidenceGaps,sales.Unretained,refunds.Unretained,
            sales.Events.Select(SaleFinancialReporting.Translate).ToArray(),refunds.Events.Select(RefundFinancialReporting.Translate).ToArray());
    public async Task InitializeAsync()
    {
        string? cs=Environment.GetEnvironmentVariable("NEXACONNECT_REPORTING_INTEGRATION_DB");
        string? environment=Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT")??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if(cs is null || environment is not ("Testing" or "Test" or "Development"))return;
        async Task<NpgsqlDataSource> Create(string service)
        {
            string schema=$"financial_{service.ToLowerInvariant()}_it_{Guid.NewGuid():N}";
            string scopedConnection=new NpgsqlConnectionStringBuilder(cs){SearchPath=schema}.ConnectionString;
            scopedConnections.Add(service,scopedConnection);
            if(service=="Reporting")reportingHostConnection=scopedConnection;
            var source=NpgsqlDataSource.Create(scopedConnection);owned.Add((source,schema));
            await using var connection=await source.OpenConnectionAsync();await Sql(connection,$"CREATE SCHEMA {new NpgsqlCommandBuilder().QuoteIdentifier(schema)}");
            foreach(string directory in Directory.GetDirectories(Scripts(service)).Order())await Sql(connection,await File.ReadAllTextAsync(Path.Combine(directory,"up.sql")));
            return source;
        }
        order=await Create("Order");payment=await Create("Payment");reporting=await Create("Reporting");
    }
    public async Task DisposeAsync()
    {
        foreach(var (source,schema) in owned)
        {await using var connection=await source.OpenConnectionAsync();await Sql(connection,$"DROP SCHEMA IF EXISTS {new NpgsqlCommandBuilder().QuoteIdentifier(schema)} CASCADE");await source.DisposeAsync();}
    }
    private static async Task<long> Count(NpgsqlConnection c,string sql)=>Convert.ToInt64(await new NpgsqlCommand(sql,c).ExecuteScalarAsync());
    private static async Task Sql(NpgsqlConnection c,string sql)=>await new NpgsqlCommand(sql,c).ExecuteNonQueryAsync();
    private static async Task Wait(Func<Task<bool>> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));while(!await condition())await Task.Delay(50,timeout.Token);}
    private static string Scripts(string service)=>Path.Combine(Root(),"src/Tools/NexaConnect.DataMigration/Scripts",service);
    private static string Root(){var root=new DirectoryInfo(AppContext.BaseDirectory);while(!File.Exists(Path.Combine(root.FullName,"NexaConnect.sln")))root=root.Parent!;return root.FullName;}
}

public sealed class FinancialHostedFactAttribute:ReportingDatabaseFactAttribute
{
    public FinancialHostedFactAttribute()
    {
        if(Environment.GetEnvironmentVariable("NEXACONNECT_FINANCIAL_PROCESS_ACCEPTANCE")!="1"
            || Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_ACCEPTANCE")!="1"
            || !Uri.TryCreate(Environment.GetEnvironmentVariable("NEXACONNECT_RABBITMQ_INTEGRATION_URI"),UriKind.Absolute,out _))
            Skip="Requires explicit financial process termination and RabbitMQ acceptance opt-ins on disposable infrastructure.";
    }
}
