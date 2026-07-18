// Sprite path resolver — builds the on-disk sprite file path for a given
// (item, layer, animation, bodyType, variant) tuple. Port of sources/state/path.ts.
//
// Critical: the folderName remap (combat→combat_idle, 1h_slash→backslash, etc.)
// must match exactly or sprite loads will fail.
using LpcSpriteGen.Core.Catalog;
using LpcSpriteGen.Core.Constants;
using LpcSpriteGen.Core.Characters;

namespace LpcSpriteGen.Core.Paths;

public enum PathErrorKind
{
    MissingLayer,
    MissingBodyTypePath,
    ItemNotFound,
}

public sealed record PathError(PathErrorKind Kind, int LayerNum = 0, string? BodyType = null, string? ItemId = null)
{
    public override string ToString() => Kind switch
    {
        PathErrorKind.MissingLayer => $"layer_{LayerNum} not on item {ItemId}",
        PathErrorKind.MissingBodyTypePath => $"bodyType {BodyType} not on layer_{LayerNum}",
        PathErrorKind.ItemNotFound => $"item {ItemId} not in catalog",
        _ => Kind.ToString(),
    };
}

public sealed class SpritePathResolver
{
    private readonly LpcCatalog _catalog;

    public SpritePathResolver(LpcCatalog catalog) => _catalog = catalog;

    /// <summary>
    /// Build the spritesheet path: "spritesheets/&lt;basePath&gt;&lt;animFolder&gt;[/&lt;variant&gt;].png".
    /// basePath comes from the layer's body-type field; animFolder is the folderName
    /// remap (combat→combat_idle); variant is the on-disk filename suffix when no recolors.
    /// Port of path.ts:getSpritePath.
    /// </summary>
    public Result<string, PathError> Resolve(
        string itemId,
        string? variant,
        bool hasRecolors,
        string bodyType,
        string animName,
        int layerNum = 1,
        Selections? selections = null,
        ItemMerged? meta = null)
    {
        if (meta is null)
        {
            var itemR = _catalog.GetItem(itemId);
            if (!itemR.IsOk)
                return Result<string, PathError>.Err(new PathError(PathErrorKind.ItemNotFound, ItemId: itemId));
            meta = itemR.Value;
        }

        var layerKey = "layer_" + layerNum.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!meta.Layers.TryGetValue(layerKey, out var layer))
            return Result<string, PathError>.Err(new PathError(PathErrorKind.MissingLayer, layerNum, null, itemId));

        if (!layer.BodyPaths.TryGetValue(bodyType, out var basePath))
            return Result<string, PathError>.Err(new PathError(PathErrorKind.MissingBodyTypePath, layerNum, bodyType, itemId));

        // ${typeName} template substitution
        if (basePath.Contains("${"))
            basePath = ReplaceInPath(basePath, selections, meta);

        // Derive variant from itemId suffix if none provided and no recolors.
        if (string.IsNullOrEmpty(variant) && !hasRecolors)
        {
            var parts = itemId.Split('_');
            variant = parts[^1];
        }

        // folderName remap (combat → combat_idle, 1h_slash → backslash, etc.)
        var animFolder = AnimationTables.ResolveFolderName(animName);

        // Filename suffix: with recolors, no suffix (recoloring happens at draw time);
        // without, the variant name appended as a sub-path.
        var fileName = !hasRecolors ? "/" + VariantToFilename(variant!) : "";
        return Result<string, PathError>.Ok($"spritesheets/{basePath}{animFolder}{fileName}.png");
    }

    /// <summary>
    /// Replace ${typeName} placeholders in a path using current selections.
    /// Port of path.ts:replaceInPath.
    /// </summary>
    private string ReplaceInPath(string path, Selections? selections, ItemMerged meta)
    {
        if (!path.Contains("${")) return path;
        selections ??= new Selections();

        foreach (var (typeName, sel) in selections)
        {
            var nameWithoutVariant = GetNameWithoutVariant(typeName, sel);
            var replacement = meta.Lite.ReplaceInPath.TryGetValue(typeName, out var rip) &&
                              rip.TryGetValue(nameWithoutVariant, out var r) ? r : null;
            if (path.Contains($"${{{typeName}}}") && replacement == null)
            {
                System.Console.WriteLine(
                    $"Warning: No replacement found for {typeName}=\"{nameWithoutVariant}\" in path template.");
            }
            path = path.Replace($"${{{typeName}}}", replacement ?? "");
        }
        return path;
    }

    /// <summary>Strip variant suffix from a name like "long_hair_red" → "long_hair".</summary>
    private string GetNameWithoutVariant(string typeName, Selection sel)
    {
        var itemIds = _catalog.GetItemIdsByTypeName(typeName);
        var name = sel.Name;
        var parts = name.Split('_');
        // Try to match: longest prefix that is a known item name.
        foreach (var id in itemIds)
        {
            var item = _catalog.GetItem(id);
            if (!item.IsOk) continue;
            var itemName = item.Value.Lite.Name.Replace(' ', '_');
            if (name.StartsWith(itemName + "_", StringComparison.Ordinal))
                return itemName;
        }
        return parts.Length > 1 ? string.Join('_', parts[..^1]) : name;
    }

    /// <summary>Variant → filename component (spaces → underscores). Port of helpers.ts:variantToFilename.</summary>
    public static string VariantToFilename(string variant) => variant.Replace(' ', '_');
}
