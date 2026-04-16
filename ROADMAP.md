# DevTeam Roadmap

This file tracks the **active, open roadmap only**.

- Archived detailed roadmap: `ROADMAP.archive.md`
- Completed foundation work lives in the archive so this file stays focused on what is still left to do.

## Priority order

| Priority | Item | Scope | Why now |
|---|---|---|---|
| 1 | **W1 - Workflow-first onboarding** | Medium | Biggest product gap: DevTeam is usable by experts, but not yet guided enough for new users, medior users, or non-programmers. |
| 2 | **R1 - Dry-run preview before /run** | Small | Highest-value execution visibility improvement. Helps users trust and understand the loop before credits are spent. |
| 3 | **R5 - Question TTL / stall indicator** | Small | Makes the most confusing pause state legible. |
| 4 | **R10 - Traceability links (ATM: Audit)** | Small-Medium | Makes "what changed and why?" answerable. Important for trust. |
| 5 | **R11 - Testability-first architect prompting (ATM: Testable)** | Small | Improves quality of generated execution issues before they hit implementation. |
| 6 | **R14 - Auditor role for AI drift and legacy drift** | Medium | Reviewer is feature-scoped. Auditor should catch broader codebase drift, especially recent AI-shaped shortcuts and maintainability erosion. |
| 7 | **R2 - /edit-issue command** | Small-Medium | Gives medior and expert users more control without rerunning planning. |
| 8 | **R4 - Run diff (/diff-run)** | Small | Helps users understand what each loop iteration actually accomplished. |
| 9 | **R7 - Workspace export/import** | Medium | Important for handoff, sharing, and multi-machine usage after core UX is clearer. |
| 10 | **R8 - Role chaining configuration** | Medium | Expert workflow improvement after the default workflow is easier to understand. |
| 11 | **R6 - Per-role token and cost telemetry** | Small-Medium | Useful for tuning and optimization after the main UX pain is addressed. |
| 12 | **R12 - Brownfield change delta** | Medium | Valuable audit layer, but should follow visibility basics and traceability. |
| 13 | **#6 - GitHub mode** | Major | Major expansion feature; should come after the core interactive workflow is clearer. |
| 14 | **R13 - BYOK / provider-agnostic auth** | Small-Medium | Broadens adoption, but is less urgent than making the current product easier to use. |

---

## W1 - Workflow-first onboarding

**Goal:** Make DevTeam understandable and usable for three distinct personas:

1. **New user / non-programmer** - safe defaults, clear next steps, plain-language guidance
2. **Medior user** - visibility into issues, runs, questions, and recovery paths
3. **Expert user** - customization, autonomy, and advanced controls without polluting the beginner path

**Why now:** The runtime has already reached a strong technical baseline. The adoption gap is now product clarity, not loop capability.

| Step | Detail |
|---|---|
| W1.1 | Improve in-app help so it teaches the normal workflow, not just the command list. |
| W1.2 | Add phase-aware shell guidance: planning, plan review, architecture, architect review, execution. |
| W1.3 | Expand README with workflow-based examples for feedback, questions, and safe first runs. |
| W1.4 | Add a beginner-first interactive init/onboarding flow after the current help/docs slice lands. |
| W1.5 | Add persona-based docs: new user, medior user, expert user. |

**Current slice:** W1.1-W1.3

---

## R1 - Dry-run preview before /run

**Goal:** Let the user preview which issues would run, which roles would handle them, and the likely cost/concurrency before starting execution.

| Step | Detail |
|---|---|
| R1.1 | Extract batch selection into a reusable planning surface. |
| R1.2 | Add a `/preview` shell command. |
| R1.3 | Add `--dry-run` to `run-loop`. |
| R1.4 | Show issue, role, and estimated cost in preview output. |

---

## R5 - Question TTL / stall indicator

**Goal:** Make waiting states obvious instead of mysterious.

| Step | Detail |
|---|---|
| R5.1 | Show how long a blocking question has been open. |
| R5.2 | Show when the loop is stalled on user input. |
| R5.3 | Distinguish blocking vs non-blocking questions in shell status. |

---

## R10 - Traceability links

**Goal:** Link issue, run, decision, and changed files so DevTeam remains auditable at scale.

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
