---
name: debug
description: Perform systematic root-cause debugging before proposing fixes.
---
# Skill: Systematic Debugging

Find the root cause before attempting fixes. Random fixes waste time and create new bugs.

## When to Use

- Test failures
- Bugs or unexpected behavior
- Build/parse/runtime errors
- "It should work but doesn't"
- After a failed fix attempt

## The Iron Law

```
NO FIXES WITHOUT ROOT CAUSE INVESTIGATION FIRST
```

If you haven't completed Phase 1, you cannot propose fixes.

## The Four Phases

### Phase 1: Root Cause Investigation

**BEFORE attempting ANY fix:**

1. **Read Error Messages Carefully**
   - Don't skip past errors or warnings — they often contain the answer
   - Read stack traces completely
   - Note line numbers, file paths, error codes

2. **Reproduce Consistently**
   - Can you trigger it reliably?
   - What are the exact steps?
   - If not reproducible → gather more data, don't guess

3. **Check Recent Changes**
   - What changed since it last worked?
   - `git diff`, recent commits
   - New dependencies, config changes

4. **Trace Data Flow**
   - Where does the bad value originate?
   - Trace backward from the error to the source
   - Fix at the source, not at the symptom

### Phase 2: Pattern Analysis

1. **Find Working Examples**
   - Locate similar working code in the codebase
   - What works that's similar to what's broken?

2. **Compare Differences**
   - What's different between working and broken?
   - List every difference, however small
   - Don't assume "that can't matter"

3. **Check Dependencies**
   - What components does this depend on?
   - What config/environment does it assume?

### Phase 3: Hypothesis and Testing

1. **Form Single Hypothesis**
   - "I think X is the root cause because Y"
   - Be specific, not vague

2. **Test Minimally**
   - Make the SMALLEST possible change to test hypothesis
   - One variable at a time
   - Don't fix multiple things at once

3. **Evaluate Result**
   - Did it work? → Phase 4
   - Didn't work? → Form NEW hypothesis, don't pile on more fixes

### Phase 4: Implementation

1. **Write Failing Test** (see `tdd.md` Skill)
   - Simplest possible reproduction of the bug
   - MUST have this before fixing

2. **Implement Single Fix**
   - Address the root cause identified
   - ONE change at a time
   - No "while I'm here" improvements

3. **Verify Fix**
   - Test passes
   - No other tests broken
   - Issue actually resolved

4. **If 3+ Fixes Have Failed: Question the Architecture**
   - Each fix reveals new problems in different places? → architectural issue
   - Fixes require "massive refactoring"? → wrong approach
   - Stop fixing symptoms. Document the architectural problem in handoff.
   - Suggest Architect role for next iteration.

## Quick Reference

| Phase | Do | Success Criteria |
|-------|-----|------------------|
| 1. Root Cause | Read errors, reproduce, trace | Understand WHAT and WHY |
| 2. Pattern | Find working examples, compare | Identify differences |
| 3. Hypothesis | Form theory, test minimally | Confirmed or new hypothesis |
| 4. Implement | Write test, fix, verify | Bug resolved, tests pass |

## Red Flags — Stop and Follow Process
- "Just try changing X and see if it works"
- "Quick fix for now, investigate later"
- Proposing solutions before understanding the problem
- "One more fix attempt" after 2+ failures
- Multiple changes bundled into one attempt

