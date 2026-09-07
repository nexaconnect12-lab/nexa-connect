using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ComponentModel;

namespace NexaConnect.POS;

internal static class WindowsDataProtection
{
    internal static byte[] Protect(byte[] plaintext) => CryptographicOperation(plaintext, NativeMethods.CryptProtectData);
    internal static byte[] Unprotect(byte[] protectedBytes) => CryptographicOperation(protectedBytes, NativeMethods.CryptUnprotectData);

    private static byte[] CryptographicOperation(byte[] input, NativeMethods.CryptOperation operation)
    {
        var source = new NativeMethods.DataBlob(input);
        try
        {
            const uint CryptProtectUiForbidden = 0x1;
            if (!operation(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out NativeMethods.DataBlob result))
            {
                int error = Marshal.GetLastWin32Error();
                throw new CryptographicException(new Win32Exception(error).Message);
            }
            try
            {
                byte[] output = new byte[result.Size];
                Marshal.Copy(result.Data, output, 0, output.Length);
                return output;
            }
            finally { NativeMethods.LocalFree(result.Data); }
        }
        finally { source.Dispose(); }
    }

    private static class NativeMethods
    {
        internal delegate bool CryptOperation(ref DataBlob dataIn, IntPtr description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, uint flags, out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CryptProtectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, uint flags, out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, uint flags, out DataBlob dataOut);

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
                if (Data == IntPtr.Zero) return;
                Marshal.FreeHGlobal(Data);
                Data = IntPtr.Zero;
            }
        }
    }
}
