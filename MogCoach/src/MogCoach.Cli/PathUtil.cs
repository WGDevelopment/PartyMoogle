namespace MogCoach.Cli;

/// <summary>Small path helpers for config values.</summary>
public static class PathUtil
{
    /// <summary>Expands a leading <c>~</c> to the user's home directory.</summary>
    public static string Expand(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path == "~" || path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path.Length <= 2 ? "" : path[2..]);
        }
        return path;
    }
}
