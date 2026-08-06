namespace NexaConnect.UnitTests;

public sealed class DataGenerationRunnerTests
{
    [Fact]
    public void Parse_requires_an_explicit_command()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => DataGenerationOptions.Parse(["--service", "Catalog"]));
        Assert.Contains("--plan", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_accepts_plan_alias_and_environment_file()
    {
        string environmentFile = Path.GetFullPath(".env");
        DataGenerationOptions options = Assert.IsType<DataGenerationOptions>(
            DataGenerationOptions.Parse(
            [
                "--service", "Catalog", "--import-package", "ImportPackages/CatalogSample", "--dry-run",
                "--environment-file", ".env"
            ]));

        Assert.Equal(DataGenerationCommand.Plan, options.Command);
        Assert.Equal(environmentFile, options.EnvironmentFile);
    }

    [Fact]
    public void Parse_accepts_all_services()
    {
        DataGenerationOptions options = Assert.IsType<DataGenerationOptions>(
            DataGenerationOptions.Parse(["--all", "--import-package", "ImportPackages", "--confirm"]));

        Assert.True(options.AllServices);
        Assert.Null(options.Service);
        Assert.Equal(DataGenerationCommand.Confirm, options.Command);
    }

    [Fact]
    public void Parse_rejects_service_combined_with_all()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => DataGenerationOptions.Parse(
                ["--service", "Catalog", "--all", "--import-package", "ImportPackages", "--plan"]));

        Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_environment_requires_an_explicit_safe_environment()
    {
        string? originalNexa = Environment.GetEnvironmentVariable("NEXACONNECT_ENVIRONMENT");
        string? originalDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        string? originalAspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_ENVIRONMENT", null);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);

            Assert.Throws<DataGenerationException>(DataGenerationApplication.ValidateEnvironment);

            Environment.SetEnvironmentVariable("NEXACONNECT_ENVIRONMENT", "Development");
            DataGenerationApplication.ValidateEnvironment();
        }
        finally
        {
            Environment.SetEnvironmentVariable("NEXACONNECT_ENVIRONMENT", originalNexa);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", originalDotnet);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalAspnet);
        }
    }

}
