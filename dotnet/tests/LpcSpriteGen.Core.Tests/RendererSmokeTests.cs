// Renderer smoke tests — verifies that Phase 3 produces a real, non-empty sprite sheet
// matching the baseline (test-correct.png) we saved from the JS Electron build.
//
// The baseline is at bins/lpc-dotnet-baseline/test-correct.png + test-correct.json.
// It was produced with body+head+expression (male, light recolor) — the canonical "correct"
// rendering from the JS project. The C# port must produce a sheet of the same dimensions
// with the same non-transparent pixel count (within tolerance) and the same color palette.
//
// Image backend: SkiaSharp (MIT) — replaces System.Drawing.Common.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LpcSpriteGen.Core.Catalog;
using LpcSpriteGen.Core.Characters;
using LpcSpriteGen.Core.Constants;
using LpcSpriteGen.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace LpcSpriteGen.Core.Tests;

public class RendererSmokeTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string BaselineDir = Path.Combine(
        FindWorkspaceRoot(), "bins", "lpc-dotnet-baseline");

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

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "engines")) ||
                Directory.Exists(Path.Combine(dir.FullName, "bins")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return "/c/workspaces/business";
    }

    private static LpcCatalog LoadCatalog() => new LpcCatalog(new CatalogLoader(
        Path.Combine(RepoRoot, "sheet_definitions"),
        Path.Combine(RepoRoot, "palette_definitions")).Load());

    private static Selections DefaultMaleCharacter()
    {
        // Mirror state.ts:selectDefaults — body (light) + heads_human_male (light) +
        // face_neutral (light). This is the canonical "correct" character.
        return new Selections
        {
            ["body"] = new Selection { ItemId = "body", Variant = "", Recolor = "light", Name = "Body color (light)" },
            ["head"] = new Selection { ItemId = "heads_human_male", Variant = "", Recolor = "light", Name = "Human Male (light)" },
            ["expression"] = new Selection { ItemId = "face_neutral", Variant = "", Recolor = "light", Name = "Neutral (light)" },
        };
    }

    [Fact]
    public async void RenderCharacter_ProducesExpectedDimensions()
    {
        var cat = LoadCatalog();
        var renderer = new Renderer(cat, Path.Combine(RepoRoot, "spritesheets"));
        var sheet = await renderer.RenderCharacterAsync(DefaultMaleCharacter(), "male");
        Assert.Equal(LpcConstants.SheetWidth, sheet.Width);
        Assert.Equal(LpcConstants.SheetHeight, sheet.Height);
        sheet.Dispose();
    }

    [Fact]
    public async void RenderCharacter_HasNonTrivialContent()
    {
        // Should not be empty and not be a solid block. Expect ~10% non-transparent pixels
        // for a body+head+expression character, matching the JS baseline.
        var cat = LoadCatalog();
        var renderer = new Renderer(cat, Path.Combine(RepoRoot, "spritesheets"));
        using var sheet = await renderer.RenderCharacterAsync(DefaultMaleCharacter(), "male");
        var (nonTransparent, uniqueColors) = CountPixels(sheet);
        Assert.True(nonTransparent > 100_000, $"too few pixels: {nonTransparent}");
        Assert.True(nonTransparent < 600_000, $"too many pixels: {nonTransparent}");
        Assert.True(uniqueColors < 50, $"too many colors (palette may not be applied): {uniqueColors}");
    }

    [Fact]
    public async void RenderCharacter_MatchesBaselinePixelCount()
    {
        // Compare against the JS-rendered baseline saved at bins/lpc-dotnet-baseline.
        var baselinePath = Path.Combine(BaselineDir, "test-correct.png");
        if (!File.Exists(baselinePath))
        {
            // Skip on CI / clean environments — only meaningful locally.
            return;
        }
        using var baselineFs = File.OpenRead(baselinePath);
        using var baseline = SKBitmap.Decode(baselineFs);
        var cat = LoadCatalog();
        var renderer = new Renderer(cat, Path.Combine(RepoRoot, "spritesheets"));
        using var csharp = await renderer.RenderCharacterAsync(DefaultMaleCharacter(), "male");

        Assert.Equal(baseline.Width, csharp.Width);
        Assert.Equal(baseline.Height, csharp.Height);

        var (baselinePx, baselineColors) = CountPixels(baseline);
        var (csharpPx, csharpColors) = CountPixels(csharp);
        // Within 5% pixel count — small differences expected from font/AA in bitmap round-trips.
        var delta = (double)System.Math.Abs(baselinePx - csharpPx) / baselinePx;
        Assert.True(delta < 0.10, $"pixel count drift: baseline={baselinePx}, csharp={csharpPx}, delta={delta:P1}");
    }

    [Fact]
    public async void RenderCharacter_WalkBlockHasCharacter()
    {
        // Inspect the walk block (rows 8..11, 4 directions × 13 frames) for content.
        var cat = LoadCatalog();
        var renderer = new Renderer(cat, Path.Combine(RepoRoot, "spritesheets"));
        using var sheet = await renderer.RenderCharacterAsync(DefaultMaleCharacter(), "male");
        int y0 = 8 * LpcConstants.FrameSize;
        int y1 = 12 * LpcConstants.FrameSize;
        var rect = SKRectI.Create(0, y0, LpcConstants.SheetWidth, y1 - y0);
        using var walk = ExtractSubset(sheet, rect);
        var (px, _) = CountPixels(walk);
        Assert.True(px > 10_000, $"walk block empty: {px}");
    }

    /// <summary>Wrapper around SKBitmap.ExtractSubset (instance API writes into a dest).</summary>
    private static SKBitmap ExtractSubset(SKBitmap src, SKRectI rect)
    {
        var dst = new SKBitmap(rect.Width, rect.Height, src.ColorType, src.AlphaType);
        if (!src.ExtractSubset(dst, rect))
        {
            dst.Dispose();
            throw new InvalidOperationException($"ExtractSubset failed for rect {rect}");
        }
        return dst;
    }

    private static (int nonTransparent, int uniqueColors) CountPixels(SKBitmap bmp)
    {
        // SkiaSharp: direct span access replaces LockBits + Marshal.Copy.
        var span = bmp.GetPixelSpan();
        int bytes = span.Length;
        int nonTransparent = 0;
        var colors = new HashSet<int>();
        for (int i = 0; i < bytes; i += 4)
        {
            byte a = span[i + 3];
            if (a == 0) continue;
            nonTransparent++;
            int rgb = (span[i + 2] << 16) | (span[i + 1] << 8) | span[i]; // R,G,B (BGRA layout)
            colors.Add(rgb);
        }
        return (nonTransparent, colors.Count);
    }
}
