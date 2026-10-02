using System.IO;
using WinModes.Core;
using WinModes.Core.Usage;
using WinModes.Core.Usage.Tokens;

namespace WinModes.App.Services;

/// <summary>
/// The token statistics of Claude Code and Codex, read from the logs they keep on this PC. Only runs when the user turned it on:
/// the first scan reads the logs of the last month (some seconds, on a thread of low priority), the next ones only what was
/// added. The figures are kept in a small file, never the text of a conversation.
/// </summary>
internal static class TokenStats
{
    private static readonly Lock Gate = new();
    private static readonly TokenIndex Index = TokenIndex.Load(TokenIndex.DefaultPath);
    private static int _running;
    private static TokenScanProgress? _progress;
    private static DateTimeOffset? _updatedAt;

    /// <summary>A scan is running now.</summary>
    public static bool IsScanning => Volatile.Read(ref _running) == 1;

    /// <summary>How far the running scan is; null when none is running.</summary>
    public static TokenScanProgress? Progress
    {
        get
        {
            lock (Gate)
            {
                return _progress;
            }
        }
    }

    /// <summary>When the last scan ended; null until one did in this run.</summary>
    public static DateTimeOffset? UpdatedAt
    {
        get
        {
            lock (Gate)
            {
                return _updatedAt;
            }
        }
    }

    /// <summary>Reads what the logs gained. One scan at a time: a call during a scan returns at once.</summary>
    public static Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return Task.CompletedTask;
        }

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                Index.Update(TokenIndex.DefaultClaudeFolder, Subscriptions.DefaultCodexHome, DateTimeOffset.Now, new ScanReport(), CancellationToken.None);
                Index.Save(TokenIndex.DefaultPath);
                lock (Gate)
                {
                    _updatedAt = DateTimeOffset.Now;
                }
            }
            catch (Exception ex)
            {
                // A worker thread has no handler above it: the failure is written down instead of ending the app.
                new ErrorLog(ErrorLog.DefaultPath).Append("Token statistics", ex);
            }
            finally
            {
                lock (Gate)
                {
                    _progress = null;
                }

                Interlocked.Exchange(ref _running, 0);
                finished.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "WinModes token scan",
        };
        worker.Start();
        return finished.Task;
    }

    public static IReadOnlyList<TokenSummary> Summarize(int days) => Index.Summarize(DateOnly.FromDateTime(DateTime.Now), days);

    /// <summary>Forgets the figures and the saved file; the next scan reads the logs again.</summary>
    public static void Clear()
    {
        Index.Clear();
        try
        {
            File.Delete(TokenIndex.DefaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not deleted: it is replaced at the next save.
        }

        lock (Gate)
        {
            _updatedAt = null;
        }
    }

    private sealed class ScanReport : IProgress<TokenScanProgress>
    {
        public void Report(TokenScanProgress value)
        {
            lock (Gate)
            {
                _progress = value;
            }
        }
    }
}
