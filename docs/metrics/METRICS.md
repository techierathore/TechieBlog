# TechieBlog — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md.

     THE ONE RULE FOR THIS DOCUMENT: no combined first-pass rate, gate catch
     distribution, escape rate, miss rate, or cost-per-miss across live/backfilled,
     across project_type, across attribution confidence, across cost attribution,
     or across estimate and measurement. No "total" row, no "overall" line, no
     averaged intro sentence. -->

**Snapshot as of 2026-11-27** (sections 1–4 refreshed after the night *triage-and-fix; section 5 as of 2026-11-26; sections 4 and 6 still read as of 2026-11-16 unless dated) · project_type `app` · schema v1 · every figure from `tf-metrics.sh --report`, `--phases` and `--estimates`; nothing recomputed by hand

| Stream | Records |
|---|---|
| `runs.jsonl` | 87 (2026-08-08 → 2026-11-26) |
| `gates.jsonl` | 541 (0 backfilled, 0 malformed) |
| `sessions.jsonl` | 35 (1 duplicate collapsed) |
| `commits.jsonl` | 110 |
| `misses.jsonl` | 122 miss + miss-fix records |
| `estimates.jsonl` | 688 (one estimate: phase 1, 2026-11-16) |

Written by `*metrics TechieBlog` on 2026-11-16, straight after `*estimate-phase TechieBlog --phase 1`. Every record is live; this project has no backfilled data, so there is no backfilled column anywhere on this page.

---

## 1. First-pass rate

*What fraction of REQs reach `Verified` on attempt 1.*

| Provenance | project_type | REQs scored | First-pass | Rate |
|---|---|---|---|---|
| **Live** | app | 172 | — | 80% |

No REQ is excluded for backfilled history (0 tainted rows).

---

## 2. Gate catch distribution

*Of all failures, which gate caught them.*

### Live · `app` — 158 failures

| Gate | Caught | Share |
|---|---|---|
| build | 4 | 3% |
| acceptance | 110 | 70% |
| render (data shows) | 7 | 4% |
| visual (nothing overlaps or clips) | 13 | 8% |
| mockup-parity | 6 | 4% |
| standards | 1 | 1% |
| **escaped** — no gate caught it | 17 | 11% |

Gates added after the first records ran on fewer rows than the others: `assets` ran 23 times and caught 0; `mockup-parity` ran 19 times and caught 6 (32%); `perf` ran once and caught 0 (insufficient data, n=1); `test-users` (added 2026-11-01) has not run yet. A gate that was not there cannot have caught anything, so these shares undercount the late gates.

---

## 3. Escape rate

*What fraction of defective REQs reached UAT/production instead of a gate.*

| Provenance | project_type | Failures over the scored rows | Escaped to UAT/prod | Rate |
|---|---|---|---|---|
| **Live** | app | 158 | 17 | 16% (per defective REQ, as `tf-metrics.sh` prints it) |

2026-11-26: eight rows escaped to production in one report — a reader's rating was lost because no confirmation mail could ever be sent (REQ-FN-033, REQ-FN-048, REQ-UI-027/029/030 demoted; REQ-UI-072, REQ-UI-073, REQ-NFR-045 added). All eight were fixed and re-verified the same day by `*triage-and-fix`.

2026-11-26 night: three more found by the owner, none caught by a gate — box top padding (REQ-UI-074), no Blog page (REQ-UI-075), usage-guide mail timing (REQ-NFR-046). All three fixed and Verified the same night.

---

## 4. Throughput and rework — poolable

| Metric | Value |
|---|---|
| Runs total | 87 (verify-phase=25, fix-issues=22, build-phase=17, triage-and-fix=8, amend-docs=4, metrics-report=4, handoff-phase=2, triage-issues=2, devguide=1, estimate-phase=1, refresh-status=1) |
| Rework ratio (fix-mode ÷ build-phase runs) | 259% |
| Batch size — median REQs per `build-phase` run | 3 |
| REQ throughput — median REQs/hour | 6.09 |
| Sessions / total tokens | 35 / 13 210 986 |
| Tokens per `Verified` REQ | 31 243 |
| Commit cadence | 1.2 commits/active day (109 commits over 91 days) |

**Cost in USD is not reported as a measurement.** Every run here is Claude Code on a subscription: `cost_usd` is null on all of them. The list-price figure the tool prints (823.70 USD over 43 priced runs, 2.24 USD per Verified REQ) is a rate-card product, not money paid, and is shown only so the reader knows it exists.

---

## 5. Misses — what was missed, who missed it, what the fix cost

| Metric | Value |
|---|---|
| Misses logged | 110 (1 open, 97 resolved, 12 wont-fix; 185 fix records) |
| Design-miss share (`unspecified-gap`) | 1% |
| Found by a human (`owner` / `production`) | 5% (2 owner, 3 production) |

*Reported beside the escape rate in §3, never merged with it: different records, different definition. `wont-fix` is a decision, not a backlog item, so it is not counted as open.*

**Miss classes** — *what* was missed

| Class | n | Share |
|---|---|---|
| regression | 91 | 83% |
| partial-implementation | 14 | 13% |
| other | 1 | 1% |
| spec-contradiction | 1 | 1% |
| standards-violation | 1 | 1% |
| unspecified-gap | 1 | 1% |
| wrong-behaviour | 1 | 1% |

Found by: gate 103, production 3, agent-review 2, owner 2.

**Why it was missed** (6 of 110 misses assessed): insufficient-verify-method 4 (67%), instruction-ignored 2 (33%).

**Whose gap it was** (`sort`, 7 of 22 eligible misses carry it; 88 predate the field, added 2026-09-07): weak-check 5 (71%), spec 2 (29%).

### 5a. Attribution — `linked` records only

**40 of 110 misses are attributed; 70 are excluded** because they name a phase no `runs.jsonl` record backs, so the model that produced them is unknown.

| By | Counts |
|---|---|
| Origin phase | build-phase=35, fix-issues=5 |
| Origin agent | general-purpose=31, flow-master=6, trblazeui=3 |
| Origin model | unknown=33, claude-opus-5=3, claude-opus-5-5=3, claude-sonnet-5-5=1 |

**These are observational, not causal.** Which model gets the hard work is not random. 33 of the 40 attributed misses have no model recorded, so the per-model line rests on 7 records: insufficient data for any conclusion.

### 5b. Rework cost — measured and apportioned never combine

| | Fix records | Tokens out per miss |
|---|---|---|
| **Measured** (`sole`) | 0 | insufficient data (n=0) |
| Apportioned (`shared:n` — divided equally, **not a measurement**) | 10 | 88 537 |
| Unattributable (`none` — no usable token window) | 175 | — |

Discovery cost per miss: none recorded (all 110 misses predate the field). No measured dollars: Claude Code carries `cost_usd: null`, and tokens are the honest figure.

---

## 6. Effort per phase — time, tokens, model, fan-out

Aggregated over **73 live run records**. Token-window coverage: `tree 16` · `main 27` · `none 17` · absent 13. Duration: 60 of 73 records usable, 13 discarded (start after end), 4 taken from the timestamps where the stored figure disagreed.

| Phase (`cmd`) | Runs | Wall clock (total / median) | Tokens out | % of all output | Tokens measured on |
|---|---|---|---|---|---|
| `verify-phase` | 21 | 45h31m / 27m50s | 3.3M | 35% | 15 of 21 runs |
| `build-phase` | 14 | 40h22m / 1h50m | 1.9M | 20% | 7 of 14 runs |
| `fix-issues` | 22 | 7h55m / 37m30s | 1.8M | 19% | 9 of 22 runs |
| `estimate-phase` | 1 | 3m21s / 3m21s | 772.3k | 8% | 1 of 1 run ⚠ |
| `triage-and-fix` | 4 | 2h54m / 23m44s | 697.9k | 7% | 3 of 4 runs |
| `devguide` | 1 | 17m02s / 17m02s | 572.9k | 6% | 1 of 1 run |
| `amend-docs` | 3 | 43m43s / 6m38s | 178.2k | 2% | 3 of 3 runs |
| `handoff-phase` | 2 | 36m25s / 18m12s | 145.4k | 2% | 1 of 2 runs |
| `triage-issues` | 2 | 39m14s / 19m37s | 109.7k | 1% | 1 of 2 runs |
| `metrics-report` | 2 | 3m25s / 1m42s | 35.3k | 0% | 2 of 2 runs |
| `refresh-status` | 1 | — | 0 | 0% | 0 of 1 run |

Runs whose token window could not be computed are excluded from the token columns, never averaged in as zero.

⚠ **The `estimate-phase` figure is suspect.** The one run (2026-11-16) records 772.3k output tokens over 3 minutes 21 seconds on the main thread with no subagents. That is far more than the run wrote; the emitter's token window most likely over-counted. It is printed as the tool gave it, not corrected by hand.

`verify-phase` costing more than `build-phase` is a fact about what those phases are, not a finding about either.

### 6a. Which model did the work

| Phase | Model | Output tokens | Share of the phase | Runs |
|---|---|---|---|---|
| `verify-phase` | `claude-sonnet-5-5` | 2.1M | 64% | 1 |
| `verify-phase` | `claude-opus-5-5` | 424.4k | 13% | 6 |
| `verify-phase` | `claude-fable-5` | 315.6k | 10% | 2 |
| `verify-phase` | `claude-opus-5` | 303.9k | 9% | 4 |
| `verify-phase` | `claude-sonnet-5` | 143.0k | 4% | 1 |
| `verify-phase` | `claude-fable-5-1` | 13.9k | 0% | 2 |
| `build-phase` | `claude-sonnet-5-5` | 1.2M | 64% | 2 |
| `build-phase` | `claude-opus-5-5` | 567.4k | 30% | 6 |
| `build-phase` | `claude-fable-5-1` | 111.3k | 6% | 1 |
| `fix-issues` | `claude-opus-5` | 1.8M | 100% | 9 |
| `triage-and-fix` | `claude-fable-5-1` | 480.1k | 69% | 2 |
| `triage-and-fix` | `claude-opus-5-5` | 217.8k | 31% | 1 |
| `devguide` | `claude-haiku-5-5` | 461.7k | 81% | 1 |
| `devguide` | `claude-opus-5-5` | 111.1k | 19% | 1 |

**This split is observational, not causal.** The large `claude-sonnet-5-5` shares are subagent output inside one fanned-out run each.

### 6b. Subagent fan-out — measured, on its own denominator

| Phase | Runs observed | Spawns (total / median / max) | Runs that fanned out | Output tokens in subagents | Subagent share |
|---|---|---|---|---|---|
| `verify-phase` | 4 of 21 | 19 / 0 / 19 | 1 | 2.1M | 84% |
| `build-phase` | 6 of 14 | 10 / 0 / 8 | 2 | 1.2M | 68% |
| `devguide` | 1 of 1 | 4 / 4 / 4 | 1 | 461.7k | 81% |
| `handoff-phase` | 1 of 2 | 0 / 0 / 0 | 0 | 0 | 0% |
| `fix-issues` | 0 of 22 | not observed | — | — | — |

Fan-out is visible only on a `tokens_scope: "tree"` record; 0 on any other run means *not looked*, not *none ran*. `fix-issues`: 18 runs not tree scope, 4 written before `subagent_runs` existed (2026-08-31).

**Declared vs measured** (the measured figure is right): `build-phase` declared 11, measured 10; `verify-phase` declared 8, measured 19; `handoff-phase` declared 2, measured 0.

### 6c. Estimate against actual — two columns, never one number

**Phase 1 was estimated after the fact.** 44 runs on its own rows already existed, so this is hindsight, not a forecast. Those 44 runs were left out of the estimate's own history (a phase is never estimated from itself), and the figures come from the family repos TrBlazeUI and Chatur.

| Phase | Estimated by | Step | Estimate mid (low–high) | Actual | In range (tokens · time) | Actual runs (apportioned · named no row) |
|---|---|---|---|---|---|---|
| 1 | `claude-opus-5-5` | `build-phase` [171/171] | 2.5M (1.3M–4.9M) · 10h09m (4h18m–26h51m) | 1.9M · 40h22m | within · above | 14 (0 · 0) |
| 1 | `claude-opus-5-5` | `verify-phase` [171/171] | 84.8k (18.5k–341.8k) · 2h14m (1h01m–5h36m) | 3.3M · 45h16m | above · above | 20 (0 · 1) |
| 1 | `claude-opus-5-5` | `fix-issues` [171/171] | 4.7M (3.0M–8.1M) · 22h02m (16h55m–46h30m) | 754.2k · 4h55m | below · below | 7 (0 · 0) |
| 1 | `claude-opus-5-5` | after handoff [127/171] | 3.3M (1.3M–6.5M) · 10h24m (3h02m–28h08m) | insufficient data (n=2) · 2h59m | — · below | 3 (1 · 3) |

**Phase 1 in three parts**

| Part | Estimate mid (low–high) | Actual |
|---|---|---|
| build | 2.5M (1.3M–4.9M) tok · 10h09m (4h18m–26h51m) | 1.9M tok · 40h22m |
| machine QA (verify + fix) | 4.8M (3.0M–8.5M) tok · 24h16m (17h56m–52h06m) | 4.0M tok · 50h12m |
| after handoff | 3.3M (1.3M–6.5M) tok · 10h24m (3h02m–28h08m) | insufficient data (n=2) · 2h59m |

Unmeasured actual runs: `build-phase` 7 of 14 with no tokens (1 main-thread only); `verify-phase` 6 of 20 with no tokens (10 main-thread only); `fix-issues` 2 of 7 with no tokens (2 main-thread only); after handoff 1 of 3 with no tokens (2 main-thread only). A main-thread window did not read subagent transcripts, so those actuals are floors.

**Runs that named no row: 4** (1 `verify-phase`, 3 after handoff). They cannot be placed in any phase, so they are in no actual; they are counted, never shared out.

The 44 REQ-NFR rows have no precedent for the after-handoff step (2 family runs, none here), so that line covers 127 of 171 rows. Each of the 44 rows carries a dated Remark in the checklist. A row's QA estimate is one verify and one fix pass, so a phase that re-verifies many times sits above it. Owner UAT time is not measured.

---

## 5c. Run of 2026-11-26 — `*triage-and-fix` (mail confirmation)

| Miss | Row | Discovery cost | Fix cost (tokens out) | Verdict |
|---|---|---|---|---|
| ISS-TechieBlog-20261126-01 … -08 | REQ-FN-048, REQ-FN-033, REQ-UI-027, REQ-UI-029, REQ-UI-030, REQ-UI-072, REQ-UI-073, REQ-NFR-045 | not measured (the harness store held no messages in the triage window) | 45.5k each — **apportioned 1/8** of one run, not a measurement per row | Verified (all eight) |

## 7. What is missing

- **Dollars.** No run on this project carries a measured cost; the list-price line in §4 is a rate card, not a bill.
- **Tokens on half the build and fix runs.** 7 of 14 `build-phase` and 13 of 22 `fix-issues` runs have no token window, so their actuals in §6c are floors, not totals.
- **Fan-out on most runs.** 57 of 73 runs have no `tree` window.
- **`sort` and `why_missed` on old records.** 88 of 110 misses predate `sort`; `why_missed` was assessed on 6.
- **Per-model attribution.** 33 of 40 attributed misses carry no origin model.
- **A real forecast for phase 1.** The only estimate is hindsight; the first true forecast will be the next phase's, run before its `*build-phase`.
- **Commit telemetry.** The pre-commit hook is installed (`commit_hook: true`); the stream lags the owner's last commit by one by design.
- **Cycle time per feature** is not reported and never will be: the unit of work in this framework is the run, not the ticket.
