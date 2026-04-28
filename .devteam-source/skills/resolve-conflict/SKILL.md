---
name: resolve-conflict
description: Resolve git merge conflicts safely and completely.
---
# Skill: Resolve Merge Conflict

You have been spawned to resolve a git merge conflict. This is your ONLY task.

## Context

The orchestrator tried to merge a parallel agent's branch and hit conflicts.
The merge has been re-started for you with the conflicts present in the working tree.

## Steps

### 1. Assess the Conflict

```bash
git status
```

List every file marked as "both modified", "both added", or "deleted by us/them".

### 2. Read the Conflicted Files

For each conflicted file, read the full file content. Look for the conflict markers:

```
<<<<<<< HEAD
(current branch — orchestrator's version)
=======
(incoming branch — agent's version)
>>>>>>> devteam-<agentId>
```

### 3. Resolve Each File

For each conflicted file:
1. **Understand both sides** — what was each branch trying to do?
2. **Combine the intent** — merge both changes logically. Don't just pick one side.
3. **Remove all conflict markers** — no `<<<<<<<`, `=======`, or `>>>>>>>` may remain.
4. **Ensure correctness** — the result must be valid code/markdown. Run a syntax check if possible.

### 4. Stage and Verify

```bash
git add <resolved-file>
```

After staging all resolved files:
```bash
git diff --check  # Verify no conflict markers remain
```

### 5. Complete the Merge

```bash
git commit --no-edit
```

The merge commit message is already set. Do not change it.

## Rules

- Resolve ALL conflicts — do not leave any file unresolved.
- Do NOT modify files that are not conflicted.
- Do NOT create new issues or plan future work.
- Do NOT include NEXT_ROLE, PIPELINE, or PARALLEL in your output.
- If a conflict is genuinely unresolvable (e.g. two completely different rewrites of the same file), pick the version that is more complete and note what was dropped in your output.

## Output Format

```markdown
# Role
Conflict Resolver

# Conflicts Resolved
- file/path.ext — combined both changes (brief description)
- other/file.ext — kept incoming, current was outdated

# Handoff
All conflicts resolved. Merge committed.
```

