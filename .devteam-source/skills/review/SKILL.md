---
name: review
description: Self-review completed work before handoff.
---
# Skill: Self-Review

Review your own work before handoff. Catch issues before the next iteration has to.

## When to Use

- After completing implementation work
- Before writing the Handoff section
- Before marking issues as done

## Checklist

Go through each item. If any fails, fix it before claiming done.

### 1. Code Quality
- [ ] No commented-out code left behind
- [ ] No TODO/FIXME added without a corresponding issue
- [ ] No debug prints or temporary logging
- [ ] Variable and function names are clear
- [ ] No duplicated code that should be a function

### 2. Correctness
- [ ] Does the code actually do what the issue asked?
- [ ] Edge cases handled (empty input, missing files, etc.)
- [ ] Error messages are helpful (not just "error occurred")
- [ ] No hardcoded values that should be configurable

### 3. Tests
- [ ] New code has tests
- [ ] Tests actually test behavior (not just "runs without crashing")
- [ ] All tests pass (run them — see `verify.md` Skill)
- [ ] Edge case tests exist

### 4. Scope
- [ ] Only changed what the issue asked for
- [ ] No "while I'm here" improvements unrelated to the issue
- [ ] No new features snuck in without an issue
- [ ] Changes are minimal and focused

### 5. Handoff Quality
- [ ] Issue statuses updated accurately
- [ ] Handoff describes what was ACTUALLY done (not planned)
- [ ] Open problems or risks noted
- [ ] NEXT_ROLE suggestion makes sense for remaining work

## Common Issues to Watch For

| Problem | How to Spot |
|---------|------------|
| Scope creep | Changed files not listed in original issue |
| Incomplete work | Issue marked done but acceptance criteria unchecked |
| Missing tests | New functions/classes without corresponding tests |
| Broken imports | Added code that references moved/renamed files |
| Stale references | Documentation pointing to old file paths |

## Quick Self-Test

Ask yourself:
1. If I were the next agent, would the handoff give me everything I need?
2. If I run the tests right now, will they all pass?
3. Did I actually verify my claims, or am I assuming?

If the answer to any is "no" or "I think so" — fix it first.

