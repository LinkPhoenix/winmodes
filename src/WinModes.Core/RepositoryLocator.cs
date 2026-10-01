namespace WinModes.Core;

/// <summary>Finds the folder that holds data/protected.json and profiles/ by walking up from a start directory.</summary>
public static class RepositoryLocator
{
    public static string? Find(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "data", "protected.json")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
