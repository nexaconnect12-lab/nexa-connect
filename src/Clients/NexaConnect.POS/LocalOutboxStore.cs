using System.IO;
using System.Text.Json;

namespace NexaConnect.POS;

public sealed record LocalOutboxOperation(
    Guid OperationId,
    string OperationType,
    string RelativeUri,
    string Method,
    string PayloadJson,
    DateTimeOffset CreatedAtUtc,
    int Attempts,
    DateTimeOffset? LastAttemptAtUtc);

public sealed class LocalOutboxStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexaConnect",
        "POS",
        "outbox.json");

    public IReadOnlyList<LocalOutboxOperation> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<LocalOutboxOperation>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Enqueue(string operationType, string relativeUri, string method, string payloadJson)
    {
        var operations = Load().ToList();
        operations.Add(new LocalOutboxOperation(
            Guid.NewGuid(), operationType, relativeUri, method, payloadJson,
            DateTimeOffset.UtcNow, 0, null));
        Save(operations);
    }

    public void MarkAttempted(Guid operationId)
    {
        Save(Load().Select(operation => operation.OperationId == operationId
            ? operation with { Attempts = operation.Attempts + 1, LastAttemptAtUtc = DateTimeOffset.UtcNow }
            : operation).ToList());
    }

    public void Remove(Guid operationId) => Save(
        Load().Where(operation => operation.OperationId != operationId).ToList());

    private void Save(IReadOnlyList<LocalOutboxOperation> operations)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(operations));
        File.Move(temporary, _path, true);
    }
}
