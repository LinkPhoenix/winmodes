using System.Windows.Markup;
using WinModes.Core.Localization;

namespace WinModes.App.Localization;

/// <summary>XAML side of <see cref="Loc"/>: <c>Text="{l:T 'Save'}"</c> shows the text in the chosen language.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string text) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(text);
}
