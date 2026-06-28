# Naming conventions

Owner: Mehdi
Status: Accepted
Last reviewed: 2026-06-28

## Files & directories

- `lowercase-kebab-case` for all docs, folders, and non-code assets. No spaces, no PascalCase, no
  locale punctuation.
- ADRs: zero-padded sequence + slug — `NNNN-short-title.md` (e.g. `0003-focus-detection-winevent-uia-ia2.md`).
- One topic per file; prefer several small linked files over one large mixed file.

## Durable doc header

Every durable doc starts with:

```markdown
# Title
Owner: Mehdi
Status: Draft | Accepted | Superseded
Last reviewed: YYYY-MM-DD
```

## Code identifiers (C#)

- Solution/assembly/namespace root: `AiTranslator` (PascalCase). Namespaces follow folders:
  `AiTranslator.<Layer>.<Feature>`.
- Types & members: PascalCase. Interfaces: `IName`. Private fields: `_camelCase`. Locals/params:
  `camelCase`. Constants & enum members: PascalCase.
- One public type per file; file name matches the type. File-scoped namespaces; nullable enabled.
- Async methods end in `Async`. Avoid abbreviations except well-known ones (UIA, IA2, HWND, DPI).

These rules are enforced where possible by [`.editorconfig`](../../.editorconfig).
