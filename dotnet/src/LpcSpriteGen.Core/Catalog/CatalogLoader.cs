// Catalog loader — parses the 786 raw JSON files (sheet_definitions + palette_definitions)
// into in-memory Catalog models. This replaces the JS build pipeline (generateSources/*)
// + the interned-array expansion (resolve-hash-param.ts) entirely.
//
// Port of:
//   scripts/generateSources/items.js     (parseItem, collectLayers, defaults)
//   scripts/generateSources/item-helper.js (collectRecolorEntries, applyRecolorDefaults,
//                                           expandRecolorPalettes, resolvePaletteToken)
//   scripts/generateSources/tree.js      (parseTree, sortCategoryTree)
//   scripts/generateSources/palettes.js  (loadPaletteMetadata)
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LpcSpriteGen.Core.Constants;

namespace LpcSpriteGen.Core.Catalog;

public sealed class CatalogLoader
{
    private readonly string _sheetDefsDir;
    private readonly string _paletteDefsDir;
    private readonly PaletteMetadata _paletteMeta;

    public CatalogLoader(string sheetDefsDir, string paletteDefsDir)
    {
        _sheetDefsDir = sheetDefsDir;
        _paletteDefsDir = paletteDefsDir;
        // Palette meta must load first — item recolor normalization depends on material defaults.
        _paletteMeta = PaletteMetaLoader.Load(paletteDefsDir);
    }

    public PaletteMetadata PaletteMeta => _paletteMeta;

    /// <summary>Load every item sheet definition and palette metadata. Items are keyed by itemId (= filename without .json).</summary>
    public LoadedCatalog Load()
    {
        var items = new Dictionary<string, ItemMerged>();
        var tree = new CategoryTreeNode { Key = "", Label = "root" };

        foreach (var file in Directory.EnumerateFiles(_sheetDefsDir, "*.json", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("meta_", StringComparison.Ordinal))
                continue; // meta files label a directory, not an item
            var relPath = Path.GetRelativePath(_sheetDefsDir, file).Replace('\\', '/');
            var itemId = name.EndsWith(".json", StringComparison.Ordinal)
                ? name[..^".json".Length]
                : name;

            var raw = JsonNode.Parse(File.ReadAllText(file));
            if (raw is null) continue;
            if (raw["ignore"]?.GetValue<bool>() == true) continue;

            var item = ParseItem(itemId, relPath, raw.AsObject());
            items[itemId] = item;
        }

        BuildCategoryTree(_sheetDefsDir, tree, items);
        SortCategoryTree(tree);
        return new LoadedCatalog(items, tree, _paletteMeta);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Item parsing (port of items.js:parseItem + item-helper.js)
    // ────────────────────────────────────────────────────────────────────────────

    private ItemMerged ParseItem(string itemId, string relPath, JsonObject raw)
    {
        var lite = new ItemLite
        {
            Name = (string?)raw["name"] ?? itemId,
            TypeName = (string?)raw["type_name"] ?? InferTypeName(itemId),
            Priority = (int?)raw["priority"] ?? 0,
            MatchBodyColor = (bool?)raw["match_body_color"] ?? false,
            Animations = ParseStringArray(raw["animations"]) ?? AnimationTables.AnimationDefaults,
            Variants = ParseStringArray(raw["variants"]) ?? Array.Empty<string>(),
            Required = CollectRequired(raw),
            Tags = ParseStringArray(raw["tags"]) ?? Array.Empty<string>(),
            RequiredTags = ParseStringArray(raw["required_tags"]) ?? Array.Empty<string>(),
            ExcludedTags = ParseStringArray(raw["excluded_tags"]) ?? Array.Empty<string>(),
            PreviewRow = (int?)raw["preview_row"],
            PreviewColumn = (int?)raw["preview_column"] ?? 0,
            PreviewXOffset = (int?)raw["preview_x_offset"] ?? 0,
            PreviewYOffset = (int?)raw["preview_y_offset"] ?? 0,
            Path = ParseStringArray(raw["path"]) ?? BuildTreePath(relPath),
        };
        lite.Recolors = CollectRecolors(raw, lite.TypeName);

        // Parse replace_in_path
        if (raw["replace_in_path"] is JsonObject rip)
            foreach (var (k, v) in rip)
                if (v is JsonObject inner)
                {
                    var dict = new Dictionary<string, string>();
                    foreach (var (ik, iv) in inner)
                        dict[ik] = (string?)iv ?? "";
                    lite.ReplaceInPath[k] = dict;
                }

        var merged = new ItemMerged
        {
            ItemId = itemId,
            Lite = lite,
            Layers = CollectLayers(raw),
            Credits = CollectCredits(raw),
        };

        // Build licenses map from credits (per bodyType)
        PopulateLicenses(merged);
        return merged;
    }

    private PaletteRecolor[] CollectRecolors(JsonObject raw, string itemTypeName)
    {
        if (raw["recolors"] is not JsonObject recolorsRaw)
            return Array.Empty<PaletteRecolor>();

        // Two forms: shorthand `{material, palettes:[tokens]}` (body, hair_long) — the item's
        // own type_name; or longhand `{color_1: {...}, color_2: {...}, ...}` — multiple slots.
        // Detected by checking for "material" key at top level (shorthand) — port of
        // item-helper.js:collectRecolorEntries.
        bool shorthand = recolorsRaw.ContainsKey("material");
        if (shorthand)
            return new[] { NormalizeRecolorImpl(recolorsRaw, itemTypeName) };

        var list = new List<PaletteRecolor>();
        // color_1..color_N in numeric order. color_1 has null type_name (= item's own).
        foreach (var kp in recolorsRaw
                     .Where(k => k.Key.StartsWith("color_", StringComparison.Ordinal))
                     .OrderBy(k => int.Parse(k.Key["color_".Length..], CultureInfo.InvariantCulture)))
        {
            if (kp.Value is JsonObject obj)
            {
                // color_1 → null type_name; color_N → its own type_name (e.g. "eyes")
                var typeName = kp.Key == "color_1" ? null : (string?)obj["type_name"];
                list.Add(NormalizeRecolorImpl(obj, typeName));
            }
        }
        return list.ToArray();
    }

    /// <summary>
    /// Expand the raw recolor object: resolve palette tokens (e.g. "ulpc", "all.lpcr",
    /// "metal.ulpc") into the {key: [color names]} form. Port of
    /// item-helper.js:expandRecolorPalettes + resolvePaletteToken.
    /// </summary>
    private PaletteRecolor NormalizeRecolorImpl(JsonObject raw, string? typeName)
    {
        var material = (string?)raw["material"] ?? "all";
        var paletteTokens = ParseStringArray(raw["palettes"]) ?? Array.Empty<string>();

        var palettes = new Dictionary<string, string[]>();
        var variants = new HashSet<string>();
        foreach (var token in paletteTokens)
        {
            var (tokMaterial, tokVersion) = ResolvePaletteToken(token, material);
            var key = $"{tokMaterial}.{tokVersion}";
            // Look up the named colors available for this material/version from palette metadata.
            if (_paletteMeta.Materials.TryGetValue(tokMaterial, out var matMeta) &&
                matMeta.Palettes.TryGetValue(tokVersion, out var colors))
            {
                palettes[key] = colors.Keys.ToArray();
                foreach (var c in colors.Keys) variants.Add(c);
            }
            else
            {
                palettes[key] = Array.Empty<string>();
            }
        }

        // Default version: explicit in raw, else material meta default, else "ulpc".
        string defaultVersion = "ulpc";
        if (raw["default"] is JsonValue dv && dv.TryGetValue<string>(out var ds) && !string.IsNullOrEmpty(ds))
            defaultVersion = ds;
        else if (_paletteMeta.Materials.TryGetValue(material, out var matMetaDefault))
            defaultVersion = matMetaDefault.Default;

        return new PaletteRecolor
        {
            Material = material,
            Palettes = palettes,
            TypeName = typeName,
            Variants = variants.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray(),
            Label = (string?)raw["label"],
            Base = (string?)raw["base"],
            Default = defaultVersion,
            Source = ParseStringArray(raw["source"]),
        };
    }

    /// <summary>
    /// Parse a palette token. Forms: "ulpc"/"lpcr" → (itemMaterial, version);
    /// "metal.ulpc" → (cross-material, version). Port of resolvePaletteToken.
    /// </summary>
    private static (string material, string version) ResolvePaletteToken(string token, string fallbackMaterial)
    {
        var parts = token.Split('.');
        return parts.Length switch
        {
            1 => (fallbackMaterial, parts[0]),
            2 => (parts[0], parts[1]),
            _ => (parts[0], parts[1]),
        };
    }

    private static Dictionary<string, LayerEntry> CollectLayers(JsonObject raw)
    {
        var layers = new Dictionary<string, LayerEntry>();
        for (int n = 1; n <= 9; n++)
        {
            var key = "layer_" + n.ToString(CultureInfo.InvariantCulture);
            if (raw[key] is not JsonObject layerRaw) break;

            var entry = new LayerEntry
            {
                ZPos = (int?)layerRaw["zPos"],
                CustomAnimation = (string?)layerRaw["custom_animation"],
            };
            foreach (var (k, v) in layerRaw)
            {
                if (k == "zPos" || k == "custom_animation") continue;
                if (v is JsonValue jv && jv.TryGetValue<string>(out var s))
                    entry.BodyPaths[k] = s;
            }
            layers[key] = entry;
        }
        return layers;
    }

    private static Credit[] CollectCredits(JsonObject raw)
    {
        if (raw["credits"] is not JsonArray arr) return Array.Empty<Credit>();
        var list = new List<Credit>();
        foreach (var node in arr)
        {
            if (node is not JsonObject o) continue;
            list.Add(new Credit
            {
                File = (string?)o["file"] ?? "",
                Authors = ParseStringArray(o["authors"]) ?? Array.Empty<string>(),
                Licenses = ParseStringArray(o["licenses"]) ?? Array.Empty<string>(),
                Urls = ParseStringArray(o["urls"]) ?? Array.Empty<string>(),
                Notes = (string?)o["notes"],
            });
        }
        return list.ToArray();
    }

    private static void PopulateLicenses(ItemMerged item)
    {
        var byBody = new Dictionary<string, string[]>();
        // Use the layer_1 body paths to know which body types this item serves.
        if (!item.Layers.TryGetValue("layer_1", out var l1)) return;
        foreach (var (bodyType, _) in l1.BodyPaths)
        {
            var licenses = new HashSet<string>();
            foreach (var c in item.Credits)
            {
                if (string.IsNullOrEmpty(c.File) || c.File.Contains(bodyType, StringComparison.Ordinal))
                    foreach (var l in c.Licenses) licenses.Add(l);
            }
            byBody[bodyType] = licenses.ToArray();
        }
        item.Lite.Licenses = byBody;
    }

    /// <summary>Collect body types served by layer_1 paths. Falls back to all body types when none present.</summary>
    private static string[] CollectRequired(JsonObject raw)
    {
        if (raw["layer_1"] is JsonObject l1)
        {
            var present = l1.Where(k => k.Key != "zPos" && k.Key != "custom_animation")
                            .Select(k => k.Key).ToList();
            if (present.Count > 0) return present.ToArray();
        }
        return LpcConstants.BodyTypes.ToArray();
    }

    private static string[]? ParseStringArray(JsonNode? node)
    {
        if (node is not JsonArray arr) return null;
        var list = new List<string>(arr.Count);
        foreach (var v in arr)
        {
            if (v is JsonValue jv && jv.TryGetValue<string>(out var s))
                list.Add(s);
        }
        return list.ToArray();
    }

    private static string InferTypeName(string itemId)
    {
        // type_name is usually present. As a fallback, take chars up to first '_'.
        var i = itemId.IndexOf('_');
        return i >= 0 ? itemId[..i] : itemId;
    }

    private static string[] BuildTreePath(string relPath)
    {
        // relPath like "body/lizard/tail_lizard.json" → ["body", "lizard"]
        var parts = relPath.Split('/');
        if (parts.Length <= 1) return Array.Empty<string>();
        return parts[..^1];
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Category tree (port of tree.js)
    // ────────────────────────────────────────────────────────────────────────────

    private void BuildCategoryTree(string dir, CategoryTreeNode parent, Dictionary<string, ItemMerged> items)
    {
        // Per-directory: create a child node, read its meta_*.json for label/priority,
        // add item children, then recurse into subdirectories.
        var dirName = Path.GetFileName(dir);
        if (dirName == "_unused") return;

        foreach (var subDir in Directory.EnumerateDirectories(dir))
        {
            var subName = Path.GetFileName(subDir);
            var child = new CategoryTreeNode { Key = subName, Label = subName };
            ApplyMeta(child, subDir);
            AddItemChildren(child, subDir, items);
            parent.Children.Add(child);
            BuildCategoryTree(subDir, child, items);
        }
    }

    private static void ApplyMeta(CategoryTreeNode node, string dir)
    {
        var metaFiles = Directory.EnumerateFiles(dir, "meta_*.json").FirstOrDefault();
        if (metaFiles == null) return;
        var raw = JsonNode.Parse(File.ReadAllText(metaFiles));
        if (raw is null) return;
        if (raw["label"] is JsonValue lv && lv.TryGetValue<string>(out var s)) node.Label = s;
        if (raw["priority"] is JsonValue pv && pv.TryGetValue<int>(out var p)) node.Priority = p;
        node.Required = ParseStringArray(raw["required"]);
        node.Animations = ParseStringArray(raw["animations"]);
    }

    private static void AddItemChildren(CategoryTreeNode node, string dir, Dictionary<string, ItemMerged> items)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("meta_", StringComparison.Ordinal)) continue;
            var itemId = name.EndsWith(".json", StringComparison.Ordinal) ? name[..^5] : name;
            if (items.ContainsKey(itemId)) node.Items.Add(itemId);
        }
    }

    private static void SortCategoryTree(CategoryTreeNode node)
    {
        node.Children.Sort((a, b) =>
        {
            var c = a.Priority.CompareTo(b.Priority);
            if (c != 0) return c;
            return string.Compare(a.Label ?? a.Key, b.Label ?? b.Key, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var c in node.Children) SortCategoryTree(c);
    }
}

public sealed record LoadedCatalog(
    Dictionary<string, ItemMerged> Items,
    CategoryTreeNode Tree,
    PaletteMetadata PaletteMeta);
