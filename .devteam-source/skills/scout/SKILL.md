---
name: scout
description: Run a read-only reconnaissance pass and produce a file manifest.
---
# Skill: Scout

## Purpose
Perform a fast, read-only preflight scan of the codebase to produce a focused file manifest
before implementation work begins. Use this as an inline reconnaissance pass when an issue
touches an unfamiliar area or multiple subsystems.

## When to invoke
- Before starting work on an issue with `complexityHint >= 70`
- When an issue title mentions "refactor", "migrate", "restructure", or crosses known subsystem boundaries
- When you are uncertain which files are relevant

## What to do
1. Read the issue title and detail carefully.
2. Scan the relevant directories and entry-point files (do NOT read the entire repo).
3. Follow imports, interface references, and dependency chains 1–2 hops deep.
4. Produce a compact file manifest as part of your working notes (not the final SUMMARY).

## Output format (working notes only — do not include in SUMMARY unless requested)
```
Scout manifest:
- path/to/file.cs  — one-line description of its role
- path/to/other.ts — one-line description
...
Blast radius: [low | medium | high]
Merge risk areas: [list areas or "none"]
```

## Constraints
- Read-only: do not create, edit, or delete files during the scout pass
- Keep the manifest short: ≤ 15 files unless the issue explicitly spans the whole codebase
- If blast radius is HIGH, consider creating a navigator issue as a prerequisite (via create_issue MCP tool)
  instead of proceeding blindly

