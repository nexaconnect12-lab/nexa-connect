namespace NexaConnect.IntegrationTests;

[Collection("Order provider payment recovery live acceptance")]
public sealed class OmiseHostedAcceptanceGuardTests
{
    [Theory]
    [InlineData("NEXACONNECT_ENVIRONMENT", "Production", true)]
    [InlineData("NEXACONNECT_ORDER_PROVIDER_RECOVERY_ADAPTER", "Other", true)]
    [InlineData("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL", "https://example.invalid/", true)]
    [InlineData("NEXACONNECT_OMISE_TEST_SECRET_KEY", "skey_live_aaaaaaaaaaaa", true)]
    [InlineData("NEXACONNECT_OMISE_AUTHORIZATION_RESPONSE_TEST_TOKEN", "", true)]
    [InlineData("NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO", "intent_created", true)]
    [InlineData("NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY", "USD", true)]
    [InlineData("NEXACONNECT_ORDER_PROVIDER_RECOVERY_STAGE", "verify", true)]
    [InlineData("NEXACONNECT_ORDER_PROVIDER_RECOVERY_LIVE_ACCEPTANCE", "0", true)]
    [InlineData("NEXACONNECT_ENVIRONMENT", "Testing", false)]
    [InlineData("NEXACONNECT_ORDER_PROVIDER_RECOVERY_CARD_HANDOFF", "1", false)]
    [InlineData("NEXACONNECT_ORDER_PROVIDER_RECOVERY_CARD_HANDOFF", "0", true)]
    public void Financial_stage_requires_guarded_test_account_context(string name, string value, bool skipped)
    {
        // Synthetic credentials only: constructing a discovery attribute cannot send provider requests.
        var settings = new Dictionary<string, string>
        {
            ["NEXACONNECT_ENVIRONMENT"] = "Testing",
            ["NEXACONNECT_ORDER_PROVIDER_RECOVERY_ADAPTER"] = "Omise",
            ["NEXACONNECT_ORDER_PROVIDER_RECOVERY_STAGE"] = "arm",
            ["NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO"] = "authorization_response",
            ["NEXACONNECT_ORDER_PROVIDER_RECOVERY_CARD_HANDOFF"] = "0",
            ["NEXACONNECT_OMISE_INTENT_CREATED_TEST_TOKEN"] = "tokn_test_bbbbbbbbbbbb",
            ["NEXACONNECT_ORDER_PROVIDER_RECOVERY_LIVE_ACCEPTANCE"] = "1",
            ["NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_URL"] = "https://api.omise.co/",
            ["NEXACONNECT_OMISE_TEST_SECRET_KEY"] = "skey_test_aaaaaaaaaaaa",
            ["NEXACONNECT_OMISE_AUTHORIZATION_RESPONSE_TEST_TOKEN"] = "tokn_test_aaaaaaaaaaaa",
            ["NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_API_KEY"] = "unused",
            ["NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_AMOUNT"] = "50",
            ["NEXACONNECT_PAYMENT_PROVIDER_SANDBOX_CURRENCY"] = "THB"
        };
        foreach (string suffix in new[] { "ORDER_DB", "PAYMENT_DB", "RABBITMQ", "HOST_DLL", "PAYMENT_HOST_DLL", "CONTROL" })
            settings["NEXACONNECT_ORDER_PROVIDER_RECOVERY_" + suffix] = "acceptance-fixture";
        if (name == "NEXACONNECT_ORDER_PROVIDER_RECOVERY_CARD_HANDOFF") settings["NEXACONNECT_ORDER_PROVIDER_RECOVERY_SCENARIO"] = "intent_created";
        var saved = settings.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var setting in settings) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            Environment.SetEnvironmentVariable(name, value);
            Assert.Equal(skipped, new OrderProviderRecoveryLiveFactAttribute("arm").Skip is not null);
        }
        finally
        {
            foreach (var setting in saved) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }
    }
}
