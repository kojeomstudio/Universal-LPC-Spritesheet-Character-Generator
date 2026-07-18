// Custom-animation definitions — oversized weapons, wheelchair, tools. Port of
// sources/custom-animations.ts. Each entry's frames grid references rows on the
// standard sheet via "rowName,columnIndex" specs (e.g. "sit-n,2").
//
// NOTE: class is named CustomAnimationTables (not CustomAnimations) to avoid the
// C# class-vs-namespace collision when both share the name "CustomAnimations".
namespace LpcSpriteGen.Core.CustomAnimations;

/// <summary>One oversized/custom animation. Frames[r][c] = "rowName,columnIndex".</summary>
public sealed record CustomAnimationDefinition(
    int FrameSize,
    string[][] Frames,
    bool SkipFirstFrameInPreview = false);

public static class CustomAnimationTables
{
    /// <summary>
    /// Standard-sheet row index for a given "anim-direction" token. Port of
    /// animationRowsLayout. Used by DrawFrames when re-gridding standard sheet frames into
    /// a custom-animation area.
    /// </summary>
    public static readonly Dictionary<string, int> AnimationRowsLayout = new()
    {
        ["thrust-n"] = 3, ["thrust-w"] = 4, ["thrust-s"] = 5, ["thrust-e"] = 6,
        ["walk-n"] = 7, ["walk-w"] = 8, ["walk-s"] = 9, ["walk-e"] = 10,
        ["slash-n"] = 11, ["slash-w"] = 12, ["slash-s"] = 13, ["slash-e"] = 14,
        ["backslash-n"] = 45, ["backslash-w"] = 46, ["backslash-s"] = 47, ["backslash-e"] = 48,
        ["halfslash-n"] = 49, ["halfslash-w"] = 50, ["halfslash-s"] = 51, ["halfslash-e"] = 52,
        ["sit-n"] = 29, ["sit-w"] = 30, ["sit-s"] = 31, ["sit-e"] = 32,
    };

    /// <summary>n/w/s/e direction → row index within a single-animation sheet.</summary>
    public static readonly Dictionary<string, int> DirectionMap = new()
    {
        ["n"] = 0, ["w"] = 1, ["s"] = 2, ["e"] = 3,
    };

    public static readonly Dictionary<string, CustomAnimationDefinition> Definitions = new()
    {
        ["wheelchair"] = new(64, new[]
        {
            new[] { "sit-n,2", "sit-n,2" },
            new[] { "sit-w,2", "sit-w,2" },
            new[] { "sit-s,2", "sit-s,2" },
            new[] { "sit-e,2", "sit-e,2" },
        }),
        ["tool_rod"] = new(128, new[]
        {
            ThrustRow("n"), ThrustRow("w"), ThrustRow("s"), ThrustRow("e"),
        }),
        ["slash_128"] = new(128, new[]
        {
            SlashRow6("n"), SlashRow6("w"), SlashRow6("s"), SlashRow6("e"),
        }),
        ["backslash_128"] = new(128, new[]
        {
            BackslashRow13("n"), BackslashRow13("w"), BackslashRow13("s"), BackslashRow13("e"),
        }),
        ["halfslash_128"] = new(128, new[]
        {
            HalfslashRow6("n"), HalfslashRow6("w"), HalfslashRow6("s"), HalfslashRow6("e"),
        }),
        ["thrust_oversize"] = new(192, new[]
        {
            ThrustRow8("n"), ThrustRow8("w"), ThrustRow8("s"), ThrustRow8("e"),
        }),
        ["slash_oversize"] = new(192, new[]
        {
            SlashRow6("n"), SlashRow6("w"), SlashRow6("s"), SlashRow6("e"),
        }),
        ["walk_128"] = new(128, new[]
        {
            WalkRow9("n"), WalkRow9("w"), WalkRow9("s"), WalkRow9("e"),
        }, SkipFirstFrameInPreview: true),
        ["thrust_128"] = new(128, new[]
        {
            ThrustRow8("n"), ThrustRow8("w"), ThrustRow8("s"), ThrustRow8("e"),
        }),
        ["slash_reverse_oversize"] = new(192, new[]
        {
            SlashRowReverse6("n"), SlashRowReverse6("w"), SlashRowReverse6("s"), SlashRowReverse6("e"),
        }),
        ["whip_oversize"] = new(192, new[]
        {
            WhipRow("n"), WhipRow("w"), WhipRowS("s"), WhipRow("e"),
        }),
        ["tool_whip"] = new(192, new[]
        {
            WhipRow("n"), WhipRow("w"), WhipRow("s"), WhipRow("e"),
        }),
    };

    /// <summary>Width/height of a custom-animation's destination area on the sheet.</summary>
    public static (int Width, int Height) GetSize(CustomAnimationDefinition def)
        => (def.FrameSize * def.Frames[0].Length, def.FrameSize * def.Frames.Length);

    /// <summary>
    /// Derive the base animation name (e.g. "sit", "thrust", "slash") used to extract
    /// source frames from the standard sheet. Port of customAnimationBase:
    /// takes the substring before the first "-" of the substring before the first ","
    /// in frames[0][0]. E.g. "sit-n,2" → "sit", "thrust-n,0" → "thrust".
    /// </summary>
    public static string GetBaseAnimation(CustomAnimationDefinition def)
    {
        var first = def.Frames[0][0];
        var comma = first.IndexOf(',');
        var head = comma >= 0 ? first[..comma] : first;
        var dash = head.IndexOf('-');
        return dash >= 0 ? head[..dash] : head;
    }

    // --- frame-row builders (keep data table compact, mirrors the TS source verbatim) ---

    private static string[] ThrustRow(string d) =>
        new[]
        {
            $"thrust-{d},0", $"thrust-{d},1", $"thrust-{d},2", $"thrust-{d},3",
            $"thrust-{d},4", $"thrust-{d},5", $"thrust-{d},4", $"thrust-{d},4",
            $"thrust-{d},4", $"thrust-{d},5", $"thrust-{d},4", $"thrust-{d},2",
            $"thrust-{d},3",
        };

    private static string[] ThrustRow8(string d) =>
        new[]
        {
            $"thrust-{d},0", $"thrust-{d},1", $"thrust-{d},2", $"thrust-{d},3",
            $"thrust-{d},4", $"thrust-{d},5", $"thrust-{d},6", $"thrust-{d},7",
        };

    private static string[] SlashRow6(string d) =>
        new[]
        {
            $"slash-{d},0", $"slash-{d},1", $"slash-{d},2",
            $"slash-{d},3", $"slash-{d},4", $"slash-{d},5",
        };

    private static string[] SlashRowReverse6(string d) =>
        new[]
        {
            $"slash-{d},5", $"slash-{d},4", $"slash-{d},3",
            $"slash-{d},2", $"slash-{d},1", $"slash-{d},0",
        };

    private static string[] BackslashRow13(string d) =>
        new[]
        {
            $"backslash-{d},0", $"backslash-{d},1", $"backslash-{d},2", $"backslash-{d},3",
            $"backslash-{d},4", $"backslash-{d},5", $"backslash-{d},6", $"backslash-{d},7",
            $"backslash-{d},8", $"backslash-{d},9", $"backslash-{d},10", $"backslash-{d},11",
            $"backslash-{d},12",
        };

    private static string[] HalfslashRow6(string d) =>
        new[]
        {
            $"halfslash-{d},0", $"halfslash-{d},1", $"halfslash-{d},2",
            $"halfslash-{d},3", $"halfslash-{d},4", $"halfslash-{d},5",
        };

    private static string[] WalkRow9(string d) =>
        new[]
        {
            $"walk-{d},0", $"walk-{d},1", $"walk-{d},2", $"walk-{d},3", $"walk-{d},4",
            $"walk-{d},5", $"walk-{d},6", $"walk-{d},7", $"walk-{d},8",
        };

    // whip_oversize has an asymmetric 's' row (one cell is "slash-w,1" not "slash-s,1")
    private static string[] WhipRow(string d) =>
        new[]
        {
            $"slash-{d},0", $"slash-{d},1", $"slash-{d},4", $"slash-{d},5",
            $"slash-{d},3", $"slash-{d},2", $"slash-{d},2", $"slash-{d},1",
        };

    private static string[] WhipRowS(string d) =>
        new[]
        {
            $"slash-{d},0", $"slash-{d},1", $"slash-{d},5", $"slash-{d},4",
            $"slash-{d},3", $"slash-{d},3", $"slash-{d},2", "slash-w,1",
        };
}
