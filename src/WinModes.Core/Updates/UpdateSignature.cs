using System.Reflection;
using System.Security.Cryptography;

namespace WinModes.Core.Updates;

/// <summary>
/// The signature of a release. The SHA256SUMS.txt file of a release lists the checksum of each package; it is signed with a key
/// only the maintainer holds (ECDSA P-256, SHA-256), and the app carries the matching public key. A checksum alone only proves the
/// download is not damaged, since whoever could replace a package could replace the checksum beside it; the signature proves the
/// list comes from the holder of the key. The app checks it when it carries a public key, and then refuses an unsigned release.
/// </summary>
public static class UpdateSignature
{
    /// <summary>The release file that holds the signature of <c>SHA256SUMS.txt</c>, as base64.</summary>
    public const string SignatureFile = "SHA256SUMS.txt.sig";

    private const string PublicKeyResource = "WinModes.Core.Updates.update-public-key.pem";

    /// <summary>The public key built into the app (PEM); null when this build carries none, in which case only the checksum is checked.</summary>
    public static string? EmbeddedPublicKey()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PublicKeyResource);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        var pem = reader.ReadToEnd();
        return string.IsNullOrWhiteSpace(pem) ? null : pem;
    }

    /// <summary>Whether <paramref name="signatureBase64"/> is the signature of <paramref name="data"/> by the owner of <paramref name="publicKeyPem"/>.</summary>
    public static bool Verify(ReadOnlySpan<byte> data, string signatureBase64, string publicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(signatureBase64);
        ArgumentNullException.ThrowIfNull(publicKeyPem);
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            return key.VerifyData(data, Convert.FromBase64String(signatureBase64.Trim()), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
