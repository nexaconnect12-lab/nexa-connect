using NexaConnect.Contracts.Media;

namespace NexaConnect.Services.Media.Application;

public sealed record GalleryReaderGrant(Guid Id,Guid OrganizationId,Guid BranchId,string Subject,
    DateTimeOffset IssuedAtUtc,DateTimeOffset ExpiresAtUtc);
public interface IGalleryReaderProtector
{
    string Protect(GalleryReaderGrant grant);
    GalleryReaderGrant? Read(string credential);
}
public interface IDelegatedGallerySelection
{
    Task<BranchGallery> ReadAsync(Guid org,Guid branch,string subject,CancellationToken ct);
}

public sealed class GalleryReaders(IBranchGallerySelectionPort callerSelections,IDelegatedGallerySelection delegated,
    IGalleryReaderProtector protector,BranchGalleryDelivery delivery,TimeProvider clock)
{
    public static bool Valid(GalleryReaderGrant? grant,Guid org,Guid branch,DateTimeOffset now)=>grant is not null
        && grant.Id!=Guid.Empty && org!=Guid.Empty && branch!=Guid.Empty && grant.OrganizationId==org && grant.BranchId==branch
        && !string.IsNullOrWhiteSpace(grant.Subject) && grant.Subject.Length<=256 && !grant.Subject.Any(char.IsControl)
        && grant.IssuedAtUtc<=now && grant.ExpiresAtUtc>now && grant.ExpiresAtUtc>grant.IssuedAtUtc
        && grant.ExpiresAtUtc-grant.IssuedAtUtc<=TimeSpan.FromHours(24);

    public async Task<GalleryReaderCredential> IssueAsync(Guid org,Guid branch,string subject,string bearer,CancellationToken ct)
    {
        var now=clock.GetUtcNow();var grant=new GalleryReaderGrant(Guid.NewGuid(),org,branch,subject,now,now.AddHours(24));
        if(!Valid(grant,org,branch,now))throw new UnauthorizedAccessException();
        var selected=await callerSelections.ReadAsync(org,branch,bearer,ct);
        if(selected.OrganizationId!=org || selected.BranchId!=branch)throw new UnauthorizedAccessException();
        return new(protector.Protect(grant),org,branch,grant.IssuedAtUtc,grant.ExpiresAtUtc);
    }
    public async Task<GalleryBundle> ReadAsync(Guid org,Guid branch,string credential,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(credential)||credential.Length>8192)throw new UnauthorizedAccessException();
        var grant=protector.Read(credential);
        if(!Valid(grant,org,branch,clock.GetUtcNow()))throw new UnauthorizedAccessException();
        var selection=await delegated.ReadAsync(org,branch,grant!.Subject,ct);
        var bundle=await delivery.DeliverSelectionAsync(org,branch,selection,ct);
        // Recheck live authority and selection after provider I/O, before granting a new image lease.
        var current=await delegated.ReadAsync(org,branch,grant.Subject,ct);
        if(current.OrganizationId!=org||current.BranchId!=branch||selection.Version!=current.Version
            ||selection.SecondsPerSlide!=current.SecondsPerSlide||!selection.Staff.SequenceEqual(current.Staff)
            ||!selection.Customer.SequenceEqual(current.Customer))throw new InvalidDataException("Gallery changed during delivery.");
        if(!Valid(grant,org,branch,clock.GetUtcNow()))throw new UnauthorizedAccessException();
        return bundle with{ExpiresAtUtc=bundle.ExpiresAtUtc<grant.ExpiresAtUtc?bundle.ExpiresAtUtc:grant.ExpiresAtUtc};
    }
}
