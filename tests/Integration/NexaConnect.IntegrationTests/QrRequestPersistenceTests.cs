extern alias ORDER;
using Npgsql;
using NexaConnect.Contracts.RestaurantQr;
using ORDER::NexaConnect.Services.Order.Application.Orders;
using ORDER::NexaConnect.Services.Order.Domain.Qr;
using ORDER::NexaConnect.Services.Order.Infrastructure.Persistence;
namespace NexaConnect.IntegrationTests;

public sealed class QrRequestPersistenceTests
{
    [ConfiguredEnvironmentFact("NEXACONNECT_QR_INTEGRATION_DB")]
    public async Task Requests_are_durable_private_idempotent_versioned_bounded_and_atomic()
    {
        var settings=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEXACONNECT_QR_INTEGRATION_DB"));Assert.Equal("127.0.0.1",settings.Host);Assert.Matches("^nexa_qr_it_[a-f0-9]{32}$",settings.Database!);
        string schema="qr_requests_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
        try
        {
            await using(var command=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"",admin))await command.ExecuteNonQueryAsync();
            settings.SearchPath=schema;await using var source=NpgsqlDataSource.Create(settings.ConnectionString);
            string scripts=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Tools/NexaConnect.DataMigration/Scripts/Order"));
            foreach(var dir in Directory.GetDirectories(scripts).Order()){await Sql(source,await File.ReadAllTextAsync(Path.Combine(dir,"up.sql")));}
            await Sql(source,await File.ReadAllTextAsync(Path.Combine(scripts,"0020_qr_guest_requests/down.sql")));
            await Sql(source,await File.ReadAllTextAsync(Path.Combine(scripts,"0020_qr_guest_requests/up.sql")));
            var repo=new PostgresQrRequestRepository(source);var scope=new GuestRequestScope(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"A1");
            var quote=Quote();await repo.SaveQuoteAsync(new(scope,quote),default);var input=new QrSubmission(quote.QuoteId!.Value,quote.Fingerprint);
            var results=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>repo.SubmitAsync(scope,input,DateTimeOffset.UtcNow.AddHours(1),Guid.NewGuid(),default)));
            Assert.All(results,r=>Assert.Equal(input.QuoteId,r.Request.Id));Assert.All(results,r=>Assert.Equal("pending",r.Request.Status));
            Assert.Single((await new PostgresQrRequestRepository(source).ListAsync(scope.OrganizationId,scope.BranchId,scope.VisitorId,"all",default)).Items);
            Assert.Empty((await repo.ListAsync(scope.OrganizationId,scope.BranchId,Guid.NewGuid(),"all",default)).Items);
            Assert.Empty((await repo.ListAsync(Guid.NewGuid(),scope.BranchId,null,"pending",default)).Items);
            await Assert.ThrowsAsync<GuestRequestConflictException>(()=>repo.SubmitAsync(scope,input with{Fingerprint=new string('B',64)},DateTimeOffset.UtcNow.AddHours(1),Guid.NewGuid(),default));
            await Assert.ThrowsAsync<GuestRequestConflictException>(()=>repo.SubmitAsync(scope with{VisitorId=Guid.NewGuid()},input,DateTimeOffset.UtcNow.AddHours(1),Guid.NewGuid(),default));
            // A second quote cannot create another pending request for this visitor.
            var next=Quote();await repo.SaveQuoteAsync(new(scope,next),default);
            await Assert.ThrowsAsync<GuestRequestConflictException>(()=>repo.SubmitAsync(scope,new(next.QuoteId!.Value,next.Fingerprint),DateTimeOffset.UtcNow.AddHours(1),Guid.NewGuid(),default));
            await using(var command=source.CreateCommand("SELECT count(*) FROM order_qr_request_audit"))Assert.Equal(1L,await command.ExecuteScalarAsync());
            await Sql(source,"UPDATE order_qr_quotes SET expires_at_utc=now()-interval '1 minute'");
            Assert.Equal(input.QuoteId,(await repo.SubmitAsync(scope,input,DateTimeOffset.UtcNow.AddHours(1),Guid.NewGuid(),default)).Request.Id);
            var rejected=await repo.RejectAsync(scope.OrganizationId,scope.BranchId,input.QuoteId,new(1,"unavailable"),"operator",Guid.NewGuid(),Guid.NewGuid(),default);Assert.Equal("rejected",rejected!.Request.Status);Assert.Equal(2,rejected.Request.Version);
            await Assert.ThrowsAsync<GuestRequestConflictException>(()=>repo.RejectAsync(scope.OrganizationId,scope.BranchId,input.QuoteId,new(1,"closing"),"operator",Guid.NewGuid(),Guid.NewGuid(),default));
            // Expiry is durable and audited on scoped access.
            var expiring=Quote();var visitor=scope with{VisitorId=Guid.NewGuid()};await repo.SaveQuoteAsync(new(visitor,expiring),default);
            await repo.SubmitAsync(visitor,new(expiring.QuoteId!.Value,expiring.Fingerprint),DateTimeOffset.UtcNow.AddHours(1),Guid.NewGuid(),default);
            await Sql(source,"UPDATE order_qr_requests SET submitted_at_utc=now()-interval '20 minutes',deadline_utc=now()-interval '1 minute' WHERE status='pending'");
            var expired=await repo.ListAsync(scope.OrganizationId,scope.BranchId,visitor.VisitorId,"all",default);Assert.Equal("expired",Assert.Single(expired.Items).Status);Assert.Equal(2,expired.Items[0].Version);
            // Audit/outbox failure rolls back the request; retry original quote succeeds.
            var rollbackScope=scope with{VisitorId=Guid.NewGuid()};var rollbackQuote=Quote();await repo.SaveQuoteAsync(new(rollbackScope,rollbackQuote),default);
            await Sql(source,"ALTER TABLE outbox_messages RENAME TO unavailable_outbox_messages");
            try{await Assert.ThrowsAsync<PostgresException>(()=>repo.SubmitAsync(rollbackScope,new(rollbackQuote.QuoteId!.Value,rollbackQuote.Fingerprint),DateTimeOffset.UtcNow.AddHours(1),Guid.NewGuid(),default));}
            finally{await Sql(source,"ALTER TABLE unavailable_outbox_messages RENAME TO outbox_messages");}
            Assert.Empty((await repo.ListAsync(scope.OrganizationId,scope.BranchId,rollbackScope.VisitorId,"all",default)).Items);
            var original=await repo.SubmitAsync(rollbackScope,new(rollbackQuote.QuoteId!.Value,rollbackQuote.Fingerprint),DateTimeOffset.UtcNow.AddHours(1),Guid.NewGuid(),default);
            Assert.Equal("pending",original.Request.Status);
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(source,"UPDATE order_qr_request_audit SET action='expired'"));
            string down=await File.ReadAllTextAsync(Path.Combine(scripts,"0020_qr_guest_requests/down.sql"));
            await Assert.ThrowsAsync<PostgresException>(()=>Sql(source,down));
            // Quote admission is serialized and rejects the 11th live quote for one visitor.
            var budgetScope=scope with{VisitorId=Guid.NewGuid()};
            var admissions=await Task.WhenAll(Enumerable.Range(0,11).Select(async _=>{try{await repo.SaveQuoteAsync(new(budgetScope,Quote()),default);return true;}catch(GuestRequestLimitException){return false;}}));Assert.Equal(10,admissions.Count(x=>x));
            await using(var count=source.CreateCommand("SELECT (SELECT count(*) FROM orders),(SELECT count(*) FROM order_qr_request_audit),(SELECT count(*) FROM outbox_messages WHERE event_type<>'order.audit.v1')"))
            await using(var reader=await count.ExecuteReaderAsync()){await reader.ReadAsync();Assert.Equal(0,reader.GetInt64(0));Assert.Equal(5,reader.GetInt64(1));Assert.Equal(0,reader.GetInt64(2));}
            var decisions=await Task.WhenAll(new[]{"closing","unavailable"}.Select(async reason=>{try{await repo.RejectAsync(scope.OrganizationId,scope.BranchId,original.Request.Id,new(1,reason),"operator",Guid.NewGuid(),Guid.NewGuid(),default);return true;}catch(GuestRequestConflictException){return false;}}));Assert.Single(decisions,x=>x);
            var tableScope=scope with{BranchId=Guid.NewGuid()};
            var tableAdmissions=await Task.WhenAll(Enumerable.Range(0,11).Select(async _=>
            {
                var s=tableScope with{VisitorId=Guid.NewGuid()};var q=Quote();await repo.SaveQuoteAsync(new(s,q),default);
                try{await repo.SubmitAsync(s,new(q.QuoteId!.Value,q.Fingerprint),DateTimeOffset.UtcNow.AddHours(1),Guid.NewGuid(),default);return true;}catch(GuestRequestLimitException){return false;}
            }));Assert.Equal(10,tableAdmissions.Count(x=>x));
            await Sql(source,"UPDATE order_qr_requests SET submitted_at_utc=now()-interval '2 days',deadline_utc=now()-interval '1 day' WHERE status<>'pending'");
            Assert.Empty((await repo.ListAsync(scope.OrganizationId,scope.BranchId,scope.VisitorId,"all",default)).Items);
            await using(var audit=source.CreateCommand("SELECT count(*) FROM order_qr_request_audit"))Assert.True((long)(await audit.ExecuteScalarAsync())!>=6);
        }
        finally{await using var command=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",admin);await command.ExecuteNonQueryAsync();}
    }
    private static QrQuote Quote()=>new(new string('A',64),"THB",DateTimeOffset.UtcNow.AddMinutes(2),[new(Guid.NewGuid(),"Soup",107,1,107)],107,100,10,7.7m,117.7m,7,true,10,Guid.NewGuid());
    private static async Task Sql(NpgsqlDataSource source,string sql){await using var command=source.CreateCommand(sql);await command.ExecuteNonQueryAsync();}
}
