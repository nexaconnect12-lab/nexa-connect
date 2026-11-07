using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.POS;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.ManualRefunds;

namespace NexaConnect.Services.POS.Application.ManualRefunds;

public interface IManualRefundSaleReader
{
    Task<ManualRefundSaleV1?> ReadAsync(RestaurantAuthorizationScope scope, Guid orderId, PosUserContext user, CancellationToken ct);
}

public interface IManualRefundMembership
{
    Task<bool> HasAccessAsync(Guid organizationId, PosUserContext user, CancellationToken ct);
}

public sealed class ManualRefunds(IManualRefundSaleReader sales, IManualRefundStore store, IRestaurantScopeReader scopes,
    IAuthorizationDecisionClient authorization, IManualRefundMembership membership, TimeProvider clock)
{
    public async Task<bool> RegisterAsync(Guid organization, Guid branch, ManualRefundAvailabilityV1 terminal,
        PosUserContext user, CancellationToken ct)
    {
        if (terminal.TerminalId == Guid.Empty || terminal.ShiftId == Guid.Empty) throw new ArgumentException("An open shift is required.");
        var scope = await BranchScopeAsync(organization, branch, user, ct);
        return await GrantedAsync(user, scope, ProductPermissions.PosManualRefundPayout, ct)
            && await store.RegisterCashierAsync(scope, terminal, user.Subject, clock.GetUtcNow(), ct);
    }

    public async Task<ManualRefundCashiersV1?> CashiersAsync(Guid organization, Guid branch, Guid order,
        PosUserContext user, CancellationToken ct)
    {
        var scope = await ScopeAsync(organization, branch, order, user, ct);
        if (!await GrantedAsync(user, scope, ProductPermissions.PosManualRefundApprove, ct)) return null;
        var sale = await OriginalAsync(scope, order, user, ct);
        if (sale is null) return null;
        var ready = await store.ListReadyCashiersAsync(scope, clock.GetUtcNow(), ct);
        return new(ready.Where(row => row.Subject != user.Subject
            && (sale.Tender != ManualRefundTender.Cash || row.CashDrawerOpen)).ToArray());
    }

    public async Task<AssignedManualRefundsV1?> AssignedAsync(Guid organization, Guid branch, PosUserContext user, CancellationToken ct)
    {
        const int pageSize = 25;
        var scope = await BranchScopeAsync(organization, branch, user, ct);
        if (!await GrantedAsync(user, scope, ProductPermissions.PosManualRefundPayout, ct)) return null;
        var items = await store.ListOutstandingAsync(scope, user.Subject, pageSize + 1, ct);
        return new(items.Take(pageSize).Select(item =>
            View(item.Sale, item.Approval, item.StartedAtUtc, item.CompletedAtUtc, replayed: true)).ToArray(),
            items.Count > pageSize);
    }

    public async Task<ManualRefundPreviewV1?> PreviewAsync(Guid organization, Guid branch, Guid order, PosUserContext user, CancellationToken ct)
    {
        var scope = await ScopeAsync(organization, branch, order, user, ct);
        if (!await GrantedAsync(user, scope, ProductPermissions.PosManualRefundApprove, ct)) return null;
        var sale = await OriginalAsync(scope, order, user, ct);
        return sale is null ? null : new(order, Method(sale), sale.Currency, sale.PaidAmount, await store.AvailableAsync(sale, ct));
    }

    public async Task<ManualRefundStatusV1?> ApproveAsync(Guid organization, Guid branch, Guid order, ApproveManualRefundV1 request,
        PosUserContext user, CancellationToken ct)
    {
        if (request.OperationId == Guid.Empty || request.CashierShiftId == Guid.Empty || request.Amount is <= 0 or >= 1_000_000_000_000_000m
            || decimal.Round(request.Amount, 2) != request.Amount || string.IsNullOrWhiteSpace(request.CashierSubject)
            || request.CashierSubject.Length > 200 || request.CashierSubject.Any(char.IsControl)
            || request.CashierSubject == user.Subject
            || request.Reason is not ("customer_request" or "duplicate_charge" or "item_unavailable" or "service_issue" or "other"))
            throw new ArgumentException("Invalid refund approval.");
        var scope = await ScopeAsync(organization, branch, order, user, ct);
        var decision = await authorization.DecideForAmountAsync(user, scope, ProductPermissions.PosManualRefundApprove, request.Amount, "THB", ct);
        if (!decision.Granted || decision.DecisionId == Guid.Empty || decision.EvaluatedLimit is not { } limit || limit < request.Amount)
            return null;
        var sale = await OriginalAsync(scope, order, user, ct);
        if (sale is null) return null;
        var result = await store.ApproveAsync(sale, new(request.OperationId, request.Amount, request.Reason, user.Subject,
            decision.DecisionId, request.CashierSubject, clock.GetUtcNow()), ct, request.CashierShiftId);
        return Status(sale, result);
    }

    public async Task<ManualRefundStatusV1?> ReleaseAsync(Guid organization, Guid branch, Guid order, Guid operation,
        PosUserContext user, CancellationToken ct)
    {
        var scope = await ScopeAsync(organization, branch, order, user, ct);
        var decision = await authorization.DecideAsync(user, scope, ProductPermissions.PosManualRefundApprove, ct);
        if (!decision.Granted || decision.DecisionId == Guid.Empty) return null;
        var existing = await ReadOperationAsync(scope, order, operation, ct);
        if (existing is null || existing.Approval.ManagerSubject != user.Subject) return null;
        return Status(existing.Sale, await store.ReleaseAsync(existing.Sale, operation,
            new(user.Subject, decision.DecisionId, clock.GetUtcNow()), ct));
    }

    public async Task<AssignedManualRefundsV1?> ManagedAsync(Guid organization, Guid branch, Guid order,
        PosUserContext user, CancellationToken ct)
    {
        const int pageSize = 25;
        var scope = await ScopeAsync(organization, branch, order, user, ct);
        if (!await GrantedAsync(user, scope, ProductPermissions.PosManualRefundApprove, ct)) return null;
        var items = await store.ListManagedAsync(scope, order, user.Subject, pageSize + 1, ct);
        return new(items.Take(pageSize).Select(item => View(item.Sale, item.Approval, item.StartedAtUtc,
            item.CompletedAtUtc, replayed: true, item.ReleasedAtUtc)).ToArray(), items.Count > pageSize);
    }

    public async Task<ManualRefundStatusV1?> ReadAsync(Guid organization, Guid branch, Guid order, Guid operation,
        PosUserContext user, CancellationToken ct)
    {
        var scope = await ScopeAsync(organization, branch, order, user, ct);
        var existing = await ReadOperationAsync(scope, order, operation, ct);
        if (existing is null) return null;
        string? permission = user.Subject == existing.Approval.ManagerSubject ? ProductPermissions.PosManualRefundApprove
            : user.Subject == existing.Approval.CashierSubject ? ProductPermissions.PosManualRefundPayout : null;
        if (permission is null || !await GrantedAsync(user, scope, permission, ct)) return null;
        return View(existing.Sale, existing.Approval, existing.StartedAtUtc, existing.CompletedAtUtc, replayed: true, existing.ReleasedAtUtc);
    }

    public async Task<ManualRefundStatusV1?> PayoutAsync(Guid organization, Guid branch, Guid order, Guid operation,
        ManualRefundCustodyV1 custody, CompleteManualRefundV1? completion, PosUserContext user, CancellationToken ct)
    {
        if (custody is null || custody.TerminalId == Guid.Empty || custody.ShiftId == Guid.Empty || custody.CashSessionId == Guid.Empty)
            throw new ArgumentException("Invalid payout custody.");
        var scope = await ScopeAsync(organization, branch, order, user, ct);
        if (!await GrantedAsync(user, scope, ProductPermissions.PosManualRefundPayout, ct)) return null;
        var existing = await ReadOperationAsync(scope, order, operation, ct);
        if (existing is null || existing.Approval.CashierSubject != user.Subject) return null;
        var now = clock.GetUtcNow();
        var payout = new ManualRefundPayout(custody.TerminalId, custody.ShiftId, custody.CashSessionId, user.Subject, now);
        var result = completion is null
            ? await store.BeginAsync(existing.Sale, operation, payout, ct)
            : await store.CompleteAsync(existing.Sale, operation, payout, completion.TransferVerified, completion.TransferReference, now, ct);
        return Status(existing.Sale, result);
    }

    private async Task<RestaurantAuthorizationScope> ScopeAsync(Guid organization, Guid branch, Guid order, PosUserContext user, CancellationToken ct)
    {
        if (order == Guid.Empty) throw new ArgumentException("Invalid refund order.");
        return await BranchScopeAsync(organization, branch, user, ct);
    }

    private async Task<RestaurantAuthorizationScope> BranchScopeAsync(Guid organization, Guid branch, PosUserContext user, CancellationToken ct)
    {
        if (organization == Guid.Empty || branch == Guid.Empty) throw new ArgumentException("Invalid refund scope.");
        if (!await membership.HasAccessAsync(organization, user, ct)) throw new UnauthorizedAccessException();
        var scope = await scopes.GetAsync(branch, ct);
        if (scope.OrganizationId != organization || scope.BranchId != branch || scope.RestaurantId == Guid.Empty)
            throw new UnauthorizedAccessException();
        return scope;
    }

    private async Task<OriginalManualSale?> OriginalAsync(RestaurantAuthorizationScope scope, Guid order, PosUserContext user, CancellationToken ct)
    {
        var sale = await sales.ReadAsync(scope, order, user, ct);
        if (sale is null) return null;
        if (sale.OrganizationId != scope.OrganizationId || sale.RestaurantId != scope.RestaurantId || sale.BranchId != scope.BranchId
            || sale.OrderId != order || sale.SettlementId == Guid.Empty || sale.Amount is <= 0 or >= 1_000_000_000_000_000m
            || decimal.Round(sale.Amount, 4) != sale.Amount || sale.Currency != "THB" || sale.PaidAtUtc == default
            || sale.Method is not ("cash" or "promptpay_manual"))
            throw new HttpRequestException("Original refund sale evidence is inconsistent.");
        return new(sale.OrganizationId, sale.RestaurantId, sale.BranchId, sale.OrderId, sale.SettlementId,
            sale.Method == "cash" ? ManualRefundTender.Cash : ManualRefundTender.PromptPay, sale.Amount, sale.Currency, sale.PaidAtUtc);
    }

    private Task<StoredManualRefund?> ReadOperationAsync(RestaurantAuthorizationScope scope, Guid order, Guid operation, CancellationToken ct) =>
        operation == Guid.Empty ? throw new ArgumentException("Invalid refund operation.") : store.ReadAsync(scope, order, operation, ct);

    private async Task<bool> GrantedAsync(PosUserContext user, RestaurantAuthorizationScope scope, string permission, CancellationToken ct)
    {
        var decision = await authorization.DecideAsync(user, scope, permission, ct);
        return decision.Granted && decision.DecisionId != Guid.Empty;
    }

    private static string Method(OriginalManualSale sale) => sale.Tender == ManualRefundTender.Cash ? "cash" : "promptpay_manual";
    private static ManualRefundStatusV1 Status(OriginalManualSale sale, ManualRefundWrite result) =>
        View(sale, result.Refund.Approval, result.Refund.Payout?.StartedAtUtc, result.Refund.Completion?.RecordedAtUtc,
            result.Replayed, result.Refund.Release?.ReleasedAtUtc);
    private static ManualRefundStatusV1 View(OriginalManualSale sale, ManualRefundApproval approval,
        DateTimeOffset? started, DateTimeOffset? completed, bool replayed, DateTimeOffset? released = null) =>
        new(approval.OperationId, sale.OrderId, Method(sale), sale.Currency, approval.Amount, approval.Reason,
            released is not null ? "released" : completed is not null ? "completed" : started is not null ? "started" : "approved",
            approval.ApprovedAtUtc, started, completed, replayed, released);
}
