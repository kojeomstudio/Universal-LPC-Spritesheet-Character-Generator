// RandomGenerator tests — verify the 7 known bugs of the JS version are fixed.
using System.IO;
using System.Linq;
using LpcSpriteGen.Core.Catalog;
using LpcSpriteGen.Core.Characters;
using LpcSpriteGen.Core.Constants;
using LpcSpriteGen.Core.Palettes;
using LpcSpriteGen.Core.Rendering;
using Xunit;

namespace LpcSpriteGen.Core.Tests;

public class RandomGeneratorTests
{
    private static LpcCatalog Cat => new LpcCatalog(new CatalogLoader(
        Path.Combine(FindRepo(), "sheet_definitions"),
        Path.Combine(FindRepo(), "palette_definitions")).Load());

    private static string FindRepo()
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
    public void AlwaysIncludes_Body_Head_Expression()
    {
        // Bug fixes 1-3: head, body, expression must always be present.
        var gen = new RandomGenerator(Cat);
        for (int seed = 1; seed <= 20; seed++)
        {
            var r = gen.Generate(seed, "male");
            Assert.True(r.Selections.ContainsKey("body"), $"seed {seed}: body missing");
            Assert.True(r.Selections.ContainsKey("head"), $"seed {seed}: head missing");
            Assert.True(r.Selections.ContainsKey("expression"), $"seed {seed}: expression missing");
        }
    }

    [Fact]
    public void Head_IsBodyTypeAware()
    {
        // Bug 1 fix: head itemId matches the body type's gender.
        var gen = new RandomGenerator(Cat);
        Assert.Equal("heads_human_male", gen.Generate(1, "male").Selections["head"].ItemId);
        Assert.Equal("heads_human_male", gen.Generate(1, "muscular").Selections["head"].ItemId);
        Assert.Equal("heads_human_female", gen.Generate(1, "female").Selections["head"].ItemId);
        Assert.Equal("heads_human_female", gen.Generate(1, "pregnant").Selections["head"].ItemId);
    }

    [Fact]
    public void BodyRecolor_IsAlwaysValid()
    {
        // Bug 3 fix: body recolor must be a real body.ulpc key, never "tan"/"dark".
        var gen = new RandomGenerator(Cat);
        var palette = Cat.PaletteMeta.Materials["body"].Palettes["ulpc"].Keys.ToHashSet();
        for (int seed = 1; seed <= 30; seed++)
        {
            var bodyRecolor = gen.Generate(seed, "male").Selections["body"].Recolor;
            Assert.True(palette.Contains(bodyRecolor),
                $"seed {seed}: body recolor '{bodyRecolor}' is not in body.ulpc");
            Assert.NotEqual("tan", bodyRecolor);
            Assert.NotEqual("dark", bodyRecolor);
        }
    }

    [Fact]
    public void Expression_IsFaceNeutral()
    {
        // Bug 2 fix: expression must be face_neutral.
        var gen = new RandomGenerator(Cat);
        for (int seed = 1; seed <= 10; seed++)
            Assert.Equal("face_neutral", gen.Generate(seed, "male").Selections["expression"].ItemId);
    }

    [Fact]
    public void Deterministic_WithSeed()
    {
        // Same seed → identical selections.
        var gen = new RandomGenerator(Cat);
        var r1 = gen.Generate(42, "male");
        var r2 = gen.Generate(42, "male");
        Assert.Equal(r1.Selections.Count, r2.Selections.Count);
        foreach (var k in r1.Selections.Keys)
        {
            Assert.Equal(r1.Selections[k].ItemId, r2.Selections[k].ItemId);
            Assert.Equal(r1.Selections[k].Recolor, r2.Selections[k].Recolor);
        }
    }

    [Fact]
    public void OptionalCategories_RespectBodyType()
    {
        // Bug 6 fix: every selected item must support the chosen bodyType.
        var gen = new RandomGenerator(Cat);
        for (int seed = 1; seed <= 10; seed++)
        {
            var r = gen.Generate(seed, "female");
            foreach (var (typeName, sel) in r.Selections)
            {
                var item = Cat.GetItem(sel.ItemId);
                if (!item.IsOk) continue;
                Assert.True(item.Value.Lite.Required.Contains("female") ||
                            (item.Value.Layers.TryGetValue("layer_1", out var l1) && l1.BodyPaths.ContainsKey("female")),
                    $"seed {seed}: {sel.ItemId} doesn't support bodyType=female");
            }
        }
    }

    [Fact]
    public async void GeneratedSelections_RenderToValidSprite()
    {
        // The ultimate acceptance test: a randomly-generated character renders to a sheet
        // with substantial non-transparent content (no headless, no palette-broken output).
        var gen = new RandomGenerator(Cat);
        var r = gen.Generate(7, "male");
        var renderer = new Renderer(Cat, Path.Combine(FindRepo(), "spritesheets"));
        using var sheet = await renderer.RenderCharacterAsync(r.Selections, "male");
        Assert.Equal(LpcConstants.SheetWidth, sheet.Width);
        var (nonTransparent, _) = CountPixels(sheet);
        Assert.True(nonTransparent > 100_000, $"generated sprite too sparse: {nonTransparent}");
    }

    [Fact]
    public void Credits_AreCollectedFromSelectedItems()
    {
        var gen = new RandomGenerator(Cat);
        var r = gen.Generate(1, "male");
        // body alone has many authors — credits must not be empty.
        Assert.NotEmpty(r.Credits);
        // No duplicate (author, license) pairs.
        var distinct = r.Credits.Select(c => (c.Author, c.License)).Distinct().Count();
        Assert.Equal(distinct, r.Credits.Count);
    }

    private static (int nonTransparent, int uniqueColors) CountPixels(System.Drawing.Bitmap bmp)
    {
        var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int bytes = System.Math.Abs(data.Stride) * bmp.Height;
            var buf = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, bytes);
            int nonTransparent = 0;
            var colors = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < bytes; i += 4)
            {
                byte a = buf[i + 3];
                if (a == 0) continue;
                nonTransparent++;
                int rgb = (buf[i + 2] << 16) | (buf[i + 1] << 8) | buf[i];
                colors.Add(rgb);
            }
            return (nonTransparent, colors.Count);
        }
        finally { bmp.UnlockBits(data); }
    }
}
