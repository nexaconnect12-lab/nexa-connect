using System.Security.Cryptography;
using System.Text;

namespace NexaConnect.POS.Hardware.Drawers;

public enum DrawerCause { ConfirmedCashSale, Manual }
public enum DrawerReason { ChangeRequired, CashCheck, SupervisorAccess }
public enum DrawerOutcome { Disabled, Denied, Cancelled, AlreadyAttempted, Conflict, AuditUnavailable, AuditUnavailableAfterAttempt, Submitted, Unknown }
public enum DrawerReservation { Created, AlreadyAttempted, Conflict }

// A client orchestration request, never proof of payment or authorization.
public sealed record DrawerRequest(Guid OrganizationId, Guid RestaurantId, Guid BranchId, Guid StoreId, Guid TerminalId, Guid ShiftId,
    Guid CashSessionId, string Subject, Guid OperationId, DrawerCause Cause, Guid? OrderId = null,
    DrawerReason? Reason = null)
{
    public void Validate()
    {
        if(new[]{OrganizationId,RestaurantId,BranchId,StoreId,TerminalId,ShiftId,CashSessionId,OperationId}.Any(x=>x==Guid.Empty)
            ||string.IsNullOrWhiteSpace(Subject)||Subject.Length>200||Subject.Any(char.IsControl)
            ||!Enum.IsDefined(Cause)||Reason is { } reason&&!Enum.IsDefined(reason)
            ||Cause==DrawerCause.ConfirmedCashSale&&(OrderId is null||OrderId==Guid.Empty||Reason is not null)
            ||Cause==DrawerCause.Manual&&(Reason is null||OrderId is not null))
            throw new ArgumentException("Invalid drawer request.");
    }
    // Sale keys are original-order based, not retry-operation based.
    public string Key => Hash($"{OrganizationId:N}|{RestaurantId:N}|{BranchId:N}|{StoreId:N}|{TerminalId:N}|{Cause}|{(Cause==DrawerCause.ConfirmedCashSale?OrderId:OperationId):N}");
    public string Fingerprint => Hash($"{OrganizationId:N}|{RestaurantId:N}|{BranchId:N}|{StoreId:N}|{TerminalId:N}|{ShiftId:N}|{CashSessionId:N}|{Subject}|{Cause}|{OrderId:N}|{Reason}");
    private static string Hash(string value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public static class DrawerAvailability
{
    public const string Status="Disabled — hardware and authorization integration pending.";
    public const string Requirements="Confirm the printer model, drawer connection and vendor opening command. Physical opening is unavailable in this build; receipt-printer selection does not enable it.";
}
