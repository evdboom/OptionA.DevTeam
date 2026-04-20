---
name: plan
description: Break work into small, verifiable implementation tasks.
---
# Skill: Plan

Break work into bite-sized, verifiable tasks before touching code.
Use this when you have an issue to implement and need a clear execution path.

## When to Use

- Before implementing any feature (even "simple" ones)
- When an issue has multiple parts
- When you need to modify multiple files
- When the Orchestrator assigns a multi-step issue

## Steps

### 1. Read the Issue and Context
- Read the assigned issue(s) completely
- Read related code that will be modified
- Understand what "done" looks like (acceptance criteria)

### 2. Map Affected Files
Before writing tasks, list every file you'll create or modify:
```
- Create: src/path/to/new-file.ext
- Modify: src/path/to/existing-file.ext (lines ~X-Y)
- Test:   src/tests/test_feature.ext
```
This prevents surprises and keeps scope visible.

### 3. Write Bite-Sized Tasks
Each task should take 2-5 minutes and be independently verifiable:

```markdown
### Task 1: Write failing test for [specific behavior]
- File: src/tests/test_feature.py
- Expected: test fails because function doesn't exist yet

### Task 2: Implement [specific function]
- File: src/path/to/module.py
- Verify: run test from Task 1, should pass

### Task 3: Add [edge case] handling
- File: src/path/to/module.py
- Verify: add and run edge case test
```

### 4. Include Verification Steps
Every task must have a way to verify it's done:
- A test to run (`CMD: pytest src/tests/test_x.py`)
- A command to execute (`CMD: python src/app.py --check`)
- An observable output to confirm

### 5. Order for TDD
Follow the natural TDD rhythm:
1. Write failing test
2. Run it (confirm it fails)
3. Write minimal code to pass
4. Run test (confirm it passes)
5. Refactor if needed
6. Next test

## Task Granularity Guide

| Too Big | Right Size | Too Small |
|---------|------------|-----------|
| "Implement the parser" | "Add loop statement to parser grammar" | "Add a comma" |
| "Write all tests" | "Write test for loop iteration" | "Import pytest" |
| "Refactor everything" | "Extract validation into helper" | "Rename variable" |

## Anti-Patterns
- Starting to code without a plan ("I'll figure it out as I go")
- Plans with no verification steps ("implement X" with no way to check)
- Tasks too large to verify independently
- Skipping file mapping (leads to forgotten changes)

