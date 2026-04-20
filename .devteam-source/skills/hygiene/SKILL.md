---
name: hygiene
description: Enforce file-size, separation-of-concerns, and maintainability hygiene.
---
# Skill: Hygiene

## Purpose
Apply project hygiene rules consistently across all code you produce or review.
Load this Skill when reviewing or scaffolding new files.

## Rules

### File size
- No file should own multiple concerns.
- When a file exceeds ~400 lines, split it by theme into separate files.
- Prefer more small, focused files over fewer large ones.

### Separation of concerns
- Blazor `.razor` files contain markup and binding glue **only** — no `@code { }` blocks with real logic.
- All component logic goes in the paired `.razor.cs` partial class (code-behind).
- The same principle applies broadly: never mix rendering/presentation with domain/business logic in the same file.

### Entry points
- `Program.cs` (or equivalent top-level file) must be ≤ ~30 lines.
- Its only job: wire DI, resolve the dispatcher or entry point, call it.
- All logic belongs in focused service classes.

### When adding a feature
- Before adding to an existing file, check its current size.
- If it is already close to the ~400-line limit, extract an existing concern into a new file first, then add.

## Checklist (apply before submitting)
- [ ] No new file exceeds ~400 lines
- [ ] No `.razor` file has a meaningful `@code { }` block — use `.razor.cs` instead
- [ ] `Program.cs` (or equivalent) is still ≤ ~30 lines
- [ ] Each new class/file owns exactly one concern

