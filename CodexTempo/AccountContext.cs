using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CodexTempo;

internal static class AccountContext
{
    // Metadata only: never read or persist credentials. A credential-file change
    // invalidates the child connection and any unverified startup cache.
    public static string Stamp()
    {
        try
        {
            var home = Path.GetFullPath(CodexPathResolver.ResolveHome());
            var file = new FileInfo(Path.Combine(home, "auth.json"));
            return Hash(home + "|" + (file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : "missing"));
        }
        catch { return "unavailable"; }
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
