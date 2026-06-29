# Document routing — what goes where

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

Decide a new document's destination **before** creating it. This keeps the tree ordered and
prevents scatter. Mirrors `~/.claude/standards/documentation-structure.md`; the table below is the
project-specific routing.

| If you are writing… | Put it in… | Notes |
| --- | --- | --- |
| The system design, how components fit, data flow | `architecture/overview.md` | One canonical overview; don't fork it |
| A load-bearing technical decision + rationale | `architecture/decision-records/NNNN-slug.md` | Immutable once `Accepted`; supersede with a new ADR |
| A diagram (component, sequence, flow) | `architecture/diagrams/` | Prefer Mermaid in-repo; export binaries to `_attachments/` |
| Settings schema, config keys, defaults | `reference/configuration.md` | Stable reference, not prose |
| OpenAI model/pricing/API specifics | `reference/openai-models.md` | Verify against live docs before relying on it |
| The planned/actual source tree | `reference/source-layout.md` | Update when the solution structure changes |
| A how-to / setup / build / run procedure | `guides/` | Task-oriented, step-by-step |
| An implementation plan (per milestone) | `plans/YYYY-MM-DD-<feature>.md` | Bite-sized TDD tasks; one per milestone |
| A project-specific rule or standard override | `governance/` | Otherwise the global standards apply as-is |
| An image/PDF referenced by a doc | `_attachments/` | Reference it; don't inline binaries elsewhere |
| A reusable doc skeleton | `templates/` | e.g. the ADR template |

## Before finishing any docs change

Run the wiring checklist from the standard: the fact lives in exactly one canonical doc; it's
linked from the area index; related docs cross-link; no duplicated passages; no orphan/stale files.
