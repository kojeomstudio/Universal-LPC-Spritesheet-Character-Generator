// Tests for SpritePathResolver and PaletteResolver — the core of phase 2.
using System.Collections.Generic;
using System.IO;
using LpcSpriteGen.Core.Catalog;
using LpcSpriteGen.Core.Palettes;
using LpcSpriteGen.Core.Paths;
using LpcSpriteGen.Core.Characters;
using Xunit;

namespace LpcSpriteGen.Core.Tests;

public class PathAndPaletteTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static LpcCatalog Cat => new CatalogLoader(
        Path.Combine(RepoRoot, "sheet_definitions"),
        Path.Combine(RepoRoot, "palette_definitions")).Load() is { } loaded
        ? new LpcCatalog(loaded) : throw new System.Exception("load failed");

    private static string FindRepoRoot()
    {
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

    [Fact]
    public void SpritePath_BodyMaleWalk_HasExpectedShape()
    {
        var resolver = new SpritePathResolver(Cat);
        // body item, bodyType=male, anim=walk, layer 1, with recolors (body has recolors)
        var r = resolver.Resolve("body", variant: null, hasRecolors: true, bodyType: "male", animName: "walk");
        Assert.True(r.IsOk, r.IsOk ? "" : r.Error.ToString());
        Assert.Equal("spritesheets/body/bodies/male/walk.png", r.Value);
    }

    [Fact]
    public void SpritePath_CombatAnim_IsRemappedToCombatIdleFolder()
    {
        var resolver = new SpritePathResolver(Cat);
        var r = resolver.Resolve("body", variant: null, hasRecolors: true, bodyType: "male", animName: "combat");
        Assert.True(r.IsOk);
        Assert.Equal("spritesheets/body/bodies/male/combat_idle.png", r.Value);
    }

    [Fact]
    public void SpritePath_1hSlash_IsRemappedToBackslashFolder()
    {
        var resolver = new SpritePathResolver(Cat);
        var r = resolver.Resolve("body", variant: null, hasRecolors: true, bodyType: "male", animName: "1h_slash");
        Assert.True(r.IsOk);
        Assert.Equal("spritesheets/body/bodies/male/backslash.png", r.Value);
    }

    [Fact]
    public void SpritePath_WithoutRecolors_AppendsVariantSuffix()
    {
        var resolver = new SpritePathResolver(Cat);
        // legs_pants item has variants like "black", "blue". Pick a known variant.
        // For an item with recolors=false and a variant, path includes /<variant>.
        var r = resolver.Resolve("body", variant: "light", hasRecolors: false, bodyType: "male", animName: "walk");
        Assert.True(r.IsOk);
        Assert.Equal("spritesheets/body/bodies/male/walk/light.png", r.Value);
    }

    [Fact]
    public void SpritePath_MissingBodyType_ReturnsErr()
    {
        var resolver = new SpritePathResolver(Cat);
        // heads_human_male does NOT have child body type on layer_1
        var r = resolver.Resolve("heads_human_male", variant: null, hasRecolors: true, bodyType: "child", animName: "walk");
        Assert.False(r.IsOk);
        Assert.Equal(LpcSpriteGen.Core.Paths.PathErrorKind.MissingBodyTypePath, r.Error.Kind);
    }

    [Fact]
    public void PaletteResolver_BodyLightResolves()
    {
        var pr = new PaletteResolver(Cat);
        var r = pr.GetTargetPalette("body", "light");
        Assert.True(r.IsOk, "light should resolve for body material");
        Assert.Equal(6, r.Value.Length); // body palettes are 6-hex arrays
        Assert.StartsWith("#", r.Value[0]);
    }

    [Fact]
    public void PaletteResolver_TanIsNotBodyColor()
    {
        // Critical regression: confirms the random-generator bug. "tan" is NOT a body.ulpc color.
        var pr = new PaletteResolver(Cat);
        var r = pr.GetTargetPalette("body", "tan");
        Assert.False(r.IsOk, "tan must NOT be resolvable for body material");
    }

    [Fact]
    public void PaletteResolver_QualifiedMaterialVersionKey()
    {
        var pr = new PaletteResolver(Cat);
        // "metal.ulpc.steel" — qualified cross-material
        var r = pr.GetTargetPalette("all", "metal.ulpc.steel");
        Assert.True(r.IsOk, "metal.ulpc.steel should resolve");
    }

    [Fact]
    public void PaletteResolver_GetPalettesFromMeta_HeadsHasTwoSlots()
    {
        var pr = new PaletteResolver(Cat);
        var head = Cat.GetItem("heads_human_male").UnsafeUnwrap();
        var palettes = pr.GetPalettesFromMeta(head.Lite);
        // color_1 = head (skintone), color_2 = eyes
        Assert.Contains("head", palettes.Keys);
        Assert.Contains("eyes", palettes.Keys);
        Assert.Equal("body", palettes["head"].Material);
        Assert.Equal("eye", palettes["eyes"].Material);
    }

    [Fact]
    public void ParseRecolorKey_FourForms()
    {
        var pr = new PaletteResolver(Cat);
        // bare recolor — falls back to palette's material/default
        var (_, v1, r1) = pr.ParseRecolorKey("light", new PaletteRecolor { Material = "body", Default = "ulpc" });
        Assert.Equal("light", r1);
        Assert.Equal("ulpc", v1);

        // version.recolor
        var (_, v2, r2) = pr.ParseRecolorKey("ulpc.light", new PaletteRecolor { Material = "body" });
        Assert.Equal("light", r2);
        Assert.Equal("ulpc", v2);

        // material.version.recolor
        var (m3, v3, r3) = pr.ParseRecolorKey("metal.ulpc.steel", new PaletteRecolor { Material = "body" });
        Assert.Equal("metal", m3);
        Assert.Equal("ulpc", v3);
        Assert.Equal("steel", r3);

        // material.recolor — when middle segment IS a known material, treat it as material
        var (m4, v4, r4) = pr.ParseRecolorKey("body.light", new PaletteRecolor { Material = "body" });
        Assert.Equal("light", r4);
        // "body" is a known material name → it's the material, version falls back to palette default
        Assert.Equal("body", m4);
    }
}
