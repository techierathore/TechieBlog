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

**All four files are created empty, on purpose, and an empty one is not a fault.**
The installer seeds the set so every repo has the same shape and no writer has to
guess whether its stream exists. A stream stays at zero bytes until something
actually happens: `gates.jsonl` until the first `*verify`, `runs.jsonl` until the
first framework command, `sessions.jsonl` until the first agent session ends, and
**`commits.jsonl` until your first commit after telemetry was installed** — that
one is written by *your* `git commit`, never by an agent, so a repo you haven't
committed to since the refresh will show it empty indefinitely. Nothing is broken;
`.git/hooks/post-commit` is sitting there waiting. Commit these empty files along
with the rest — a tracked empty stream is what makes the first record a one-line
diff instead of a new file appearing from nowhere.

**Never edit these files by hand, never sort them, never compact them.** They are
a log. Rewriting one destroys exactly the history it exists to keep.

**No secrets, no content, no client data** — records carry IDs, counts, durations,
verdicts and file paths at most. Never requirement text, prompt text, file
contents, or commit subjects. Assume every line here could become public.

`commits.jsonl` lags by one commit: the `post-commit` hook fires after the commit
is sealed, so its record rides in the next one. If that bothers you, delete the
hook — `.tfcore/telemetry/tf-metrics.sh --backfill-commits` reconstructs the same
data perfectly at any time, because the commit log is itself an append-only log.
