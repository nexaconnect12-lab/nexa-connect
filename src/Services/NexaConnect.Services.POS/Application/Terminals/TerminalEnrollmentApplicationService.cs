using NexaConnect.Services.POS.Application.Shifts;

namespace NexaConnect.Services.POS.Application.Terminals;

public sealed record EnrollTerminalCommand(
    Guid BranchId,
    Guid StoreId,
    Guid TerminalId,
    string Code,
    string DeviceType);

public interface ITerminalStore
{
    Task<bool> EnrollAsync(Guid organizationId, Guid restaurantId, Guid branchId, Guid storeId, Guid terminalId, string code, string deviceType, CancellationToken cancellationToken);
}

public sealed class TerminalEnrollmentApplicationService(
    ITerminalStore terminals,
    IRestaurantScopeReader scopeReader,
    IAuthorizationDecisionClient authorization)
{
    private static readonly HashSet<string> DeviceTypes = ["pos", "kiosk", "kds", "edge"];

    public async Task<bool> EnrollAsync(
        EnrollTerminalCommand command,
        PosUserContext user,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Subject) || string.IsNullOrWhiteSpace(user.AccessToken))
        {
            throw new TerminalEnrollmentAuthorizationException("missing-subject");
        }

        if (command.BranchId == Guid.Empty || command.StoreId == Guid.Empty || command.TerminalId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.Code) || !DeviceTypes.Contains(command.DeviceType))
        {
            throw new TerminalEnrollmentValidationException(
                "Branch, store, terminal, code, and a supported device type are required.");
        }

        RestaurantAuthorizationScope scope;
        try
        {
            scope = await scopeReader.GetAsync(command.BranchId, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            throw new TerminalEnrollmentDependencyException("Restaurant", exception);
        }

        if (scope.BranchId != command.BranchId)
        {
            throw new TerminalEnrollmentAuthorizationException("branch-scope");
        }

        AuthorizationDecision decision;
        try
        {
            decision = await authorization.DecideAsync(user, scope, "pos.terminal.enroll", cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            throw new TerminalEnrollmentDependencyException("Authorization", exception);
        }

        if (!decision.Granted)
        {
            throw new TerminalEnrollmentAuthorizationException("authorization-decision");
        }

        return await terminals.EnrollAsync(
            scope.OrganizationId,
            scope.RestaurantId,
            scope.BranchId,
            command.StoreId,
            command.TerminalId,
            command.Code.Trim(),
            command.DeviceType,
            cancellationToken);
    }
}

public sealed class TerminalEnrollmentValidationException(string message) : Exception(message);

public sealed class TerminalEnrollmentAuthorizationException(string stage)
    : Exception($"POS terminal enrollment authorization denied at {stage}.")
{
    public string Stage { get; } = stage;
}

public sealed class TerminalEnrollmentDependencyException(string dependency, Exception innerException)
    : Exception($"The {dependency} dependency is unavailable.", innerException)
{
    public string Dependency { get; } = dependency;
}
