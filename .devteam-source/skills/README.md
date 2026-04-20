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
| `brainstorm` | Explore alternatives and trade-offs before implementation. |
| `plan` | Break work into small, verifiable tasks before coding. |
| `tdd` | Apply red-green-refactor test-driven development. |
| `debug` | Run systematic root-cause analysis before fixing. |
| `verify` | Validate claims with fresh command output and evidence. |
| `review` | Perform self-review before handoff. |
| `scout` | Do a read-only reconnaissance pass and map affected files. |
| `hygiene` | Enforce maintainability rules and file-boundary hygiene. |
| `resolve-conflict` | Resolve git merge conflicts safely and completely. |

## Typical Flow

1. `brainstorm` to compare approaches.
2. `plan` to define scoped tasks and affected files.
3. `tdd` (or `debug` when broken) during implementation.
4. `verify` to confirm outcomes with evidence.
5. `review` and `hygiene` before marking work complete.

## Notes

- DevTeam loads these skills from `.devteam-source/skills/`.
- During `/init` and `/customize`, DevTeam can export them to `.github/skills/` for native Copilot skill discovery.
