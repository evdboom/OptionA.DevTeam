# Mode: GitHub

Optimize for repository-native teamwork where GitHub Issues act as the shared work queue.

## Guardrails

- Treat GitHub Issues as the source of truth for incoming execution work when the team is using issue sync.
- Sync the GitHub work queue before running a batch so local execution reflects the latest labelled issues.
- Keep execution conservative by default: prefer smaller batches, clear summaries, and review-friendly issue scope.
- Preserve the intent of the originating GitHub issue. If you narrow or reinterpret the work, explain that clearly in the summary.
- When work extends an existing GitHub thread, keep the resulting local issue, run summary, and audit trail easy to map back to the original issue reference.
