// Catalog data models. Port of sources/state/catalog.ts types.
// Note: in C# we parse the raw sheet_definitions/*.json directly (no ES-module chunks,
// no interned arrays). The shapes here mirror the post-build runtime form.
namespace LpcSpriteGen.Core.Catalog;

/// <summary>One recolor slot on an item (color_1, color_2, ...).</summary>
public sealed class PaletteRecolor
{
    public string Material { get; set; } = "";
    /// <summary>Expanded palette map: e.g. { "body.ulpc": ["light","amber",...] }.</summary>
    public Dictionary<string, string[]> Palettes { get; set; } = new();
    /// <summary>Sub-type this recolor targets; null for the item's own type_name.</summary>
    public string? TypeName { get; set; }
    public string[] Variants { get; set; } = Array.Empty<string>();
    public string? Label { get; set; }
    public bool? MatchBodyColor { get; set; }
    /// <summary>Default-version base hint, e.g. "ulpc.light".</summary>
    public string? Base { get; set; }
    public string[]? Source { get; set; }
    public string? Default { get; set; }
}

/// <summary>Lite per-item metadata (the shape stored in itemMetadata).</summary>
public sealed class ItemLite
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string[] Required { get; set; } = Array.Empty<string>();
    public string[] Animations { get; set; } = Array.Empty<string>();
    public PaletteRecolor[] Recolors { get; set; } = Array.Empty<PaletteRecolor>();
    public bool MatchBodyColor { get; set; }
    public string[] Variants { get; set; } = Array.Empty<string>();
    public string[] Path { get; set; } = Array.Empty<string>();
    public int? PreviewRow { get; set; }
    public int Priority { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string[] RequiredTags { get; set; } = Array.Empty<string>();
    public string[] ExcludedTags { get; set; } = Array.Empty<string>();
    public Dictionary<string, Dictionary<string, string>> ReplaceInPath { get; set; } = new();
    public int PreviewColumn { get; set; }
    public int PreviewXOffset { get; set; }
    public int PreviewYOffset { get; set; }
    /// <summary>bodyType → license list (e.g. "male" → ["OGA-BY 3.0", ...]).</summary>
    public Dictionary<string, string[]> Licenses { get; set; } = new();
}

public sealed class Credit
{
    public string File { get; set; } = "";
    public string[] Authors { get; set; } = Array.Empty<string>();
    public string[] Licenses { get; set; } = Array.Empty<string>();
    public string[] Urls { get; set; } = Array.Empty<string>();
    public string? Notes { get; set; }
}

/// <summary>One layer entry (layer_1..9). zPos/custom_animation plus body-type paths.</summary>
public sealed class LayerEntry
{
    public int? ZPos { get; set; }
    public string? CustomAnimation { get; set; }
    /// <summary>bodyType → asset path stem (e.g. "body/bodies/male/").</summary>
    public Dictionary<string, string> BodyPaths { get; set; } = new();
}

public sealed class ItemMerged
{
    public string ItemId { get; set; } = "";
    public ItemLite Lite { get; set; } = new();
    public Dictionary<string, LayerEntry> Layers { get; set; } = new();
    public Credit[] Credits { get; set; } = Array.Empty<Credit>();
}

public sealed class CategoryTreeNode
{
    public string Key { get; set; } = "";
    public string? Label { get; set; }
    public int Priority { get; set; }
    public string[]? Required { get; set; }
    public string[]? Animations { get; set; }
    public List<string> Items { get; set; } = new();
    public List<CategoryTreeNode> Children { get; set; } = new();
}

public sealed record PaletteMaterialMeta(
    Dictionary<string, Dictionary<string, string[]>> Palettes,
    string Label,
    string Desc,
    string Default,
    string Base);

public sealed record PaletteVersionMeta(string Label, string Desc);

public sealed class PaletteMetadata
{
    public Dictionary<string, PaletteMaterialMeta> Materials { get; set; } = new();
    public Dictionary<string, PaletteVersionMeta> Versions { get; set; } = new();
}
