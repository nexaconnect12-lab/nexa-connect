namespace NexaConnect.POS;

/// <summary>Native display capability; has no access to checkout or authentication.</summary>
internal interface IPosDisplayPlatform
{
    IReadOnlyList<PosMonitor> Read();
    DisplayBounds WindowBounds(nint handle);
    void Place(nint handle, DisplayBounds bounds);
    void MakeCustomerReadOnly(nint handle, Action displayChanged);
}
