using System.Security.Cryptography;
using System.Text;
using WinModes.Core.Updates;

namespace WinModes.Core.Tests;

public sealed class UpdateSignatureTests
{
    private static readonly byte[] Sums = Encoding.UTF8.GetBytes("aaaa  WinModes-v0.9.0-setup-win-x64.exe\r\nbbbb  WinModes-v0.9.0-portable-win-x64.zip\r\n");

    private static (string Public, string Private) NewKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportSubjectPublicKeyInfoPem(), key.ExportPkcs8PrivateKeyPem());
    }

    private static string Sign(byte[] data, string privatePem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privatePem);
        return Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void ASignature_VerifiesWithTheMatchingPublicKey()
    {
        var (publicKey, privateKey) = NewKey();

        Assert.True(UpdateSignature.Verify(Sums, Sign(Sums, privateKey), publicKey));
        // The file written by the signing tool ends with a newline.
        Assert.True(UpdateSignature.Verify(Sums, Sign(Sums, privateKey) + "\n", publicKey));
    }

    [Fact]
    public void AChangedList_AnotherKey_OrAMangledSignature_AreRefused()
    {
        var (publicKey, privateKey) = NewKey();
        var signature = Sign(Sums, privateKey);
        var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Sums).Replace("aaaa", "cccc", StringComparison.Ordinal));

        Assert.False(UpdateSignature.Verify(changed, signature, publicKey));
        Assert.False(UpdateSignature.Verify(Sums, Sign(Sums, NewKey().Private), publicKey));
        Assert.False(UpdateSignature.Verify(Sums, "not base64 !!", publicKey));
        Assert.False(UpdateSignature.Verify(Sums, Convert.ToBase64String(new byte[64]), publicKey));
        Assert.False(UpdateSignature.Verify(Sums, "", publicKey));
        Assert.False(UpdateSignature.Verify(Sums, signature, "not a key"));
    }

    [Fact]
    public void BuildWithoutAPublicKey_ReportsNone()
    {
        // The key is added by tools/new-update-key.ps1; until then a build checks the checksum only.
        var key = UpdateSignature.EmbeddedPublicKey();

        Assert.True(key is null || key.Contains("PUBLIC KEY", StringComparison.Ordinal));
    }
}
