using System.Net;
using WinModes.Core.Localization;

namespace WinModes.Core.Accounts;

/// <summary>The page the browser shows once the sign-in is over: a card in the colours of the app, in the language of the app.</summary>
internal static class CallbackPage
{
    // Self-contained: no script, no font or picture fetched from anywhere. The bolt is the WinModes mark.
    private const string Template = """
        <!doctype html>
        <html lang="{LANG}">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>WinModes</title>
        <style>
        :root { color-scheme: dark; }
        * { box-sizing: border-box; }
        body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 24px;
               font-family: "Segoe UI Variable", "Segoe UI", system-ui, sans-serif; color: #e8e8f0;
               background: radial-gradient(900px 500px at 50% -10%, #2a2160 0%, #14141c 55%, #0e0e13 100%); }
        .card { width: min(440px, 100%); padding: 36px 32px 28px; text-align: center; border-radius: 16px;
                background: #1b1b24; border: 1px solid #2c2c3a; box-shadow: 0 20px 60px rgba(0, 0, 0, .45); }
        .brand { display: inline-flex; align-items: center; gap: 10px; font-weight: 600; letter-spacing: .2px; color: #c9c9d6; }
        .mark { width: 28px; height: 28px; border-radius: 8px; display: grid; place-items: center;
                background: linear-gradient(135deg, #7c5cfc, #4f8bff); }
        .badge { width: 72px; height: 72px; border-radius: 50%; margin: 28px auto 20px; display: grid; place-items: center;
                 background: {TINT}; border: 2px solid {ACCENT}; }
        h1 { margin: 0 0 10px; font-size: 22px; font-weight: 600; }
        p { margin: 0; line-height: 1.55; color: #a9a9bb; font-size: 15px; }
        .hint { margin-top: 26px; padding-top: 18px; border-top: 1px solid #2c2c3a; font-size: 13px; color: #7d7d90; }
        </style>
        </head>
        <body>
        <main class="card">
          <div class="brand">
            <span class="mark"><svg width="16" height="16" viewBox="0 0 24 24" fill="#fff"><path d="M13 2 4 14h6l-1 8 9-12h-6z"/></svg></span>
            WinModes
          </div>
          <div class="badge">{GLYPH}</div>
          <h1>{TITLE}</h1>
          <p>{MESSAGE}</p>
          <p class="hint">{HINT}</p>
        </main>
        </body>
        </html>
        """;

    private const string Check = """<svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="#34d399" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12.5l4.2 4.2L19 7"/></svg>""";
    private const string Cross = """<svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="#f87171" stroke-width="2.6" stroke-linecap="round"><path d="M6 6l12 12M18 6L6 18"/></svg>""";

    public static string Success(string accountName) => Render(true,
        Loc.T("You are signed in"),
        Loc.F("WinModes can now read your {0} usage with a session of its own.", accountName));

    public static string Refused() => Render(false,
        Loc.T("The sign-in was refused"),
        Loc.T("Nothing was changed. You can try again from the Widget page of WinModes."));

    private static string Render(bool success, string title, string message) => Template
        .Replace("{LANG}", WebUtility.HtmlEncode(Loc.Current), StringComparison.Ordinal)
        .Replace("{TINT}", success ? "rgba(52,211,153,.14)" : "rgba(248,113,113,.14)", StringComparison.Ordinal)
        .Replace("{ACCENT}", success ? "#34d399" : "#f87171", StringComparison.Ordinal)
        .Replace("{GLYPH}", success ? Check : Cross, StringComparison.Ordinal)
        .Replace("{TITLE}", WebUtility.HtmlEncode(title), StringComparison.Ordinal)
        .Replace("{MESSAGE}", WebUtility.HtmlEncode(message), StringComparison.Ordinal)
        .Replace("{HINT}", WebUtility.HtmlEncode(Loc.T("You can close this tab and go back to WinModes.")), StringComparison.Ordinal);
}
