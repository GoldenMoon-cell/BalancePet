using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace BalancePet.Wpf.Services;

public sealed class DpapiTokenStore
{
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Size; public IntPtr Data; }

    public string Protect(string token)
    {
        if (string.IsNullOrEmpty(token)) return "";
        var bytes = Encoding.UTF8.GetBytes(token);
        var input = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Marshal.Copy(bytes, 0, input.Data, bytes.Length);
        try
        {
            var output = new DataBlob();
            if (!CryptProtectData(ref input, "BalancePet token", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output)) throw new SecurityException("DPAPI protect failed");
            try { var protectedBytes = new byte[output.Size]; Marshal.Copy(output.Data, protectedBytes, 0, output.Size); return Convert.ToBase64String(protectedBytes); }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }

    public string Unprotect(string encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return "";
        var bytes = Convert.FromBase64String(encoded);
        var input = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Marshal.Copy(bytes, 0, input.Data, bytes.Length);
        try
        {
            var output = new DataBlob();
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output)) throw new SecurityException("DPAPI unprotect failed");
            try { var clearBytes = new byte[output.Size]; Marshal.Copy(output.Data, clearBytes, 0, output.Size); return Encoding.UTF8.GetString(clearBytes); }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
}
