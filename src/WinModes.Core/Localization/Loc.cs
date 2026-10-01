using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace WinModes.Core.Localization;

/// <summary>A language the interface can be shown in.</summary>
public sealed record Language(string Code, string Name);

/// <summary>
/// Translates the interface. The English text is the key: a text with no translation is shown in English, so
/// a missing entry never breaks a screen. The language is chosen once, at startup; until then, and in the
/// command line and the elevated helper, everything stays in English.
/// </summary>
public static class Loc
{
    public const string DefaultLanguage = "en";

    private static IReadOnlyDictionary<string, string> _table = new Dictionary<string, string>();

    /// <summary>Each language under its own name, English first.</summary>
    public static IReadOnlyList<Language> Languages { get; } =
    [
        new(DefaultLanguage, "English"),
        new("fr", "Français"),
        new("es", "Español"),
        new("it", "Italiano"),
    ];

    /// <summary>Code of the language in use.</summary>
    public static string Current { get; private set; } = DefaultLanguage;

    /// <summary>Switches to a language; an unknown code falls back to English.</summary>
    public static void Use(string? code)
    {
        var language = Languages.FirstOrDefault(known => string.Equals(known.Code, code, StringComparison.OrdinalIgnoreCase))?.Code ?? DefaultLanguage;
        _table = language == DefaultLanguage ? new Dictionary<string, string>() : Load(language);
        Current = language;
    }

    /// <summary>The translations of one language, as shipped; empty for English.</summary>
    public static IReadOnlyDictionary<string, string> Load(string code)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"WinModes.Core.Localization.{code}.json");
        return stream is null
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }

    /// <summary>The text in the current language.</summary>
    public static string T(string text) => _table.TryGetValue(text, out var translated) ? translated : text;

    /// <summary>A translated text with its {0}, {1}… placeholders filled in.</summary>
    public static string F(string format, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, T(format), arguments);

    /// <summary>The same, with the numbers and dates written for the given culture.</summary>
    public static string In(CultureInfo culture, string format, params object?[] arguments) => string.Format(culture, T(format), arguments);

    /// <summary>Chooses between the singular and the plural text, then fills {0} with the count.</summary>
    public static string N(int count, string one, string many) => F(count == 1 ? one : many, count);
}
