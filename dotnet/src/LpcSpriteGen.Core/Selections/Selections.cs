// Selection model + Selections collection. Port of sources/state/state.ts Selection type.
// A Selection describes one chosen item (e.g. body, head, hair) in the character sheet.
//
// NOTE: namespace is Characters (not Selections) to avoid the C# class-vs-namespace
// naming collision that bites when the type and namespace share the same name.
namespace LpcSpriteGen.Core.Characters;

/// <summary>One chosen item slot. Keyed by selection group (= item's type_name).</summary>
public sealed class Selection
{
    public string ItemId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Top-level variant (e.g. body color name). Empty for default.</summary>
    public string Variant { get; set; } = "";
    /// <summary>Recolor key (e.g. "light", "amber", "metal.ulpc.steel"). Empty = default.</summary>
    public string Recolor { get; set; } = "";
    /// <summary>For sub-recolor slots (color_2, color_3); null for top-level.</summary>
    public int? SubId { get; set; }

    public Selection Clone() => new()
    {
        ItemId = ItemId, Name = Name, Variant = Variant, Recolor = Recolor, SubId = SubId,
    };
}

/// <summary>All selections, keyed by selection group (item's type_name or recolor slot type_name).</summary>
public class Selections : Dictionary<string, Selection> { }
