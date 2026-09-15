namespace NexaConnect.Observability;

public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> CurrentValue = new();

    public static string? Current => CurrentValue.Value;

    public static IDisposable Push(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        string? previous = CurrentValue.Value;
        CurrentValue.Value = correlationId;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(string? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            CurrentValue.Value = previous;
            disposed = true;
        }
    }
}
