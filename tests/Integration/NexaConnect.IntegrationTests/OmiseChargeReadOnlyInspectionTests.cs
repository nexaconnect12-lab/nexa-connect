extern alias PAYMENT;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using OmisePaymentProvider = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.OmisePaymentProvider;
using PaymentProviderOptions = PAYMENT::NexaConnect.Services.Payment.Infrastructure.Providers.PaymentProviderOptions;

namespace NexaConnect.IntegrationTests;

public sealed class OmiseChargeReadOnlyInspectionTests
{
    [OmiseInspectionFact]
    public async Task Existing_test_charge_is_inspected_without_financial_commands()
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri("https://api.omise.co/"), Timeout = TimeSpan.FromSeconds(15) };
        var provider = new OmisePaymentProvider(client, Options.Create(new PaymentProviderOptions
        { OmiseSecretKey = Required("NEXACONNECT_OMISE_TEST_SECRET_KEY") }), NullLogger<OmisePaymentProvider>.Instance);
        decimal amount = decimal.Parse(Required("NEXACONNECT_OMISE_INSPECT_AMOUNT"), System.Globalization.CultureInfo.InvariantCulture);
        var report = await provider.InspectTestChargeAsync(Required("NEXACONNECT_OMISE_INSPECT_CHARGE_ID"), amount, default);
        await File.WriteAllTextAsync(Required("NEXACONNECT_OMISE_INSPECT_REPORT"), JsonSerializer.Serialize(new
        {
            inspectedAtUtc = DateTimeOffset.UtcNow, provider = "Omise", readOnly = true,
            financialCommandsSent = 0, localIntentOwnershipVerified = false, charge = report
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(report.ReadSucceeded, $"Read-only inspection failed. Category={report.FailureCategory ?? "none"}.");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException("Missing setting: " + name);
}

public sealed class OmiseInspectionFactAttribute : FactAttribute
{
    public OmiseInspectionFactAttribute()
    {
        string[] names = ["NEXACONNECT_OMISE_TEST_SECRET_KEY", "NEXACONNECT_OMISE_INSPECT_CHARGE_ID",
            "NEXACONNECT_OMISE_INSPECT_AMOUNT", "NEXACONNECT_OMISE_INSPECT_REPORT"];
        if (Environment.GetEnvironmentVariable("NEXACONNECT_OMISE_READ_ONLY_INSPECTION") != "1"
            || Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT") != "Testing"
            || names.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Requires the read-only Omise inspection runner and an existing test charge.";
    }
}
