using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosInstallerPolicyTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "nexa-package-it-" + Guid.NewGuid().ToString("N"));
    internal static PosClientConfiguration Hosted() => PosCheckoutIntegrationTests.Configuration() with
    {
        Authority = "https://identity.nexa.test/realms/pilot", PosApi = "https://pos.nexa.test/",
        OrderApi = "https://order.nexa.test/", CatalogApi = "https://catalog.nexa.test/", MediaApi = "https://media.nexa.test/",
        Scopes = "openid profile email nexaconnect-api"
    };
    private string Profile(PosClientConfiguration c, string name = "profile.json")
    {
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, name);
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            Identity = new { c.Authority, c.ClientId, c.RedirectUri, c.Scopes },
            Services = new { c.PosApi, c.OrderApi, c.CatalogApi, c.PaymentApi, c.MediaApi },
            Session = new { IdleTimeoutMinutes = c.SessionIdleTimeoutMinutes, AbsoluteTimeoutHours = c.SessionAbsoluteTimeoutHours, TokenRefreshLeadSeconds = c.TokenRefreshLeadSeconds },
            Pos = new { c.OrganizationId, c.RestaurantId, c.BranchId, c.StoreId, c.TerminalId, c.Currency, c.PaymentMethod, c.EnableOmiseTestCheckout, c.PromptPayQrImagePath }
        }));
        return file;
    }
    [Fact]
    public void First_install_requires_no_existing_state_and_does_not_create_it()
    {
        string recovery = Path.Combine(directory, "absent");
        PosInstallationPolicy.Preflight(Profile(Hosted()), null, recovery);
        Assert.False(Directory.Exists(recovery));
        Directory.CreateDirectory(recovery); File.WriteAllText(Path.Combine(recovery, "tokens.bin"), "retained");
        Assert.Throws<InvalidDataException>(() => PosInstallationPolicy.Preflight(Profile(Hosted()), null, recovery));
        Assert.Equal("retained", File.ReadAllText(Path.Combine(recovery, "tokens.bin")));
    }
    [Theory]
    [InlineData("authority")][InlineData("organization")][InlineData("restaurant")][InlineData("branch")]
    [InlineData("store")][InlineData("terminal")][InlineData("currency")][InlineData("method")][InlineData("client")][InlineData("callback")]
    public void Custody_changes_cannot_transfer_retained_work(string change)
    {
        var c = Hosted(); var next = change switch
        {
            "authority" => c with { Authority = "https://identity.nexa.test/realms/other" },
            "organization" => c with { OrganizationId = Guid.NewGuid() }, "restaurant" => c with { RestaurantId = Guid.NewGuid() },
            "branch" => c with { BranchId = Guid.NewGuid() }, "store" => c with { StoreId = Guid.NewGuid() },
            "terminal" => c with { TerminalId = Guid.NewGuid() }, "currency" => c with { Currency = "SGD" },
            "method" => c with { PaymentMethod = "promptpay_manual" }, "client" => c with { ClientId = "admin" },
            _ => c with { RedirectUri = "nexaconnect://callback" }
        };
        Assert.Throws<InvalidDataException>(() => PosInstallationPolicy.AssertSameCustody(c, next));
    }
    [Fact]
    public void Endpoint_relocation_and_session_settings_preserve_identity_and_work()
    {
        var c = Hosted(); var next = c with { OrderApi = "https://replacement.nexa.test/", SessionIdleTimeoutMinutes = 10 };
        PosInstallationPolicy.ValidateHosted(next); PosInstallationPolicy.AssertSameCustody(c, next);
    }
    [Theory]
    [InlineData("secret")][InlineData("duplicate")][InlineData("nested")][InlineData("unknown")]
    public void Installer_profile_never_accepts_credentials_or_ambiguous_settings(string change)
    {
        string file = Profile(Hosted()); string text = File.ReadAllText(file);
        text = change switch
        {
            "secret" => text.Replace("\"Identity\":{", "\"Identity\":{\"ClientSecret\":\"forbidden\","),
            "duplicate" => text.Replace("\"Identity\":{", "\"Identity\":{\"authority\":\"https://wrong.nexa.test\","),
            "nested" => text.Replace("\"Currency\":\"THB\"", "\"Currency\":{}"),
            _ => text[..^1] + ",\"ConnectionStrings\":{\"Password\":\"forbidden\"}}"
        };
        File.WriteAllText(file, text); Assert.Throws<InvalidDataException>(() => PosInstallationPolicy.ReadProfile(file));
    }
    [Theory]
    [InlineData("http")][InlineData("loopback")][InlineData("placeholder")][InlineData("scope")][InlineData("card")][InlineData("qr")]
    public void Unsafe_hosted_profiles_are_rejected(string change)
    {
        var c = Hosted(); var next = change switch
        {
            "http" => c with { PosApi = "http://pos.nexa.test/" }, "loopback" => c with { Authority = "https://localhost/realms/pilot" },
            "placeholder" => c with { MediaApi = "https://media.example.invalid/" }, "scope" => c with { Scopes = c.Scopes + " offline_access" },
            "card" => c with { EnableOmiseTestCheckout = true }, _ => c with { PromptPayQrImagePath = "draft-qr.png" }
        };
        Assert.Throws<InvalidDataException>(() => PosInstallationPolicy.ValidateHosted(next));
    }
    [Fact]
    public void Oversize_profile_and_unmanaged_directory_are_rejected()
    {
        string file = Profile(Hosted()); File.WriteAllText(file, new string(' ', 65537));
        Assert.Throws<InvalidDataException>(() => PosInstallationPolicy.ReadProfile(file));
        Assert.Throws<InvalidDataException>(() => PosInstallationPolicy.AssertManagedDirectory(directory));
    }
    [PosDpapiAcceptanceFact]
    public void Reinstall_and_compatible_upgrade_preserve_actual_encrypted_pending_checkout()
    {
        var c = Hosted(); string recovery = Path.Combine(directory, "recovery");
        var store = new LocalPosStore(recovery, scope: LocalPosScope.From(c));
        new WindowsTokenStore(recovery).Save(new PosTokenSet("", null, DateTimeOffset.MinValue, "Bearer", Subject: "original-cashier", Authority: c.Authority));
        var checkout = PendingCheckout.Create(c, [new(Guid.NewGuid(), 1)]); store.SavePendingCheckout(checkout);
        string db = Path.Combine(recovery, "pos-state.db"); var before = SHA256.HashData(File.ReadAllBytes(db));
        string prior = Profile(c, "prior.json"), next = Profile(c with { OrderApi = "https://replacement.nexa.test/" });
        PosInstallationPolicy.Preflight(next, prior, recovery);
        PosInstallationPolicy.Preflight(next, prior, recovery);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(db)));
        Assert.Equal(checkout.OrderId, store.LoadPendingCheckout()!.OrderId);
        Assert.Equal(checkout.SettlementKey, store.LoadPendingCheckout()!.SettlementKey);
    }
    [PosDpapiAcceptanceFact]
    public void Newer_schema_blocks_upgrade_without_migration_or_cleanup()
    {
        var c = Hosted(); string recovery = Path.Combine(directory, "recovery");
        _ = new LocalPosStore(recovery, scope: LocalPosScope.From(c));
        string db = Path.Combine(recovery, "pos-state.db");
        using (var connection = new SqliteConnection($"Data Source={db};Pooling=False"))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=99"; command.ExecuteNonQuery(); }
        var before = SHA256.HashData(File.ReadAllBytes(db));
        Assert.Throws<InvalidDataException>(() => PosInstallationPolicy.Preflight(Profile(c), Profile(c, "prior.json"), recovery));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(db)));
    }
    [PosDpapiAcceptanceFact]
    public void Actual_encrypted_resume_binding_cannot_move_to_another_identity_realm()
    {
        var c = Hosted(); string recovery = Path.Combine(directory, "recovery"); Directory.CreateDirectory(recovery);
        var tokens = new WindowsTokenStore(recovery);
        tokens.Save(new PosTokenSet("", null, DateTimeOffset.MinValue, "Bearer", Subject: "original-cashier", Authority: c.Authority));
        byte[] before = File.ReadAllBytes(Path.Combine(recovery, "tokens.bin"));
        string prior = Profile(c, "prior.json"), proposed = Profile(c);
        PosInstallationPolicy.Preflight(proposed, prior, recovery);
        var foreign = c with { Authority = "https://identity.nexa.test/realms/another" };
        Assert.Throws<InvalidDataException>(() => PosInstallationPolicy.Preflight(Profile(foreign), Profile(foreign, "claimed-prior.json"), recovery));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(recovery, "tokens.bin")));
        Assert.Equal("original-cashier", tokens.Load()!.Subject);
    }
    [PosDpapiAcceptanceFact]
    public void Missing_cashier_binding_blocks_pending_work_but_not_clean_signed_out_state()
    {
        var c = Hosted(); string recovery = Path.Combine(directory, "recovery");
        var store = new LocalPosStore(recovery, scope: LocalPosScope.From(c));
        string proposed = Profile(c), prior = Profile(c, "prior.json");
        PosInstallationPolicy.Preflight(proposed, prior, recovery);
        store.SavePendingCheckout(PendingCheckout.Create(c, [new(Guid.NewGuid(), 1)]));
        string db = Path.Combine(recovery, "pos-state.db"); var before = SHA256.HashData(File.ReadAllBytes(db));
        Assert.Throws<InvalidDataException>(() => PosInstallationPolicy.Preflight(proposed, prior, recovery));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(db)));
        Assert.NotNull(store.LoadPendingCheckout()); Assert.False(File.Exists(Path.Combine(recovery, "tokens.bin")));
    }
    public void Dispose()
    {
        string full = Path.GetFullPath(directory);
        if (Path.GetDirectoryName(full) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(full).StartsWith("nexa-package-it-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected fixture path.");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }
}
