using WinModes.Core.Profiles;
using WinModes.Core.Protection;

namespace WinModes.Core.Tests;

public sealed class ProtectionPolicyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-policy-{Guid.NewGuid():N}");

    public ProtectionPolicyTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ProtectionPolicy Load(string json)
    {
        var path = Path.Combine(_directory, "protected.json");
        File.WriteAllText(path, json);
        return ProtectionPolicy.Load(path);
    }

    [Fact]
    public void Load_RefusesAListWithoutProtectedServices()
    {
        Assert.Throws<ProfileException>(() => Load("""{ "requiredCapabilities": {} }"""));
        Assert.Throws<ProfileException>(() => Load("""{ "requiredCapabilities": { "security": { "services": [] } } }"""));
    }

    [Fact]
    public void Load_RefusesAMissingOrMalformedFile()
    {
        Assert.ThrowsAny<Exception>(() => ProtectionPolicy.Load(Path.Combine(_directory, "absent.json")));
        Assert.ThrowsAny<Exception>(() => Load("{ not json"));
        Assert.ThrowsAny<Exception>(() => Load("{}"));
    }

    [Fact]
    public void IsProtectedService_IgnoresCaseAndPerUserSuffix()
    {
        var policy = Load("""{ "requiredCapabilities": { "security": { "services": ["WinDefend", "CDPUserSvc"] } } }""");

        Assert.True(policy.IsProtectedService("windefend"));
        Assert.True(policy.IsProtectedService("CDPUserSvc_1a2b3c"));
        Assert.False(policy.IsProtectedService("Fax"));
    }

    [Fact]
    public void IsProtectedProcess_MatchesByNameWithOrWithoutExtension()
    {
        var policy = Load("""{ "requiredCapabilities": { "dev": { "services": ["WinDefend"], "processes": ["Code"] } } }""");

        Assert.True(policy.IsProtectedProcess("Code.exe"));
        Assert.True(policy.IsProtectedProcess("code"));
        Assert.False(policy.IsProtectedProcess("notepad.exe"));
    }
}
