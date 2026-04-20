---
name: verify
description: Verify claims with fresh execution evidence before completion.
---
# Skill: Verify Before Completion

Evidence before claims. Run the command, read the output, THEN claim the result.

## When to Use

- **Before** claiming any work is done
- **Before** marking an issue as complete
- **Before** writing "tests pass" in handoff
- **Before** moving to the next task

## The Iron Law

```
NO COMPLETION CLAIMS WITHOUT FRESH VERIFICATION EVIDENCE
```

If you haven't run the verification command in this iteration, you cannot claim it passes.

## The Gate

Before claiming ANY status:

1. **IDENTIFY** — What command proves this claim?
2. **RUN** — Execute the full command (fresh, not cached)
3. **READ** — Full output, check exit code, count failures
4. **VERIFY** — Does output confirm the claim?
   - NO → State actual status with evidence
   - YES → State claim WITH evidence
5. **ONLY THEN** — Make the claim

Skip any step = unverified claim.

## Verification by Claim Type

| Claim | Requires | NOT Sufficient |
|-------|----------|----------------|
| "Tests pass" | Test command output showing 0 failures | Previous run, "should pass" |
| "Build works" | Build command with exit code 0 | "Linter passed" |
| "Bug is fixed" | Test original symptom: passes | "Code changed, should work" |
| "Feature works" | Run it, show output | "I implemented it" |
| "No regressions" | Full test suite passes | Only new tests run |

## Evidence Format

Include verification evidence in your output:

```markdown
# Verification
CMD: pytest src/tests/ -v
Result: 12 passed, 0 failed
Exit code: 0
```

## Words That Signal Missing Verification
- "should work now"
- "probably passes"
- "looks correct"
- "I'm confident this works"
- "this should fix it"

All of these mean: STOP → run the verification command → report actual result.

## Red Flags — Stop and Verify
- About to write "done" without running tests
- Expressing satisfaction before verification ("Great!", "Fixed!")
- Trusting that code changes = working code
- "Just this once" skipping verification
- Marking issue as done without evidence

