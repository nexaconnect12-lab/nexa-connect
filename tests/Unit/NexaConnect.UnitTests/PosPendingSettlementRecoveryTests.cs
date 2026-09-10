using System.Security.Cryptography;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosPendingSettlementRecoveryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "nexaconnect-pos-paid-" + Guid.NewGuid().ToString("N"));

    [PosDpapiAcceptanceFact]
    public void Pending_settlement_survives_new_store_instance_with_identical_retry_fields()
    {
        var expected = new LocalPendingSettlementState(Guid.NewGuid(), 120m, "THB", Guid.NewGuid(),
            "promptpay_manual", true, "receipt-reference", true);
        new LocalPosStore(directory).SavePendingSettlement(expected);

        LocalPendingSettlementState? restored = new LocalPosStore(directory).LoadPendingSettlement();

        Assert.Equal(expected, restored);
        byte[] stored = File.ReadAllBytes(Path.Combine(directory, "pos-state.db"));
        Assert.True(stored.AsSpan().IndexOf("receipt-reference"u8) < 0);
    }

    [PosDpapiAcceptanceFact]
    public void Corrupt_pending_settlement_fails_closed_and_is_not_deleted()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "pending-settlement.bin");
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(64));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => new LocalPosStore(directory));

        Assert.Contains("cannot be opened safely", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path));
    }

    [PosDpapiAcceptanceFact]
    public void Clearing_a_verified_settlement_removes_only_the_pending_settlement_file()
    {
        var store = new LocalPosStore(directory);
        store.SaveActiveShift(new LocalShiftState(Guid.NewGuid(), "SHIFT-1", DateTimeOffset.UtcNow));
        store.SavePendingSettlement(new(Guid.NewGuid(), 50m, "THB", Guid.NewGuid()));

        store.ClearPendingSettlement();

        Assert.Null(store.LoadPendingSettlement());
        Assert.NotNull(store.LoadActiveShift());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [PosDpapiAcceptanceFact]
    public void Checkout_restart_retains_order_scope_lines_and_settlement_identity()
    {
        var configuration = PosCheckoutIntegrationTests.Configuration();
        var checkout = PendingCheckout.Create(configuration, [new(Guid.NewGuid(), 2)]);
        new LocalPosStore(directory).SavePendingCheckout(checkout);
        var restored = new LocalPosStore(directory).LoadPendingCheckout()!;
        restored.Validate(configuration);
        Assert.Equal(checkout.OrderId, restored.OrderId);
        Assert.Equal(checkout.SettlementKey, restored.SettlementKey);
        Assert.Equal(checkout.Lines, restored.Lines);
        Assert.Throws<InvalidDataException>(() => restored.Validate(configuration with { BranchId = Guid.NewGuid() }));
    }

    [PosDpapiAcceptanceFact]
    public void Corrupt_checkout_fails_closed_and_preserves_evidence()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "pending-checkout.bin");
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(64));
        Assert.Throws<InvalidDataException>(() => new LocalPosStore(directory));
        Assert.True(File.Exists(path));
    }

}

public sealed class PosDpapiAcceptanceFactAttribute : FactAttribute
{
    public PosDpapiAcceptanceFactAttribute()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("NEXACONNECT_POS_DPAPI_ACCEPTANCE") != "1")
            Skip = "POS DPAPI acceptance requires Windows and explicit opt-in.";
    }
}
