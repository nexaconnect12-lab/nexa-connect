using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NexaConnect.Observability;

public sealed partial class CorrelationLoggingMiddleware(
    RequestDelegate next,
    ILogger<CorrelationLoggingMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemName = "NexaConnect.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName]);
        context.TraceIdentifier = correlationId;
        context.Items[ItemName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        Stopwatch stopwatch = Stopwatch.StartNew();
        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? string.Empty
        });

        try
        {
            await next(context);
            logger.LogInformation(
                "HTTP {RequestMethod} {RequestPath} completed with {StatusCode} in {ElapsedMilliseconds} ms",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "HTTP {RequestMethod} {RequestPath} failed after {ElapsedMilliseconds} ms",
                context.Request.Method,
                context.Request.Path.Value,
                stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    internal static string ResolveCorrelationId(string? candidate) =>
        candidate is { Length: > 0 and <= 128 } && SafeCorrelationId().IsMatch(candidate)
            ? candidate
            : Guid.NewGuid().ToString("N");

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeCorrelationId();
}
