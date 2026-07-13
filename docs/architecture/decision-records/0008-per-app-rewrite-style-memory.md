# ADR-0008: Per-app rewrite-style memory

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-07-13

Refines the persistence decision in
[ADR-0007](0007-rewrite-styles-and-humanizer.md) (which stored a single global style). The rest of
ADR-0007 (single-call prompt composition, the style set, the humanizer layer) still stands.

## Context

ADR-0007 persisted the compose-box rewrite style in one global setting, `rewriteStyle`. In practice
the right style depends on *where* the user is writing: Teams is usually Formal, a chat with friends
is Friendly, a webmail tab is Email. With one global value, every switch of app forced the user to
re-pick the style, and a change made for one app silently followed them into every other.

## Decision

**Each app remembers its own style.** Settings gain `appStyles`, a per-exe map
(`{ "ms-teams.exe": "Formal", "chrome.exe": "Friendly" }`), mirroring the existing per-exe
`appOffsets` convention and reusing the same `ExeName` moniker matching (case-insensitive, works with
or without `.exe`, and against a full path).

Resolution (`AppStyles.For`): the style remembered for the target app if there is one, otherwise the
global `rewriteStyle`, which now serves as the **default for apps with no memory yet** rather than as
the single source of truth.

Writing (`AppStyles.Remember`): picking a style in the box stores it under the target app's normalized
executable name and touches nothing else, so apps stay independent. If the target app cannot be
identified, the choice updates the global default instead, so it is never silently lost. Remembering
also collapses any pre-existing entry whose moniker already matches the same exe, so an app never ends
up with two entries where the older one shadows the newer choice.

To make this work on both entry paths, `FocusTarget` now carries the target's `ExeName`: the badge
path takes it from the resolved `FocusedField`, and the hotkey path resolves it from the foreground
window in `ForegroundFocusTargetProvider`.

## Alternatives considered

- **Keep one global style.** Rejected: it was the reported problem.
- **Seed a new app with the last style used anywhere.** Rejected: the owner asked for genuinely
  independent memory per app. A new app starts at the global default instead.
- **A Settings screen to manage the per-app map.** Deferred (YAGNI): the map is written implicitly by
  using the box, and `settings.json` is hand-editable. Add a UI only if it turns out to be needed.

## Consequences

- `settings.json` grows an `appStyles` map. Old files without the key load fine (it defaults to empty).
- Style resolution is a pure lookup, unit-tested without a live desktop (`AppStylesTests`).
- An app the OS will not let us identify falls back to the global default. That is the correct
  degradation, not an error.
