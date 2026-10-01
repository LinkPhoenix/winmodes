using System.Net;
using System.Text;

namespace WinModes.Core.Accounts;

/// <summary>What the browser brought back to the redirect address: the code to exchange, or the reason the sign-in was refused.</summary>
public sealed record CallbackResult(string? Code, string? Error)
{
    public bool Succeeded => Code is not null;
}

/// <summary>
/// Waits, on this PC only, for the browser to be sent back after a sign-in. Only a request with the expected path and the
/// state of this sign-in counts; anything else (another page, a stray request, a forged redirect) gets a 404 and is ignored.
/// </summary>
public sealed class CallbackListener : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _path;
    private readonly string _accountName;

    /// <exception cref="HttpListenerException">The port is used by another program.</exception>
    public CallbackListener(int port, string path, string accountName)
    {
        (_path, _accountName) = (path, accountName);
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
    }

    /// <summary>Waits for the redirect of the sign-in that carries <paramref name="expectedState"/>; null when time runs out or it is cancelled.</summary>
    public async Task<CallbackResult?> WaitAsync(string expectedState, TimeSpan timeout, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        using var stop = limit.Token.Register(_listener.Stop);
        try
        {
            while (true)
            {
                var context = await _listener.GetContextAsync();
                var query = context.Request.QueryString;
                var matches = string.Equals(context.Request.Url?.AbsolutePath, _path, StringComparison.Ordinal)
                    && string.Equals(query["state"], expectedState, StringComparison.Ordinal);
                if (!matches)
                {
                    Reply(context, HttpStatusCode.NotFound, "Not found.", "text/plain");
                    continue;
                }

                var code = query["code"];
                if (string.IsNullOrEmpty(code) || query["error"] is { Length: > 0 })
                {
                    Reply(context, HttpStatusCode.OK, CallbackPage.Refused());
                    return new CallbackResult(null, query["error"] is { Length: > 0 } error ? error : "no_code");
                }

                Reply(context, HttpStatusCode.OK, CallbackPage.Success(_accountName));
                // Claude can append "#state" to the code when it shows it for pasting.
                var hash = code.IndexOf('#', StringComparison.Ordinal);
                return new CallbackResult(hash >= 0 ? code[..hash] : code, null);
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose() => _listener.Close();

    private static void Reply(HttpListenerContext context, HttpStatusCode status, string body, string contentType = "text/html")
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = (int)status;
            context.Response.ContentType = contentType + "; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes);
            context.Response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            // The browser went away: nothing to answer.
        }
    }
}
