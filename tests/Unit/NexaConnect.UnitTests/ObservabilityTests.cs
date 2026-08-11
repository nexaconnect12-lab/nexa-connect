using NexaConnect.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NexaConnect.UnitTests;

public sealed class ObservabilityTests
{
    [Theory]
    [InlineData("request-123")]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01")]
    public void Safe_correlation_identifier_is_preserved(string candidate) =>
        Assert.Equal(candidate, CorrelationLoggingMiddleware.ResolveCorrelationId(candidate));

    [Theory]
    [InlineData("")]
    [InlineData("contains spaces")]
    [InlineData("contains\r\nforged-log")]
    public void Unsafe_correlation_identifier_is_replaced(string candidate)
    {
        string resolved = CorrelationLoggingMiddleware.ResolveCorrelationId(candidate);
        Assert.NotEqual(candidate, resolved);
        Assert.True(Guid.TryParseExact(resolved, "N", out _));
    }

    [Fact]
    public void Enabled_otlp_requires_absolute_http_endpoint()
    {
        var options = new ObservabilityOptions { OtlpEnabled = true, OtlpEndpoint = "collector:4317" };
        Assert.Throws<InvalidOperationException>(() => NexaConnectObservabilityExtensions.Validate(options));
    }

    [Fact]
    public void Disabled_otlp_does_not_require_collector() =>
        Assert.Null(NexaConnectObservabilityExtensions.Validate(new ObservabilityOptions()));

    [Fact]
    public async Task Middleware_propagates_correlation_and_logs_only_safe_request_fields()
    {
        var logger = new CapturingLogger<CorrelationLoggingMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/orders";
        context.Request.QueryString = new QueryString("?secret=query-value");
        context.Request.Headers.Authorization = "Bearer token-value";
        context.Request.Headers.Cookie = "session=cookie-value";
        context.Request.Headers[CorrelationLoggingMiddleware.HeaderName] = "safe-correlation-42";

        var middleware = new CorrelationLoggingMiddleware(
            nextContext =>
            {
                nextContext.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);

        Assert.Equal("safe-correlation-42", context.TraceIdentifier);
        Assert.Equal("safe-correlation-42", context.Response.Headers[CorrelationLoggingMiddleware.HeaderName]);
        string log = Assert.Single(logger.Messages);
        Assert.Contains("POST /orders completed with 204", log, StringComparison.Ordinal);
        Assert.DoesNotContain("query-value", log, StringComparison.Ordinal);
        Assert.DoesNotContain("token-value", log, StringComparison.Ordinal);
        Assert.DoesNotContain("cookie-value", log, StringComparison.Ordinal);
        Assert.Contains(logger.Scopes, scope =>
            scope.TryGetValue("CorrelationId", out object? value)
            && Equals(value, "safe-correlation-42")
            && scope.ContainsKey("TraceId"));
    }

    [Fact]
    public async Task Middleware_logs_failure_without_sensitive_request_values_and_rethrows()
    {
        var logger = new CapturingLogger<CorrelationLoggingMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/failure";
        context.Request.QueryString = new QueryString("?secret=query-value");
        context.Request.Headers.Authorization = "Bearer token-value";

        var middleware = new CorrelationLoggingMiddleware(
            _ => throw new InvalidOperationException("controlled failure"),
            logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        string log = Assert.Single(logger.Messages);
        Assert.Contains("GET /failure failed after", log, StringComparison.Ordinal);
        Assert.DoesNotContain("query-value", log, StringComparison.Ordinal);
        Assert.DoesNotContain("token-value", log, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<IReadOnlyDictionary<string, object>> Scopes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object>> properties)
                Scopes.Add(properties.ToDictionary(pair => pair.Key, pair => pair.Value));
            return NullScope.Instance;
        }
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }
}
