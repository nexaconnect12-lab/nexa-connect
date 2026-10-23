using NexaConnect.Contracts.Platform;
using NexaConnect.Contracts.RestaurantQr;
using NexaConnect.Services.Restaurant.Application.Branches;
using NexaConnect.Services.Restaurant.Domain.Qr;

namespace NexaConnect.Services.Restaurant.Application.Qr;

public interface IQrSecrets
{
    string Generate();
    string Hash(string token);
}
public interface IQrEntryRepository
{
    Task<QrScope?> ScopeAsync(Guid organization, Guid branch, CancellationToken ct);
    Task<IReadOnlyList<QrEntry>> ListAsync(QrScope scope, CancellationToken ct);
    Task<QrEntry?> GetAsync(QrScope scope, Guid id, CancellationToken ct);
    Task SaveAsync(QrEntry entry, long expectedVersion, string actor, DateTimeOffset now, CancellationToken ct);
    Task<QrGuestContext?> AdmitAsync(string entryHash, string sessionHash, Guid visitor, DateTimeOffset now, CancellationToken ct);
    Task<QrGuestContext?> ContextAsync(string sessionHash, DateTimeOffset now, CancellationToken ct);
    Task EndAsync(string sessionHash, CancellationToken ct);
}
public sealed class QrEntryService(IQrEntryRepository repository, IBranchCustomerAuthorizer authorization, IQrSecrets secrets, TimeProvider clock)
{
    public static bool ValidToken(string? value) => value?.Length == 43 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private async Task<QrScope> Require(Guid org, Guid branch, string bearer, CancellationToken ct)
    {
        var scope = await repository.ScopeAsync(org, branch, ct);
        if (scope is null || !await authorization.IsGrantedAsync(org, scope.RestaurantId, branch,
            ProductPermissions.RestaurantConfigurationManage, bearer, ct)) throw new UnauthorizedAccessException();
        return scope;
    }
    public async Task<IReadOnlyList<QrEntryView>> ListAsync(Guid org, Guid branch, string bearer, CancellationToken ct) =>
        (await repository.ListAsync(await Require(org, branch, bearer, ct), ct)).Select(View).ToArray();
    public async Task<QrEntryIssue> CreateAsync(Guid org, Guid branch, string code, string actor, string bearer, CancellationToken ct)
    {
        var scope = await Require(org, branch, bearer, ct); RequireActor(actor);
        var now = clock.GetUtcNow(); string token = secrets.Generate();
        var entry = new QrEntry(Guid.NewGuid(), scope, QrEntry.NormalizeCode(code), secrets.Hash(token), 1, true, now.AddDays(30));
        await repository.SaveAsync(entry, 0, actor, now, ct); return new(View(entry), token);
    }
    public async Task<QrEntryIssue> ChangeAsync(Guid org, Guid branch, Guid id, long expectedVersion, bool revoke, string actor, string bearer, CancellationToken ct)
    {
        var scope = await Require(org, branch, bearer, ct); RequireActor(actor);
        var entry = await repository.GetAsync(scope, id, ct) ?? throw new QrConflictException();
        var now = clock.GetUtcNow(); string token = revoke ? "" : secrets.Generate();
        if (revoke) entry.Revoke(expectedVersion); else entry.Rotate(expectedVersion, secrets.Hash(token), now);
        await repository.SaveAsync(entry, expectedVersion, actor, now, ct); return new(View(entry), token);
    }
    public async Task<QrSessionIssue?> StartAsync(string token, CancellationToken ct)
    {
        if (!ValidToken(token)) return null;
        string session = secrets.Generate();
        var context = await repository.AdmitAsync(secrets.Hash(token), secrets.Hash(session), Guid.NewGuid(), clock.GetUtcNow(), ct);
        return context is null ? null : new(session, context);
    }
    public Task<QrGuestContext?> ContextAsync(string? token, CancellationToken ct) => ValidToken(token)
        ? repository.ContextAsync(secrets.Hash(token!), clock.GetUtcNow(), ct) : Task.FromResult<QrGuestContext?>(null);
    public Task EndAsync(string? token, CancellationToken ct) => ValidToken(token) ? repository.EndAsync(secrets.Hash(token!), ct) : Task.CompletedTask;
    private static QrEntryView View(QrEntry entry) => new(entry.Id, entry.TableCode, entry.Version, entry.Active, entry.ExpiresAtUtc);
    private static void RequireActor(string actor) { if (string.IsNullOrWhiteSpace(actor) || actor.Length > 200) throw new UnauthorizedAccessException(); }
}
