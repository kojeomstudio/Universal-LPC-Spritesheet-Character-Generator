// Loads palette metadata from palette_definitions/. Layout:
//   palette_definitions/meta_<version>.json    — version meta (ulpc/lpcr/custom)
//   palette_definitions/<material>/meta_<material>.json — material defaults
//   palette_definitions/<material>/<material>_<version>.json — colorName → [6 hexes]
//   palette_definitions/<material>/<version>.json         — (alt) colorName → [6 hexes]
//
// Port of scripts/generateSources/palettes.js.
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace LpcSpriteGen.Core.Catalog;

public static class PaletteMetaLoader
{
    public static PaletteMetadata Load(string paletteDefsDir)
    {
        var meta = new PaletteMetadata();

        // Pass 1: top-level meta files. Both version meta (meta_ulpc.json etc.) and
        // material meta live in two possible places — root or <material>/ subdir.
        //   meta_<X>.json at root            → X is a version (type:"version")
        //   <material>/meta_<material>.json  → material meta (type:"material")
        foreach (var file in Directory.EnumerateFiles(paletteDefsDir, "meta_*.json", SearchOption.TopDirectoryOnly))
        {
            var raw = JsonNode.Parse(File.ReadAllText(file));
            if (raw is null) continue;
            var type = (string?)raw["type"];
            var id = Path.GetFileNameWithoutExtension(file)["meta_".Length..];
            if (type == "version")
                meta.Versions[id] = new PaletteVersionMeta(
                    (string?)raw["label"] ?? id, (string?)raw["desc"] ?? "");
        }

        // Pass 2: per-material subdirectories.
        foreach (var subDir in Directory.EnumerateDirectories(paletteDefsDir))
        {
            var material = Path.GetFileName(subDir);

            // material meta (e.g. body/meta_body.json)
            var matMetaFile = Path.Combine(subDir, "meta_" + material + ".json");
            var matMeta = new PaletteMaterialMeta(
                new Dictionary<string, Dictionary<string, string[]>>(),
                material, "", "ulpc", "");
            if (File.Exists(matMetaFile))
            {
                var mm = JsonNode.Parse(File.ReadAllText(matMetaFile));
                if (mm is not null)
                {
                    matMeta = new PaletteMaterialMeta(
                        matMeta.Palettes,
                        (string?)mm["label"] ?? material,
                        (string?)mm["desc"] ?? "",
                        (string?)mm["default"] ?? "ulpc",
                        (string?)mm["base"] ?? "");
                }
            }
            meta.Materials[material] = matMeta;

            // data files: <material>_<version>.json (preferred) OR <version>.json (fallback)
            foreach (var file in Directory.EnumerateFiles(subDir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith("meta_", StringComparison.Ordinal)) continue;

                string version;
                var prefix = material + "_";
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    version = name[prefix.Length..];
                else
                    version = name; // alt layout: just the version name

                var raw = JsonNode.Parse(File.ReadAllText(file));
                if (raw is null) continue;
                if (!matMeta.Palettes.ContainsKey(version))
                    matMeta.Palettes[version] = new Dictionary<string, string[]>();
                foreach (var (colorName, val) in (JsonObject)raw)
                {
                    if (val is JsonArray arr)
                    {
                        var hexes = new List<string>(arr.Count);
                        foreach (var h in arr)
                            if (h is JsonValue hv && hv.TryGetValue<string>(out var s)) hexes.Add(s);
                        matMeta.Palettes[version][colorName] = hexes.ToArray();
                    }
                }
            }
        }

        return meta;
    }
}
