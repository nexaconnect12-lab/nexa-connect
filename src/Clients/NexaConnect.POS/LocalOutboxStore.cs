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
    DateTimeOffset? LastAttemptAtUtc,
    int? TerminalFailureStatusCode = null,
    DateTimeOffset? TerminalFailureAtUtc = null,
    Guid? TerminalId = null);

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
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The POS offline queue is corrupt and must be recovered before operations continue.",
                exception);
        }
    }

    public LocalOutboxOperation Enqueue(
        string operationType,
        string relativeUri,
        string method,
        string payloadJson,
        Guid? terminalId = null)
    {
        var operations = Load().ToList();
        var operation = new LocalOutboxOperation(
            Guid.NewGuid(), operationType, relativeUri, method, payloadJson,
            DateTimeOffset.UtcNow, 0, null, TerminalId: terminalId);
        operations.Add(operation);
        Save(operations);
        return operation;
    }

    public void MarkAttempted(Guid operationId)
    {
        Save(Load().Select(operation => operation.OperationId == operationId
            ? operation with { Attempts = operation.Attempts + 1, LastAttemptAtUtc = DateTimeOffset.UtcNow }
            : operation).ToList());
    }

    public void Remove(Guid operationId) => Save(
        Load().Where(operation => operation.OperationId != operationId).ToList());

    public void MarkTerminalFailure(Guid operationId, int statusCode)
    {
        Save(Load().Select(operation => operation.OperationId == operationId
            ? operation with
            {
                TerminalFailureStatusCode = statusCode,
                TerminalFailureAtUtc = DateTimeOffset.UtcNow
            }
            : operation).ToList());
    }

    public int RetryTerminalFailures()
    {
        IReadOnlyList<LocalOutboxOperation> operations = Load();
        int retried = operations.Count(operation => operation.TerminalFailureStatusCode is not null);
        Save(operations.Select(operation => operation.TerminalFailureStatusCode is null
            ? operation
            : operation with { TerminalFailureStatusCode = null, TerminalFailureAtUtc = null }).ToList());
        return retried;
    }

    private void Save(IReadOnlyList<LocalOutboxOperation> operations)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(operations));
        File.Move(temporary, _path, true);
    }
}
