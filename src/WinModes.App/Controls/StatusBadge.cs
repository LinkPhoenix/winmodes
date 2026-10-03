using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Media;

namespace WinModes.App.Controls;

/// <summary>A compact status with an icon and readable text; colour is supplementary.</summary>
public sealed class StatusBadge : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(""));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(StatusBadge), new PropertyMetadata(""));
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(Brush), typeof(StatusBadge), new PropertyMetadata(Palette.Neutral,
            (d, _) => ((StatusBadge)d).RefreshTint()));
    private static readonly DependencyPropertyKey TintKey = DependencyProperty.RegisterReadOnly(
        nameof(Tint), typeof(Brush), typeof(StatusBadge), new PropertyMetadata(Brushes.Transparent));
    public static readonly DependencyProperty TintProperty = TintKey.DependencyProperty;

    static StatusBadge() => DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusBadge), new FrameworkPropertyMetadata(typeof(StatusBadge)));

    public StatusBadge()
    {
        Focusable = false;
        IsTabStop = false;
        RefreshTint();
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public Brush Tone { get => (Brush)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public Brush Tint => (Brush)GetValue(TintProperty);
    private void RefreshTint() => SetValue(TintKey, Tone is SolidColorBrush ? Palette.Tint(Tone) : Brushes.Transparent);

    protected override AutomationPeer OnCreateAutomationPeer() => new BadgeAutomationPeer(this);

    private sealed class BadgeAutomationPeer(StatusBadge owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(StatusBadge);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetNameCore() => owner.Text;
        protected override string GetHelpTextCore() => owner.ToolTip as string ?? base.GetHelpTextCore();
    }
}
