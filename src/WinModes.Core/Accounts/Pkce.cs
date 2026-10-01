using System.Security.Cryptography;
using System.Text;

namespace WinModes.Core.Accounts;

/// <summary>The one-time secrets of an OAuth sign-in with PKCE (RFC 7636): nothing a copied redirect can be replayed with.</summary>
public static class Pkce
{
    private const int VerifierBytes = 32;
    private const int StateBytes = 16;

    /// <summary>A random secret kept by the app until the code is exchanged; 43 characters.</summary>
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(VerifierBytes));

    /// <summary>The value sent in the sign-in address: the SHA-256 of the verifier, so the verifier itself stays private.</summary>
    public static string Challenge(string verifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifier);
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    /// <summary>A random value echoed back by the redirect, to tell the answer to this sign-in from any other request.</summary>
    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(StateBytes));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
