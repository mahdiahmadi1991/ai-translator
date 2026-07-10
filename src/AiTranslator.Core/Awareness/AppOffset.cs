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
    /// Fallback for an app with no calibration: zero nudge. The badge anchors just inside the field's
    /// right edge, vertically centred (Grammarly-style); <see cref="Dx"/>/<see cref="Dy"/> only fine-tune
    /// that per app. <see cref="Corner"/> is reserved for future anchor variants.
    /// </summary>
    public static AppOffset Default { get; } = new(Corner: 1, Dx: 0, Dy: 0);
}
