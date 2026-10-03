using System.Windows;
using System.Windows.Controls;
using WinModes.App.Services;
using WinModes.Core;
using WinModes.Core.Tuning;

namespace WinModes.App;

internal sealed record PolicyRecoveryEntry(string Id, int Index, string Title, string Target, string Observation, string Reason,
    bool PolicyPresent, bool DisabledSwitch, bool RecoveryRecorded, bool CanRelease, bool CanUndo);

public partial class PolicyRecoveryWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly List<PolicyRecoveryEntry> _entries = [];
    internal (TuneAction Action, string Target)? Request { get; private set; }

    internal PolicyRecoveryWindow(OptimizeSnapshot snapshot)
    {
        InitializeComponent();
        EnvironmentText.Text = Loc.F("Windows edition: {0}; build: {1}. {2}", snapshot.PolicyEnvironment.Edition,
            snapshot.PolicyEnvironment.Build, Loc.T(PolicyRecovery.Explain(snapshot.PolicyEnvironment)));
        foreach (var tweak in ServiceTuning.Catalog.Tweaks)
        foreach (var part in tweak.Parts)
        {
            var observed = snapshot.Observations[tweak.Id][part.Index];
            var journaled = snapshot.JournaledParts.GetValueOrDefault(tweak.Id)?.Contains(part.Index) == true;
            var recovery = snapshot.Records.Any(record => record.Id.Equals(tweak.Id, StringComparison.OrdinalIgnoreCase)
                && record.Values.Any(value => value.WrittenAbsent && tweak.PartOf(value) == part.Index));
            var policy = part.Index < tweak.Values.Count && PolicyRecovery.IsPolicy(tweak.Values[part.Index]);
            var present = policy && observed.RegistryValuePresent;
            var disabled = !observed.CanChange || observed.Applied == true && !journaled;
            if (!present && !disabled && !recovery) continue;
            var supported = policy && PolicyRecovery.Supports(tweak.Values[part.Index], snapshot.PolicyEnvironment);
            var release = supported && observed.CanChange && present && !journaled && snapshot.PolicyEnvironment.CanReleaseValue(tweak.Values[part.Index]);
            var localSource = policy && snapshot.PolicyEnvironment.MayReapply(tweak.Values[part.Index]);
            var reason = !observed.CanChange ? observed.Error ?? "This task is absent on this PC."
                : recovery ? "The policy was removed by WinModes. Undo can restore its recorded value if it is still absent."
                : journaled ? "WinModes has a recorded previous value. Use Undo to restore it."
                : release ? "Documented policy recovery available. The current value will be recorded before removing this one value."
                : present && !snapshot.PolicyEnvironment.CanReleaseValue(tweak.Values[part.Index]) ? PolicyRecovery.Explain(snapshot.PolicyEnvironment)
                : present ? "Policy value present. Its effect depends on the Windows edition and build. No verified recovery is offered for this value yet."
                : "Already set before WinModes. No previous value is recorded; an inverse value cannot be guessed safely.";
            _entries.Add(new(tweak.Id, part.Index, Loc.T(tweak.Title), part.Target, Loc.T(observed.Actual), Loc.T(reason),
                present, disabled, recovery, release, journaled && observed.CanChange));
            if (localSource) _entries[^1] = _entries[^1] with { Reason = Loc.T("This value is also configured in a local Registry.pol file. It may be reapplied. Source recovery is required before a registry-only reset.") };
        }
        EnvironmentText.Text += "\n" + Loc.F("{0} policy values present; {1} recovery options available; {2} matching local policy entries.",
            _entries.Count(entry => entry.PolicyPresent), _entries.Count(entry => entry.CanRelease),
            ServiceTuning.Catalog.Tweaks.SelectMany(tweak => tweak.Values).Count(snapshot.PolicyEnvironment.MayReapply));
        ApplyFilter();
        Loaded += (_, _) => { MaxWidth = SystemParameters.WorkArea.Width; MaxHeight = SystemParameters.WorkArea.Height; };
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) { if (Entries is not null) ApplyFilter(); }
    private void ApplyFilter()
    {
        Entries.ItemsSource = _entries.Where(entry => Filter.SelectedIndex switch
        { 1 => entry.DisabledSwitch, 2 => entry.RecoveryRecorded, _ => entry.PolicyPresent }).ToList();
        SelectionHelp.Text = Loc.T("Select a setting to see its recovery options. Each change requires a separate review.");
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReleaseButton is null) return;
        var entry = Entries.SelectedItem as PolicyRecoveryEntry;
        ReleaseButton.IsEnabled = entry?.CanRelease == true;
        UndoButton.IsEnabled = entry?.CanUndo == true;
        SelectionHelp.Text = entry?.Reason ?? Loc.T("Select a setting to see its recovery options. Each change requires a separate review.");
    }
    private void OnRelease(object sender, RoutedEventArgs e) => Choose(TuneAction.ReleasePolicy);
    private void OnUndo(object sender, RoutedEventArgs e) => Choose(TuneAction.Untweak);
    private void Choose(TuneAction action)
    {
        if (Entries.SelectedItem is not PolicyRecoveryEntry entry || (action == TuneAction.ReleasePolicy ? !entry.CanRelease : !entry.CanUndo)) return;
        Request = (action, $"{entry.Id}#{entry.Index}");
        DialogResult = true;
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
