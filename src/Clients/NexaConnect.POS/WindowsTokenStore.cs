using System.Runtime.InteropServices;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace NexaConnect.POS;

internal sealed class WindowsTokenStore
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
            byte[] plaintext = Unprotect(protectedBytes);
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
        File.WriteAllBytes(_path, Protect(plaintext));
    }

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static byte[] Protect(byte[] plaintext) => CryptographicOperation(
        plaintext,
        NativeMethods.CryptProtectData);

    private static byte[] Unprotect(byte[] protectedBytes) => CryptographicOperation(
        protectedBytes,
        NativeMethods.CryptUnprotectData);

    private static byte[] CryptographicOperation(
        byte[] input,
        NativeMethods.CryptOperation operation)
    {
        var source = new NativeMethods.DataBlob(input);
        try
        {
            if (!operation(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out NativeMethods.DataBlob result))
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            try
            {
                byte[] output = new byte[result.Size];
                Marshal.Copy(result.Data, output, 0, output.Length);
                return output;
            }
            finally
            {
                NativeMethods.LocalFree(result.Data);
            }
        }
        finally
        {
            source.Dispose();
        }
    }

    private static class NativeMethods
    {
        internal delegate bool CryptOperation(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr entropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CryptProtectData(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr entropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr entropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);

        [StructLayout(LayoutKind.Sequential)]
        internal struct DataBlob : IDisposable
        {
            internal int Size;
            internal IntPtr Data;

            internal DataBlob(byte[] bytes)
            {
                Size = bytes.Length;
                Data = Marshal.AllocHGlobal(Size);
                Marshal.Copy(bytes, 0, Data, Size);
            }

            public void Dispose()
            {
                if (Data != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(Data);
                    Data = IntPtr.Zero;
                }
            }
        }
    }
}
