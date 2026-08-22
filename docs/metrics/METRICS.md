# TechieBlog — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md.

     THE ONE RULE FOR THIS DOCUMENT: no combined first-pass rate, gate catch
     distribution, or escape rate across live/backfilled or across project_type.
     No "total" row, no "overall" line, no averaged intro sentence. If you are
     tempted to add one, re-read metrics-report.md §2 — the reason is not
     cosmetic. Commit-derived metrics are exempt from both separations. -->

**Snapshot as of 2026-08-20** · project_type `app` · schema v1

| Stream | Records | Span |
|---|---|---|
| `runs.jsonl` | 13 | 2026-08-09 → 2026-08-14 |
| `gates.jsonl` | 190 (0 backfilled) | 2026-08-09 → 2026-08-14 |
| `sessions.jsonl` | 14 | 2026-08-08 → 2026-08-20 |
| `commits.jsonl` | 93 | 2024-10-18 → 2026-08-11 |

Every record in every stream is **live** (written at the moment of the event —
zero backfilled) and carries `project_type: app`. No provenance separation is
required beyond this statement; there is no backfilled column anywhere on this
page.

---

## 1. First-pass rate

*What fraction of REQs reach `Verified` on attempt 1.*

| Provenance | project_type | REQs scored | First-pass | Rate |
|---|---|---|---|---|
| **Live** | app | 151 | 121 | 80% |

There is no backfilled row: `gates_backfilled = 0`.

**Excluded from the live rate:** 0 REQs. No `req_id` in this repo carries any
backfilled history, so the live `attempt` numbering is trustworthy and nothing
was excluded.

---

## 2. Gate catch distribution

*Of all failures, which gate caught them.*

### Live · `app` — 36 failures

| Gate | Caught | Share |
|---|---|---|
| build | 4 | 11% |
| acceptance | 19 | 53% |
| render (§4a data-render) | 7 | 19% |
| visual (§4b visual-truth) | 5 | 14% |
| standards | 1 | 3% |
| **escaped** — no gate caught it | 0 | 0% |

**Most common failure classes:** `other` ×15, `assert-fail` ×7, `blank-data` ×5.

**`perf` — insufficient data (n=1).** The perf gate entered the enum on
2026-08-10, mid-stream. Every record written before that date had zero chance of
recording it, and the gate only fires for a REQ carrying a `perf-budget:`. In
this repo `perf` ran on exactly 1 record and caught 0. Its catch rate is read
against that coverage, not against the 36-failure total above — a share computed
against the total would structurally understate it.

---

## 3. Escape rate

*What fraction of defective REQs reached UAT/production instead of a gate.*

| Provenance | project_type | REQs with any failure | Escaped to UAT/prod | Rate |
|---|---|---|---|---|
| **Live** | app | 34 | 0 | 0% |

Escapes are the `gate:"escaped"` records written by `*triage-issues` — a human
found the defect after every gate passed it. There are none in this stream; the
zero is real (the field exists in the data and never fired), not an absent field.

---

## 4. Throughput and rework — poolable

*These are comparable across `project_type` and across provenance, so they are
pooled deliberately.*

| Metric | Value |
|---|---|
| Runs total | 13 (build-phase=7, verify-phase=5, handoff-phase=1) |
| Rework ratio (fix-mode ÷ build-phase runs) | 71% |
| Batch size — median REQs per `build-phase` run | 4 |
| REQ throughput — median REQs/hour | 5.04 |
| Sessions / total tokens | 14 / 4,430,759 |
| Tokens per `Verified` REQ | 28,771.2 |
| Commit cadence | 1.13 commits/active day over 82 days |

**Cost in USD is not reported.** Claude Code transcripts carry token counts but
no per-message dollar cost, and this framework runs on a subscription where
marginal per-token cost is not the real unit. Multiplying tokens by a rate card
would be an estimate presented as a measurement, so the row says tokens and
stops. Two OpenCode sessions carry real `cost_usd`, but dollars are never pooled
across harness (Claude records are `null`; a mixed sum silently under-reports),
so the pooled figure stays tokens.

`commits.jsonl` is written by this repo's `pre-commit` hook, which is present
here (`commit_hook: true`) and reconciles against the log rather than appending
a single line. The stream lags reality by one commit — at `pre-commit` time HEAD
is still the previous commit, so the newest record ships in the next one.
Unavoidable in either direction, and the reconcile means the lag never becomes a
loss. Zero duplicate shas were collapsed at read time.

---

## 5. What is missing

- `perf` gate catch rate — `insufficient data (n=1)`; the gate has run on only 1
  record since its 2026-08-10 introduction. Needs ≥3 supporting records to read
  a share.
- `cost_usd` (pooled) — `null` across the mixed-harness stream; two OpenCode
  sessions carry real cost but pooling dollars across harness is forbidden by
  the schema. Reported in tokens instead.
- No backfilled records exist in any stream — nothing was reconstructed, so no
  metric here was guessed. This is the ideal state for every number on the page.
- Single-repo report. No cross-project rollup was requested; gates/runs/sessions
  here cover TechieBlog only.

---

<!-- Privacy: this document may only contain IDs, counts, durations, verdicts and
     file paths — exactly like the streams it summarises. Never requirement text,
     never prompt text, never a commit subject, never a failure description in
     prose. Assume it could become public. -->