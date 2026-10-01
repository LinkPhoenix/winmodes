using System.Globalization;

namespace WinModes.Core;

/// <summary>
/// Local text log of unexpected errors, so a failure that was kept from closing the app can still be diagnosed.
/// Nothing is sent anywhere. Writing never throws: a log that fails must not cause a second failure.
/// </summary>
public sealed class ErrorLog(string path, long maxBytes = ErrorLog.DefaultMaxBytes)
{
    public const long DefaultMaxBytes = 256 * 1024;

    private readonly Lock _gate = new();

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "errors.log");

    public string FilePath { get; } = path;

    public void Append(string source, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var entry = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.UtcNow:O} [{source}] {exception}{Environment.NewLine}{Environment.NewLine}");
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > maxBytes)
                {
                    // One previous log is kept; anything older is dropped.
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }

                File.AppendAllText(FilePath, entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing more can be done about it.
            }
        }
    }
}
