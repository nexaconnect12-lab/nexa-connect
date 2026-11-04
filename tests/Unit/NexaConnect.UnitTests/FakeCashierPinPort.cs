using NexaConnect.Contracts.POS;
using NexaConnect.POS;
using NexaConnect.Services.POS.Domain.CashierPins;

namespace NexaConnect.UnitTests;

internal sealed class FakeCashierPinPort : ICashierPinPort
{
    public CashierPinCredential Credential { get; } = new();
    public bool Unavailable;
    public bool DelayVerify;
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CashierPinStatusV1 Status => new(Credential.State, Credential.Version, Credential.LockedUntilUtc);
    public Task<CashierPinStatusV1> StatusAsync(string token, CancellationToken ct)
        => Unavailable ? throw new HttpRequestException("Simulated outage") : Task.FromResult(Status);
    public Task<CashierPinResultV1> ChangeAsync(string token, string pin, string? currentPin, long version, CancellationToken ct)
        => Task.FromResult(new CashierPinResultV1(Credential.Change(version, pin, DateTimeOffset.UtcNow, v => v == currentPin), Status));
    public async Task<CashierPinResultV1> VerifyAsync(string token, string pin, long version, CancellationToken ct)
    {
        bool accepted=Credential.Verify(version, DateTimeOffset.UtcNow, v => v == pin);
        if(DelayVerify) { Started.TrySetResult(); await Release.Task; }
        return new(accepted, Status);
    }
}
