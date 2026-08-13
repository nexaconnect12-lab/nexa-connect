using System.Text;
using NexaConnect.Contracts.IntegrationEvents;

namespace NexaConnect.Services.Reporting.Application;

public sealed record ActivityRecord(Guid EventId,Guid OrganizationId,string ApplicationCode,string SourceService,string ActorSubjectId,string Action,string ResourceType,string ResourceId,string Outcome,DateTimeOffset OccurredAtUtc,DateTimeOffset ProjectedAtUtc);
public sealed record ActivityPage(IReadOnlyCollection<ActivityRecord> Items,string? NextCursor);
public sealed record ActivityFilter(Guid OrganizationId,string ApplicationCode,string? ActorSubjectId,string? Action,DateTimeOffset? BeforeUtc,Guid? BeforeEventId,int Limit);
public sealed record ProjectAuditActivityCommand(PlatformAuditEventV1 Event,string ApplicationCode,string SourceService);

public interface IActivityProjectionRepository
{
    Task<ActivityPage> QueryAsync(ActivityFilter filter,CancellationToken cancellationToken);
    Task<bool> ProjectAsync(ProjectAuditActivityCommand command,CancellationToken cancellationToken);
}

public sealed class ActivityService(IActivityProjectionRepository repository)
{
    private static readonly HashSet<string> Actions=["customer-membership.changed","branch.created","branch.updated","branch.configuration.updated","media.asset.created","media.asset.deleted"];
    private static readonly HashSet<string> ResourceTypes=["organization-membership","branch","branch-configuration","media-asset"];
    private static readonly HashSet<string> Outcomes=["succeeded","failed","denied"];
    public Task<ActivityPage> QueryAsync(Guid organizationId,string applicationCode,string? actor,string? action,string? cursor,int limit,CancellationToken cancellationToken)
    {
        if(organizationId==Guid.Empty||string.IsNullOrWhiteSpace(applicationCode))throw new ArgumentException("Organization and application are required.");
        if(limit is <1 or >200)throw new ArgumentException("Limit must be between 1 and 200.");
        (DateTimeOffset? before,Guid? eventId)=Decode(cursor);
        return repository.QueryAsync(new(organizationId,applicationCode.Trim().ToLowerInvariant(),Clean(actor),Clean(action),before,eventId,limit),cancellationToken);
    }
    public Task<bool> ProjectAsync(ProjectAuditActivityCommand command,CancellationToken cancellationToken)
    {
        if(command.Event.EventId==Guid.Empty||command.Event.OrganizationId is null||command.Event.OrganizationId==Guid.Empty)throw new ArgumentException("A tenant-scoped audit event is required.");
        string application=command.ApplicationCode.Trim().ToLowerInvariant(),source=command.SourceService.Trim().ToLowerInvariant();var e=command.Event;
        if(application!="nexa_connect")throw new ArgumentException("Audit application is not allowed.");
        if(!Actions.Contains(e.Action)||!ResourceTypes.Contains(e.ResourceType)||!Outcomes.Contains(e.Outcome))throw new ArgumentException("Audit vocabulary is not allowed.");
        if(!ValidIdentifier(e.SubjectId,200)||!ValidIdentifier(e.ResourceId,300)||source.Length>64)throw new ArgumentException("Audit identifiers are invalid.");
        return repository.ProjectAsync(command with{ApplicationCode=application,SourceService=source},cancellationToken);
    }
    public static string Encode(DateTimeOffset occurred,Guid id)=>Convert.ToBase64String(Encoding.UTF8.GetBytes($"{occurred:O}|{id:D}"));
    private static (DateTimeOffset?,Guid?) Decode(string? cursor){if(string.IsNullOrWhiteSpace(cursor))return(null,null);try{string[] parts=Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');if(parts.Length==2&&DateTimeOffset.TryParse(parts[0],out var time)&&Guid.TryParse(parts[1],out var id))return(time,id);}catch(FormatException){}throw new ArgumentException("Cursor is invalid.");}
    private static string? Clean(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static bool ValidIdentifier(string value,int maximum)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=maximum&&value.All(ch=>!char.IsControl(ch));
}
