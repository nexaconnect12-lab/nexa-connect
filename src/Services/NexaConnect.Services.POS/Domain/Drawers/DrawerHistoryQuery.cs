using System.Security.Cryptography;
using System.Text;
namespace NexaConnect.Services.POS.Domain.Drawers;

public sealed record DrawerHistoryScope(Guid OrganizationId,Guid RestaurantId,Guid BranchId,Guid StoreId,Guid TerminalId)
{
    public void Validate(string actor){if(new[]{OrganizationId,RestaurantId,BranchId,StoreId,TerminalId}.Any(x=>x==Guid.Empty)||string.IsNullOrWhiteSpace(actor)||actor.Length>200||actor.Any(char.IsControl))throw new ArgumentException();}
}
public sealed record DrawerHistoryQuery(DrawerHistoryScope Scope,string Actor,DateTimeOffset FromUtc,DateTimeOffset ToUtc,
    int Limit,DateTimeOffset? BeforeUtc=null,Guid? BeforeOperation=null)
{
    public static DrawerHistoryQuery Create(DrawerHistoryScope scope,string actor,DateTimeOffset from,DateTimeOffset to,int limit,string? cursor,DateTimeOffset now)
    {
        scope.Validate(actor);
        if(from>=to||to-from>TimeSpan.FromDays(31)
            ||from==default||to>now.AddSeconds(5)||limit is <1 or >50)throw new ArgumentException("A scoped history window of at most 31 days and 1–50 rows is required.");
        var query=new DrawerHistoryQuery(scope,actor,from.ToUniversalTime(),to.ToUniversalTime(),limit);
        if(cursor is null)return query;
        try{
            if(cursor.Length is <1 or >256)throw new FormatException();
            string encoded=cursor.Replace('-','+').Replace('_','/');encoded=encoded.PadRight((encoded.Length+3)/4*4,'=');
            var fields=Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split('|');
            if(fields.Length!=3||fields[0]!=query.Binding||!long.TryParse(fields[1],out var ticks)||!Guid.TryParseExact(fields[2],"N",out var operation)||operation==Guid.Empty)throw new FormatException();
            var before=new DateTimeOffset(ticks,TimeSpan.Zero);if(before<query.FromUtc||before>=query.ToUtc)throw new FormatException();
            return query with{BeforeUtc=before,BeforeOperation=operation};
        }catch(Exception e)when(e is FormatException or ArgumentException){throw new ArgumentException("Invalid drawer history cursor.");}
    }
    // Context binding is not a credential or signature; every page reauthorizes and
    // filters exact actor/scope. A modified cursor cannot grant access to other rows.
    private string Binding=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Scope.OrganizationId:N}/{Scope.RestaurantId:N}/{Scope.BranchId:N}/{Scope.StoreId:N}/{Scope.TerminalId:N}/{Actor}/{FromUtc.UtcTicks}/{ToUtc.UtcTicks}")));
    public string Cursor(DrawerIntent last)=>Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Binding}|{last.ReservedAtUtc.UtcTicks}|{last.Command.OperationId:N}")).TrimEnd('=').Replace('+','-').Replace('/','_');
}
