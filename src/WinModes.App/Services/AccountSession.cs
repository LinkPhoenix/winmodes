using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using WinModes.Core.Accounts;

namespace WinModes.App.Services;

/// <summary>How a sign-in ended.</summary>
internal enum SignInOutcome { SignedIn, PortBusy, Refused, TimedOut, Failed }

/// <summary>
/// The sign-ins of WinModes (Claude, ChatGPT/Codex). Each is a session of its own: renewing it never touches the tokens Claude
/// Code or Codex keep, so it cannot sign them out. Tokens stay in the encrypted <see cref="AccountVault"/> and are sent only to
/// the provider that issued them.
/// </summary>
internal static class AccountSession
{
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RenewMargin = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private static readonly AccountVault Vault = new(AccountVault.DefaultPath);

    // No redirect is followed, so a token or a code can never be sent to another host.
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = RequestTimeout };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RenewGates = new();

    public static bool IsSignedIn(AccountProvider provider) => Vault.Get(provider.Tool) is not null;

    /// <summary>The email of the signed-in account when the provider gave it; null otherwise.</summary>
    public static string? Email(AccountProvider provider) => Vault.Get(provider.Tool)?.Email;

    public static void SignOut(AccountProvider provider) => Vault.Remove(provider.Tool);

    private static CancellationTokenSource? _pendingSignIn;

    /// <summary>Stops the sign-in being waited for, if any (the page was left); it ends as timed out.</summary>
    public static void CancelSignIn() => Interlocked.Exchange(ref _pendingSignIn, null)?.Cancel();

    /// <summary>Opens the provider's sign-in in the browser and waits for the browser to come back to this PC.</summary>
    public static async Task<SignInOutcome> SignInAsync(AccountProvider provider)
    {
        CancelSignIn();
        using var source = new CancellationTokenSource();
        _pendingSignIn = source;
        try
        {
            return await SignInCoreAsync(provider, source.Token);
        }
        finally
        {
            // Leave nothing behind that a later cancel could reach after the source is disposed.
            Interlocked.CompareExchange(ref _pendingSignIn, null, source);
        }
    }

    private static async Task<SignInOutcome> SignInCoreAsync(AccountProvider provider, CancellationToken cancellation)
    {
        CallbackListener listener;
        try
        {
            listener = new CallbackListener(provider.CallbackPort, provider.CallbackPath, provider.DisplayName);
        }
        catch (HttpListenerException)
        {
            return SignInOutcome.PortBusy;
        }

        using (listener)
        {
            var verifier = Pkce.NewVerifier();
            var state = Pkce.NewState();
            try
            {
                Process.Start(new ProcessStartInfo(provider.BuildAuthorizeUrl(Pkce.Challenge(verifier), state).AbsoluteUri) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                return SignInOutcome.Failed;
            }

            var result = await listener.WaitAsync(state, SignInTimeout, cancellation);
            if (result is null)
            {
                return SignInOutcome.TimedOut;
            }

            if (!result.Succeeded)
            {
                return SignInOutcome.Refused;
            }

            var answer = await PostAsync(provider.ExchangeRequest(result.Code!, state, verifier), cancellation);
            var tokens = answer is { Status: HttpStatusCode.OK } ? provider.ParseTokens(answer.Body, DateTimeOffset.UtcNow) : null;
            if (tokens is null)
            {
                return SignInOutcome.Failed;
            }

            Vault.Set(provider.Tool, tokens);
            return SignInOutcome.SignedIn;
        }
    }

    /// <summary>
    /// Valid tokens for the account, renewed first when they are about to expire; null when not signed in or when renewing failed.
    /// A refusal by the provider (the session was revoked) signs the account out; a network failure keeps it for the next try.
    /// </summary>
    public static async Task<OAuthTokens?> TokensAsync(AccountProvider provider, CancellationToken cancellation = default)
    {
        var tokens = Vault.Get(provider.Tool);
        if (tokens is null || tokens.ExpiresAt - DateTimeOffset.UtcNow > RenewMargin)
        {
            return tokens;
        }

        var gate = RenewGates.GetOrAdd(provider.Tool, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellation);
        try
        {
            // Another caller may have renewed while this one waited.
            tokens = Vault.Get(provider.Tool);
            if (tokens is null || tokens.ExpiresAt - DateTimeOffset.UtcNow > RenewMargin)
            {
                return tokens;
            }

            if (tokens.RefreshToken.Length == 0)
            {
                return null;
            }

            var answer = await PostAsync(provider.RefreshRequest(tokens.RefreshToken), cancellation);
            if (answer is null)
            {
                return null;
            }

            if (answer.Status == HttpStatusCode.OK && provider.ParseTokens(answer.Body, DateTimeOffset.UtcNow, tokens.RefreshToken) is { } renewed)
            {
                renewed = renewed with { AccountId = renewed.AccountId ?? tokens.AccountId, Email = renewed.Email ?? tokens.Email };
                Vault.Set(provider.Tool, renewed);
                return renewed;
            }

            if (answer.Status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                Vault.Remove(provider.Tool);
            }

            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed record Answer(HttpStatusCode Status, string Body);

    private static async Task<Answer?> PostAsync(TokenRequest request, CancellationToken cancellation)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, request.Address)
            {
                Content = new StringContent(request.Body, Encoding.UTF8, request.ContentType),
            };
            message.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var response = await Http.SendAsync(message, cancellation);
            return new Answer(response.StatusCode, await response.Content.ReadAsStringAsync(cancellation));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}
