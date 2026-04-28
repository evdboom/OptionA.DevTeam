---
name: brainstorm
description: Explore approaches and trade-offs before implementation.
---
# Skill: Brainstorm

Explore approaches before committing to implementation.
Use this when designing features, making architectural decisions, or starting new work.

## When to Use

- Starting a new feature or component
- Making architectural decisions
- Exploring unfamiliar problem space
- Issue says "design" or "plan" or "explore"

## Steps

### 1. Gather Context
- Read the active goal, roadmap items, open issues, recent decisions, and open questions provided by the runtime
- Check existing code structure (`src/` directory)
- Understand what exists before proposing what's new

### 2. Explore 2-3 Approaches
For the problem at hand, identify at least 2 different approaches:
- **Approach A:** (describe) — trade-offs: ...
- **Approach B:** (describe) — trade-offs: ...
- **Recommended:** (which and why)

Don't skip this. Even "obvious" solutions have alternatives worth considering.

### 3. Check Constraints
- Does this fit the existing architecture?
- Does this add unnecessary complexity? (YAGNI)
- Can this be done incrementally?
- Will this break existing functionality?

### 4. Document the Decision
Put the decision in the response summary so the runtime can persist it as part of the current plan or run artifact.

### 5. Create Actionable Issues
Break the chosen approach into implementable issues:
- Each issue should be closeable in 1-2 iterations
- Note dependencies between issues

## Anti-Patterns
- Jumping straight to code without considering alternatives
- "This is too simple to need design" — simple decisions still benefit from 30 seconds of thought
- Designing everything upfront — design just enough for the next 2-3 issues
- Ignoring existing patterns in the codebase

