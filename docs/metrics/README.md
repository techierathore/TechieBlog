# docs/metrics — development telemetry

Append-only JSONL. **Tracked by git on purpose** — this is the project's own
development history, and it is the one thing the framework cannot reconstruct
after the fact.

| File | One record per | Written by |
|---|---|---|
| `runs.jsonl` | framework command run | the task, at completion |
| `gates.jsonl` | REQ verdict per verify run — **the primary stream** | `verify-phase` §6a, `triage-issues` |
| `sessions.jsonl` | agent session | the `SessionEnd` hook |
| `commits.jsonl` | commit | the repo's own `post-commit` hook |

Schema, enums, and every known limitation: `.tfcore/telemetry/SCHEMA.md`.
Report: `/TechieFlow:agents:flow-master *metrics <AppName>` → `METRICS.md`.

**Never edit these files by hand, never sort them, never compact them.** They are
a log. Rewriting one destroys exactly the history it exists to keep.

**No secrets, no content, no client data** — records carry IDs, counts, durations,
verdicts and file paths at most. Never requirement text, prompt text, file
contents, or commit subjects. Assume every line here could become public.

`commits.jsonl` lags by one commit: the `post-commit` hook fires after the commit
is sealed, so its record rides in the next one. If that bothers you, delete the
hook — `.tfcore/telemetry/tf-metrics.sh --backfill-commits` reconstructs the same
data perfectly at any time, because the commit log is itself an append-only log.
