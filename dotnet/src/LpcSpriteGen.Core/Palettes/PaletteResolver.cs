// Palette resolution — turns a recolor key (e.g. "light", "metal.ulpc.steel") into
// the actual 6-hex color array used for pixel remapping. Also builds per-item
// source-palette configurations. Port of sources/state/palettes.ts.
using System;
using System.Collections.Generic;
using System.Linq;
using LpcSpriteGen.Core.Catalog;
using LpcSpriteGen.Core.Characters;

namespace LpcSpriteGen.Core.Palettes;

public enum PaletteLookupErrorKind
{
    MaterialNotFound,
    ColorsNotFound,
}

public sealed record PaletteLookupError(PaletteLookupErrorKind Kind, string? Material = null, string? Version = null, string? Recolor = null);

public sealed record PaletteForItem(string Material, string Version, string Source, string[] Colors);

public sealed class PaletteResolver
{
    private readonly PaletteMetadata _meta;
    public PaletteResolver(PaletteMetadata meta) => _meta = meta;
    public PaletteResolver(LpcCatalog catalog) : this(catalog.PaletteMeta) { }

    /// <summary>
    /// Parse a recolor key into (material, version, recolor). Accepts the forms
    ///   material.version.recolor  (e.g. "metal.ulpc.steel")
    ///   material.recolor          (e.g. "body.light" — but ambiguous with version.recolor)
    ///   version.recolor           (e.g. "ulpc.light")
    ///   recolor                   (e.g. "light" — falls back to palette's material/default)
    /// Port of palettes.ts:parseRecolorKey. The reverse-split trick is the trick.
    /// </summary>
    public (string? Material, string? Version, string Recolor) ParseRecolorKey(string? recolorKey, PaletteRecolor? palette)
    {
        if (string.IsNullOrEmpty(recolorKey))
            recolorKey = palette?.Base ?? "";

        // Reverse-split: the recolor name is the LAST segment; what's before is
        // either version or material depending on whether it matches a known name.
        var parts = recolorKey.Split('.').Reverse().ToArray();
        string recolor = parts.Length > 0 ? parts[0] : "";
        string? version = parts.Length > 1 ? parts[1] : null;
        string? material = parts.Length > 2 ? parts[2] : null;

        // If no explicit material, maybe `version` is actually a material name.
        if (material == null)
        {
            if (version != null && _meta.Materials.ContainsKey(version))
            {
                material = version;
                version = null;
            }
            else
            {
                material = palette?.Material;
            }
        }
        // If no version, fall back to the palette's default version.
        if (version == null) version = palette?.Default;

        return (material, version, recolor);
    }

    /// <summary>
    /// Resolve the base palette colors for a material. Returns (version, recolor, colors).
    /// Port of palettes.ts:getBasePalette.
    /// </summary>
    public Result<(string Version, string Recolor, string[] Colors), PaletteLookupError> GetBasePalette(
        string material, string? baseKey = null, string[]? source = null)
    {
        if (!_meta.Materials.TryGetValue(material, out var matMeta))
            return Result<(string, string, string[]), PaletteLookupError>.Err(
                new PaletteLookupError(PaletteLookupErrorKind.MaterialNotFound, material));

        // If source provided (custom user palette), use it directly.
        if (source != null)
            return Result<(string, string, string[]), PaletteLookupError>.Ok(
                (matMeta.Default, baseKey ?? matMeta.Base, source));

        // Determine base variant from "version.recolor" or material defaults.
        string version, recolor;
        if (!string.IsNullOrEmpty(baseKey) && baseKey.Contains('.'))
        {
            var seg = baseKey.Split('.');
            version = seg[0];
            recolor = seg.Length > 1 ? seg[1] : "";
        }
        else if (!string.IsNullOrEmpty(baseKey))
        {
            version = matMeta.Default;
            recolor = baseKey;
        }
        else
        {
            version = matMeta.Default;
            recolor = matMeta.Base;
        }

        var colors = matMeta.Palettes.TryGetValue(version, out var vpal) &&
                     vpal.TryGetValue(recolor, out var c) ? c : Array.Empty<string>();
        return Result<(string, string, string[]), PaletteLookupError>.Ok((version, recolor, colors));
    }

    /// <summary>
    /// Resolve the target color array for a recolor key (e.g. "light" → 6-hex array).
    /// Port of palettes.ts:getTargetPalette.
    /// </summary>
    public Result<string[], PaletteLookupError> GetTargetPalette(string material, string targetColor, PaletteRecolor? palette = null)
    {
        if (!_meta.Materials.TryGetValue(material, out var matMeta))
            return Result<string[], PaletteLookupError>.Err(
                new PaletteLookupError(PaletteLookupErrorKind.MaterialNotFound, material));

        var (newMat, version, recolor) = ParseRecolorKey(targetColor, palette ?? new PaletteRecolor { Material = material, Default = matMeta.Default, Base = matMeta.Base });
        if (newMat != null && _meta.Materials.TryGetValue(newMat, out var crossMat))
        {
            material = newMat;
            matMeta = crossMat;
        }

        if (version != null &&
            matMeta.Palettes.TryGetValue(version, out var vpal) &&
            vpal.TryGetValue(recolor, out var colors))
            return Result<string[], PaletteLookupError>.Ok(colors);

        return Result<string[], PaletteLookupError>.Err(
            new PaletteLookupError(PaletteLookupErrorKind.ColorsNotFound, material, version, recolor));
    }

    /// <summary>
    /// Build the palette configuration from an item's recolor slots, keyed by type_name.
    /// Port of palettes.ts:getPalettesFromMeta.
    /// </summary>
    public Dictionary<string, PaletteForItem> GetPalettesFromMeta(ItemLite? meta)
    {
        var result = new Dictionary<string, PaletteForItem>();
        if (meta?.Recolors == null || meta.Recolors.Length == 0) return result;

        foreach (var palette in meta.Recolors)
        {
            var baseR = GetBasePalette(palette.Material, palette.Base, palette.Source);
            if (!baseR.IsOk) continue;
            var (version, source, colors) = baseR.Value;
            var key = palette.TypeName ?? meta.TypeName;
            result[key] = new PaletteForItem(
                palette.Material,
                string.IsNullOrEmpty(version) ? (palette.Default ?? "") : version,
                source, colors);
        }
        return result;
    }

    /// <summary>
    /// Resolve the active recolor per type_name across current selections.
    /// Returns a map like { "body": "light", "eyes": "ulpc.blue" }.
    /// Port of palettes.ts:getMultiRecolors.
    /// </summary>
    public Dictionary<string, string> GetMultiRecolors(ItemMerged item, LpcCatalog catalog, Selections selections, bool matchBodyColorEnabled = true)
    {
        var recolors = new Dictionary<string, string>();
        if (item.Lite.Recolors.Length == 0) return recolors;

        // For each recolor slot, find the matching selection by type_name.
        for (int i = 0; i < item.Lite.Recolors.Length; i++)
        {
            var slot = item.Lite.Recolors[i];
            var group = i == 0 ? item.Lite.TypeName : (slot.TypeName ?? item.Lite.TypeName);
            // Find a selection whose itemId matches or whose key matches the group.
            Selection? sel = null;
            if (selections.TryGetValue(group, out var g)) sel = g;
            else
            {
                // Try matching by item's own selection group
                foreach (var (k, v) in selections)
                    if (v.ItemId == item.ItemId) { sel = v; break; }
            }
            if (sel != null && !string.IsNullOrEmpty(sel.Recolor))
                recolors[group] = sel.Recolor;
        }

        // matchBodyColor propagation: if enabled and item has matchBodyColor, override
        // the item's own type_name with the body's selected recolor.
        if (matchBodyColorEnabled && item.Lite.MatchBodyColor)
        {
            var bodyColor = GetBodyColor(catalog, selections);
            if (bodyColor != null)
                recolors[item.Lite.TypeName] = bodyColor;
        }
        return recolors;
    }

    /// <summary>Find the body-color selection (first matchBodyColor item's recolor).</summary>
    private static string? GetBodyColor(LpcCatalog catalog, Selections selections)
    {
        foreach (var (_, sel) in selections)
        {
            var item = catalog.GetItem(sel.ItemId);
            if (!item.IsOk) continue;
            // matchBodyColor on any recolor slot → this item is the "body color" source
            if (item.Value.Lite.Recolors.Any(r => r.MatchBodyColor == true) ||
                item.Value.Lite.MatchBodyColor)
                return sel.Recolor;
        }
        return null;
    }
}
