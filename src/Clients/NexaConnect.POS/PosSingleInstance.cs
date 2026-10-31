namespace NexaConnect.POS;

/// <summary>The lease lives on the UI thread until application exit, not window hiding.</summary>
internal sealed class PosSingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private PosSingleInstance(Mutex mutex) => this.mutex = mutex;

    internal static PosSingleInstance? TryAcquire(string name)
    {
        var mutex = new Mutex(false, name);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (acquired) return new(mutex);
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
