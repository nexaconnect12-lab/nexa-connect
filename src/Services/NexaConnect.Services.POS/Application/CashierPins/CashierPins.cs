using NexaConnect.Contracts.POS;
using NexaConnect.Services.POS.Application.Shifts;
using NexaConnect.Services.POS.Domain.CashierPins;

namespace NexaConnect.Services.POS.Application.CashierPins;

public sealed record CashierPinKey(Guid OrganizationId, string Issuer, string Subject);
public sealed record CashierPinAudit(Guid Id, string Actor, string Action, Guid DecisionId, CashierPinScopeV1 Scope);
public interface ICashierPinRepository
{
    Task<CashierPinStatusV1> ReadAsync(CashierPinKey key, CancellationToken ct);
    Task<CashierPinResultV1> MutateAsync(CashierPinKey key, CashierPinAudit audit, long version,
        Func<CashierPinCredential, bool> transition, CancellationToken ct);
}
public interface ICashierPinProtector
{
    string Create(CashierPinKey key, string pin);
    bool Matches(CashierPinKey key, string protectedVerifier, string pin);
}
public sealed record CashierPinAuthority(string Issuer);

public sealed class CashierPins(ICashierPinRepository repository, ICashierPinProtector protector,
    IRestaurantScopeReader scopes, IShiftStore terminals, IAuthorizationDecisionClient authorization,
    CashierPinAuthority authority, TimeProvider clock)
{
    public async Task<CashierPinStatusV1> StatusAsync(Guid organization, CashierPinScopeV1 scope, PosUserContext user, CancellationToken ct)
    {
        await Authorize(organization, scope, user, false, ct);
        return await repository.ReadAsync(Key(organization, user.Subject), ct);
    }
    public async Task<CashierPinResultV1> ChangeAsync(Guid organization, CashierPinChangeV1 command, PosUserContext user, CancellationToken ct)
    {
        if (!CashierPinCredential.ValidPin(command.NewPin) || command.CurrentPin is not null && !CashierPinCredential.ValidPin(command.CurrentPin)) throw new ArgumentException("Six digits required.");
        var decision = await Authorize(organization, command.Scope, user, false, ct);
        ValidateOperation(command.OperationId, command.ExpectedVersion);
        string verifier = protector.Create(Key(organization, user.Subject), command.NewPin);
        return await repository.MutateAsync(Key(organization, user.Subject), new(command.OperationId, user.Subject, "change", decision, command.Scope),
            command.ExpectedVersion, c => c.Change(command.ExpectedVersion, verifier, clock.GetUtcNow(), v => command.CurrentPin is not null && protector.Matches(Key(organization, user.Subject), v, command.CurrentPin)), ct);
    }
    public async Task<CashierPinResultV1> VerifyAsync(Guid organization, CashierPinVerifyV1 command, PosUserContext user, CancellationToken ct)
    {
        // Malformed input is a failed attempt too; otherwise format changes could evade the durable budget.
        if (command.Pin is null || command.Pin.Length > 64) throw new ArgumentException("Invalid PIN input.");
        var decision = await Authorize(organization, command.Scope, user, false, ct);
        ValidateOperation(command.AttemptId, command.ExpectedVersion);
        return await repository.MutateAsync(Key(organization, user.Subject), new(command.AttemptId, user.Subject, "verify", decision, command.Scope),
            command.ExpectedVersion, c => c.Verify(command.ExpectedVersion, clock.GetUtcNow(), v => CashierPinCredential.ValidPin(command.Pin) && protector.Matches(Key(organization, user.Subject), v, command.Pin)), ct);
    }
    public async Task<CashierPinResultV1> AdministerAsync(Guid organization, CashierPinAdminV1 command, PosUserContext user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Subject) || command.Subject.Length > 256 || command.Subject.Any(char.IsControl) || command.Action is not ("reset" or "revoke")) throw new ArgumentException("Invalid action.");
        var decision = await Authorize(organization, command.Scope, user, true, ct);
        ValidateOperation(command.OperationId, command.ExpectedVersion);
        return await repository.MutateAsync(Key(organization, command.Subject), new(command.OperationId, user.Subject, command.Action, decision, command.Scope),
            command.ExpectedVersion, c => c.Administer(command.ExpectedVersion, command.Action), ct);
    }
    public async Task<CashierPinStatusV1> StaffStatusAsync(Guid organization, CashierPinStaffV1 command, PosUserContext user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Subject) || command.Subject.Length > 256 || command.Subject.Any(char.IsControl)) throw new ArgumentException("Invalid staff reference.");
        await Authorize(organization, command.Scope, user, true, ct);
        return await repository.ReadAsync(Key(organization, command.Subject), ct);
    }
    private CashierPinKey Key(Guid organization, string subject) => new(organization, authority.Issuer, subject);
    private static void ValidateOperation(Guid id, long version) { if (id == Guid.Empty || version < 0) throw new ArgumentException("Operation and version required."); }
    private async Task<Guid> Authorize(Guid organization, CashierPinScopeV1 scope, PosUserContext user, bool manage, CancellationToken ct)
    {
        if (organization == Guid.Empty || scope is null || scope.BranchId == Guid.Empty || scope.StoreId == Guid.Empty || scope.TerminalId == Guid.Empty
            || string.IsNullOrWhiteSpace(user.Subject) || user.Subject.Length > 256 || user.Subject.Any(char.IsControl)) throw new ArgumentException("Invalid cashier scope.");
        var owned = await scopes.GetAsync(scope.BranchId, ct);
        if (owned.OrganizationId != organization || owned.BranchId != scope.BranchId || !await terminals.TerminalMatchesAsync(scope.BranchId, scope.StoreId, scope.TerminalId, owned.RestaurantId, ct)) throw new UnauthorizedAccessException();
        var decision = await authorization.DecideAsync(user, owned, manage ? "pos.cashier-pin.manage" : "pos.cashier-pin.use", ct);
        if (!decision.Granted) throw new UnauthorizedAccessException();
        return decision.DecisionId;
    }
}
