namespace NexaConnect.UnitTests;

public sealed class PaymentReviewAcceptanceFixtureOptionsTests
{
    [Fact]
    public void Accepts_only_run_scoped_loopback_databases_and_distinct_subjects()
    {
        var settings=Valid();
        AcceptanceFixtureOptions options=AcceptanceFixtureOptions.Parse(settings);
        Assert.Equal("a".PadLeft(32,'a'),options.RunId);
        Assert.NotEqual(options.ReaderSubjectId,options.ResolverSubjectId);
    }

    [Theory]
    [InlineData("ENABLED","0")]
    [InlineData("RUN_ID","../unsafe")]
    [InlineData("READER_SUBJECT_ID","resolver-subject")]
    [InlineData("ORDER_DB","Host=127.0.0.1;Database=NexaConnect_Order;Username=test;Password=secret")]
    [InlineData("AUTHORIZATION_DB","Host=example.com;Database=nexa_review_it_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_authorization;Username=test;Password=secret")]
    public void Rejects_unsafe_or_non_disposable_settings(string name,string value)
    {
        var settings=Valid();settings["NEXACONNECT_REVIEW_FIXTURE_"+name]=value;
        Assert.Throws<ArgumentException>(()=>AcceptanceFixtureOptions.Parse(settings));
    }

    [Fact]
    public async Task Failure_output_suppresses_secret_values()
    {
        var settings=Valid();settings["NEXACONNECT_REVIEW_FIXTURE_ORDER_DB"]="secret-that-must-not-appear";
        TextWriter previous=Console.Error;using var output=new StringWriter();Console.SetError(output);
        try{Assert.Equal(1,await AcceptanceFixtureApplication.RunAsync(settings));}
        finally{Console.SetError(previous);}
        Assert.DoesNotContain("secret-that-must-not-appear",output.ToString(),StringComparison.Ordinal);
    }

    private static Dictionary<string,string?> Valid()
    {
        const string run="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",prefix="NEXACONNECT_REVIEW_FIXTURE_";
        var result=new Dictionary<string,string?>
        {
            [prefix+"ENABLED"]="1",[prefix+"RUN_ID"]=run,[prefix+"READER_SUBJECT_ID"]="reader-subject",[prefix+"RESOLVER_SUBJECT_ID"]="resolver-subject"
        };
        foreach((string setting,string suffix) in new[]{("PLATFORM_DIRECTORY_DB","platform"),("RESTAURANT_DB","restaurant"),("AUTHORIZATION_DB","authorization"),("ORDER_DB","order")})
            result[prefix+setting]=$"Host=127.0.0.1;Database=nexa_review_it_{run}_{suffix};Username=test;Password=synthetic-secret";
        return result;
    }
}
