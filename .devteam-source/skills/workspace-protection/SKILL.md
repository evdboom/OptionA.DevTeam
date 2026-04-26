---
name: workspace-protection
description: Protect the .devteam runtime state directory from accidental deletion or modification.
allowed-tools: []
---
# Skill: Workspace Protection

The `.devteam` directory contains critical runtime state. Protect it at all costs.

## What is .devteam?

The `.devteam/` directory is **DevTeam's runtime state container**:

- `workspace.json` — current plan, issues, decisions, budget, phase
- `runs/` — execution history and logs
- `decisions/` — recorded decisions and rationale
- `artifacts/` — generated plans, brownfield deltas, conflict logs
- `state/` — split state chunks for large workspaces
- `checkpoints/` — guardrail snapshots

If `.devteam` is deleted or corrupted, the **entire autonomous loop will crash** and recovery requires manual intervention.

## Protected Pattern

`.devteam/` is **gitignored** intentionally — it's ephemeral workspace state, not source code.

## What NOT to Do

### ❌ DO NOT run these commands:

```bash
git restore .devteam/          # Will delete uncommitted state!
git clean -fdx                 # Deletes untracked + ignored files, including .devteam/
git reset --hard               # Wipes uncommitted changes including .devteam/
rm -rf .devteam/               # Direct deletion
```

### ❌ DO NOT use git operations that touch `.devteam`:

- `git restore` without explicit file paths (use `git restore <file>` not `git restore .`)
- `git clean -fdx` / `git clean -ffdx` (these remove ignored files and will delete .devteam)
- `git reset --hard` or `git reset --mixed` (restores tracked files; untracked .devteam is left alone, but commands that follow might delete it)

## Safe Git Patterns

### ✅ DO use these instead:

**Stage and commit specific files:**
```bash
git add src/MyFile.cs
git commit -m "message"
```

**Restore a specific file from HEAD:**
```bash
git restore src/MyFile.cs       # Single file
git restore src/                # Single directory
```

**Check what's staged:**
```bash
git status
git diff --cached
```

**Discard changes in a specific file:**
```bash
git checkout src/MyFile.cs      # Old syntax
git restore src/MyFile.cs       # New syntax (preferred)
```

**Clean only tracked files, leaving .devteam alone:**
```bash
# Don't use git clean -fd
# Instead, restore only explicit safe paths:
git restore src/
git restore tests/
```

## Why This Matters

DevTeam runs autonomously with checkpoints:

1. **Before each agent run** → checkpoint `.devteam/workspace.json`
2. **Agent executes** → may modify `.devteam/` accidentally if wrong git command
3. **After run completes** → restore from checkpoint if `.devteam` was deleted

If you accidentally delete `.devteam`:
- ✅ Loop **will recover** from checkpoint (guardrail triggered)
- ⚠️ Recovery logs a **GUARDRAIL VIOLATION**
- ❌ Multiple violations indicate a systematic problem

## Decision Checklist

Before running **any** git command in a DevTeam workspace:

- [ ] Does my command touch `.devteam/`? If yes, **use a safer alternative**
- [ ] Am I using `git restore` or `git clean` without explicit paths? If yes, **specify paths**
- [ ] Could this command be run in a subdirectory instead? If yes, **navigate to that subdir first**
- [ ] Is there a higher-level command that does what I want without git? If yes, **use it**

## Examples

### ❌ Wrong: Restores repo state including .devteam deletion
```bash
git restore .     # Danger: could leave .devteam untracked and later deletion-prone
```

### ✅ Right: Restore only source directory
```bash
git restore src/
```

### ❌ Wrong: Cleans all untracked files
```bash
git clean -fd     # Deletes .devteam even though it's gitignored!
```

### ✅ Right: Nothing to clean if you restore instead
```bash
git restore src/
```

### ❌ Wrong: Resets to HEAD including .devteam state
```bash
git reset --hard  # Wipes uncommitted state
```

### ✅ Right: Stage specific work, commit, then discard
```bash
git add src/MyFix.cs
git commit -m "Implemented feature"
git restore src/OtherFile.cs
```

## .gitignore Rule

`.devteam/` is in the repo's `.gitignore`:

```gitignore
# DevTeam runtime workspace
.devteam/
.devteam-*/
!.devteam-source/
!.devteam-source/**
!.devteam-repo/
!.devteam-repo/**
```

This means:
- ✅ `.devteam/` changes don't get staged
- ✅ `.devteam/` won't appear in `git status`
- ⚠️ `git clean` still sees it as an untracked file candidate and **will delete it**
- ⚠️ Some git commands might delete it thinking it's cruft

## If You Make a Mistake

**If you accidentally run a dangerous git command:**

1. **STOP** — don't run more commands
2. **Loop will catch it** — guardrail checkpoint restore kicks in
3. **Report the issue** — "GUARDRAIL VIOLATION" log line
4. **Document the command** — what command caused it?

**Never:** Try to recover manually by running more git commands. Let the guardrail handle it.
