// Catalog facade — the consumer-facing API over LoadedCatalog. Mirrors the read-side
// of sources/state/catalog.ts (getters return Result<T, LoadError>). Since C# loads
// everything synchronously up-front, the only error variant in practice is "not-found".
using System.Collections.Generic;

namespace LpcSpriteGen.Core.Catalog;

public sealed record LoadError(string Kind, string? Id = null, string? Chunk = null)
{
    public static LoadError NotFound(string id) => new("not-found", id);
    public static LoadError Loading(string chunk) => new("loading", Chunk: chunk);
    public override string ToString() => Kind switch
    {
        "loading" => $"chunk \"{Chunk}\" not loaded",
        "not-found" => $"item {Id} not in catalog",
        _ => Kind,
    };
}

/// <summary>Read-only catalog accessor. Build once via CatalogLoader.Load() and reuse.</summary>
public sealed class LpcCatalog
{
    private readonly Dictionary<string, ItemMerged> _items;
    public CategoryTreeNode Tree { get; }
    public PaletteMetadata PaletteMeta { get; }

    private readonly Dictionary<string, List<string>> _byTypeName;

    public LpcCatalog(LoadedCatalog loaded)
    {
        _items = loaded.Items;
        Tree = loaded.Tree;
        PaletteMeta = loaded.PaletteMeta;
        _byTypeName = new Dictionary<string, List<string>>();
        foreach (var (id, item) in _items)
        {
            var tn = item.Lite.TypeName;
            if (!_byTypeName.TryGetValue(tn, out var list))
                _byTypeName[tn] = list = new List<string>();
            list.Add(id);
        }
    }

    public Result<ItemMerged, LoadError> GetItem(string itemId)
        => _items.TryGetValue(itemId, out var item)
            ? Result<ItemMerged, LoadError>.Ok(item)
            : Result<ItemMerged, LoadError>.Err(LoadError.NotFound(itemId));

    public bool HasItem(string itemId) => _items.ContainsKey(itemId);

    public IReadOnlyCollection<string> AllItemIds => _items.Keys;

    /// <summary>All items matching a type_name.</summary>
    public IReadOnlyList<string> GetItemIdsByTypeName(string typeName)
        => _byTypeName.TryGetValue(typeName, out var list)
            ? list
            : (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>All distinct type_names present in the catalog.</summary>
    public IEnumerable<string> AllTypeNames => _byTypeName.Keys;
}
