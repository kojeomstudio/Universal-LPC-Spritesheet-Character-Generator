// The LPC character sheet compositor. Port of sources/canvas/renderer.ts:runRenderCharacter.
//
// Algorithm:
//   1. Iterate selections (insertion order) × layers (1..9) × animations, building DrawCalls
//      with (spritePath, zPos, yPos). Items whose layer_N.custom_animation is set are diverted
//      to a custom-animation pass instead.
//   2. Sort DrawCalls by zPos ascending (lower = drawn first = behind).
//   3. Parallel-load all sprite PNGs; apply palette recolor (per-item recolor map).
//   4. Draw each loaded image at (x=0, y=yPos) onto the sheet canvas.
//   5. For custom animations (wheelchair, oversize weapons), extract frames from the standard
//      sheet's base animation and re-grid into the appended custom area.
//
// The THREE duplicated folder-mapping blocks in the TS source are consolidated here into one
// ResolveAnimFolder() helper.
//
// Image backend: SkiaSharp (MIT). SKCanvas replaces Graphics; SKBitmap replaces Bitmap.
// SkiaSharp's SKSamplingOptions with Default filter gives nearest-neighbor-equivalent
// 1:1 pixel copies (matches the old InterpolationMode.NearestNeighbor + PixelOffsetMode.Half).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LpcSpriteGen.Core.Catalog;
using LpcSpriteGen.Core.Characters;
using LpcSpriteGen.Core.Constants;
using LpcSpriteGen.Core.CustomAnimations;
using LpcSpriteGen.Core.Palettes;
using LpcSpriteGen.Core.Paths;
using SkiaSharp;

namespace LpcSpriteGen.Core.Rendering;

/// <summary>One compositing operation: draw spritePath at yPos with zPos ordering.</summary>
internal sealed class DrawCall
{
    public string ItemId { get; init; } = "";
    public string SpritePath { get; init; } = "";
    public int ZPos { get; init; }
    public int YPos { get; init; }
    public string Animation { get; init; } = "";
    public int LayerNum { get; init; }
    public Dictionary<string, string>? Recolors { get; init; }
    public string? CustomAnimation { get; init; }
}

public sealed class Renderer
{
    private readonly LpcCatalog _catalog;
    private readonly SpritePathResolver _pathResolver;
    private readonly PaletteRecolorService _recolorService;
    private readonly ImageLoader _imageLoader;

    public Renderer(LpcCatalog catalog, string spritesheetsDir)
    {
        _catalog = catalog;
        _pathResolver = new SpritePathResolver(catalog);
        _recolorService = new PaletteRecolorService(catalog);
        _imageLoader = new ImageLoader(spritesheetsDir);
    }

    public ImageLoader ImageLoader => _imageLoader;
    public PaletteRecolorService RecolorService => _recolorService;

    /// <summary>
    /// Compose a full LPC spritesheet (832 × 3456, plus appended custom-animation area
    /// when needed) for the given selections + bodyType.
    /// </summary>
    public async Task<SKBitmap> RenderCharacterAsync(Selections selections, string bodyType)
    {
        var drawCalls = new List<DrawCall>();
        var customAnimationItems = new List<DrawCall>(); // diverted to custom-area pass
        var addedCustomAnimations = new HashSet<string>();

        // ── Pass 1: build draw calls from selections ──────────────────────────────
        foreach (var (_, sel) in selections)
        {
            var itemR = _catalog.GetItem(sel.ItemId);
            if (!itemR.IsOk) continue;
            var item = itemR.Value;
            if (sel.SubId != null) continue; // sub-recolor slot, not a drawable item
            if (!item.Lite.Required.Contains(bodyType)) continue;

            var recolors = new PaletteResolver(_catalog)
                .GetMultiRecolors(item, _catalog, selections);

            for (int layerNum = 1; layerNum <= 9; layerNum++)
            {
                var layerKey = "layer_" + layerNum.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!item.Layers.TryGetValue(layerKey, out var layer)) break;

                int zPos = layer.ZPos ?? 100;

                // Custom-animation branch: divert to custom-area pass.
                if (!string.IsNullOrEmpty(layer.CustomAnimation))
                {
                    addedCustomAnimations.Add(layer.CustomAnimation);
                    if (!layer.BodyPaths.TryGetValue(bodyType, out var basePath)) continue;
                    var spritePath = "spritesheets/" + basePath + SpritePathResolver.VariantToFilename(sel.Variant ?? "") + ".png";
                    customAnimationItems.Add(new DrawCall
                    {
                        ItemId = sel.ItemId,
                        SpritePath = spritePath,
                        ZPos = zPos,
                        YPos = 0,
                        Animation = layer.CustomAnimation,
                        LayerNum = layerNum,
                        Recolors = recolors.Count > 0 ? recolors : null,
                        CustomAnimation = layer.CustomAnimation,
                    });
                    continue;
                }

                // Standard animations branch.
                if (item.Lite.Animations.Length == 0) continue;

                foreach (var (animName, yPos) in AnimationTables.AnimationOffsets)
                {
                    if (!AnimSupportedByItem(animName, item.Lite.Animations)) continue;

                    var pathR = _pathResolver.Resolve(
                        sel.ItemId, sel.Variant, recolors.Count > 0, bodyType, animName,
                        layerNum, selections, item);
                    if (!pathR.IsOk) continue;

                    drawCalls.Add(new DrawCall
                    {
                        ItemId = sel.ItemId,
                        SpritePath = pathR.Value,
                        ZPos = zPos,
                        YPos = yPos,
                        Animation = animName,
                        LayerNum = layerNum,
                        Recolors = recolors.Count > 0 ? recolors : null,
                    });
                }
            }
        }

        // ── Pass 2: determine canvas size ──────────────────────────────────────────
        int width = LpcConstants.SheetWidth;
        int height = LpcConstants.SheetHeight;

        // Custom animations append below the standard sheet.
        var customYOffsets = new Dictionary<string, int>();
        int currentY = LpcConstants.SheetHeight;
        foreach (var name in addedCustomAnimations)
        {
            if (!CustomAnimationTables.Definitions.TryGetValue(name, out var def)) continue;
            customYOffsets[name] = currentY;
            var (w, h) = CustomAnimationTables.GetSize(def);
            width = Math.Max(width, w);
            currentY += h;
        }
        height = currentY;

        // ── Pass 3: load + recolor + draw standard calls ───────────────────────────
        drawCalls.Sort((a, b) => a.ZPos.CompareTo(b.ZPos));

        // NOTE: canvas is RETURNED to the caller — must not be wrapped in `using`.
        // BGRA8888 + Premul alpha matches the old Format32bppArgb compositing surface.
        var canvasBmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        // Erase to fully transparent (SkiaSharp bitmaps are not zero-initialized reliably).
        canvasBmp.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(canvasBmp))
        {
            // Nearest-neighbor sampling for pixel-perfect 1:1 blits (no interpolation),
            // matching the old InterpolationMode.NearestNeighbor setting.
            var loaded = await _imageLoader.LoadParallelAsync(drawCalls, c => c.SpritePath);
            foreach (var (call, bmp) in loaded)
            {
                if (bmp == null) continue;
                var toDraw = call.Recolors != null
                    ? await _recolorService.RecolorAsync(bmp, call.ItemId, call.Recolors, call.SpritePath)
                    : bmp;
                canvas.DrawBitmap(toDraw, 0, call.YPos);
            }
        }

        // ── Pass 4: custom-animation area composition ───────────────────────────────
        if (addedCustomAnimations.Count > 0)
        {
            using var canvas2 = new SKCanvas(canvasBmp);

            foreach (var name in addedCustomAnimations)
            {
                if (!CustomAnimationTables.Definitions.TryGetValue(name, out var def)) continue;
                if (!customYOffsets.TryGetValue(name, out var offsetY)) continue;
                var baseAnim = CustomAnimationTables.GetBaseAnimation(def);

                // Items tagged with this custom_animation: paste directly.
                var customLayerCalls = customAnimationItems
                    .Where(c => c.CustomAnimation == name)
                    .OrderBy(c => c.ZPos)
                    .ToList();
                var loadedCustom = await _imageLoader.LoadParallelAsync(customLayerCalls, c => c.SpritePath);
                foreach (var (call, bmp) in loadedCustom)
                {
                    if (bmp == null) continue;
                    var toDraw = call.Recolors != null
                        ? await _recolorService.RecolorAsync(bmp, call.ItemId, call.Recolors, call.SpritePath)
                        : bmp;
                    canvas2.DrawBitmap(toDraw, 0, offsetY);
                }

                // Extract base-anim frames from standard items and re-grid into the area.
                if (!string.IsNullOrEmpty(baseAnim))
                {
                    var baseCalls = drawCalls
                        .Where(c => c.Animation == baseAnim)
                        .OrderBy(c => c.ZPos)
                        .ToList();
                    var loadedBase = await _imageLoader.LoadParallelAsync(baseCalls, c => c.SpritePath);
                    foreach (var (call, bmp) in loadedBase)
                    {
                        if (bmp == null) continue;
                        var toDraw = call.Recolors != null
                            ? await _recolorService.RecolorAsync(bmp, call.ItemId, call.Recolors, call.SpritePath)
                            : bmp;
                        DrawFramesToCustomAnimation(canvas2, def, offsetY, toDraw);
                    }
                }
            }
        }

        return canvasBmp;
    }

    /// <summary>
    /// Consolidated folder-mapping check. Port of the THREE duplicated blocks in
    /// renderer.ts (combat_idle↔combat, backslash↔1h_slash/1h_backslash,
    /// halfslash↔1h_halfslash). Returns true if the item supports the offset-table anim.
    /// </summary>
    private static bool AnimSupportedByItem(string offsetAnim, string[] itemAnims)
    {
        // offsetAnim is in folderName form (combat_idle, backslash, halfslash). Map back
        // to the value-form the item's animations[] list uses.
        return offsetAnim switch
        {
            "combat_idle" => itemAnims.Contains("combat"),
            "backslash" => itemAnims.Contains("1h_slash") || itemAnims.Contains("1h_backslash"),
            "halfslash" => itemAnims.Contains("1h_halfslash"),
            _ => itemAnims.Contains(offsetAnim),
        };
    }

    /// <summary>
    /// Re-grid frames from the source standard sheet into a custom-animation's cells.
    /// Port of sources/canvas/draw-frames.ts:drawFramesToCustomAnimation.
    /// </summary>
    private static void DrawFramesToCustomAnimation(SKCanvas g, CustomAnimationDefinition def, int offsetY, SKBitmap src)
    {
        int frameSize = def.FrameSize;
        bool isSingleAnimation = src.Height <= 256; // single-anim sheet (4 rows × 64)

        for (int i = 0; i < def.Frames.Length; i++)
        {
            for (int j = 0; j < def.Frames[i].Length; j++)
            {
                var spec = def.Frames[i][j];
                var comma = spec.IndexOf(',');
                var rowName = comma >= 0 ? spec[..comma] : spec;
                int srcColumn = comma >= 0 && int.TryParse(spec[(comma + 1)..], out var c) ? c : 0;

                int srcRow;
                if (isSingleAnimation)
                {
                    var dash = rowName.IndexOf('-');
                    var dir = dash >= 0 ? rowName[(dash + 1)..] : "n";
                    srcRow = CustomAnimationTables.DirectionMap.TryGetValue(dir, out var dr) ? dr : 0;
                }
                else
                {
                    srcRow = CustomAnimationTables.AnimationRowsLayout.TryGetValue(rowName, out var rr) ? rr : i;
                }

                int srcX = LpcConstants.FrameSize * srcColumn;
                int srcY = LpcConstants.FrameSize * srcRow;
                int destX = frameSize * j;
                int destY = frameSize * i + offsetY;

                DrawFrameToFrame(g, destX, destY, frameSize, src, srcX, srcY, LpcConstants.FrameSize);
            }
        }
    }

    /// <summary>
    /// Draw a square frame from src to dest. Same size → 1:1 copy. Larger dest → center
    /// (no upscale). Port of draw-frames.ts:drawFrameToFrame.
    /// </summary>
    private static void DrawFrameToFrame(SKCanvas g, int destX, int destY, int destSize, SKBitmap src, int srcX, int srcY, int srcSize)
    {
        var srcRectI = new SKRectI(srcX, srcY, srcX + srcSize, srcY + srcSize);
        if (destSize == srcSize)
        {
            // 1:1 copy at integer-aligned dest — SkiaSharp default sampling (no filter) is
            // nearest-neighbor, equivalent to the old GraphicsUnit.Pixel blit.
            g.DrawBitmap(src, srcRectI, new SKRectI(destX, destY, destX + destSize, destY + destSize));
        }
        else
        {
            // Center src at its native pixel size within the larger dest cell.
            int offset = (destSize - srcSize) / 2;
            g.DrawBitmap(src, srcRectI, new SKRectI(destX + offset, destY + offset, destX + offset + srcSize, destY + offset + srcSize));
        }
    }
}
