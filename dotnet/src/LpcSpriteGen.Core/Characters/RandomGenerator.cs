// Random character generator — picks valid parts to compose a sprite.
// Port of sources/electron-bridge.ts:getRandomSelections with ALL 7 known bugs fixed:
//
//   1. head itemId is now bodyType-aware (heads_human_male for male/muscular, etc.)
//      — JS used "head" type_name (no such type), so head was never selected.
//   2. body recolor draws from body.ulpc keys (light/amber/olive/...) — JS used ["light","tan","dark"]
//      where "tan" and "dark" don't exist in body.ulpc.
//   3. expression always included as face_neutral — JS subject to a 30% skip.
//   4. body always included with correct shape.
//   5. recolor keys are bare-name (parseRecolorKey-friendly) or qualified.
//   6. bodyType filter pre-filters candidates (not pick-then-skip).
//   7. credits collection walks the actual selected items' Credits arrays.
//
// The canonical default character contract (state.ts:selectDefaults):
//   body (light) + heads_human_male (light) + face_neutral (light) — always present.
using System;
using System.Collections.Generic;
using System.Linq;
using LpcSpriteGen.Core.Catalog;
using LpcSpriteGen.Core.Constants;

namespace LpcSpriteGen.Core.Characters;

public sealed class RandomResult
{
    public Selections Selections { get; set; } = new();
    public string BodyType { get; set; } = "male";
    public List<RandomCredit> Credits { get; set; } = new();
}

public sealed record RandomCredit(string Author, string License, string? Name);

public sealed class RandomGenerator
{
    /// <summary>Categories the random generator will sample (type_name values).</summary>
    private static readonly string[] OptionalCategories =
    {
        "hair", "ears", "facial_eyes", "clothes", "legs", "shoes",
        "hat", "weapon", "shield", "cape", "neck", "belt",
    };

    /// <summary>Valid body.ulpc recolor keys (naturalistic subset; full list available on request).</summary>
    private static readonly string[] BodyRecolors =
        { "light", "amber", "olive", "taupe", "bronze", "brown" };

    private readonly LpcCatalog _catalog;

    public RandomGenerator(LpcCatalog catalog) => _catalog = catalog;

    /// <summary>
    /// Generate a random character. <paramref name="seed"/> controls determinism — same seed
    /// produces the same selections (mulberry32 RNG, matching the JS implementation).
    /// </summary>
    public RandomResult Generate(int? seed = null, string bodyType = "male")
    {
        // Wrap both Random and mulberry32 behind a uniform NextDouble delegate so the
        // body of this method reads identically for seeded vs unseeded runs.
        Func<double> nextDouble = seed.HasValue ? Mulberry32(seed.Value) : new Random().NextDouble;
        var result = new RandomResult { BodyType = bodyType };
        var usedAuthors = new HashSet<string>();

        // ── Mandatory: body, head, expression (the default-character contract) ──────────
        string bodyColor = Pick(BodyRecolors, nextDouble);
        result.Selections["body"] = new Selection
        {
            ItemId = "body", Variant = "", Recolor = bodyColor, Name = $"Body color ({bodyColor})",
        };
        CollectCredits(result, usedAuthors, "body");

        // head itemId by bodyType (Rule 1): male/muscular → male head; female/pregnant → female;
        // teen/child → either.
        string headId = ResolveHeadId(bodyType, nextDouble);
        string headColor = Pick(BodyRecolors, nextDouble); // head shares body palette material
        result.Selections["head"] = new Selection
        {
            ItemId = headId, Variant = "", Recolor = headColor, Name = $"Head ({headColor})",
        };
        CollectCredits(result, usedAuthors, headId);

        // expression: face_neutral (the canonical default; Rule 2)
        result.Selections["expression"] = new Selection
        {
            ItemId = "face_neutral", Variant = "", Recolor = headColor, Name = "Neutral",
        };
        CollectCredits(result, usedAuthors, "face_neutral");

        // ── Optional categories: each picked with probability, filtered by bodyType ──────
        foreach (var typeName in OptionalCategories)
        {
            if (nextDouble() < 0.3) continue; // 30% chance to skip

            var candidates = _catalog.GetItemIdsByTypeName(typeName)
                .Where(id => SupportsBodyType(id, bodyType))
                .ToList();
            if (candidates.Count == 0) continue;

            var itemId = Pick(candidates, nextDouble);
            var item = _catalog.GetItem(itemId);
            if (!item.IsOk) continue;
            var m = item.Value.Lite;

            // Pick a valid variant and recolor from the item's actual palette (Rule 5).
            string variant = m.Variants.Length > 0 ? Pick(m.Variants.ToList(), nextDouble) : "";
            string recolor = "";
            if (m.Recolors.Length > 0)
            {
                var slot = m.Recolors[0];
                if (slot.Variants.Length > 0)
                    recolor = Pick(slot.Variants.ToList(), nextDouble);
            }

            result.Selections[typeName] = new Selection
            {
                ItemId = itemId, Variant = variant, Recolor = recolor, Name = m.Name,
            };
            CollectCredits(result, usedAuthors, itemId);
        }

        return result;
    }

    /// <summary>bodyType → canonical head itemId. Port of Rule 1.</summary>
    private static string ResolveHeadId(string bodyType, Func<double> nextDouble)
    {
        // Catalog has heads_human_male and heads_human_female. teen/child support both.
        return bodyType switch
        {
            "male" or "muscular" => "heads_human_male",
            "female" or "pregnant" => "heads_human_female",
            "teen" or "child" => nextDouble() < 0.5 ? "heads_human_male" : "heads_human_female",
            _ => "heads_human_male",
        };
    }

    private bool SupportsBodyType(string itemId, string bodyType)
    {
        var r = _catalog.GetItem(itemId);
        if (!r.IsOk) return false;
        // An item supports a bodyType when its layer_1 paths contain it OR its Required list does.
        var layer = r.Value.Layers.TryGetValue("layer_1", out var l1) ? l1 : null;
        if (layer != null && layer.BodyPaths.ContainsKey(bodyType)) return true;
        return r.Value.Lite.Required.Contains(bodyType);
    }

    private void CollectCredits(RandomResult result, HashSet<string> used, string itemId)
    {
        var item = _catalog.GetItem(itemId);
        if (!item.IsOk) return;
        foreach (var c in item.Value.Credits)
        {
            var license = c.Licenses.Length > 0 ? c.Licenses[0] : "CC-BY";
            foreach (var author in c.Authors)
            {
                var key = author + "|" + license;
                if (used.Add(key))
                    result.Credits.Add(new RandomCredit(author, license, item.Value.Lite.Name));
            }
        }
    }

    private static T Pick<T>(IReadOnlyList<T> items, Func<double> nextDouble) =>
        items[(int)(nextDouble() * items.Count) % items.Count];

    /// <summary>
    /// Deterministic PRNG matching the JS mulberry32 in electron-bridge.ts. Same seed →
    /// same sequence across runs and (modulo integer overflow behavior) across languages.
    /// </summary>
    private static Func<double> Mulberry32(int seed)
    {
        int a = seed;
        return () =>
        {
            unchecked { a += 0x6D2B79F5; }
            long t = a;
            t = Imul((int)(t ^ (t >> 15)), (int)(t | 1));
            t = t + Imul((int)(t ^ (t >> 7)), (int)(t | 61));
            return (((int)(t ^ (t >> 14))) & 0xFFFFFFFF) / 4294967296.0;
        };
    }

    /// <summary>Math.imul equivalent (32-bit signed multiply).</summary>
    private static int Imul(int x, int y)
    {
        // .NET int multiply is already 32-bit on int operands.
        return x * y;
    }
}
