namespace WinModes.Core.Apps;

/// <summary>One AppX/MSIX package installed for the current user, as Windows reports it.</summary>
/// <param name="Name">Package name, for example Microsoft.BingNews.</param>
/// <param name="FullName">Full name including version and architecture; what a removal is asked with.</param>
/// <param name="Family">Package family name, stable across versions.</param>
/// <param name="Version">Version text.</param>
/// <param name="InstallLocation">Folder under WindowsApps where the package files are.</param>
/// <param name="IsFramework">The package is a framework other apps load.</param>
/// <param name="NonRemovable">Windows marks it as part of the system: it cannot be removed.</param>
public sealed record InstalledPackage(
    string Name, string FullName, string Family, string Version, string InstallLocation, bool IsFramework, bool NonRemovable);
