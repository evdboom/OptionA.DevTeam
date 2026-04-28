---
name: tdd
description: Apply red-green-refactor test-driven development.
---
# Skill: Test-Driven Development (TDD)

Write the test first. Watch it fail. Write minimal code to pass. Refactor.

## When to Use

- **Always** when writing new code
- **Always** when fixing bugs
- **Always** when changing behavior

Exception: config files, scaffolding, documentation-only changes.

## The Iron Law

```
NO PRODUCTION CODE WITHOUT A FAILING TEST FIRST
```

Wrote code before the test? Delete it. Start over from the test.
Don't keep it as "reference." Implement fresh from the test.

## Red-Green-Refactor Cycle

### RED — Write One Failing Test
Write the smallest test that demonstrates the behavior you need:

```python
def test_rejects_empty_input():
    result = validate("")
    assert result.is_error
    assert "required" in result.message
```

Requirements:
- One behavior per test
- Clear, descriptive name
- Real code (no mocks unless unavoidable)

### Verify RED — Run It
```
CMD: pytest src/tests/test_validate.py::test_rejects_empty_input -v
```
Confirm:
- Test **fails** (not errors from syntax/import issues)
- Failure message makes sense
- It fails because the feature is missing

Test passes immediately? You're testing existing behavior. Fix the test.

### GREEN — Write Minimal Code
Write the simplest code that makes the test pass:

```python
def validate(value):
    if not value.strip():
        return Error("required")
    return Ok(value)
```

Don't add:
- Extra features
- "Improvements"
- Error handling for cases not yet tested
- Configurability

### Verify GREEN — Run It
```
CMD: pytest src/tests/test_validate.py -v
```
Confirm:
- This test passes
- All other tests still pass
- No warnings or errors in output

Test fails? Fix the **code**, not the test.

### REFACTOR — Clean Up (Only After Green)
- Remove duplication
- Improve names
- Extract helpers
All tests must stay green after refactoring.

### Repeat
Next behavior → next failing test → next minimal implementation.

## Bug Fixes

Found a bug? The cycle is:
1. Write a failing test that reproduces the bug
2. Run it — confirm it fails
3. Fix the bug with minimal change
4. Run it — confirm it passes
5. Test proves the fix AND prevents regression

## Quick Reference

| Step | Do | Don't |
|------|-----|-------|
| RED | One test, one behavior | Multiple behaviors per test |
| Verify RED | Run, see it fail | Skip verification |
| GREEN | Minimal code to pass | Over-engineer, add features |
| Verify GREEN | Run all tests | Only run new test |
| REFACTOR | Clean, keep tests green | Add new behavior |

## Red Flags — Stop and Reset
- Writing code before a test exists
- Test passes immediately (testing existing behavior)
- Multiple fixes bundled together
- "I'll write tests after" (tests-after prove nothing)
- "Too simple to test" (simple code breaks too)

