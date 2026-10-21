using System.IO;
using System.Text.Json;

namespace NexaConnect.POS;

internal interface IPosTokenStore
{
    PosTokenSet? Load();
    void Save(PosTokenSet token);
    void Delete();
}

internal sealed class WindowsTokenStore : IPosTokenStore
{
    private readonly string _path;

    public WindowsTokenStore() : this(null) { }

    internal WindowsTokenStore(string? storageDirectory) => _path = Path.Combine(storageDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexaConnect", "POS"), "tokens.bin");

    public PosTokenSet? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            byte[] protectedBytes = File.ReadAllBytes(_path);
            byte[] plaintext = WindowsDataProtection.Unprotect(protectedBytes);
            return JsonSerializer.Deserialize<PosTokenSet>(plaintext)
                ?? throw new InvalidDataException("Protected POS session metadata is empty.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException or JsonException)
        {
            throw new InvalidDataException("Protected POS session metadata cannot be read. Preserve it and reconcile with the original cashier.", error);
        }
    }

    public void Save(PosTokenSet token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(token);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, WindowsDataProtection.Protect(plaintext));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

}
