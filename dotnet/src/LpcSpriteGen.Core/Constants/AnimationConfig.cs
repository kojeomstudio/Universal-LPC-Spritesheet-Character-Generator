// Animation tables — ANIMATIONS, ANIMATION_DEFAULTS, ANIMATION_OFFSETS, ANIMATION_CONFIGS.
// Port of sources/state/constants.ts. The folderName remapping (combat → combat_idle etc.)
// is critical for sprite path resolution.
namespace LpcSpriteGen.Core.Constants;

public sealed record AnimationEntry(
    string Value,
    string Label,
    string? FolderName = null,
    bool NoExport = false);

public sealed record AnimationConfig(int Row, int Num, int[] Cycle);

public static class AnimationTables
{
    /// <summary>
    /// Animation list used for filters and preview. The 4 entries with FolderName
    /// remap the value to a different on-disk folder name during sprite path resolution
    /// (see SpritePathResolver). All other entries use Value as the folder name.
    /// </summary>
    public static readonly AnimationEntry[] Animations =
    {
        new("spellcast", "Spellcast"),
        new("thrust", "Thrust"),
        new("walk", "Walk"),
        new("slash", "Slash"),
        new("shoot", "Shoot"),
        new("hurt", "Hurt"),
        new("climb", "Climb"),
        new("idle", "Idle"),
        new("jump", "Jump"),
        new("sit", "Sit"),
        new("emote", "Emote"),
        new("run", "Run"),
        new("watering", "Watering", NoExport: true),
        new("combat", "Combat Idle", FolderName: "combat_idle"),
        new("1h_slash", "1-Handed Slash", FolderName: "backslash", NoExport: true),
        new("1h_backslash", "1-Handed Backslash", FolderName: "backslash"),
        new("1h_halfslash", "1-Handed Halfslash", FolderName: "halfslash"),
    };

    /// <summary>Default animations when a sheet definition omits an explicit list.</summary>
    public static readonly string[] AnimationDefaults =
        { "spellcast", "thrust", "walk", "slash", "shoot", "hurt", "watering" };

    /// <summary>
    /// Y-pixel offset (top of the animation block) on the spritesheet, keyed by
    /// <em>folderName-form</em> (note: keys are combat_idle/backslash/halfslash, not
    /// combat/1h_slash/etc.). Matches ANIMATION_OFFSETS in constants.ts.
    /// </summary>
    public static readonly Dictionary<string, int> AnimationOffsets = new()
    {
        ["spellcast"] = 0,
        ["thrust"] = 4 * LpcConstants.FrameSize,    // 256
        ["walk"] = 8 * LpcConstants.FrameSize,      // 512
        ["slash"] = 12 * LpcConstants.FrameSize,    // 768
        ["shoot"] = 16 * LpcConstants.FrameSize,    // 1024
        ["hurt"] = 20 * LpcConstants.FrameSize,     // 1280
        ["climb"] = 21 * LpcConstants.FrameSize,    // 1344
        ["idle"] = 22 * LpcConstants.FrameSize,     // 1408
        ["jump"] = 26 * LpcConstants.FrameSize,     // 1664
        ["sit"] = 30 * LpcConstants.FrameSize,      // 1920
        ["emote"] = 34 * LpcConstants.FrameSize,    // 2176
        ["run"] = 38 * LpcConstants.FrameSize,      // 2432
        ["combat_idle"] = 42 * LpcConstants.FrameSize, // 2688
        ["backslash"] = 46 * LpcConstants.FrameSize,   // 2944
        ["halfslash"] = 50 * LpcConstants.FrameSize,   // 3200
    };

    /// <summary>
    /// Per-animation row/num/cycle for the preview loop. Keyed by value-form (combat,
    /// 1h_slash, etc.). Matches ANIMATION_CONFIGS in constants.ts.
    /// </summary>
    public static readonly Dictionary<string, AnimationConfig> AnimationConfigs = new()
    {
        ["spellcast"] = new(0, 4, new[] { 0, 1, 2, 3, 4, 5, 6 }),
        ["thrust"] = new(4, 4, new[] { 0, 1, 2, 3, 4, 5, 6, 7 }),
        ["walk"] = new(8, 4, new[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
        ["slash"] = new(12, 4, new[] { 0, 1, 2, 3, 4, 5 }),
        ["shoot"] = new(16, 4, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }),
        ["hurt"] = new(20, 1, new[] { 0, 1, 2, 3, 4, 5 }),
        ["climb"] = new(21, 1, new[] { 0, 1, 2, 3, 4, 5 }),
        ["idle"] = new(22, 4, new[] { 0, 0, 1 }),
        ["jump"] = new(26, 4, new[] { 0, 1, 2, 3, 4, 1 }),
        ["sit"] = new(30, 4, new[] { 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2 }),
        ["emote"] = new(34, 4, new[] { 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2 }),
        ["run"] = new(38, 4, new[] { 0, 1, 2, 3, 4, 5, 6, 7 }),
        ["watering"] = new(4, 4, new[] { 0, 1, 4, 4, 4, 4, 5 }),
        ["combat"] = new(42, 4, new[] { 0, 0, 1 }),
        ["1h_slash"] = new(46, 4, new[] { 0, 1, 2, 3, 4, 5, 6 }),
        ["1h_backslash"] = new(46, 4, new[] { 0, 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12 }),
        ["1h_halfslash"] = new(50, 4, new[] { 0, 1, 2, 3, 4, 5 }),
    };

    /// <summary>
    /// Remap an animation value (e.g. "combat") to its on-disk folder name (e.g. "combat_idle").
    /// For values without a FolderName, the value itself is the folder name.
    /// Port of path.ts:150-153 remap logic.
    /// </summary>
    public static string ResolveFolderName(string animValue)
    {
        foreach (var a in Animations)
            if (a.Value == animValue)
                return a.FolderName ?? a.Value;
        return animValue;
    }
}
