namespace NexaConnect.Services.Catalog.Domain.Photos;

public sealed class MenuPhotoConflictException : Exception;
public sealed record MenuPhotoChange(Guid? AssetId, long Version, bool Changed);
public static class MenuPhotoAssignment
{
    public static MenuPhotoChange Change(Guid? previous, long version, Guid? asset, long expected, bool approved)
    {
        if (!approved || expected < 0 || version < 0 || asset == Guid.Empty) throw new ArgumentException("Approve a valid current menu photo selection.");
        if (expected != version) throw new MenuPhotoConflictException();
        return previous == asset ? new(previous, version, false) : new(asset, checked(version + 1), true);
    }
}
