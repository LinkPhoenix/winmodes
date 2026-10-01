using WinModes.Core.Profiles;
using WinModes.Core.Protection;

namespace WinModes.Core.Tests;

public sealed class ProfileLibraryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-library-{Guid.NewGuid():N}");
    private readonly ProfileLibrary _library;

    public ProfileLibraryTests()
    {
        Directory.CreateDirectory(_directory);
        var policyPath = Path.Combine(_directory, "protected.json");
        File.WriteAllText(policyPath, """{ "requiredCapabilities": { "security": { "services": ["WinDefend"] } } }""");
        _library = new ProfileLibrary(_directory, ProtectionPolicy.Load(policyPath));
        File.WriteAllText(Path.Combine(_directory, "code.json"),
            """{ "mode": "code", "label": "Code", "futureField": 42, "services": { "stop": [{ "id": "StiSvc" }] } }""");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Duplicate_CreatesANewModeAndKeepsUnknownFields()
    {
        _library.Duplicate("code", "focus", "Focus");

        var copy = _library.Load("focus");
        Assert.Equal("focus", copy["mode"]!.GetValue<string>());
        Assert.Equal("Focus", copy["label"]!.GetValue<string>());
        Assert.Equal(42, copy["futureField"]!.GetValue<int>());
    }

    [Fact]
    public void Import_RejectsAProfileThatStopsAProtectedService()
    {
        var file = Path.Combine(_directory, "incoming.txt");
        File.WriteAllText(file, """{ "mode": "evil", "services": { "stop": [{ "id": "WinDefend" }] } }""");

        Assert.Throws<ProfileException>(() => _library.Import(file));
        Assert.False(File.Exists(Path.Combine(_directory, "evil.json")));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("Has Space")]
    [InlineData("baseline")]
    [InlineData("")]
    public void Save_RejectsUnsafeModeNames(string name) =>
        Assert.Throws<ProfileException>(() => _library.Duplicate("code", name, "X"));

    [Fact]
    public void Delete_RefusesBuiltInModesAndRemovesCustomOnes()
    {
        _library.Duplicate("code", "focus", "Focus");

        Assert.Throws<ProfileException>(() => _library.Delete("code"));
        _library.Delete("focus");

        Assert.False(File.Exists(Path.Combine(_directory, "focus.json")));
    }

    [Fact]
    public void Import_DoesNotOverwriteAnExistingMode()
    {
        var file = Path.Combine(_directory, "incoming.txt");
        File.WriteAllText(file, """{ "mode": "code" }""");

        Assert.Throws<ProfileException>(() => _library.Import(file));
    }
}
