# DevTeam Roadmap

This file tracks the **active, open roadmap only**.

- Archived detailed roadmap: `ROADMAP.archive.md`
- Completed foundation work lives in the archive so this file stays focused on what is still left to do.

## Priority order

| Priority | Item | Scope | Why now |
|---|---|---|---|
| 1 | **R11 - Testability-first architect prompting (ATM: Testable)** | Small | Improves quality of generated execution issues before they hit implementation. |
| 2 | **R14 - Auditor role for AI drift and legacy drift** | Medium | Reviewer is feature-scoped. Auditor should catch broader codebase drift, especially recent AI-shaped shortcuts and maintainability erosion. |
| 3 | **R2 - /edit-issue command** | Small-Medium | Gives medior and expert users more control without rerunning planning. |
| 4 | **R4 - Run diff (/diff-run)** | Small | Helps users understand what each loop iteration actually accomplished. |
| 5 | **R7 - Workspace export/import** | Medium | Important for handoff, sharing, and multi-machine usage after core UX is clearer. |
| 6 | **R8 - Role chaining configuration** | Medium | Expert workflow improvement after the default workflow is easier to understand. |
| 7 | **R6 - Per-role token and cost telemetry** | Small-Medium | Useful for tuning and optimization after the main UX pain is addressed. |
| 8 | **R12 - Brownfield change delta** | Medium | Valuable audit layer, but should follow visibility basics and traceability. |
| 9 | **#6 - GitHub mode** | Major | Major expansion feature; should come after the core interactive workflow is clearer. |
| 10 | **R13 - BYOK / provider-agnostic auth** | Small-Medium | Broadens adoption, but is less urgent than making the current product easier to use. |

---

## R11 - Testability-first architect prompting

**Goal:** Ensure architect output explicitly encodes testability constraints, interfaces, and injectable boundaries.

---

## R14 - Auditor role for AI drift and legacy drift

**Goal:** Add an **auditor** role that periodically inspects the codebase for ATM drift:

- **Auditable** regressions: invisible side effects, weak traceability, silent failures
- **Testable** regressions: hidden dependencies, missing abstractions, shrinking isolation
- **Maintainable** regressions: file-size creep, mixed concerns, shortcut-heavy additions, repeated patterns

**Why this matters:** Reviewer is primarily feature- or change-scoped. Auditor should be **codebase-scoped** and catch longer-term degradation that accumulates over multiple iterations.

**Distinguishing legacy vs AI drift:**

- **Legacy drift** = old debt already present in the codebase
- **AI drift** = newer changes that introduce shortcut-heavy structure, file bloat, weak tests, or instruction drift quickly across several iterations

Both matter, but auditor should prioritize:

1. recent drift first
2. cross-cutting maintainability issues
3. remediation proposals that can be converted into focused issues

**Suggested shape:**

| Step | Detail |
|---|---|
| R14.1 | Define the auditor role prompt and scope boundaries relative to reviewer, security, and navigator. |
| R14.2 | Let auditor call or depend on navigator-style reconnaissance for broad codebase inspection. |
| R14.3 | Teach auditor to classify findings as `legacy`, `recent drift`, or `active regression risk`. |
| R14.4 | Have auditor propose remediation issues instead of broad rewrite mandates. |
| R14.5 | Add documentation explaining when to use reviewer vs auditor. |

---

## Remaining open items

- **R2 - /edit-issue command**
- **R4 - Run diff**
- **R7 - Workspace export/import**
- **R8 - Role chaining configuration**
- **R6 - Per-role token and cost telemetry**
- **R12 - Brownfield change delta**
- **#6 - GitHub mode**
- **R13 - BYOK / provider-agnostic auth**
