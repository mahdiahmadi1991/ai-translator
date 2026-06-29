namespace AiTranslator.Core.Awareness;

/// <summary>
/// Per-app calibration for where the badge anchors relative to the focused field's rectangle.
/// <paramref name="Corner"/> selects which corner of the field rect to anchor from (the enum used by
/// the badge anchoring code in M2 Task 4); <paramref name="Dx"/>/<paramref name="Dy"/> nudge in DIPs.
/// Mirrors Grammarly's per-app <c>ButtonPositions.json</c>. Serialized under <c>appOffsets</c>
/// (see docs/reference/configuration.md).
/// </summary>
public sealed record AppOffset(int Corner, int Dx, int Dy)
{
    /// <summary>
    /// Fallback for an app with no calibration: bottom-right of the field, nudged in a little
    /// (matches the documented example and the overview's "bottom-right corner by default").
    /// </summary>
    public static AppOffset Default { get; } = new(Corner: 1, Dx: 64, Dy: -6);
}
