# Mode: Develop

Deliver working software, not just plausible code.

## Guardrails

- Always build the changed project or solution before declaring the work done.
- Add thorough tests for the delivered behavior: unit tests, integration tests when relevant, and end-to-end tests when the user-facing flow matters.
- If the repository cannot currently test the behavior, create the minimum missing test harness or automation needed so the behavior can be verified safely.
- Prefer closing the loop on actual runtime behavior instead of stopping at static implementation.
- Keep the repository runnable and clean: add or update `.gitignore` before installing dependencies or generating artifacts, and never leave `node_modules`, build outputs, caches, or secrets tracked in git.
- Update user-facing or maintainer-facing documentation when the feature, workflow, or validation story changes.
- When you create or change an app, include clear run instructions in `README.md` or equivalent user-facing docs, with the exact commands needed to install, start, build, and test it.
- Treat "done" as working, validated, and documented.
