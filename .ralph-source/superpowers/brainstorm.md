# Superpower: Brainstorm

Explore approaches before committing to implementation.
Use this when designing features, making architectural decisions, or starting new work.

## When to Use

- Starting a new feature or component
- Making architectural decisions
- Exploring unfamiliar problem space
- Issue says "design" or "plan" or "explore"

## Steps

### 1. Gather Context
- Read GOAL.md, ROADMAP.md, and relevant issues
- Check existing code structure (`src/` directory)
- Read recent decisions in `.ralph/.ralph-project/decisions/`
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
Write a decision record to `.ralph/.ralph-project/decisions/NNNN-title.md`:
```markdown
# Decision NNNN: Title
**Status:** accepted
**Context:** What problem are we solving?
**Options Considered:**
1. Option A — pros/cons
2. Option B — pros/cons
**Decision:** Which option and why
**Consequences:** What follows from this decision
```

### 5. Create Actionable Issues
Break the chosen approach into implementable issues:
- Each issue should be closeable in 1-2 iterations
- Include acceptance criteria in each issue
- Note dependencies between issues

## Anti-Patterns
- Jumping straight to code without considering alternatives
- "This is too simple to need design" — simple decisions still benefit from 30 seconds of thought
- Designing everything upfront — design just enough for the next 2-3 issues
- Ignoring existing patterns in the codebase
