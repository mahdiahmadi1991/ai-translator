# ADR-0002: Translation via OpenAI Responses API streaming (not Realtime)

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

## Context

The user initially described "OpenAI Realtime translation" but chose **typed-text** input. The
OpenAI **Realtime API** is an audio-first, persistent-WebSocket surface (there is even a dedicated
`gpt-realtime-translate` model, but it is *speech-to-speech*, priced per audio minute). For typed
text → translated text, that is the wrong tool: more complexity, connection lifecycle, and audio
pricing for zero benefit.

## Decision

Use the official **`OpenAI` .NET SDK** with the **Responses API in streaming mode**
(`CreateResponseStreamingAsync`), consuming typed SSE deltas (`response.output_text.delta`). OpenAI
recommends Responses over Chat Completions for new projects. Each translation is a stateless
single-turn request (no server-side history). Output is **plain text** with a strict
translation-only system prompt; no JSON/structured-output wrapping (it only adds latency for a single
string).

The **default model is a configurable setting**, not hard-coded, because model names drift. Current
candidates and pricing are tracked in
[../../reference/openai-models.md](../../reference/openai-models.md) and must be verified against the
live `/v1/models` endpoint.

## Alternatives considered

- **Realtime API / `gpt-realtime-translate`** — audio/WebSocket, per-minute pricing. Rejected for
  typed text.
- **Chat Completions** — still supported but no longer the recommended surface. Acceptable fallback
  if a Responses-API issue arises, behind the same `TranslationService` abstraction.
- **Structured outputs (`json_schema`)** — unnecessary overhead to wrap one translated string.

## Consequences

- One `CancellationTokenSource` per request; cancel+dispose on each post-debounce change so only the
  latest input is translated (and we don't pay for discarded completions).
- Must branch on SSE event subtype (not every update carries text); catch `OperationCanceledException`.
- Handle 429/5xx with backoff + `Retry-After`; set a short per-request timeout so a hung stream can't
  freeze the overlay. The 500 ms debounce is the first cost/latency lever.

## Sources

- OpenAI .NET SDK — https://github.com/openai/openai-dotnet · https://www.nuget.org/packages/OpenAI
- "Responses is recommended for all new projects" — https://developers.openai.com/api/docs/guides/migrate-to-responses
- Streaming responses — https://developers.openai.com/api/docs/guides/streaming-responses
- Realtime is audio-first — https://platform.openai.com/docs/guides/realtime ;
  `gpt-realtime-translate` is speech-to-speech — https://developers.openai.com/api/docs/models/gpt-realtime-translate
