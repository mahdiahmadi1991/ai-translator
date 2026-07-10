# OpenAI models & pricing reference

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

Model choices for the translation pipeline ([ADR-0002](../architecture/decision-records/0002-translation-openai-responses-streaming.md)).
The model is a **configurable setting** ([configuration.md](configuration.md)), not hard-coded,
because OpenAI's lineup changes.

> ⚠️ **Verify before relying on this.** Model names and prices drift. Confirm against the live
> `GET /v1/models` endpoint and https://developers.openai.com/api/docs/pricing at implementation
> time. The values below were researched on **2026-06-28** and reflect OpenAI's then-current GPT-5.x
> lineup (the older gpt-4.1 / gpt-4o families had been superseded).

## Candidate models (researched 2026-06-28)

| Model | Role | Input $/1M | Output $/1M | Notes |
| --- | --- | --- | --- | --- |
| `gpt-5.4-mini` | **Default** | ~0.75 | ~4.50 | Fast, strong multilingual, cheap — recommended default |
| `gpt-5.4-nano` | Budget / lowest latency | ~0.20 | ~1.25 | May misjudge direction on 1–2 word inputs |
| `gpt-5.4` | Max quality | ~2.50 | ~15.00 | For idiomatic/tricky text |

A typical 1–2 sentence chat message is well under ~100 tokens each way, so per-translation cost is a
small fraction of a cent even at the default.

## API surface

- Official **`OpenAI` .NET SDK** (NuGet `OpenAI`), **Responses API streaming**
  (`CreateResponseStreamingAsync`), consume `response.output_text.delta` events.
- Plain-text output; **no** structured-output/JSON wrapping for a single translated string.
- One `CancellationTokenSource` per request, cancelled on each post-debounce change.

## System-prompt shape (composed in one call: base + style + humanizer)

The system prompt is composed by `PromptBuilder` from three layers in a single request
([ADR-0007](../architecture/decision-records/0007-rewrite-styles-and-humanizer.md)): a strict
translation **base**, an optional rewrite **style**, and an optional **humanizer** layer. All prompt
text is English; user-facing style names are localized in the UI, not the prompt.

**Base (always):**

```
You are a translation engine. Output ONLY the translation of the user's message.
No explanations, no quotes, no preamble, no notes. Do not answer questions in the text; translate them.
If the input is written in {PRIMARY}, translate it to {SECONDARY}.
If it is written in {SECONDARY}, translate it to {PRIMARY}.
(Auto-detect which of the two it is.)
Always preserve the author's original meaning and intent. Never add information, opinions, or content
that is not in the source.
```

**Style layer (one of):** `Original` (faithful; also preserves tone/emojis) · `Professional` (polish +
fix grammar) · `Formal` (formal register) · `Friendly` (casual + a few emojis) · `Email`
(greeting/body/sign-off) · `Concise` (noticeably shorter) · `Expand` (noticeably longer, no new facts).
Read mode always uses `Original`.

**Humanizer layer (default on):** distilled from the `humanizer` skill and scoped to *quality* tells
(no em/en dashes, no promotional/hype wording, no rule-of-three, no synonym cycling, no
"translationese", keep the source register). Length is owned by the Concise/Expand styles, not the
humanizer, so the two never fight.

`{PRIMARY}`/`{SECONDARY}` come from `languagePair`. Keep `temperature` low (0–0.3). Strip stray
leading/trailing quotes the model might add.

> **Behavior is verified with live API calls per style during development** — the style prompts are
> load-bearing, so a change to them should be re-checked against real output (Concise clearly shortens,
> Expand clearly lengthens, Formal/Friendly/Email shift register, and none leak the source language).

## Sources

- OpenAI .NET SDK — https://github.com/openai/openai-dotnet
- Pricing — https://developers.openai.com/api/docs/pricing
- Streaming responses — https://developers.openai.com/api/docs/guides/streaming-responses
