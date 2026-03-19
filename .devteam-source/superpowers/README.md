# Superpowers

Reusable skill instructions that any DevTeam agent can load when needed.
Inspired by [obra/superpowers](https://github.com/obra/superpowers).

## Usage

When your task requires a specific skill, **read the relevant file** before starting work.
Reference: `.devteam-source/superpowers/<skill>.md`

## Available Skills

| Skill | When to Use |
|-------|-------------|
| **brainstorm** | Before designing anything — explore approaches, trade-offs, alternatives |
| **plan** | Before implementing — break work into bite-sized, verifiable tasks |
| **tdd** | When writing code — red-green-refactor cycle |
| **debug** | When something is broken — systematic 4-phase root cause analysis |
| **verify** | Before claiming work is done — evidence before assertions |
| **review** | After implementing — self-review checklist before handoff |

## Principle

Agents should pick the superpowers they need based on their role and task.
Not every iteration needs every skill. An Orchestrator might use `brainstorm` + `plan`.
A Developer uses `tdd` + `verify`. A Tester uses `debug` + `verify`.
