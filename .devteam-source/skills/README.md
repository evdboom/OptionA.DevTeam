# Skills

Reusable skills that any DevTeam role can load when relevant.

## Layout

Each skill lives in its own folder and must use the `SKILL.md` filename:

```text
.devteam-source/skills/
  <skill-name>/
    SKILL.md
```

Skill folder names use lowercase and hyphens.

## Available Skills

| Skill | Purpose |
|-------|---------|
| `backlog-manager` | Audit and triage backlog health (duplicates, stale questions, refinement routing). |
| `brainstorm` | Explore alternatives and trade-offs before implementation. |
| `debug` | Run systematic root-cause analysis before fixing. |
| `hygiene` | Enforce maintainability rules and file-boundary hygiene. |
| `plan` | Break work into small, verifiable tasks before coding. |
| `refine` | Turn ambiguous issues into exhaustive, executable scoped work packets. |
| `resolve-conflict` | Resolve git merge conflicts safely and completely. |
| `review` | Perform self-review before handoff. |
| `scout` | Do a read-only reconnaissance pass and map affected files. |
| `spawn-subagent` | Choose inline agent vs full spawned session routing and delegation strategy. |
| `tdd` | Apply red-green-refactor test-driven development. |
| `verify` | Validate claims with fresh command output and evidence. |
| `workspace-protection` | Protect `.devteam` runtime state from accidental deletion or git command mistakes. |

## Typical Flow

1. `brainstorm` to compare approaches.
2. `plan` to define scoped tasks and affected files.
3. `tdd` (or `debug` when broken) during implementation.
4. `verify` to confirm outcomes with evidence.
5. `review` and `hygiene` before marking work complete.

## Notes

- DevTeam loads these skills from `.devteam-source/skills/`.
- During `/init` and `/customize`, DevTeam can export them to `.github/skills/` for native Copilot skill discovery.

## Maintenance

- Keep this README in sync with folders under `.devteam-source/skills/`.
- When adding, removing, or renaming a skill, update this file in the same change.
- Keep each row aligned to the current `name` and intent in that skill's `SKILL.md`.
