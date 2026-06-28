# AI Translator — documentation

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

The front door to all project documentation. Each fact has exactly one canonical home; this index
just routes you there.

## Map

| Area | Start here | What's inside |
| --- | --- | --- |
| **Architecture** | [architecture/overview.md](architecture/overview.md) | The system design / spec: behavior, components, data flow, security model |
| **Decisions** | [architecture/decision-records/](architecture/decision-records/README.md) | ADRs — the load-bearing technical choices and why |
| **Diagrams** | [architecture/diagrams/](architecture/diagrams/component-and-flow.md) | Component map and data-flow diagrams |
| **Reference** | [reference/configuration.md](reference/configuration.md) · [reference/openai-models.md](reference/openai-models.md) · [reference/source-layout.md](reference/source-layout.md) | Stable reference: settings schema, model/pricing, planned source layout |
| **Guides** | [guides/development-setup.md](guides/development-setup.md) · [guides/build-and-run.md](guides/build-and-run.md) | How-to: set up, build, run |
| **Governance** | [governance/README.md](governance/README.md) | Project-specific rules and how global standards apply here |
| **Meta** | [_meta/document-routing.md](_meta/document-routing.md) · [_meta/naming-conventions.md](_meta/naming-conventions.md) | Where new docs go; naming + headers |

## Project status

**Design phase.** The architecture and decisions are written; the `src/` solution has not been
created yet. Implementation follows once the design is approved. See
[architecture/overview.md § Roadmap](architecture/overview.md#roadmap).

## Conventions for this docs tree

- `lowercase-kebab-case.md`; one topic per file; durable docs carry an `Owner / Status / Last
  reviewed` header.
- Single source of truth + DRY: link, don't duplicate. Keep this index and other always-loaded
  files lean; push depth into leaf docs.
- Update the relevant doc in the **same change** as the code/behavior it documents.
