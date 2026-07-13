# ADR-0009: Speech-to-text dictation via the OpenAI Realtime transcription session

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-07-13

## Context

The compose box should let the user speak instead of type: press a mic button, talk (usually Persian),
watch the words appear in the box **as they speak**, then translate as usual.

"Live as you speak" is the requirement, not "transcribe after I stop". That rules the design, because
the two OpenAI speech-to-text routes behave very differently:

- **File transcription** (`/v1/audio/transcriptions` with `gpt-4o-transcribe` / `gpt-4o-mini-transcribe`)
  is cheap (~$0.003-0.006/min) and simple, but the text only arrives after the clip is uploaded.
- **Realtime transcription** (`gpt-realtime-whisper` over the Realtime WebSocket) streams transcript
  deltas while the audio is still flowing (~$0.017/min).

[ADR-0002](0002-translation-openai-responses-streaming.md) rejected the Realtime API for *text*
translation because it is audio-first. That reasoning does not apply here: this feature *is* audio, so
Realtime is the correct tool rather than the wrong one.

## Evidence (spike, 2026-07-13)

Before committing to the design, a throwaway spike streamed a synthesized Persian sample to the real
API and logged the event timeline. It settled the one thing the docs left ambiguous:

- Transcript **deltas arrive continuously while audio is being appended** (first word ~1.3 s after
  audio starts), so no periodic `input_audio_buffer.commit` is needed to keep the text live.
- Deltas are additive word fragments. `input_audio_buffer.commit` closes the utterance and yields
  `conversation.item.input_audio_transcription.completed` with a **cleaner, punctuated final
  transcript** that differs slightly from the concatenated deltas.
- Persian accuracy was effectively perfect on the sample.
- The session works over `wss://api.openai.com/v1/realtime?intent=transcription` with a Bearer token
  and a `session.update` of `type: "transcription"`, `audio.input.format = audio/pcm @ 24000`,
  `transcription = { model: gpt-realtime-whisper, language, delay }`, and `turn_detection: null`.

## Decision

Dictate through a **Realtime transcription session**, streaming microphone audio as 24 kHz mono PCM16.

- **Capture:** `NAudio` (the stable, standard .NET audio package) behind an `IAudioCapture`
  abstraction, using the default input device. This is the only new dependency; the WebSocket client
  is `System.Net.WebSockets.ClientWebSocket` from the BCL.
- **Recognition:** `OpenAiRealtimeSpeechRecognizer` isolates every provider and WebSocket detail in one
  Infrastructure file, the same containment rule ADR-0002 set for the translation client. It exposes
  `ISpeechRecognizer` (start/stop plus `PartialTranscript`, `FinalTranscript`, `StateChanged`,
  `Failed`).
- **Text reconciliation:** a pure Core `DictationBuffer` keeps the text already committed separate
  from the in-flight partial, so deltas can be re-rendered without disturbing what the user typed
  before starting. On `completed`, the partial is replaced by the model's final transcript. Being pure,
  it is unit-tested without audio or a network.
- **Interaction:** click the mic to start, click again (or `Esc`) to stop. Dictated text is **appended**
  to whatever is already in the box. The box is read-only while listening, so a live re-render can
  never clobber typing. Translation stays a deliberate, separate action.
- **Language:** the speech language is `languagePair.primary`, sent explicitly (it measurably improves
  accuracy). No new setting.
- **Settings:** `dictation` (bool, default on) turns the whole feature off, and `speechModel` (default
  `gpt-realtime-whisper`) is configurable for the same reason `model` is: model names drift.

## Alternatives considered

- **File transcription after recording.** Cheaper and simpler, but not live, which was the whole point.
  The `ISpeechRecognizer` seam leaves the door open if cost ever outweighs the experience.
- **Local Whisper (Whisper.net / whisper.cpp).** Private and free per use, but needs a large model
  download, is slow on CPU, and Persian quality on the small models is noticeably worse. Wrong trade
  for a lightweight tray app.
- **Azure Speech.** Excellent streaming, but it means a second provider, a second key, and a second
  bill for no gain over the route we already pay for.

## Consequences

- **Audio leaves the machine** while listening. That is a real escalation over sending text, so the
  mic only ever records between an explicit start and stop, and `dictation` can disable the feature
  outright. This is stated in the docs, not buried.
- **Cost is time-based** (~$0.017/min of listening), unlike translation's per-token cost.
- Microphone access can be blocked by Windows privacy settings or absent entirely. That is surfaced as
  a clear message, never a crash.
- Long, unbroken dictation is bounded by the model's 16k context. Messaging-length speech is far
  inside it; a very long monologue would need chunking, which is out of scope until it is a real problem.

## Sources

- Realtime transcription guide and `gpt-realtime-whisper` model page (developers.openai.com), plus the
  spike above, which is the authority for the delta cadence the docs left unclear.
