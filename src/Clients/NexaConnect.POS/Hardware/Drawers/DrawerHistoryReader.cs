using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using NexaConnect.Contracts.POS;
namespace NexaConnect.POS.Hardware.Drawers;

public interface IDrawerHistoryPort
{
    Task<DrawerHistoryPageV1> ListAsync(string subject,DateTimeOffset from,DateTimeOffset to,string? cursor,CancellationToken ct);
    Task<DrawerHistoryItemV1?> ReviewAsync(string subject,Guid operation,CancellationToken ct);
}
public sealed record DrawerHistoryRow(DrawerRequest Context,DateTimeOffset AtUtc,string? LocalStatus,DrawerHistoryItemV1? Server,string? ReviewStatus=null)
{
    public Guid OperationId=>Context.OperationId;
    public string TimeLabel=>AtUtc.UtcDateTime.ToString("u");
    public string CauseLabel=>Context.Cause==DrawerCause.ConfirmedCashSale?"Cash sale":"Manual — "+(Context.Reason switch{DrawerReason.ChangeRequired=>"Change required",DrawerReason.CashCheck=>"Cash check",_=>"Supervisor access"});
    public string LocalLabel=>LocalStatus??"Not available on this PC";
    public string ServerLabel=>(ReviewStatus??Server?.Status) switch{"reserved"=>"Reserved","submitted"=>"Submitted","unknown"=>"Unknown","cancelled"=>"Cancelled","not_found"=>"Not found","facts_conflict"=>"Facts conflict",_=>"Not checked"};
    public string Guidance=>ReviewStatus=="not_found"?"No matching server record found at check time. This does not prove no attempt occurred. Preserve the journal; do not retry the device or collect payment again."
        :ReviewStatus=="facts_conflict"?"Original local and server facts differ. Preserve both observations and investigate; do not reset or repeat the intent."
        :(ReviewStatus??Server?.Status) switch{"submitted"=>"Submission was reported. Physical opening is unverified. Review never opens the drawer or changes payment.","cancelled"=>"Cancellation was reported. The original intent remains consumed; review never renews it.","reserved"=>"Server reservation is consumed with no terminal outcome. Investigate the original attempt; do not submit again.","unknown"=>"The outcome is uncertain. Preserve the original attempt and investigate; do not retry the device or collect again.",_=>"Check the original record to compare observations. This read-only view cannot open, reset or retry a drawer."};
}
public sealed record DrawerHistoryBatch(IReadOnlyList<DrawerHistoryRow> Rows,string? NextCursor,bool LocalAvailable);
public sealed class DrawerHistoryReader(PosClientConfiguration configuration,IDrawerHistoryPort remote,ILocalDrawerHistory local,IDisposable? lifetime=null,ILogger? logger=null):IDisposable
{
    public static IReadOnlyList<DrawerHistoryRow> Merge(IEnumerable<DrawerHistoryRow> previous,IEnumerable<DrawerHistoryRow> next)
    {
        var rows=previous.ToDictionary(x=>x.OperationId);
        foreach(var item in next){if(rows.TryGetValue(item.OperationId,out var old)){
                var server=item.Server??old.Server;var context=item.Server is null?old.Context:item.Context;
                bool conflict=item.Context.Fingerprint!=old.Context.Fingerprint;
                rows[item.OperationId]=new(context,server?.ReservedAtUtc??item.AtUtc,item.LocalStatus??old.LocalStatus,server,conflict?"facts_conflict":item.Server is null?old.ReviewStatus:item.ReviewStatus);
            }else rows.Add(item.OperationId,item);}
        return rows.Values.OrderByDescending(x=>x.AtUtc).ThenBy(x=>x.OperationId).Take(250).ToArray();
    }
    public static DrawerHistoryReader ForTerminal(PosClientConfiguration c,Func<PosTokenSet?> token)
    {var port=new HttpDrawerAuthority(c,token);return new(c,port,new SqliteDrawerIntentStore(Path.Combine(c.GetStorageDirectory(),"drawer-intents.db"),c.Authority),port,port.HistoryLogger);}
    public async Task<DrawerHistoryBatch> LoadAsync(string subject,DateTimeOffset from,DateTimeOffset to,string? cursor,CancellationToken ct)
    {
        var page=await remote.ListAsync(subject,from,to,cursor,ct);ValidatePage(configuration,subject,page,from,to);
        IReadOnlyList<DrawerLocalHistoryEntry> locals=[];bool available=true;
        try{locals=await Task.Run(()=>local.ReadHistory(configuration,subject,from,to),ct);}
        catch(Exception e)when(e is IOException or InvalidDataException or System.Text.Json.JsonException or CryptographicException or SqliteException or UnauthorizedAccessException or ArgumentException or FormatException){available=false;logger?.LogWarning("Drawer history local read unavailable; category {Category}",e.GetType().Name);}
        var rows=new Dictionary<Guid,DrawerHistoryRow>();foreach(var item in locals){ValidateRequest(configuration,subject,item.Request);rows.Add(item.Request.OperationId,new(item.Request,item.ReservedAtUtc,item.LocalStatus,null));}
        foreach(var item in page.Items){var context=Context(configuration,subject,item);rows.TryGetValue(item.OperationId,out var old);bool conflict=old is not null&&old.Context.Fingerprint!=context.Fingerprint;
            rows[item.OperationId]=new(context,item.ReservedAtUtc,old?.LocalStatus,item,conflict?"facts_conflict":null);}
        return new(rows.Values.OrderByDescending(x=>x.AtUtc).ThenBy(x=>x.OperationId).Take(250).ToArray(),page.NextCursor,available);
    }
    public async Task<DrawerHistoryRow> CheckAsync(DrawerHistoryRow row,CancellationToken ct)
    {
        ValidateRequest(configuration,row.Context.Subject,row.Context);
        var item=await remote.ReviewAsync(row.Context.Subject,row.OperationId,ct);
        if(item is null)return row with{ReviewStatus="not_found"};
        var original=Context(configuration,row.Context.Subject,item);
        if(item.OperationId!=row.OperationId||original.Fingerprint!=row.Context.Fingerprint)return row with{ReviewStatus="facts_conflict"};
        return row with{Server=item,ReviewStatus=item.Status};
    }
    public static void ValidatePage(PosClientConfiguration c,string subject,DrawerHistoryPageV1 p,DateTimeOffset from,DateTimeOffset to)
    {
        if(p.OrganizationId!=c.OrganizationId||p.RestaurantId!=c.RestaurantId||p.BranchId!=c.BranchId||p.StoreId!=c.StoreId||p.TerminalId!=c.TerminalId||p.FromUtc!=from||p.ToUtc!=to
            ||p.Items is null||p.Items.Count>25||p.Items.Select(x=>x.OperationId).Distinct().Count()!=p.Items.Count||p.NextCursor?.Length>256)throw new InvalidDataException();
        foreach(var row in p.Items){_ = Context(c,subject,row);if(row.ReservedAtUtc<from||row.ReservedAtUtc>=to)throw new InvalidDataException();}
    }
    private static DrawerRequest Context(PosClientConfiguration c,string subject,DrawerHistoryItemV1 row)
    {
        if(row.Status is not ("reserved" or "submitted" or "unknown" or "cancelled")||row.ExpiresAtUtc-row.ReservedAtUtc!=TimeSpan.FromSeconds(60)||row.ReservedAtUtc>DateTimeOffset.UtcNow.AddSeconds(5)
            ||row.ReportedAtUtc<row.ReservedAtUtc||(row.Status=="reserved")!=(row.ReportedAtUtc is null))throw new InvalidDataException();
        var cause=row.Cause switch{"cash_sale"=>DrawerCause.ConfirmedCashSale,"manual"=>DrawerCause.Manual,_=>throw new InvalidDataException()};
        DrawerReason? reason=row.Reason switch{null=>null,"change_required"=>DrawerReason.ChangeRequired,"cash_check"=>DrawerReason.CashCheck,"supervisor_access"=>DrawerReason.SupervisorAccess,_=>throw new InvalidDataException()};
        var context=new DrawerRequest(c.OrganizationId,c.RestaurantId,c.BranchId,c.StoreId,c.TerminalId,row.ShiftId,row.CashSessionId,subject,row.OperationId,cause,row.OrderId,reason);context.Validate();return context;
    }
    private static void ValidateRequest(PosClientConfiguration c,string subject,DrawerRequest r)
    {r.Validate();if(r.Subject!=subject||r.OrganizationId!=c.OrganizationId||r.RestaurantId!=c.RestaurantId||r.BranchId!=c.BranchId||r.StoreId!=c.StoreId||r.TerminalId!=c.TerminalId)throw new InvalidDataException();}
    public void Dispose()=>lifetime?.Dispose();
}
