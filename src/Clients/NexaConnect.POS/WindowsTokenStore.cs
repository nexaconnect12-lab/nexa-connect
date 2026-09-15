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
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexaConnect",
        "POS",
        "tokens.bin");

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
            return JsonSerializer.Deserialize<PosTokenSet>(plaintext);
        }
        catch (Exception) when (File.Exists(_path))
        {
            Delete();
            return null;
        }
    }

    public void Save(PosTokenSet token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(token);
        File.WriteAllBytes(_path, WindowsDataProtection.Protect(plaintext));
    }

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

}
