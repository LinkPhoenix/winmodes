using System.Text.RegularExpressions;

namespace WinModes.Core.Apps;

/// <summary>
/// Decides which packages WinModes will never remove, and checks every name before it is given to Windows. The protected list covers
/// what the tools of the same kind break by going too far: the Store and App Installer (winget), WebView2 and Edge, the frameworks
/// other apps load, the shell, Windows Security, and the tools of developers.
/// </summary>
public static partial class AppGuard
{
    private static readonly string[] ProtectedPatterns =
    [
        // The Store and winget: App Installer is delivered through the Store, and winget needs both.
        "Microsoft.WindowsStore*", "Microsoft.StorePurchaseApp*", "Microsoft.DesktopAppInstaller*", "Microsoft.Services.Store.Engagement*",
        "Microsoft.WindowsAppRuntime*", "MicrosoftCorporationII.WinAppRuntime*",

        // Frameworks and runtimes that other apps load.
        "Microsoft.VCLibs*", "Microsoft.UI.Xaml*", "Microsoft.NET.*", "Microsoft.WinJS*", "Microsoft.Advertising.Xaml*",

        // WebView2 and Edge: many Windows features and third-party apps (including the AI tools of the user) draw their window with them.
        "Microsoft.MicrosoftEdge*", "Microsoft.Win32WebViewHost*", "Microsoft.Edge*", "Microsoft.WebView2*",

        // The shell and system pieces.
        "Microsoft.Windows.ShellExperienceHost*", "Microsoft.Windows.StartMenuExperienceHost*", "MicrosoftWindows.Client.*",
        "Windows.*", "Microsoft.Windows.CloudExperienceHost*", "Microsoft.Windows.ContentDeliveryManager*", "Microsoft.AAD.BrokerPlugin*",
        "Microsoft.AccountsControl*", "Microsoft.LockApp*", "Microsoft.CredDialogHost*", "Microsoft.Windows.OOBENetworkCaptivePortal*",
        "Microsoft.Windows.OOBENetworkConnectionFlow*", "Microsoft.Windows.PeopleExperienceHost*", "Microsoft.Windows.SecureAssessmentBrowser*",
        "Microsoft.Windows.CapturePicker*", "Microsoft.Windows.NarratorQuickStart*", "Microsoft.Windows.ParentalControls*",
        "Microsoft.Windows.XGpuEjectDialog*", "Microsoft.AsyncTextService*", "Microsoft.BioEnrollment*", "Microsoft.ECApp*",
        "Microsoft.Windows.Apprep.ChxApp*", "NcsiUwpApp*",

        // Security.
        "Microsoft.SecHealthUI*", "Microsoft.Windows.SecHealthUI*", "Microsoft.Windows.Defender*",

        // Tools of developers, and what the AI tools of the user need.
        "Microsoft.WindowsTerminal*", "Microsoft.PowerShell*", "Microsoft.WSL*", "MicrosoftCorporationII.WindowsSubsystemForLinux*",
        "Microsoft.DevHome*", "Microsoft.VisualStudio*", "OpenAI.*", "Anthropic*", "Claude*", "SpotifyAB.*",

        // Image, video and audio codec extensions: Photos, Explorer thumbnails and other apps use them.
        "Microsoft.HEIFImageExtension*", "Microsoft.HEVCVideoExtension*", "Microsoft.AV1VideoExtension*", "Microsoft.VP9VideoExtensions*",
        "Microsoft.WebpImageExtension*", "Microsoft.RawImageExtension*", "Microsoft.WebMediaExtensions*", "Microsoft.MPEG2VideoExtension*",
        "Microsoft.DolbyAudioExtensions*",

        // Xbox identity: Game Pass, the Xbox app and several games sign in through it.
        "Microsoft.XboxIdentityProvider*", "Microsoft.Xbox.TCUI*", "Microsoft.XboxGameCallableUI*",

        // Language and input.
        "Microsoft.LanguageExperiencePack*", "Microsoft.Windows.Holographic*",
    ];

    // A package name is made of letters, digits, dots, dashes and underscores. Nothing else is ever passed to Windows.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex PackageNamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._~-]{0,199}$")]
    private static partial Regex PackageFullNamePattern();

    public static bool IsValidName(string name) => PackageNamePattern().IsMatch(name);

    public static bool IsValidFullName(string fullName) => PackageFullNamePattern().IsMatch(fullName);

    // Vendors whose namespace holds the system itself: a pattern under them needs a real product name.
    private static readonly string[] SystemVendors = ["Microsoft", "MicrosoftWindows", "MicrosoftCorporationII", "Windows"];

    /// <summary>True when the pattern would match something protected, or is too broad to be safe.</summary>
    public static bool IsProtectedPattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var stem = pattern.TrimEnd('*');
        if (ProtectedPatterns.Any(protectedPattern => Matches(protectedPattern, stem) || Matches(pattern, protectedPattern.TrimEnd('*'))))
        {
            return true;
        }

        // An exact name is as precise as it gets, if it is more than a few letters.
        if (!pattern.EndsWith('*'))
        {
            return stem.Length < 5 || stem.EndsWith('.');
        }

        // A wildcard needs a vendor and, under a vendor of the system, a product too ("Microsoft.Bing*", never "Microsoft.*").
        var dot = stem.IndexOf('.', StringComparison.Ordinal);
        if (dot < 1)
        {
            return true;
        }

        return SystemVendors.Contains(stem[..dot], StringComparer.OrdinalIgnoreCase) ? stem.Length - dot - 1 < 3 : stem.Length < 6;
    }

    /// <summary>True when a package must never be offered for removal.</summary>
    public static bool IsProtected(InstalledPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return package.IsFramework || package.NonRemovable || !IsValidName(package.Name) || ProtectedPatterns.Any(pattern => Matches(pattern, package.Name));
    }

    public static bool IsRemovable(InstalledPackage package) => !IsProtected(package);

    /// <summary>Matches from the start of the name, case-insensitively; a trailing * means "and what follows".</summary>
    public static bool Matches(string pattern, string name)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(name);
        return pattern.EndsWith('*')
            ? name.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
            : name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Install folders of packages are under WindowsApps; anything else is refused before a re-registration.</summary>
    public static bool IsPackageFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0 || path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + Path.DirectorySeparatorChar;
        return path.StartsWith(windowsApps, StringComparison.OrdinalIgnoreCase);
    }
}
