// Image loader — disk-backed cache of decoded sprite PNGs, plus parallel-load helper.
// Port of sources/canvas/load-image.ts.
//
// On disk: spritesheets/<basePath>/<anim>[/<variant>].png — each file is a 832px-wide
// strip containing one animation (4 rows of 13 frames). Decoded to SKBitmap (BGRA).
//
// Image backend: SkiaSharp (MIT) — cross-platform, replaces System.Drawing.Common.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;

namespace LpcSpriteGen.Core.Rendering;

public sealed class ImageLoader
{
    private readonly string _spritesheetsDir;
    private readonly ConcurrentDictionary<string, SKBitmap> _cache = new();
    private readonly ConcurrentDictionary<string, Task<SKBitmap?>> _inFlight = new();

    public ImageLoader(string spritesheetsDir) => _spritesheetsDir = spritesheetsDir;

    /// <summary>The on-disk spritesheets root, e.g. .../tools/lpc-sprite-generator/spritesheets.</summary>
    public string SpritesheetsDir => _spritesheetsDir;

    /// <summary>
    /// Load a sprite by its catalog-relative path. The <paramref name="spritePath"/>
    /// begins with "spritesheets/" — that prefix is stripped before resolving on disk.
    /// Returns null on missing/corrupt file (swallowed error, matches TS behavior).
    /// Port of load-image.ts:loadImage.
    /// </summary>
    public Task<SKBitmap?> LoadAsync(string spritePath)
    {
        if (_cache.TryGetValue(spritePath, out var cached))
            return Task.FromResult<SKBitmap?>(cached);

        if (_inFlight.TryGetValue(spritePath, out var existing))
            return existing;

        var t = Task.Run(() =>
        {
            try
            {
                var rel = spritePath.StartsWith("spritesheets/", StringComparison.Ordinal)
                    ? spritePath["spritesheets/".Length..]
                    : spritePath;
                var full = Path.Combine(_spritesheetsDir, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    Console.Error.WriteLine($"Failed to load image: {spritePath} (file not found)");
                    return (SKBitmap?)null;
                }
                using var fs = File.OpenRead(full);
                // SkiaSharp always decodes to the bitmap's native format; we force BGRA8888
                // via SKBitmap.Decode + normalization below for consistent pixel access.
                var bmp = SKBitmap.Decode(fs);
                if (bmp == null)
                {
                    Console.Error.WriteLine($"Failed to decode image: {spritePath}");
                    return (SKBitmap?)null;
                }
                // Normalize to BGRA8888 (32bpp with alpha) for consistent span access
                // across all decoder output formats (equivalent to old Format32bppArgb).
                if (bmp.ColorType != SKColorType.Bgra8888)
                {
                    var normalized = new SKBitmap(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using (var canvas = new SKCanvas(normalized))
                    {
                        canvas.DrawBitmap(bmp, 0, 0);
                    }
                    bmp.Dispose();
                    bmp = normalized;
                }
                _cache[spritePath] = bmp;
                return (SKBitmap?)bmp;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to load image: {spritePath} ({e.Message})");
                return (SKBitmap?)null;
            }
        });

        // Dedupe: only the first task wins; later callers reuse it.
        if (!_inFlight.TryAdd(spritePath, t))
            return _inFlight[spritePath];
        // Once complete, the in-flight slot can be cleared (cache holds the final value).
        t.ContinueWith(_ => _inFlight.TryRemove(spritePath, out _));
        return t;
    }

    /// <summary>
    /// Load many items in parallel; per-item failures return null entries.
    /// Port of load-image.ts:loadImagesInParallel.
    /// </summary>
    public async Task<List<(T Item, SKBitmap? Bitmap)>> LoadParallelAsync<T>(
        IEnumerable<T> items, Func<T, string> pathSelector)
    {
        var list = new List<(T, Task<SKBitmap?>)>();
        foreach (var item in items)
            list.Add((item, LoadAsync(pathSelector(item))));
        var result = new List<(T, SKBitmap?)>(list.Count);
        foreach (var (item, task) in list)
            result.Add((item, await task));
        return result;
    }

    public void ClearCache()
    {
        foreach (var (_, bmp) in _cache) bmp.Dispose();
        _cache.Clear();
    }
}
