using System.IO;
using System.Text.Json;

namespace NexaConnect.POS;

public sealed record LocalShiftState(Guid ShiftId, string ShiftNumber, DateTimeOffset OpenedAtUtc);

public sealed class LocalPosStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexaConnect",
        "POS",
        "state.json");

    public LocalShiftState? LoadActiveShift()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<LocalShiftState>(File.ReadAllText(_path))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void SaveActiveShift(LocalShiftState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(state));
    }

    public void ClearActiveShift()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
