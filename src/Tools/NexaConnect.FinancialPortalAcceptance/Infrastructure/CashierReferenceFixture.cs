using Npgsql;
namespace NexaConnect.FinancialPortalAcceptance.Infrastructure;

internal static class CashierReferenceFixture
{
    public static async Task ProofAsync(FixtureOptions options, CancellationToken ct)
    {
        var state=System.Text.Json.JsonSerializer.Deserialize<FixtureState>(await File.ReadAllTextAsync(options.StatePath,ct),new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? throw new InvalidOperationException();
        if(state.RunId!=options.RunId || state.Cashier is null)throw new InvalidOperationException();
        await using var orders=NpgsqlDataSource.Create(options.Connection("order"));
        await using var pos=NpgsqlDataSource.Create(options.Connection("pos"));
        await using var auth=NpgsqlDataSource.Create(options.Connection("authorization"));
        await using var report=NpgsqlDataSource.Create(options.Connection("reporting"));
        async Task<long> Count(NpgsqlDataSource source,string sql,object scope){await using var command=source.CreateCommand(sql);command.Parameters.AddWithValue(scope);return Convert.ToInt64(await command.ExecuteScalarAsync(ct));}
        var counts=new Dictionary<string,long>{
            ["paidOrders"]=await Count(orders,"SELECT count(*) FROM orders WHERE organization_id=$1 AND status='completed' AND receipt_snapshot IS NOT NULL",state.OrganizationId),
            ["settlements"]=await Count(orders,"SELECT count(*) FROM order_manual_tender_settlements WHERE organization_id=$1",state.OrganizationId),
            ["drawers"]=await Count(pos,"SELECT count(*) FROM cash_sessions WHERE store_id=$1 AND status='closed'",state.Cashier.StoreId),
            ["movements"]=await Count(pos,"SELECT count(*) FROM cash_movements m JOIN cash_sessions c ON c.id=m.cash_session_id WHERE c.store_id=$1 AND m.movement_type='sale'",state.Cashier.StoreId),
            ["reviews"]=await Count(pos,"SELECT count(*) FROM cash_session_review_history h JOIN cash_sessions c ON c.id=h.cash_session_id WHERE c.store_id=$1 AND h.decision='approve'",state.Cashier.StoreId),
            ["projectedSettlements"]=await Count(pos,"SELECT count(*) FROM pos_order_settlements WHERE organization_id=$1",state.OrganizationId),
            ["sales"]=await Count(report,"SELECT count(*) FROM sales_facts WHERE organization_id=$1",state.OrganizationId),
            ["payments"]=await Count(report,"SELECT count(*) FROM payment_facts WHERE organization_id=$1",state.OrganizationId)
        };
        if(options.Cutoff)
        {
            foreach(var value in counts){long expected=value.Key=="drawers"?1:value.Key=="reviews"?3:2;if(value.Value!=expected)throw new InvalidOperationException();}
        }
        else if(counts.Values.Any(n=>n!=1))throw new InvalidOperationException();
        var decisions=new List<Guid>();
        async Task Decisions(NpgsqlDataSource source,string sql,Guid scope){await using var command=source.CreateCommand(sql);command.Parameters.AddWithValue(scope);await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))decisions.Add(reader.GetGuid(0));}
        await Decisions(orders,"SELECT authorization_decision_id FROM order_manual_tender_settlements WHERE organization_id=$1",state.OrganizationId);
        await Decisions(pos,"SELECT authorization_decision_id FROM shifts WHERE store_id=$1 UNION ALL SELECT close_authorization_decision_id FROM shifts WHERE store_id=$1 UNION ALL SELECT h.authorization_decision_id FROM cash_session_review_history h JOIN cash_sessions c ON c.id=h.cash_session_id WHERE c.store_id=$1",state.Cashier.StoreId);
        await Decisions(pos,options.Cutoff?"SELECT authorization_decision_id FROM branch_day_cutoff_audit WHERE organization_id=$1":"SELECT authorization_decision_id FROM branch_day_close_audit WHERE organization_id=$1",state.OrganizationId);
        await using var authority=auth.CreateCommand("SELECT count(*) FROM authorization_decisions WHERE id=ANY($1) AND granted AND organization_id=$2");
        authority.Parameters.AddWithValue(decisions.Distinct().ToArray());authority.Parameters.AddWithValue(state.OrganizationId);
        if(decisions.Count<8 || Convert.ToInt64(await authority.ExecuteScalarAsync(ct))!=decisions.Distinct().Count())throw new InvalidOperationException();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{verified=true,counts,authorizationVerified=true}));
    }
    // Store is deployment reference data. No shift, cash session, order, receipt, settlement or report fact is seeded.
    public static async Task<CashierReference> CreateAsync(FixtureOptions options, Guid organization, Guid restaurant, Guid branch, CancellationToken ct)
    {
        await using var db = NpgsqlDataSource.Create(options.Connection("pos"));
        await using var command = db.CreateCommand("INSERT INTO stores(id,restaurant_id,branch_id,code,name,operational_status,created_at_utc,created_by,updated_at_utc,updated_by) SELECT $1,$2,$3,'cashier','Acceptance store','active',now(),'acceptance',now(),'acceptance' WHERE NOT EXISTS(SELECT 1 FROM stores)");
        var reference = new CashierReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        command.Parameters.AddWithValue(reference.StoreId); command.Parameters.AddWithValue(restaurant); command.Parameters.AddWithValue(branch);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException();
        // Enrollment is a separately provisioned administrative permission, not a default manager grant.
        await using var auth=NpgsqlDataSource.Create(options.Connection("authorization"));
        await using var enrollment=auth.CreateCommand("INSERT INTO authorization_user_permission_overrides(id,subject_id,scope_id,permission_code,effect,status) SELECT $1,$2,id,'pos.terminal.enroll','allow','active' FROM authorization_resource_scopes WHERE organization_id=$3 AND restaurant_id=$4 AND branch_id=$5 AND status='active'");
        enrollment.Parameters.AddWithValue(Guid.NewGuid());enrollment.Parameters.AddWithValue(options.Resolver);enrollment.Parameters.AddWithValue(organization);enrollment.Parameters.AddWithValue(restaurant);enrollment.Parameters.AddWithValue(branch);
        if(await enrollment.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException();
        return reference;
    }
}
internal sealed record CashierReference(Guid StoreId, Guid TerminalId, Guid ProductId);
