using System.Diagnostics;
using System.ServiceProcess;
using System.Runtime.InteropServices;
using WinModes.Core.Tuning;
using System.Xml;
using System.Xml.Linq;

namespace WinModes.Core.Planning;

/// <summary>Reads service, process and WSL state from the local machine. Never changes anything.</summary>
public sealed class WindowsSystemProbe : ISystemProbe
{
    private static readonly string[] PolicyScopes = ["Machine", "User"];
    public WinModes.Core.Tuning.PolicyEnvironment GetPolicyEnvironment()
    {
        try
        {
            using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var version = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            using var join = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
            // Do not request a UPN or read an enrollment identity. Old GUID keys are not proof of MDM registration.
            var mdmResult = IsDeviceRegisteredWithManagement(out var mdm, 0, IntPtr.Zero);
            var joinResult = NetGetJoinInformation(IntPtr.Zero, out var nameBuffer, out var joinStatus);
            if (nameBuffer != IntPtr.Zero) _ = NetApiBufferFree(nameBuffer);
            if (mdmResult != 0 || joinResult != 0 || joinStatus == 0) return PolicyEnvironment.Unknown;
            var managed = mdm || joinStatus == 3 || join?.SubKeyCount > 0;
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var files = false;
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var scope in PolicyScopes)
            {
                var path = Path.Combine(windows, "System32", "GroupPolicy", scope, "Registry.pol");
                if (!PolicyFileExists(path)) continue;
                files = true;
                targets.UnionWith(RegistryPolicyReader.Read(path, scope == "Machine" ? TweakHive.Machine : TweakHive.User));
            }
            var users = Path.Combine(windows, "System32", "GroupPolicyUsers");
            if (Directory.Exists(users))
            {
                foreach (var path in Directory.EnumerateFiles(users, "Registry.pol", SearchOption.AllDirectories))
                {
                    files = true;
                    targets.UnionWith(RegistryPolicyReader.Read(path, TweakHive.User));
                }
            }
            return new(version?.GetValue("EditionID") as string ?? "Unknown", version?.GetValue("CurrentBuildNumber") as string ?? "Unknown", files, managed)
                { LocalPolicyValues = targets, DocumentedPolicyValues = ReadPolicyDefinitions(windows) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or DllNotFoundException or EntryPointNotFoundException or XmlException)
        {
            return WinModes.Core.Tuning.PolicyEnvironment.Unknown;
        }
    }

    private static HashSet<string> ReadPolicyDefinitions(string windows)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folder = Path.Combine(windows, "PolicyDefinitions");
        foreach (var path in Directory.EnumerateFiles(folder, "*.admx", SearchOption.TopDirectoryOnly))
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 2 * 1024 * 1024 });
            var document = XDocument.Load(reader);
            foreach (var policy in document.Descendants().Where(element => element.Name.LocalName == "policy"))
            {
                var key = (string?)policy.Attribute("key");
                var name = (string?)policy.Attribute("valueName");
                if (key is null || name is null || !key.StartsWith(@"SOFTWARE\Policies\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase)) continue;
                var scope = (string?)policy.Attribute("class");
                if (scope is "Machine" or "Both") targets.Add($"{TweakHive.Machine}\\{key.TrimEnd('\\')}\\{name}");
                if (scope is "User" or "Both") targets.Add($"{TweakHive.User}\\{key.TrimEnd('\\')}\\{name}");
            }
        }
        return targets;
    }

    [DllImport("MDMRegistration.dll", ExactSpelling = true)]
    private static extern int IsDeviceRegisteredWithManagement([MarshalAs(UnmanagedType.Bool)] out bool registered, uint upnLength, IntPtr upn);
    [DllImport("Netapi32.dll", ExactSpelling = true)]
    private static extern int NetGetJoinInformation(IntPtr server, out IntPtr name, out int status);
    [DllImport("Netapi32.dll", ExactSpelling = true)]
    private static extern int NetApiBufferFree(IntPtr buffer);

    private static bool PolicyFileExists(string path)
    {
        try { return !File.GetAttributes(path).HasFlag(FileAttributes.Directory); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    public IReadOnlyList<WinModes.Core.Tuning.TweakPartObservation> GetTweakObservations(WinModes.Core.Tuning.Tweak tweak) =>
        WinModes.Core.Tuning.TweakEngine.ObserveParts(tweak, new WinModes.Core.Tuning.WindowsRegistryAccess(), new WinModes.Core.Tuning.WindowsTaskControl());

    // The WSL2 utility VM shows up as this process while any distribution or Docker Desktop runs.
    private const string WslVmProcessName = "vmmemWSL";

    public ServiceState? GetService(string name)
    {
        try
        {
            using var controller = new ServiceController(name);
            var startMode = controller.StartType switch
            {
                System.ServiceProcess.ServiceStartMode.Automatic or System.ServiceProcess.ServiceStartMode.Boot
                    or System.ServiceProcess.ServiceStartMode.System => ServiceStartMode.Automatic,
                System.ServiceProcess.ServiceStartMode.Manual => ServiceStartMode.Manual,
                System.ServiceProcess.ServiceStartMode.Disabled => ServiceStartMode.Disabled,
                _ => ServiceStartMode.Unknown,
            };
            return new ServiceState(startMode, controller.Status == ServiceControllerStatus.Running);
        }
        catch (InvalidOperationException)
        {
            // ServiceController throws this when the service does not exist.
            return null;
        }
    }

    public bool IsProcessRunning(string processFileName)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processFileName));
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public bool IsWslRunning() => IsProcessRunning(WslVmProcessName);
}
