using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace WinModes.App.Services;

/// <summary>Draws the memory used by AI tools (in GB) on the notification-area icon.</summary>
internal sealed class TrayMeter(Forms.NotifyIcon trayIcon, Icon defaultIcon) : IDisposable
{
    private const int IconSize = 32;
    private const int CornerRadius = 7;
    private const double MbPerGb = 1024;
    // Windows limits a notification-area tooltip to 127 characters.
    private const int MaxTooltipLength = 127;
    private static readonly Color Background = Color.FromArgb(0x7C, 0x5C, 0xFC);

    private Icon? _current;

    public void Show(StatsReading reading)
    {
        var gigabytes = reading.AiMemoryMb / MbPerGb;
        // Two characters fit the icon: "6.2" becomes "6", "12.4" becomes "12"; below 1 GB show one decimal.
        var text = gigabytes >= 1
            ? Math.Round(gigabytes).ToString(CultureInfo.InvariantCulture)
            : gigabytes.ToString("0.0", CultureInfo.InvariantCulture).TrimStart('0');

        var icon = Render(text);
        trayIcon.Icon = icon;
        Release();
        _current = icon;

        var lines = reading.AiTools.Count == 0
            ? "No AI tool is running"
            : string.Join("\n", reading.AiTools.Select(tool =>
                string.Create(CultureInfo.CurrentCulture, $"{tool.Name}: {tool.MemoryMb / MbPerGb:0.0} GB")));
        var tooltip = string.Create(CultureInfo.CurrentCulture, $"WinModes - AI tools {gigabytes:0.0} GB\n{lines}");
        trayIcon.Text = tooltip.Length > MaxTooltipLength ? tooltip[..MaxTooltipLength] : tooltip;
    }

    public void Reset()
    {
        trayIcon.Icon = defaultIcon;
        trayIcon.Text = "WinModes";
        Release();
    }

    public void Dispose() => Release();

    private static Icon Render(string text)
    {
        using var bitmap = new Bitmap(IconSize, IconSize);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var path = new GraphicsPath();
            var diameter = CornerRadius * 2;
            path.AddArc(0, 0, diameter, diameter, 180, 90);
            path.AddArc(IconSize - diameter - 1, 0, diameter, diameter, 270, 90);
            path.AddArc(IconSize - diameter - 1, IconSize - diameter - 1, diameter, diameter, 0, 90);
            path.AddArc(0, IconSize - diameter - 1, diameter, diameter, 90, 90);
            path.CloseFigure();
            using var brush = new SolidBrush(Background);
            graphics.FillPath(brush, path);

            using var font = new Font("Segoe UI", text.Length > 2 ? 12 : 16, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString(text, font, Brushes.White, new RectangleF(0, 0, IconSize, IconSize), format);
        }

        // Icon.FromHandle does not own the handle, so clone it and free the original at once.
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private void Release()
    {
        _current?.Dispose();
        _current = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
