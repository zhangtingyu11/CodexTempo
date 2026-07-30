using System.IO;

namespace CodexTempo;

internal static class CodexPathResolver
{
    public static string ResolveHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var candidates = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("USERPROFILE"),
            FindWindowsProfile(AppContext.BaseDirectory),
            FindWindowsProfile(Environment.CurrentDirectory),
            FindWindowsProfile(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
        };

        foreach (var profile in candidates.Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var codexHome = Path.Combine(profile!, ".codex");
            if (Directory.Exists(Path.Combine(codexHome, "sessions")))
                return codexHome;
        }

        var fallback = candidates.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                       ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(fallback!, ".codex");
    }

    private static string? FindWindowsProfile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(path));
            while (current.Parent is not null)
            {
                if (current.Parent.Name.Equals("Users", StringComparison.OrdinalIgnoreCase))
                    return current.FullName;
                current = current.Parent;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
        return null;
    }
}
