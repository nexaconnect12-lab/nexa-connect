using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace NexaConnect.Observability;

public static class NexaConnectObservabilityExtensions
{
    public static WebApplicationBuilder AddNexaConnectObservability(
        this WebApplicationBuilder builder,
        string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        ObservabilityOptions options = builder.Configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        Uri? endpoint = Validate(options);
        string serviceVersion = options.ServiceVersion
            ?? typeof(NexaConnectObservabilityExtensions).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        ResourceBuilder resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: serviceVersion)
            .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]);

        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
        builder.Logging.AddFilter("Microsoft.AspNetCore.HttpLogging", LogLevel.None);
        builder.Logging.AddJsonConsole(console =>
        {
            console.IncludeScopes = true;
            console.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffK";
            console.UseUtcTimestamp = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resourceBuilder => resourceBuilder.AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(instrumentation =>
                    instrumentation.Filter = context => !context.Request.Path.StartsWithSegments("/health"));
                tracing.AddHttpClientInstrumentation();
                if (endpoint is not null) tracing.AddOtlpExporter(exporter => exporter.Endpoint = endpoint);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                if (endpoint is not null) metrics.AddOtlpExporter(exporter => exporter.Endpoint = endpoint);
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(resource);
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;
            if (endpoint is not null) logging.AddOtlpExporter(exporter => exporter.Endpoint = endpoint);
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationPropagationHandler>();

        return builder;
    }

    public static IApplicationBuilder UseNexaConnectRequestLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationLoggingMiddleware>();

    public static IHttpClientBuilder AddNexaConnectCorrelationPropagation(this IHttpClientBuilder builder) =>
        builder.AddHttpMessageHandler<CorrelationPropagationHandler>();

    internal static Uri? Validate(ObservabilityOptions options)
    {
        if (!options.OtlpEnabled) return null;
        if (!Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out Uri? endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Observability:OtlpEndpoint must be an absolute HTTP(S) URI when OTLP export is enabled.");
        }

        return endpoint;
    }
}
