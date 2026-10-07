namespace WinModes.Core.Accounts;

/// <summary>How a pasted sign-in answer was written.</summary>
public enum CallbackInputKind
{
    /// <summary>A whole redirect address: <c>http://127.0.0.1:56121/callback?code=...&amp;state=...</c>.</summary>
    Url,

    /// <summary>Only the query: <c>code=...&amp;state=...</c>.</summary>
    Query,

    /// <summary>The code alone, or <c>code#state</c> as Claude shows it.</summary>
    Raw,
}

/// <summary>
/// What a sign-in answer pasted by the user holds. A provider shows the code in the browser when it cannot reach the redirect
/// address on this PC (a browser that blocks the local address, a remote session); pasting it finishes the same sign-in.
/// </summary>
public sealed record CallbackInput(CallbackInputKind Kind, string? Code, string? State)
{
    /// <summary>
    /// Reads a redirect address, a query or a bare code. Only the code and the state are taken, never a token: the sign-in asks
    /// for a code, and a paste box must not become the place where anything else is accepted.
    /// </summary>
    public static CallbackInput Parse(string? input)
    {
        var value = input?.Trim() ?? "";
        if (value.Length == 0)
        {
            return new CallbackInput(CallbackInputKind.Raw, null, null);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var address) && address.Scheme is "http" or "https")
        {
            // The query wins as a whole when it carries the answer: code and state are never mixed across the query and the fragment.
            var query = Fields(address.Query);
            var source = query.ContainsKey("code") ? query : Fields(address.Fragment);
            return new CallbackInput(CallbackInputKind.Url, source.GetValueOrDefault("code"), source.GetValueOrDefault("state"));
        }

        if (value.Contains("code=", StringComparison.Ordinal))
        {
            var query = Fields(value);
            return new CallbackInput(CallbackInputKind.Query, query.GetValueOrDefault("code"), query.GetValueOrDefault("state"));
        }

        var parts = value.Split('#', 2);
        return new CallbackInput(CallbackInputKind.Raw, parts[0].Length > 0 ? parts[0] : null, parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null);
    }

    /// <summary>
    /// The code to exchange, or null when the answer cannot belong to the sign-in waiting for <paramref name="expectedState"/>.
    /// An address or a query is an answer of the provider and must carry the state of this sign-in; a bare code (same PKCE
    /// session, nothing to compare) is accepted, and a <c>code#state</c> must match.
    /// </summary>
    public string? CodeFor(string expectedState)
    {
        if (string.IsNullOrEmpty(Code))
        {
            return null;
        }

        var carriesState = Kind != CallbackInputKind.Raw || State is not null;
        return carriesState && !string.Equals(State, expectedState, StringComparison.Ordinal) ? null : Code;
    }

    private static Dictionary<string, string> Fields(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in text.TrimStart('?', '#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var name = Uri.UnescapeDataString((separator < 0 ? pair : pair[..separator]).Replace('+', ' '));
            if (name.Length > 0 && !fields.ContainsKey(name))
            {
                fields[name] = separator < 0 ? "" : Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            }
        }

        return fields;
    }
}
