# docs/metrics — development telemetry

Append-only JSONL. **Tracked by git on purpose** — this is the project's own
development history, and it is the one thing the framework cannot reconstruct
after the fact.

| File | One record per | Written by |
|---|---|---|
| `runs.jsonl` | framework command run | the task, at completion |
| `gates.jsonl` | REQ verdict per verify run — **the primary stream** | `verify-phase` §6a, `triage-issues` |
| `sessions.jsonl` | agent session | the `SessionEnd` hook |
| `commits.jsonl` | commit | the repo's own `pre-commit` hook |
| `misses.jsonl` | a requirement/behaviour an agent MISSED, and what fixing it cost | `verify-phase`, `build-phase`, `triage-issues`, `fix-issues`, `amend-docs`, `*log-miss` |

`misses.jsonl` is the one stream with **three** record kinds — `miss` (opened: what
was missed, which phase/agent/model let it through, who found it), `miss-fix`
(closed: the repair run and its token/cost window, linked by `miss_id`) and
`miss-amend` (completes a field the `miss` left empty — it can fill a `null` and
can never overwrite a value, so it adds to the history without revising it). It is
what makes "how much did that miss cost to fix" answerable. SCHEMA.md §5.5.

Schema, enums, and every known limitation: `.tfcore/telemetry/SCHEMA.md`.
Report: `/TechieFlow:agents:flow-master *metrics <AppName>` (OpenCode: `/flow-master *metrics <AppName>`) → `METRICS.md`.

**All five files are created empty, on purpose, and an empty one is not a fault.**
The installer seeds the set so every repo has the same shape and no writer has to
guess whether its stream exists. A stream stays at zero bytes until something
actually happens: `gates.jsonl` until the first `*verify`, `runs.jsonl` until the
first framework command, `sessions.jsonl` until the first agent session ends, and
`commits.jsonl` until your first commit after telemetry was installed, and
`misses.jsonl` until an agent misses something (which it will). Commit
these empty files along with the rest — a tracked empty stream is what makes the
first record a one-line diff instead of a new file appearing from nowhere.

**Never edit these files by hand, never sort them, never compact them.** They are
a log. Rewriting one destroys exactly the history it exists to keep. To correct or
complete a record, append another one: a later `gates.jsonl` record supersedes an
earlier verdict, a `miss-fix` closes a `miss`, and
`bash .tfcore/utils/tf-emit.sh --amend <miss_id> <field> <value>` fills a field a
`miss` left empty (it refuses to overwrite one that is not empty). If nothing fits
what you need to correct, say so rather than editing — that is a framework defect
worth reporting, and it is how the amend path came to exist.

**No secrets, no content, no client data** — records carry IDs, counts, durations,
verdicts and file paths at most. Never requirement text, prompt text, file
contents, or commit subjects. Assume every line here could become public.

## Working on more than one machine

`.gitattributes` gives these streams `merge=union`, so two machines appending to
the same file keep **both** sides' lines instead of conflicting. Pull, push, carry
on — you never hand-resolve a log, which is the one way records get silently
dropped. Union merge can leave a record duplicated or out of chronological order;
every consumer sorts on `ts` and de-duplicates commits on `sha`, so neither costs
you anything.

**`commits.jsonl` needs no collecting.** The `pre-commit` hook *reconciles*: it
writes a record for every commit reachable from HEAD that the file does not
already have, then stages that one file so the records ship **inside** the commit
you are making. So after you pull another machine's work, your next commit here
records all of it. The commit log is itself an append-only log that push and pull
already replicate everywhere; this stream is a projection of it.

Three things worth knowing:

- **It stages exactly one path** — `docs/metrics/commits.jsonl`, nothing else. On
  a partial commit (`git commit -- <paths>`) it writes the record but does **not**
  stage, so it can never smuggle a file into a commit you deliberately scoped.
- **The lag is one commit, and it is committed rather than pending.** At
  pre-commit time HEAD is still the previous commit, so the record for the commit
  you are making ships in the next one. Your working tree is clean when the commit
  finishes — that is the whole reason this is a pre-commit hook and not a
  post-commit one.
- The hook lives in `.git/hooks/`, which is **not** part of the repository, so
  every clone needs its own. `update-framework.sh <repo>` installs it, and
  `tf-metrics.sh --report` warns when the clone you are standing in has none. If
  you already have your own `pre-commit` hook, the installer leaves it alone and
  tells you — add `bash .tfcore/telemetry/pre-commit` to it if you want both.

Merge commits, `--no-verify`, rebases and cherry-picks skip the hook entirely.
Nothing is lost: reconciling means the next ordinary commit — here or on any
machine that pulls — notices those commits are missing and writes them.

To fill in a machine's history immediately rather than waiting for a commit:

    .tfcore/telemetry/tf-metrics.sh --backfill-commits .

Idempotent — already-recorded shas are skipped — so run it as often as you like.
It is also why the hook is optional: delete it and reconcile by hand instead.
