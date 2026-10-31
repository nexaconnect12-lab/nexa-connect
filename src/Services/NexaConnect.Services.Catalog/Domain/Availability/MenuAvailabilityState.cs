namespace NexaConnect.Services.Catalog.Domain.Availability;

public sealed record MenuAvailabilityState
{
    public bool Available { get; }
    public long Version { get; }
    public MenuAvailabilityState(bool available, long version)
    {
        if (version < 0) throw new ArgumentException("Invalid availability version.");
        Available = available; Version = version;
    }
    public MenuAvailabilityState Change(bool available, long expectedVersion)
    {
        if (expectedVersion < 0) throw new ArgumentException("Invalid expected version.");
        if (expectedVersion != Version) throw new MenuAvailabilityConflictException();
        if (available == Available) return this;
        if (Version == long.MaxValue) throw new MenuAvailabilityConflictException();
        return new(available, Version + 1);
    }
}
public sealed class MenuAvailabilityConflictException : Exception;
