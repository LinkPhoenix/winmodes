using System.Windows;
using System.Windows.Controls;

namespace WinModes.App.Services;

/// <summary>
/// Greys out the options that live on a reading the user turned off under Monitoring: they would never see anything, so they cannot be
/// chosen, and a tooltip on the disabled control says why.
/// </summary>
internal static class MonitoringGate
{
    /// <summary>The AI tools are the reading behind the tray figure, the memory alerts, idle sessions and the usage history.</summary>
    public static void ApplyAiTools(params FrameworkElement[] controls)
    {
        var read = AppSettings.Load().Monitoring.AiTools;
        Apply(read, controls);
    }

    public static void Apply(bool read, params FrameworkElement[] controls)
    {
        var hint = read ? null : Loc.T("Turned off under Monitoring: AI tools are not read.");
        foreach (var control in controls)
        {
            control.IsEnabled = read;
            control.ToolTip = hint;
            ToolTipService.SetShowOnDisabled(control, true);
        }
    }
}
