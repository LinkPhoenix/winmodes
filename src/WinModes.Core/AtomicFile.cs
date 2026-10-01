namespace WinModes.Core;

/// <summary>
/// Replaces a file in one step: the text goes to a temporary file in the same folder, which then takes the
/// place of the target. A crash or a concurrent reader sees the old file or the new one, never half of it.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);

        // Replacing a link would swap the link for a regular file; write through it instead.
        if (File.Exists(path) && new FileInfo(path).LinkTarget is not null)
        {
            File.WriteAllText(path, content);
            return;
        }

        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The original failure is the one worth reporting.
            }

            throw;
        }
    }
}
