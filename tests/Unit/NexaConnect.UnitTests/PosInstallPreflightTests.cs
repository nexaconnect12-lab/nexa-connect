using NexaConnect.POS;
using System.Security.Cryptography;

namespace NexaConnect.UnitTests;

public sealed class PosInstallPreflightTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "nexa-install-it-" + Guid.NewGuid().ToString("N"));

    [PosDpapiAcceptanceFact]
    public void Preflight_preserves_pending_work_and_rejects_scope_or_tender_changes()
    {
        var config = PosCheckoutIntegrationTests.Configuration();
        var store = new LocalPosStore(directory, scope: LocalPosScope.From(config));
        var checkout = PendingCheckout.Create(config, [new(Guid.NewGuid(), 1)]);
        store.SavePendingCheckout(checkout);
        string path = Path.Combine(directory, "pos-state.db");
        var before = SHA256.HashData(File.ReadAllBytes(path));
        LocalPosDatabase.ValidateExistingScope(config, directory);
        Assert.Throws<InvalidDataException>(() => LocalPosDatabase.ValidateExistingScope(config with { TerminalId = Guid.NewGuid() }, directory));
        Assert.Throws<InvalidDataException>(() => LocalPosDatabase.ValidateExistingScope(config with { PaymentMethod = "promptpay_manual" }, directory));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(checkout.OrderId, store.LoadPendingCheckout()!.OrderId);
    }

    [Fact]
    public void Preflight_does_not_create_a_missing_recovery_database()
    {
        LocalPosDatabase.ValidateExistingScope(PosCheckoutIntegrationTests.Configuration(), directory);
        Assert.False(Directory.Exists(directory));
    }

    [Theory]
    [InlineData("another-client", "nexaconnect-pos://oauth/callback")]
    [InlineData("nexaconnect-pos", "https://example.invalid/callback")]
    public void Installed_callback_and_public_client_are_fixed(string client, string callback) =>
        Assert.Throws<InvalidDataException>(() => (PosCheckoutIntegrationTests.Configuration() with { ClientId = client, RedirectUri = callback }).ValidateCheckout());

    [Fact]
    public void Unmigrated_legacy_recovery_blocks_update_without_creating_a_database()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "state.json"), "retained legacy fixture");
        Assert.Throws<InvalidDataException>(() => LocalPosDatabase.ValidateExistingScope(PosCheckoutIntegrationTests.Configuration(), directory));
        Assert.False(File.Exists(Path.Combine(directory, "pos-state.db")));
        Assert.Equal("retained legacy fixture", File.ReadAllText(Path.Combine(directory, "state.json")));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
