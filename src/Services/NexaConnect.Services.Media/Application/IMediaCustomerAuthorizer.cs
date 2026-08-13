namespace NexaConnect.Services.Media.Application;
public interface IMediaCustomerAuthorizer{Task<bool> IsGrantedAsync(Guid organizationId,string permission,string authorizationHeader,CancellationToken cancellationToken);}
