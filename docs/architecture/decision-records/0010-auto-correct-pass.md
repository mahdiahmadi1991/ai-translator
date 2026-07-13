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

Add an **auto-correct pass** (`autoCorrect`, default on) that proof-reads the compose box.

- It runs on **everything in the box**, not only dictation, because typing has typos too. It fires
  after dictation stops (so the corrected text is what the user sees and can edit) and again before a
  translation if the text changed since the last correction, so pressing Translate straight after
  dictating costs nothing extra.
- `ITextCorrector` (Core) is the seam; `OpenAiTextCorrector` (Infrastructure) is one short Responses
  call that isolates the provider, as the translation client does. It reuses the configured `model`,
  so there is no new setting to keep in sync.
- The instruction lives in Core (`CorrectionPromptBuilder`) with unit tests, because it is
  load-bearing.

**It corrects spelling, never style.** This is the rule that lets it run on everything safely: style is
owned by the rewrite styles ([ADR-0007](0007-rewrite-styles-and-humanizer.md)), and in testing an
earlier prompt quietly formalized colloquial Persian ("رو" to "را"), which would have fought the user's
chosen style. The prompt now forbids that outright, and also protects URLs, paths, code, commands, and
identifiers.

**It is best-effort and never destructive.** A failure keeps the user's text exactly as it was and does
not block the translation; an empty model response is discarded rather than wiping the box.

## Alternatives considered

- **Fold the correction into the translation prompt.** Free (no extra call), but the box would still
  show the garbled text; the user reported what they *saw*, and they need to be able to verify and edit
  it before sending.
- **A second, more accurate transcription pass on stop.** Measured: the file models are not better on
  the same audio. It would have cost more for nothing.
- **Correct dictation only.** The owner asked for all text, on the grounds that typing has errors too,
  and the same pass handles both.

## Consequences

- One short model call per correction (about 0.7 to 2.5 s), skipped when the text is unchanged, and
  skipped entirely when `autoCorrect` is off.
- The box text can change under the user right before a translation. That is intended: they see exactly
  what will be translated, and can still edit it.
- The prompt is load-bearing. Changing it should be re-checked against real failures, the same
  discipline the rewrite-style prompts follow.
