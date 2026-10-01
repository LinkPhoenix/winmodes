using WinModes.App.Services;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;

namespace WinModes.App;

/// <summary>
/// Services shared with pages. The navigation view creates pages with a parameterless constructor,
/// so they read their dependencies here instead of receiving them.
/// </summary>
internal static class AppServices
{
    private static ProfileStore? _store;
    private static ModePlanner? _planner;
    private static ProtectionPolicy? _policy;
    private static ModeSwitcher? _switcher;

    public static ProfileStore Store => _store ?? throw new InvalidOperationException("App services are not initialized.");
    public static ModePlanner Planner => _planner ?? throw new InvalidOperationException("App services are not initialized.");

    public static ProtectionPolicy Policy => _policy ?? throw new InvalidOperationException("App services are not initialized.");

    public static ModeSwitcher Switcher => _switcher ?? throw new InvalidOperationException("App services are not initialized.");

    public static string ProfilesDirectory { get; private set; } = "";

    public static void Initialize(ProfileStore store, ModePlanner planner, ProtectionPolicy policy, string profilesDirectory)
    {
        _store = store;
        _planner = planner;
        _policy = policy;
        _switcher = new ModeSwitcher(policy);
        ProfilesDirectory = profilesDirectory;
    }
}
