using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using Wpf.Ui.Controls;

namespace WinModes.App;

/// <summary>
/// Edits one mode: name, power plan, WSL behaviour, services to stop and apps to close.
/// The profile is validated against the protection policy when it is saved.
/// </summary>
public partial class ModeEditorWindow : FluentWindow
{
    private static readonly (string Value, string Label)[] PowerPlans =
    [
        ("", "Leave unchanged"),
        ("balanced", "Balanced"),
        ("high-performance", "High performance"),
        ("ultimate-performance", "Ultimate performance"),
    ];

    private readonly string _mode;
    private readonly JsonNode _profile;
    private readonly ObservableCollection<Item> _services = [];
    private readonly ObservableCollection<Item> _apps = [];

    public ModeEditorWindow(string mode)
    {
        InitializeComponent();
        _mode = mode;
        _profile = AppServices.Library.Load(mode);

        LabelBox.Text = _profile["label"]?.GetValue<string>() ?? mode;
        IntentBox.Text = _profile["intent"]?.GetValue<string>() ?? "";
        Title = Loc.F("Edit {0} mode", LabelBox.Text);
        EditorTitleBar.Title = Title;

        PowerPlan.ItemsSource = PowerPlans.Select(plan => Loc.T(plan.Label)).ToList();
        var currentPlan = _profile["power"]?["plan"]?.GetValue<string>() ?? "";
        PowerPlan.SelectedIndex = Math.Max(0, Array.FindIndex(PowerPlans, plan => plan.Value == currentPlan));
        WslRunning.IsChecked = _profile["wsl"]?["running"]?.GetValue<bool>() ?? true;

        foreach (var stop in _profile["services"]?["stop"]?.AsArray() ?? [])
        {
            _services.Add(new Item(stop!["id"]!.GetValue<string>(), stop["why"]?.GetValue<string>() ?? "", null, stop));
        }

        foreach (var app in _profile["apps"]?["close"]?.AsArray() ?? [])
        {
            _apps.Add(new Item(app!["id"]!.GetValue<string>(), "", app["process"]?.GetValue<string>(), app));
        }

        ServiceList.ItemsSource = _services;
        AppList.ItemsSource = _apps;
        Loaded += async (_, _) => await LoadServiceChoicesAsync();
    }

    private async Task LoadServiceChoicesAsync()
    {
        var services = await Task.Run(SystemMonitor.GetServices);
        // Only running, unprotected services that are not already in the list can be added.
        AddServiceBox.ItemsSource = services
            .Where(service => service.IsRunning && !AppServices.Policy.IsProtectedService(service.Name))
            .Where(service => !_services.Any(item => item.Id.Equals(service.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(service => new ServiceChoice(service.Name, $"{service.DisplayName} ({service.Name})"))
            .ToList();
    }

    private void OnAddService(object sender, RoutedEventArgs e)
    {
        if (AddServiceBox.SelectedItem is ServiceChoice choice && !_services.Any(item => item.Id == choice.Name))
        {
            _services.Add(new Item(choice.Name, Loc.T("Added by the user"), null, null));
            AddServiceBox.SelectedItem = null;
        }
    }

    private void OnAddApp(object sender, RoutedEventArgs e)
    {
        var process = AddAppBox.Text.Trim();
        if (process.Length == 0)
        {
            return;
        }

        if (!process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            process += ".exe";
        }

        // A name only, never a path: the engine closes processes by name.
        if (process != Path.GetFileName(process))
        {
            ErrorText.Text = Loc.T("Enter a process name such as steam.exe, not a path.");
            return;
        }

        if (AppServices.Policy.IsProtectedProcess(process))
        {
            ErrorText.Text = Loc.F("{0} is protected and cannot be closed by a mode.", process);
            return;
        }

        ErrorText.Text = "";
        _apps.Add(new Item(Path.GetFileNameWithoutExtension(process), "", process, null));
        AddAppBox.Text = "";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var label = LabelBox.Text.Trim();
        if (label.Length == 0)
        {
            ErrorText.Text = Loc.T("The mode needs a name.");
            return;
        }

        _profile["label"] = label;
        _profile["intent"] = IntentBox.Text.Trim();

        var keepRunning = WslRunning.IsChecked == true;
        var power = Section("power");
        power["plan"] = PowerPlans[Math.Max(0, PowerPlan.SelectedIndex)].Value;
        var wsl = Section("wsl");
        wsl["running"] = keepRunning;
        wsl["docker"] = keepRunning ? "start" : "quit";

        Section("services")["stop"] = new JsonArray([.. _services.Where(item => item.IsIncluded).Select(item =>
            item.Source?.DeepClone() ?? new JsonObject
            {
                ["id"] = item.Id,
                ["setStartMode"] = "Manual",
                ["stop"] = true,
                ["why"] = item.Why,
            })]);

        Section("apps")["close"] = new JsonArray([.. _apps.Where(item => item.IsIncluded).Select(item =>
            item.Source?.DeepClone() ?? new JsonObject
            {
                ["id"] = item.Id,
                ["process"] = item.Process,
                ["why"] = "Added by the user",
            })]);

        try
        {
            AppServices.Library.Save(_mode, _profile);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ProfileException or IOException or UnauthorizedAccessException)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private JsonObject Section(string name)
    {
        if (_profile[name] is not JsonObject section)
        {
            section = [];
            _profile[name] = section;
        }

        return section;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class Item(string id, string why, string? process, JsonNode? source)
    {
        public string Id { get; } = id;
        public string Why { get; } = why;
        public string? Process { get; } = process;
        public JsonNode? Source { get; } = source;
        public bool IsIncluded { get; set; } = true;
        public string Display => Process is null ? Id : $"{Id} ({Process})";
    }

    private sealed record ServiceChoice(string Name, string Label);
}
