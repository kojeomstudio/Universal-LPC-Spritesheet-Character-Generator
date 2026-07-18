// License configuration — single source of truth. Port of LICENSE_CONFIG.
namespace LpcSpriteGen.Core.Constants;

public sealed record LicenseEntry(
    string Key,
    string Label,
    string[] Versions,
    string Url,
    string? UrlLabel = null);

public static class LicenseConfig
{
    public static readonly LicenseEntry[] Entries =
    {
        new("CC0", "CC0",
            new[] { "CC0" },
            "https://creativecommons.org/public-domain/cc0/"),
        new("CC-BY-SA", "CC-BY-SA",
            new[] { "CC-BY-SA 3.0", "CC-BY-SA 4.0" },
            "https://creativecommons.org/licenses/by-sa/4.0/deed.en",
            "4.0"),
        new("CC-BY", "CC-BY",
            new[] { "CC-BY 3.0+", "CC-BY 3.0", "CC-BY 4.0", "CC-BY" },
            "https://creativecommons.org/licenses/by/4.0/",
            "4.0"),
        new("OGA-BY", "OGA-BY",
            new[] { "OGA-BY 3.0", "OGA-BY 3.0+", "OGA-BY 4.0" },
            "https://static.opengameart.org/OGA-BY-3.0.txt",
            "3.0"),
        new("GPL", "GPL",
            new[] { "GPL 2.0", "GPL 3.0" },
            "https://www.gnu.org/licenses/gpl-3.0.en.html#license-text",
            "3.0"),
    };

    /// <summary>Resolve a raw license string (e.g. "CC-BY-SA 3.0") to its family key.</summary>
    public static string? ResolveKey(string license)
    {
        foreach (var e in Entries)
            foreach (var v in e.Versions)
                if (string.Equals(v, license, StringComparison.OrdinalIgnoreCase))
                    return e.Key;
        return null;
    }
}
