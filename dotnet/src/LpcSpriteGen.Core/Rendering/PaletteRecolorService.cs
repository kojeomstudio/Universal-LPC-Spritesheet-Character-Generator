// CPU palette recolor — replaces source-palette colors with target-palette colors in an SKBitmap.
// Direct port of sources/canvas/palette-recolor.ts CPU path (WebGL is skipped — the CPU
// path produces identical observable output and the tolerance values match).
//
// Algorithm (palette-recolor.ts:59-155):
//   1. Build a flat list of (source→target) color pairs across all palette mappings.
//   2. For each non-transparent pixel, scan the color-pair list; if any source color
//      matches within tolerance (<=1 per channel), replace RGB with the target (keep alpha).
//   3. First-match-wins: palette order matters when colors overlap.
//
// LRU cache (cap 250) keyed by "spritePath|recolorsJson". Stores Task<SKBitmap> to dedupe
// concurrent calls (matches the TS Promise-based cache).
//
// Image backend: SkiaSharp (MIT). Uses SKBitmap.GetPixelSpan() for direct BGRA byte access
// (replaces System.Drawing LockBits + Marshal.Copy — SkiaSharp exposes the buffer directly).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LpcSpriteGen.Core.Catalog;
using LpcSpriteGen.Core.Palettes;
using SkiaSharp;

namespace LpcSpriteGen.Core.Rendering;

/// <summary>One (source hex, target hex) pair, parsed to RGB.</summary>
internal readonly struct ColorPair
{
    public readonly byte SR, SG, SB;
    public readonly byte TR, TG, TB;
    public ColorPair(byte sr, byte sg, byte sb, byte tr, byte tg, byte tb)
    { SR = sr; SG = sg; SB = sb; TR = tr; TG = tg; TB = tb; }
}

public sealed class PaletteRecolorService
{
    private const int Tolerance = 1; // per-channel; matches WebGL's 0.004 * 255 ≈ 1.02
    private const int CacheCap = 250;

    private readonly LpcCatalog _catalog;
    private readonly PaletteResolver _paletteResolver;
    // LRU: linked-list of keys + dict mapping key → (node, task). LinkedListNode<string> in both.
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, Task<SKBitmap> Task)> _cache = new();

    public PaletteRecolorService(LpcCatalog catalog)
    {
        _catalog = catalog;
        _paletteResolver = new PaletteResolver(catalog);
    }

    /// <summary>
    /// Apply palette recoloring to <paramref name="src"/> if the item has recolor slots
    /// and <paramref name="recolors"/> maps type_names to target colors. Returns the
    /// original bitmap unchanged when no recolors apply. Cached per (spritePath, recolors).
    /// Port of palette-recolor.ts:getImageToDraw.
    /// </summary>
    public Task<SKBitmap> RecolorAsync(SKBitmap src, string itemId, Dictionary<string, string>? recolors, string? spritePath)
    {
        if (recolors == null || recolors.Count == 0) return Task.FromResult(src);

        var key = spritePath != null ? spritePath + "|" + JsonSerializer.Serialize(recolors) : null;
        if (key != null)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    // LRU touch: move to tail.
                    _lruOrder.Remove(entry.Node);
                    _lruOrder.AddLast(entry.Node);
                    return entry.Task;
                }
            }
        }

        var task = Task.Run(() =>
        {
            try { return RecolorWithPalette(src, itemId, recolors); }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to recolor {itemId} {JsonSerializer.Serialize(recolors)}: {e.Message}");
                return src; // fallback to original
            }
        });

        if (key != null)
        {
            lock (_cache)
            {
                var node = _lruOrder.AddLast(key);
                _cache[key] = (node, task);
                // Evict oldest over cap.
                while (_cache.Count > CacheCap)
                {
                    var oldest = _lruOrder.First!;
                    _lruOrder.RemoveFirst();
                    _cache.Remove(oldest.Value);
                }
            }
            // On failure, drop the cache entry so retries aren't poisoned.
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    lock (_cache)
                    {
                        if (_cache.TryGetValue(key, out var e) && ReferenceEquals(e.Task, task))
                        {
                            _lruOrder.Remove(e.Node);
                            _cache.Remove(key);
                        }
                    }
            });
        }
        return task;
    }

    private SKBitmap RecolorWithPalette(SKBitmap src, string itemId, Dictionary<string, string> recolors)
    {
        var itemR = _catalog.GetItem(itemId);
        if (!itemR.IsOk) return src;
        var palettes = _paletteResolver.GetPalettesFromMeta(itemR.Value.Lite);
        if (palettes.Count == 0) return src;

        // Gather (source, target) color pairs across all recolor mappings.
        var pairs = new List<ColorPair>();
        foreach (var (typeName, palette) in palettes)
        {
            if (!recolors.TryGetValue(typeName, out var targetColor)) continue;
            if (targetColor == "source") continue;

            var targetR = _paletteResolver.GetTargetPalette(palette.Material, targetColor,
                new PaletteRecolor { Material = palette.Material, Default = palette.Version, Base = palette.Source });
            // Some target colors don't apply to a slot's material (e.g. "light" passed
            // to the eyes slot whose material is "eye"). JS throws here but the outer
            // try/catch falls back to the original; here we skip the unmappable slot
            // and continue, which is the same observable behavior (no recolor applied).
            if (!targetR.IsOk)
            {
                Console.Error.WriteLine(
                    $"Skipping recolor slot {typeName}={targetColor} for {itemId}: palette not found");
                continue;
            }

            var sourceColors = palette.Colors;
            var targetColors = targetR.Value;
            // Index-aligned: zip source → target.
            for (int i = 0; i < sourceColors.Length && i < targetColors.Length; i++)
            {
                var s = HexToRgb(sourceColors[i]);
                var t = HexToRgb(targetColors[i]);
                if (s != null && t != null)
                    pairs.Add(new ColorPair(s.Value.r, s.Value.g, s.Value.b, t.Value.r, t.Value.g, t.Value.b));
            }
        }
        if (pairs.Count == 0) return src;
        return RecolorImage(src, pairs);
    }

    /// <summary>Per-pixel recolor. Port of palette-recolor.ts:recolorImageCPU.</summary>
    internal static SKBitmap RecolorImage(SKBitmap src, List<ColorPair> pairs)
    {
        int w = src.Width, h = src.Height;
        var dst = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);

        // Direct span access — SkiaSharp exposes the underlying pixel buffer without
        // pin/unpin or Marshal.Copy round-trips. BGRA byte order (matches the old
        // System.Drawing Format32bppArgb layout: B,G,R,A in memory on little-endian).
        var srcSpan = src.GetPixelSpan();
        var dstSpan = dst.GetPixelSpan();
        // Span is typed as byte; pixels are 4 bytes apart (BGRA).
        int bytes = srcSpan.Length;
        for (int i = 0; i < bytes; i += 4)
        {
            byte b = srcSpan[i], g = srcSpan[i + 1], r = srcSpan[i + 2], a = srcSpan[i + 3];
            if (a == 0) // transparent pixels untouched
            {
                dstSpan[i] = b; dstSpan[i + 1] = g; dstSpan[i + 2] = r; dstSpan[i + 3] = a;
                continue;
            }
            // First-match-wins scan.
            bool matched = false;
            foreach (var p in pairs)
            {
                if (Math.Abs(r - p.SR) <= Tolerance &&
                    Math.Abs(g - p.SG) <= Tolerance &&
                    Math.Abs(b - p.SB) <= Tolerance)
                {
                    dstSpan[i] = p.TB; dstSpan[i + 1] = p.TG; dstSpan[i + 2] = p.TR;
                    dstSpan[i + 3] = a; // alpha unchanged
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                dstSpan[i] = b; dstSpan[i + 1] = g; dstSpan[i + 2] = r; dstSpan[i + 3] = a;
            }
        }
        return dst;
    }

    private static (byte r, byte g, byte b)? HexToRgb(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        var s = hex.TrimStart('#');
        if (s.Length != 6) return null;
        if (!int.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var n))
            return null;
        byte r = (byte)((n >> 16) & 0xff);
        byte g = (byte)((n >> 8) & 0xff);
        byte b = (byte)(n & 0xff);
        return (r, g, b);
    }
}
