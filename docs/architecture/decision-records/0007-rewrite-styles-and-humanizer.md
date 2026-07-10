# ADR-0007: AI rewrite styles and human-sounding output (single-call prompt composition)

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-07-10

## Context

The write-mode floating box translates typed text into the focused field. Users want the AI to do
more than a literal rendering: sometimes make the message formal, casual, email-shaped, shorter, or
more elaborate — while still going into the messenger in the target language. Users also want the
output to read like a person wrote it, not like a machine translation, on every translation.

A naïve implementation would run a second "improve" pass after translating. That doubles latency and
cost and risks the two passes disagreeing.

## Decision

**One model call does everything.** The system prompt is composed from three layers:

1. **Base** — the existing strict translation-only instruction (output only the translation, preserve
   meaning, auto-detect direction within the configured pair).
2. **Style** — an optional instruction selected by the user in the box footer. Exactly one style
   applies per translation (`TranslationStyle` enum):
   - `Original` (default) — faithful, standard translation, no manipulation.
   - `Professional` — polished / rephrased: clearer, more fluent, awkwardness removed.
   - `Formal` — formal, respectful register and sentence structure.
   - `Friendly` — warm, casual, conversational, **with a few relevant emojis**.
   - `Email` — greeting + body + polite sign-off.
   - `Concise` — trimmed to the essential message.
   - `Expand` — naturally elaborated, **without inventing new facts**.
3. **Humanizer** — an optional layer (default on) distilled from the `humanizer` skill
   ("Signs of AI writing"), scoped to short messaging text: no em/en dashes, no filler/hedging, no
   inflated or promotional phrasing, no forced rule-of-three or synonym cycling, no stiff
   "translationese", match the source register, no meta-commentary or wrapping quotes. The skill's
   "avoid emojis" rule is deliberately dropped here because casual chat (and the `Friendly` style)
   legitimately uses them.

**Authenticity is a hard rule across every style:** the model must preserve the user's original
meaning and intent and never add content of its own. There is no separate "improve" checkbox — the
`Professional` style covers polishing, and authenticity is enforced by the prompt for all styles.

The request contract becomes a value object, `TranslationRequest(Text, Direction, Model, Style,
Humanize)`, and `ITranslationService.TranslateStreamAsync` takes it. This keeps the streaming
signature stable as options grow and lets the caching decorator (ADR-scope of the cache) key on style
and humanize so different styles are cached independently.

The selected style **persists** (`AppSettings.RewriteStyle`) so the box reopens in the last-used
style. Humanizer is a persisted toggle (`AppSettings.HumanizeTranslations`, default `true`) surfaced
in Settings. Styles are a write-mode (compose) affordance; read-mode (translate a selection) always
uses `Original` + humanizer.

## Alternatives considered

- **Two calls (translate, then restyle/humanize).** Rejected: 2× latency and cost, and the passes can
  disagree. Prompt composition in a single call is cheaper and more coherent.
- **A separate "improve/fix" checkbox orthogonal to tone.** Rejected for UI clutter; folded into the
  `Professional` style, with authenticity enforced for every style instead.
- **A standalone "add emoji" style.** Rejected per the owner's call: emojis ride along with the
  `Friendly` tone rather than being a separate option.
- **Running the actual `humanizer` skill at runtime.** Not possible — it is a Claude Code authoring
  skill, not a library. Its guidance is distilled into the prompt instead.

## Consequences

- Prompt quality is load-bearing: each style must reliably produce the intended shape. The prompt
  strings live in `PromptBuilder` and are verified with real API calls per style during development.
- The caching key includes `Style` and `Humanize`; switching styles is a cache miss (correct).
- The prompt-mode strings are English (the source language of all app strings); user-facing style
  names are localized in the UI catalog, not in the prompt.
- Interface change (`TranslationRequest`) touches both windows, the OpenAI service, the cache
  decorator, and their tests, but isolates all future per-request options behind one type.

## Sources

- `humanizer` skill (based on Wikipedia: Signs of AI writing) — distilled, messaging-scoped.
- Builds on [0002](0002-translation-openai-responses-streaming.md) (single-turn streaming, plain-text,
  configurable model) — this ADR only enriches the system prompt and the request shape.
