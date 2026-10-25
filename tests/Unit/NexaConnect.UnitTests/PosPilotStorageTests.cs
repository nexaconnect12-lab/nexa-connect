using System.IO;
using System.Text;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosPilotStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "nexa-pilot-storage-" + Guid.NewGuid().ToString("N"));
    private static PosClientConfiguration Pilot() => PosCheckoutIntegrationTests.Configuration() with
    {
        Authority = "http://127.0.0.1:8080/realms/nexa-review-it-" + Guid.NewGuid().ToString("N"),
        UseIsolatedLocalPilotStorage = true
    };

    [Fact]
    public void Default_storage_remains_the_original_terminal_directory()
    {
        Assert.Equal(Path.Combine(root, "NexaConnect", "POS"), PosCheckoutIntegrationTests.Configuration().GetStorageDirectory(root));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Pilot_path_is_stable_scope_bound_and_cannot_hide_work_by_changing_tender_or_endpoints()
    {
        var c = Pilot();
        string path = c.GetStorageDirectory(root);
        Assert.StartsWith(Path.Combine(root, "NexaConnect", "POS-Pilots") + Path.DirectorySeparatorChar, path);
        Assert.Equal(path, (c with { PaymentMethod = "promptpay_manual", PosApi = "http://localhost:33333/" }).GetStorageDirectory(root));
        foreach (var other in new[] { c with { OrganizationId = Guid.NewGuid() }, c with { BranchId = Guid.NewGuid() },
                     c with { StoreId = Guid.NewGuid() }, c with { TerminalId = Guid.NewGuid() } })
            Assert.NotEqual(path, other.GetStorageDirectory(root));
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("https://identity.example.com/realms/nexa-review-it-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("http://localhost:8080/realms/nexa-dev")]
    [InlineData("http://localhost:8080/realms/nexa-review-it-../other")]
    public void Pilot_storage_rejects_remote_or_non_owned_identity(string authority) =>
        Assert.Throws<InvalidDataException>(() => (Pilot() with { Authority = authority }).GetStorageDirectory(root));

    [Theory]
    [InlineData("pos")]
    [InlineData("order")]
    [InlineData("catalog")]
    [InlineData("payment")]
    [InlineData("media")]
    public void Pilot_storage_rejects_any_remote_service(string service)
    {
        var c = Pilot();
        c = service switch
        {
            "pos" => c with { PosApi = "https://service.example.com" },
            "order" => c with { OrderApi = "https://service.example.com" },
            "catalog" => c with { CatalogApi = "https://service.example.com" },
            "payment" => c with { PaymentApi = "https://service.example.com" },
            _ => c with { MediaApi = "https://service.example.com" }
        };
        Assert.Throws<InvalidDataException>(() => c.GetStorageDirectory(root));
    }

    [Fact]
    public void Installer_cannot_switch_an_existing_install_between_default_and_pilot_state()
    {
        var c = Pilot();
        Assert.Throws<InvalidDataException>(() => PosInstallationPolicy.AssertSameCustody(c, c with { UseIsolatedLocalPilotStorage = false }));
        Assert.Throws<InvalidDataException>(() => PosInstallationPolicy.ValidateHosted(c));
    }

    [Fact]
    public void Pilot_restart_preserves_original_checkout_and_leaves_legacy_terminal_bytes_untouched()
    {
        var c = Pilot();
        string legacy = Path.Combine(root, "NexaConnect", "POS");
        Directory.CreateDirectory(legacy);
        byte[] original = Encoding.UTF8.GetBytes("synthetic retained original terminal");
        File.WriteAllBytes(Path.Combine(legacy, "pos-state.db"), original);
        string pilot = c.GetStorageDirectory(root);
        LocalPosDatabase.ValidateExistingScope(c, pilot); // Read-only first launch ignores the unrelated legacy terminal.
        Assert.False(Directory.Exists(pilot));
        var store = new LocalPosStore(pilot, new TestProtector(), LocalPosScope.From(c));
        var shift = new LocalShiftState(Guid.NewGuid(), "PILOT", DateTimeOffset.UtcNow);
        store.SaveActiveShift(shift);
        var checkout = PendingCheckout.Create(c, [new CheckoutLine(Guid.NewGuid(), 1)]);
        store.SavePendingCheckout(checkout);
        var restarted = new LocalPosStore(pilot, new TestProtector(), LocalPosScope.From(c));
        Assert.Equal(shift, restarted.LoadActiveShift());
        Assert.Equal(checkout.OrderId, restarted.LoadPendingCheckout()!.OrderId);
        Assert.Throws<InvalidDataException>(() => new LocalPosStore(pilot, new TestProtector(), LocalPosScope.From(c with { TerminalId = Guid.NewGuid() })));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(legacy, "pos-state.db")));
        Assert.Single(Directory.GetFiles(legacy));
    }

    [PosDpapiAcceptanceFact]
    public void Current_user_pilot_keeps_dpapi_checkout_and_operator_together_without_touching_old_scope()
    {
        var c = Pilot();
        string pilot = c.GetStorageDirectory(root);
        var original = c with { TerminalId = Guid.NewGuid(), UseIsolatedLocalPilotStorage = false };
        string legacy = original.GetStorageDirectory(root);
        var oldStore = new LocalPosStore(LocalPosScope.From(original), legacy);
        var oldCheckout = PendingCheckout.Create(original, [new CheckoutLine(Guid.NewGuid(), 2)]);
        oldStore.SavePendingCheckout(oldCheckout);
        byte[] oldBytes = File.ReadAllBytes(Path.Combine(legacy, "pos-state.db"));
        Assert.Throws<InvalidDataException>(() => LocalPosDatabase.ValidateExistingScope(c, legacy));
        LocalPosDatabase.ValidateExistingScope(c, pilot);
        var current = new LocalPosStore(LocalPosScope.From(c), pilot);
        var checkout = PendingCheckout.Create(c, [new CheckoutLine(Guid.NewGuid(), 1)]);
        current.SavePendingCheckout(checkout);
        var tokens = new WindowsTokenStore(pilot);
        tokens.Save(new PosTokenSet("synthetic", null, DateTimeOffset.UtcNow.AddMinutes(5), "Bearer"));
        LocalPosDatabase.ValidateExistingScope(c, pilot);
        Assert.Equal(checkout.OrderId, new LocalPosStore(LocalPosScope.From(c), pilot).LoadPendingCheckout()!.OrderId);
        Assert.Equal("synthetic", new WindowsTokenStore(pilot).Load()!.AccessToken);
        Assert.Throws<InvalidDataException>(() => LocalPosDatabase.ValidateExistingScope(c with { PaymentMethod = "promptpay_manual" }, pilot));
        Assert.Equal(oldBytes, File.ReadAllBytes(Path.Combine(legacy, "pos-state.db")));
        Assert.Equal(oldCheckout.OrderId, oldStore.LoadPendingCheckout()!.OrderId);
    }

    private sealed class TestProtector : ILocalPayloadProtector
    {
        public byte[] Protect(byte[] value) => value;
        public byte[] Unprotect(byte[] value) => value;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
