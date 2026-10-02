using WinModes.Core.Automation;

namespace WinModes.App.Services;

/// <summary>Turning automatic switching on or off from a page, and what is proposed the first time it is turned on.</summary>
internal static class AutoSwitchSetup
{
    private const string DefaultMode = "code";

    /// <summary>The mode the coding tools start: the one of their rules, or Code.</summary>
    public static string CodingMode(IEnumerable<AutoSwitchRule> rules) =>
        rules.FirstOrDefault(rule => AutoSwitchConditions.TryParseTool(rule.Process, out _))?.Mode ?? DefaultMode;

    /// <summary>
    /// Turns the switch on or off. The first time it is turned on with no rule at all, the usual coding tools are added so that
    /// it does something at once: opening one of them starts Code mode.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        var settings = AppSettings.Load();
        var rules = settings.AutoSwitch.Rules;
        if (enabled && rules.Count == 0 && ModeCatalog.Load().Any(entry => entry.Profile.Mode.Equals(DefaultMode, StringComparison.OrdinalIgnoreCase)))
        {
            rules = [.. AutoSwitchConditions.DefaultCodingToolIds.Select(id => new AutoSwitchRule(AutoSwitchConditions.Tool(id), DefaultMode))];
        }

        (settings with { AutoSwitch = settings.AutoSwitch with { Enabled = enabled, Rules = rules } }).Save();
        (System.Windows.Application.Current as App)?.ApplyDisplaySettings();
    }
}
