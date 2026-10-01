using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace WinModes.Core.Accounts;

/// <summary>
/// The sign-ins of WinModes, one per account, encrypted for the current Windows user (DPAPI): another user of the PC, or a copy
/// of the file on another PC, cannot read them. Tokens are never written anywhere else and never logged.
/// </summary>
public sealed class AccountVault(string path)
{
    private const int UiForbidden = 0x1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("WinModes accounts v1");

    private readonly Lock _gate = new();

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "accounts.dat");

    /// <summary>The tokens saved for a tool ("Claude" or "Codex"); null when it is not signed in or the file cannot be read.</summary>
    public OAuthTokens? Get(string tool)
    {
        lock (_gate)
        {
            return Read().GetValueOrDefault(tool);
        }
    }

    public void Set(string tool, OAuthTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        lock (_gate)
        {
            var all = Read();
            all[tool] = tokens;
            Write(all);
        }
    }

    public void Remove(string tool)
    {
        lock (_gate)
        {
            var all = Read();
            if (all.Remove(tool))
            {
                Write(all);
            }
        }
    }

    private Dictionary<string, OAuthTokens> Read()
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var plain = Unprotect(Convert.FromBase64String(File.ReadAllText(path)));
            return JsonSerializer.Deserialize<Dictionary<string, OAuthTokens>>(plain) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or JsonException or Win32Exception)
        {
            // Unreadable (another user, damaged): the sign-ins count as lost, and signing in again rewrites the file.
            return [];
        }
    }

    private void Write(Dictionary<string, OAuthTokens> all)
    {
        var protectedBytes = Protect(JsonSerializer.SerializeToUtf8Bytes(all));
        AtomicFile.WriteAllText(path, Convert.ToBase64String(protectedBytes));
    }

    private static byte[] Protect(byte[] plain) => Transform(plain, protect: true);

    private static byte[] Unprotect(byte[] encrypted) => Transform(encrypted, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        var data = ToBlob(input);
        var entropy = ToBlob(Entropy);
        var output = default(Blob);
        try
        {
            var done = protect
                ? CryptProtectData(ref data, "WinModes", ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                : CryptUnprotectData(ref data, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!done)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, output.Length);
            return result;
        }
        finally
        {
            Free(data);
            Free(entropy);
            if (output.Data != IntPtr.Zero)
            {
                LocalFree(output.Data);
            }
        }
    }

    private static Blob ToBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new Blob { Length = bytes.Length, Data = pointer };
    }

    private static void Free(Blob blob)
    {
        if (blob.Data != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
