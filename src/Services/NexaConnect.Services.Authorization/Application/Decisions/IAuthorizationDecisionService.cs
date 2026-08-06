namespace NexaConnect.Services.Authorization.Application.Decisions;

public sealed record AuthorizationDecision(Guid Id, bool Granted, decimal? EvaluatedLimit);

public interface IAuthorizationDecisionService
{
    Task<AuthorizationDecision> DecideAsync(
        string subjectId,
        Guid organizationId,
        Guid? restaurantId,
        Guid? branchId,
        string permission,
        decimal? amount,
        string? currency,
        CancellationToken cancellationToken);
}
