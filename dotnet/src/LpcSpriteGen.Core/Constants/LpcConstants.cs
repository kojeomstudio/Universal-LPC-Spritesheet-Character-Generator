// Constants used throughout LPC Sprite Generator.
// Direct port of sources/state/constants.ts. Values must remain identical.
namespace LpcSpriteGen.Core.Constants;

/// <summary>Frame layout, body types, directions, licenses.</summary>
public static class LpcConstants
{
    /// <summary>Pixel size of each square sprite cell on the LPC sheet.</summary>
    public const int FrameSize = 64;

    /// <summary>Compact preview cell size.</summary>
    public const int CompactFrameSize = 32;

    /// <summary>Frames per row on the standard universal LPC sheet (13 × FrameSize wide).</summary>
    public const int StandardAnimationFramesPerRow = 13;

    /// <summary>Sheet width = 13 × 64 = 832px. Matches SHEET_WIDTH in renderer.ts.</summary>
    public const int SheetWidth = StandardAnimationFramesPerRow * FrameSize; // 832

    /// <summary>
    /// Standard sheet height for the built-in animations. Custom animations (wheelchair,
    /// oversize weapons) are appended below this Y offset. Matches SHEET_HEIGHT in renderer.ts.
    /// </summary>
    public const int SheetHeight = 3456;

    /// <summary>Body types supported by sheet metadata and UI selectors.</summary>
    public static readonly string[] BodyTypes =
        { "male", "female", "teen", "child", "muscular", "pregnant" };

    /// <summary>LPC sheet row order: up, left, down, right.</summary>
    public static readonly string[] Directions = { "up", "left", "down", "right" };

    /// <summary>Map a body type to its gender for head selection.</summary>
    public static bool IsMaleBodyType(string bodyType) =>
        bodyType is "male" or "muscular";

    /// <summary>Female-presentation body types.</summary>
    public static bool IsFemaleBodyType(string bodyType) =>
        bodyType is "female" or "pregnant";
}
