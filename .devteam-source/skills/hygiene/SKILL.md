---
name: hygiene
description: Enforce file-size, separation-of-concerns, and maintainability hygiene.
---
# Skill: Hygiene

## Purpose
Apply project hygiene rules consistently across all code you produce or review.
Load this Skill when reviewing or scaffolding new files.

## Stack Neutrality
- These rules apply to any language/framework.
- Use repository-native boundaries when they differ (for example: React/Vue/Svelte component + hook/service separation, Java Spring controller/service/repository split, Python API module/service split).
- Apply Blazor-specific rules only when the project actually uses Blazor.

## Rules

### File size
- No file should own multiple concerns.
- When a file exceeds ~400 lines, split it by theme into separate files.
- Prefer more small, focused files over fewer large ones.

### Separation of concerns
- If the project uses Blazor, `.razor` files contain markup and binding glue **only** — no `@code { }` blocks with real logic.
- If the project uses Blazor, all component logic goes in the paired `.razor.cs` partial class (code-behind).
- The same principle applies broadly: never mix rendering/presentation with domain/business logic in the same file.

### Entry points
- The top-level application bootstrap file (for example `Program.cs`, `main.ts`, `main.py`, `App.java`) should be ≤ ~30 lines when practical.
- Its only job: wire DI, resolve the dispatcher or entry point, call it.
- All logic belongs in focused service classes.

### When adding a feature
- Before adding to an existing file, check its current size.
- If it is already close to the ~400-line limit, extract an existing concern into a new file first, then add.

## Checklist (apply before submitting)
- [ ] No new file exceeds ~400 lines
- [ ] If Blazor is used, no `.razor` file has a meaningful `@code { }` block — use `.razor.cs` instead
- [ ] Entry-point/bootstrap file remains lean (≈30 lines when practical)
- [ ] Each new class/file owns exactly one concern

