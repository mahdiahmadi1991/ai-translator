# ADR-0010: Auto-correct the compose box before translating

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-07-13

## Context

Dictation ([ADR-0009](0009-speech-to-text-dictation.md)) shipped, and real use showed two failures the
recognizer will not fix on its own:

1. **Garbled words.** A quickly-spoken Persian compound comes back mangled: "پیاده‌سازی" was
   transcribed as "پیازسی".
2. **Transliteration.** English technical terms spoken inside a Persian sentence are written in Persian
   letters: "PR" becomes "پیار", "review" becomes "ریویو", "deploy" becomes "دیپلوی".

Typing has the same class of problem, just from fingers instead of a microphone.

## Evidence

A systematic investigation ruled out, with measurements against the live API, every explanation that
lived in the audio or the recognizer:

| Suspected cause | Result |
| --- | --- |
| Capture path (legacy `waveIn` at 24 kHz vs WASAPI native + anti-aliased resample) | No difference |
| `delay` (medium vs high) | No meaningful difference |
| Streaming audio before `session.updated` lands | No difference |
| Padding the utterance with leading/trailing silence | No difference |
| A file-based model instead (`gpt-4o-transcribe`, `gpt-4o-mini-transcribe`) | Not more accurate |
| `prompt` (vocabulary biasing) on the realtime model | **Rejected by the API**: "The 'prompt' parameter is not supported for this model." |

The words are *heard* correctly. The defect is in how they are *written*, and the speech-to-text layer
offers no lever to change that. So the fix cannot live there.

A text pass, on the other hand, recovers both cases from context, and was verified against the real
failing text: "پیازسی" becomes "پیاده‌سازی", "پیار" becomes "PR", "دولوپ" becomes "develop", and
mistyped English ("helo", "plese", "tomorow") is fixed too.

## Decision

Add an **auto-correct pass** (`autoCorrect`, default on) that covers **everything the user writes**, not
only dictation, because typing has typos too. It takes two forms, chosen by whether the user will *see*
the source again (see the [amendment](#amendment-2026-07-13-correction-on-the-translate-path-is-folded-into-the-translation-call)):

- **After dictation stops** it runs as a **standalone proof-read** of the box, because there the whole
  point is that the corrected text is what the user reads, verifies, and edits.
- **On the translate path** it is **folded into the translation call itself** (`TranslationRequest.
  CorrectSource` adds a source-repair layer to the prompt). The box is cleared on success and never read
  again, so a separate pass only bought the user a second wait.

Supporting pieces:

- `ITextCorrector` (Core) is the seam; `OpenAiTextCorrector` (Infrastructure) is one short Responses
  call that isolates the provider, as the translation client does. It reuses the configured `model`,
  so there is no new setting to keep in sync.
- The instructions live in Core with unit tests, because they are load-bearing:
  `CorrectionPromptBuilder` (standalone pass) and `PromptBuilder`'s source-repair layer (folded pass).

**It corrects spelling, never style.** This is the rule that lets it run on everything safely: style is
owned by the rewrite styles ([ADR-0007](0007-rewrite-styles-and-humanizer.md)), and in testing an
earlier prompt quietly formalized colloquial Persian ("رو" to "را"), which would have fought the user's
chosen style. The prompt now forbids that outright, and also protects URLs, paths, code, commands, and
identifiers.

**It is best-effort and never destructive.** A failure keeps the user's text exactly as it was and does
not block the translation; an empty model response is discarded rather than wiping the box.

## Alternatives considered

- **Fold the correction into the translation prompt everywhere.** Rejected for the dictation path: the
  box would still show the garbled text, and the user reported what they *saw*. Adopted for the translate
  path, where nobody ever sees the source again (see the amendment below).
- **A second, more accurate transcription pass on stop.** Measured: the file models are not better on
  the same audio. It would have cost more for nothing.
- **Correct dictation only.** The owner asked for all text, on the grounds that typing has errors too,
  and both forms of the pass handle both.

## Consequences

- Dictation costs one short model call on stop (about 0.7 to 2.5 s). Translation costs **no extra call
  at all**, in any case.
- After dictation the box text changes under the user. That is intended: they see exactly what will be
  translated. The box stays **editable** while it happens, and if they start editing, their version wins
  and the correction is dropped rather than overwriting it.
- The prompts are load-bearing. Changing either should be re-checked against real failures, the same
  discipline the rewrite-style prompts follow.

## Amendment (2026-07-13): correction on the translate path is folded into the translation call

The first implementation ran the standalone corrector *before* every translation as well. The owner
reported the compose box had become slow, and measurement against the live API agreed:

| Sample | correct | translate | total |
| --- | --- | --- | --- |
| "سلام لطفا گزارش پروژه رو تا فردا برام بفرست" | 3592 ms | 1185 ms | 4777 ms |
| "بهترین ایده برای پیازسی این پروژه…" | 1267 ms | 1460 ms | 2727 ms |

The proof-read was adding **87% to 303%** on top of the translation, to fix mistakes the translator then
had to read through anyway, in a box that is cleared the moment the translation is injected. So on that
path the correction now rides along inside the translation prompt (one round-trip, not two).

An A/B against the live API confirmed there is no quality cost. Same four samples, same style, both
paths:

| Source | Two calls (correct, then translate) | One folded call |
| --- | --- | --- |
| "…برای پیازسی این پروژه…" | "the best approach for **implementing** this project" | "the best idea for **implementing** this project" |
| "plese revew the merg reqest…" | "درخواست مرج را بازبینی کن…" | "درخواست مرج را بررسی کنید…" |

Both recover the garbled compound; neither carries a typo into the output. The folded call was also
19% to 36% faster end to end.

The standalone corrector is **kept** for dictation, unchanged, for the reason the original ADR gave: the
user has to be able to read and fix what the microphone heard.
