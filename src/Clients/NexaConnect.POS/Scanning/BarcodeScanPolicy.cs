namespace NexaConnect.POS;

public static class BarcodeScanPolicy
{
    public static bool Valid(string code)=>code.Length is >=1 and <=64 && code.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '_' or '-');
    public static bool CanScan(bool enabled,bool active,bool checkout,bool signedIn,bool shiftOpen,bool busy,bool pending,bool unsafeDisplay,bool confirmation)=>
        enabled&&active&&checkout&&signedIn&&shiftOpen&&!busy&&!pending&&!unsafeDisplay&&!confirmation;
}
public sealed class BarcodeRepeatGuard
{
    private string? previous;
    private long acceptedAt;
    public bool IsRepeat(string code,long timestampMilliseconds)=>previous==code&&timestampMilliseconds>=acceptedAt&&timestampMilliseconds-acceptedAt<750;
    public void Accepted(string code,long timestampMilliseconds){previous=code;acceptedAt=timestampMilliseconds;}
}
public sealed record PosBarcodeMatch(Guid OrganizationId,Guid BranchId,string Barcode,PosMenuItem Item);
