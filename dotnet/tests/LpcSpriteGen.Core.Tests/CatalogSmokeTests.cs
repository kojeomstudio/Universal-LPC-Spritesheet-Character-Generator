// Smoke tests for CatalogLoader against the real 786 JSON files.
// Validates that Phase 1 (data layer) loads cleanly and key invariants hold.
using System.IO;
using System.Linq;
using LpcSpriteGen.Core.Catalog;
using Xunit;

namespace LpcSpriteGen.Core.Tests;

public class CatalogSmokeTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string SheetDefs = Path.Combine(RepoRoot, "sheet_definitions");
    private static readonly string PaletteDefs = Path.Combine(RepoRoot, "palette_definitions");

    private static string FindRepoRoot()
    {
        // Test bin lives under dotnet/tests/LpcSpriteGen.Core.Tests/bin/Debug/net8.0
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "sheet_definitions")) &&
                File.Exists(Path.Combine(dir.FullName, "package.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return "/c/workspaces/business/tools/lpc-sprite-generator";
    }

    private static LpcCatalog LoadCatalog() => new CatalogLoader(SheetDefs, PaletteDefs).Load() switch
    {
        var loaded => new LpcCatalog(loaded),
    };

    [Fact]
    public void LoadsAllItems()
    {
        var cat = LoadCatalog();
        // Expect ~700+ items. The exact count drifts as the submodule updates; assert a floor.
        Assert.True(cat.AllItemIds.Count > 600, $"expected >600 items, got {cat.AllItemIds.Count}");
    }

    [Fact]
    public void BodyItem_HasCorrectShape()
    {
        var cat = LoadCatalog();
        var r = cat.GetItem("body");
        Assert.True(r.IsOk, "body item should exist");
        var body = r.Value;
        Assert.Equal("Body Color", body.Lite.Name);
        Assert.Equal("body", body.Lite.TypeName);
        Assert.True(body.Lite.MatchBodyColor, "body should match_body_color");
        Assert.Contains("male", body.Lite.Required);
        Assert.Contains("female", body.Lite.Required);
        Assert.NotEmpty(body.Lite.Animations);
        Assert.Contains("walk", body.Lite.Animations);

        // Layer 1 should have body-type paths to body/bodies/<type>/
        Assert.True(body.Layers.ContainsKey("layer_1"));
        var l1 = body.Layers["layer_1"];
        Assert.Equal(10, l1.ZPos);
        Assert.Equal("body/bodies/male/", l1.BodyPaths["male"]);
        Assert.Equal("body/bodies/female/", l1.BodyPaths["female"]);

        // Recolors: shorthand form → single entry with material=body
        Assert.Single(body.Lite.Recolors);
        var recolor = body.Lite.Recolors[0];
        Assert.Equal("body", recolor.Material);
        // Palette tokens "ulpc", "lpcr", "all.lpcr" → expanded keys
        Assert.Contains("body.ulpc", recolor.Palettes.Keys);
        Assert.Contains("body.lpcr", recolor.Palettes.Keys);
        Assert.Contains("all.lpcr", recolor.Palettes.Keys);
        // body.ulpc should include "light"
        Assert.Contains("light", recolor.Palettes["body.ulpc"]);
    }

    [Fact]
    public void HeadsHumanMale_HasMultiRecolor()
    {
        var cat = LoadCatalog();
        var r = cat.GetItem("heads_human_male");
        Assert.True(r.IsOk, "heads_human_male should exist");
        var head = r.Value;
        // Multi-recolor: color_1 (body skintone, type_name=null) + color_2 (eyes, type_name="eyes")
        Assert.Equal(2, head.Lite.Recolors.Length);
        Assert.Null(head.Lite.Recolors[0].TypeName); // color_1 = item's own
        Assert.Equal("eyes", head.Lite.Recolors[1].TypeName); // color_2 = eyes
        Assert.Equal("eye", head.Lite.Recolors[1].Material);
        // zPos=100, layer_1 paths all point to head/heads/human/male/
        Assert.Equal(100, head.Layers["layer_1"].ZPos);
        Assert.Equal("head/heads/human/male/", head.Layers["layer_1"].BodyPaths["male"]);
    }

    [Fact]
    public void FaceNeutral_Exists_AsExpression()
    {
        var cat = LoadCatalog();
        var r = cat.GetItem("face_neutral");
        Assert.True(r.IsOk, "face_neutral (default expression) must exist");
        Assert.Equal("expression", r.Value.Lite.TypeName);
    }

    [Fact]
    public void PaletteMetadata_HasBodyMaterial()
    {
        var cat = LoadCatalog();
        Assert.True(cat.PaletteMeta.Materials.ContainsKey("body"));
        var body = cat.PaletteMeta.Materials["body"];
        Assert.Equal("ulpc", body.Default);
        Assert.Equal("light", body.Base);
        // ulpc version should have many skin tones
        Assert.True(body.Palettes["ulpc"].ContainsKey("light"));
        Assert.True(body.Palettes["ulpc"].ContainsKey("amber"));
        Assert.True(body.Palettes["ulpc"].Count > 10, "body.ulpc should have many color variants");
    }

    [Fact]
    public void BodyRecolorKeys_AllValidAgainstPaletteMeta()
    {
        // Critical regression guard for the bug we found in the JS random generator:
        // body.ulpc keys are light/amber/olive/taupe/bronze/brown/... NOT "tan"/"dark".
        var cat = LoadCatalog();
        var bodyKeys = cat.PaletteMeta.Materials["body"].Palettes["ulpc"].Keys.ToHashSet();
        Assert.Contains("light", bodyKeys);
        Assert.Contains("amber", bodyKeys);
        Assert.DoesNotContain("tan", bodyKeys); // tan is NOT a body.ulpc color
        Assert.DoesNotContain("dark", bodyKeys);
    }

    [Fact]
    public void CategoryTree_HasTopLevelCategories()
    {
        var cat = LoadCatalog();
        var keys = cat.Tree.Children.Select(c => c.Key).ToHashSet();
        // The major LPC categories should all be present
        Assert.Contains("body", keys);
        Assert.Contains("head", keys);
        Assert.Contains("hair", keys);
        Assert.Contains("torso", keys);
        Assert.Contains("legs", keys);
        Assert.Contains("feet", keys);
        Assert.Contains("weapons", keys);
    }

    [Fact]
    public void TypeNameIndex_ResolvesHead()
    {
        var cat = LoadCatalog();
        // The 'head' type_name should include heads_human_male / _female
        var headItems = cat.GetItemIdsByTypeName("head");
        Assert.Contains("heads_human_male", headItems);
        Assert.Contains("heads_human_female", headItems);
    }
}
