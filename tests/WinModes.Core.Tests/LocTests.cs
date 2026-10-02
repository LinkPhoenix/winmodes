using System.Text.RegularExpressions;
using WinModes.Core.Localization;

namespace WinModes.Core.Tests;

/// <summary>
/// The language is never switched here: it is shared by the whole process and other tests read English texts.
/// Whether every text of the sources has a translation is checked by tools/extract-texts.ps1.
/// </summary>
public sealed partial class LocTests
{
    public static TheoryData<string> Translated => [.. Loc.Languages.Select(language => language.Code).Where(code => code != Loc.DefaultLanguage)];

    [Fact]
    public void English_IsTheTextItself()
    {
        Assert.Equal(Loc.DefaultLanguage, Loc.Languages[0].Code);
        Assert.Empty(Loc.Load(Loc.DefaultLanguage));
        Assert.Equal("3 of 5 applied", Loc.F("{0} of {1} applied", 3, 5));
        Assert.Equal("1 session", Loc.N(1, "1 session", "{0} sessions"));
        Assert.Equal("4 sessions", Loc.N(4, "1 session", "{0} sessions"));
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void EveryLanguage_TranslatesTheSameTexts(string code)
    {
        var reference = Loc.Load("fr");
        var table = Loc.Load(code);

        Assert.NotEmpty(table);
        Assert.Empty(reference.Keys.Except(table.Keys));
        Assert.Empty(table.Keys.Except(reference.Keys));
        Assert.DoesNotContain(table, entry => string.IsNullOrWhiteSpace(entry.Value));
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void EveryTranslation_KeepsThePlaceholdersOfItsText(string code)
    {
        static string[] Placeholders(string text) => [.. Placeholder().Matches(text).Select(match => match.Value).Order(StringComparer.Ordinal)];

        var broken = Loc.Load(code).Where(entry => !Placeholders(entry.Key).SequenceEqual(Placeholders(entry.Value))).Select(entry => entry.Key);

        Assert.Empty(broken);
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void EveryTranslation_CanBeFormatted(string code)
    {
        // A stray brace or a format specifier broken by a translation only shows when the text is shown: check them all here.
        var culture = System.Globalization.CultureInfo.GetCultureInfo(code);
        var broken = Loc.Load(code).Where(entry =>
        {
            try
            {
                _ = string.Format(culture, entry.Value, 1.5, 2.5, 3.5, 4.5, 5.5, 6.5);
                return false;
            }
            catch (FormatException)
            {
                return true;
            }
        }).Select(entry => entry.Key);

        Assert.Empty(broken);
    }

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();
}
