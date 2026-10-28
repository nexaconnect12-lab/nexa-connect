using NexaConnect.Contracts.Media;

namespace NexaConnect.Services.Restaurant.Application.Configuration;

public interface IGalleryReaderAuthorizer
{
    Task<bool> GrantedAsync(Guid org,Guid restaurant,Guid branch,string subject,string workloadBearer,CancellationToken ct);
}

public sealed class GalleryReaderSelection(IBranchGalleryRepository repository,IGalleryReaderAuthorizer authorizer)
{
    public async Task<BranchGallery> ReadAsync(Guid org,Guid branch,string subject,string workloadBearer,CancellationToken ct)
    {
        if(org==Guid.Empty || branch==Guid.Empty || string.IsNullOrWhiteSpace(subject)
            || subject.Length>256 || subject.Any(char.IsControl))throw new UnauthorizedAccessException();
        var restaurant=await repository.RestaurantAsync(org,branch,ct);
        if(restaurant is null || restaurant==Guid.Empty
            || !await authorizer.GrantedAsync(org,restaurant.Value,branch,subject,workloadBearer,ct))throw new UnauthorizedAccessException();
        return await repository.GetAsync(org,branch,ct)??throw new KeyNotFoundException();
    }
}
