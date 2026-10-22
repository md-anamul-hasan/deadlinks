namespace DeadLinks.Services;

/// <summary>Display-only path helpers. The real path is always kept for scanning and shown in tooltips.</summary>
public static class PathDisplay
{
    private const char Backslash = '\\';

    /// <summary>
    /// Shows a path under the user's profile folder as <c>%USERPROFILE%\…</c>, so a screenshot or a support message
    /// does not carry the Windows user name. Paths elsewhere are returned unchanged.
    /// </summary>
    public static string Shorten(string path, string? profileFolder = null)
    {
        if (string.IsNullOrEmpty(path)) return path;
        string profile = (profileFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).TrimEnd(Backslash, '/');
        if (profile.Length == 0) return path;
        string trimmed = path.TrimEnd(Backslash, '/');
        if (trimmed.Equals(profile, StringComparison.OrdinalIgnoreCase)) return "%USERPROFILE%";
        bool under = path.Length > profile.Length + 1
                     && path.StartsWith(profile, StringComparison.OrdinalIgnoreCase)
                     && (path[profile.Length] == Backslash || path[profile.Length] == '/');
        return under ? "%USERPROFILE%" + path[profile.Length..] : path;
    }
}
