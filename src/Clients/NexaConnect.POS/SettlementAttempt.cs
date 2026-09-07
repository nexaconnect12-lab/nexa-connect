namespace NexaConnect.POS;

/// <summary>Persists recovery before network I/O and retains it until verified success.</summary>
public static class SettlementAttempt
{
    public static async Task<T> ExecuteAsync<T>(LocalPendingSettlementState attempt,
        Action<LocalPendingSettlementState> persist, Func<Task<T>> send, Action clear)
    {
        persist(attempt with { OutcomeUncertain = true });
        T result = await send();
        clear();
        return result;
    }
}
