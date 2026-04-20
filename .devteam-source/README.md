# DevTeam prompt assets

The new DevTeam runtime treats this directory as the primary home for prompt assets.

## Layout

- `roles/` contains role definitions as markdown files.
- `skills/` contains reusable process/skill instructions in per-skill directories (`skills/<name>/SKILL.md`).
- `MODELS.json` defines model metadata used for role/model policy and budgeting.

At runtime, DevTeam loads assets from `.devteam-source/`.

## Markdown format

These files are plain markdown so they stay editable without recompiling the runtime.

Optional frontmatter is supported for tool requirements:

```md
---
tools: rg, git, dotnet
---
# Skill: Toolsmith

Use the registered tools above when this skill is active.
```

The runtime strips the frontmatter before sending the body to agents and stores the declared tool list as metadata. That gives roles and skills a place to express tool expectations while still staying markdown-first.

## Notes

- Roles remain the source of behavioral guidance.
- Skills remain reusable process instructions.
- Tool availability still comes from the runtime/session configuration; markdown declares intent, not implementation.
