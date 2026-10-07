using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinModes.App.Services;
using WinModes.Core.Accounts;
using Glyph = Wpf.Ui.Controls.SymbolRegular;
using GlyphIcon = Wpf.Ui.Controls.SymbolIcon;

namespace WinModes.App.Controls;

/// <summary>
/// What the sign-in and sign-out dialogs say before anything happens. Sign-in: the tool's logo, what WinModes reads and keeps, and,
/// for Claude, that Anthropic does not document this sign-in. Sign-out: which account, what is deleted and what is left alone.
/// Built in code to match the cards of the settings pages.
/// </summary>
internal static class AccountPrompt
{
    private const double ContentWidth = 400;
    private const double TileSize = 34;
    private const double LogoSize = 40;

    public static FrameworkElement CreateSignIn(AccountProvider provider, bool warnAboutRisk)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var panel = new StackPanel { Width = ContentWidth };
        panel.Children.Add(Header(provider, Loc.F("WinModes opens your browser so you can sign in to {0}.", provider.DisplayName), detail: null));

        panel.Children.Add(Row(Glyph.Gauge24, Palette.Start, Loc.T("It reads only your plan usage.")));
        panel.Children.Add(Row(Glyph.PersonSwap24, Palette.Container, Loc.T("It uses a session of its own: the sign-in of Claude Code or Codex is not touched.")));
        panel.Children.Add(Row(Glyph.LockClosed24, Palette.Apps, Loc.T("The tokens are kept encrypted for your Windows account. Sign out deletes them.")));

        if (warnAboutRisk)
        {
            var warning = new Border
            {
                Background = Palette.Tint(Palette.Power),
                BorderBrush = Palette.Power,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 6, 0, 0),
                Child = Row(Glyph.Warning24, Palette.Power, Loc.T("Anthropic does not document this sign-in and may restrict it: you use it at your own risk."), tile: false),
            };
            panel.Children.Add(warning);
        }

        return panel;
    }

    /// <summary>The account that is about to be signed out (its email unless privacy mode hides it), and what that deletes and keeps.</summary>
    public static FrameworkElement CreateSignOut(AccountProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var email = !Privacy.Enabled ? AccountSession.Email(provider) : null;
        var panel = new StackPanel { Width = ContentWidth };
        panel.Children.Add(Header(provider, Loc.F("WinModes signs out of {0} on this PC.", provider.DisplayName), string.IsNullOrEmpty(email) ? null : email));

        panel.Children.Add(Row(Glyph.Delete24, Palette.Stop, Loc.T("The saved tokens are deleted from this PC.")));
        panel.Children.Add(Row(Glyph.Gauge24, Palette.Power, Loc.T("The plan and the usage of this account are no longer shown until you sign in again.")));
        panel.Children.Add(Row(Glyph.PersonSwap24, Palette.Container, Loc.T("Your sign-ins in other tools, such as Claude Code or Codex, are not touched.")));

        return panel;
    }

    private static Grid Header(AccountProvider provider, string text, string? detail)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(Tile(ToolIcons.For(provider.Tool), Glyph.Person24, Palette.BrandBrush, LogoSize));

        var intro = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        intro.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeights.SemiBold });
        if (detail is not null)
        {
            intro.Children.Add(new TextBlock { Text = detail, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Palette.Neutral, Margin = new Thickness(0, 2, 0, 0) });
        }

        Grid.SetColumn(intro, 1);
        header.Children.Add(intro);
        return header;
    }

    private static Grid Row(Glyph glyph, Brush accent, string text, bool tile = true)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, tile ? 12 : 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(tile
            ? Tile(null, glyph, accent, TileSize)
            : new GlyphIcon { Symbol = glyph, Foreground = accent, FontSize = 20, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) });
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }

    /// <summary>A rounded tile tinted with the accent, holding the tool's logo when there is one and a glyph otherwise.</summary>
    private static Border Tile(ImageSource? picture, Glyph glyph, Brush accent, double size) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(8),
        Background = Palette.Tint(accent),
        VerticalAlignment = VerticalAlignment.Top,
        Child = picture is not null
            ? new Image { Source = picture, Width = size * 0.6, Height = size * 0.6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : new GlyphIcon { Symbol = glyph, Foreground = accent, FontSize = size * 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };
}
