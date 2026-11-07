# TechieBlog — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md.

     THE ONE RULE FOR THIS DOCUMENT: no combined first-pass rate, gate catch
     distribution, escape rate, miss rate, or cost-per-miss across live/backfilled,
     across project_type, across attribution confidence, or across cost attribution.
     No "total" row, no "overall" line, no averaged intro sentence. -->

**Snapshot as of 2026-11-05** · project_type `app` · schema v1 · every figure from `tf-metrics.sh --report` and `--phases`; nothing recomputed by hand

| Stream | Records | Span |
|---|---|---|
| `runs.jsonl` | 69 | 2026-08-08 → 2026-11-05 |
| `gates.jsonl` | 512 (0 backfilled, 0 malformed) | 2026-08-08 → 2026-11-05 |
| `sessions.jsonl` | 30 (1 duplicate collapsed) | 2026-08-08 → 2026-11-05 |
| `commits.jsonl` | 105 | 2026-08-08 → 2026-11-02 |
| `misses.jsonl` | 110 miss + 184 miss-fix | 2026-08-09 → 2026-11-05 |

Written by `*triage-and-fix TechieBlog` on 2026-11-05 (the TrBlazeUI 2.2.2 upgrade for TR-077). Every record is live; this project has no backfilled data, so there is no backfilled column anywhere on this page.

---

## 1. First-pass rate

*What fraction of REQs reach `Verified` on attempt 1.*

| Provenance | project_type | REQs scored | First-pass | Rate |
|---|---|---|---|---|
| **Live** | app | 159 | 129 | 81% |

No REQ is excluded for backfilled history (0 tainted rows).

---

## 2. Gate catch distribution

*Of all failures, which gate caught them.*

### Live · `app` — 147 failures

| Gate | Caught | Share |
|---|---|---|
| build | 4 | 3% |
| acceptance | 110 | 75% |
| render (data shows) | 7 | 5% |
| visual (nothing overlaps or clips) | 13 | 9% |
| mockup-parity | 6 | 4% |
| standards | 1 | 1% |
| **escaped** — no gate caught it | 6 | 4% |

Gates added after the first records ran on fewer rows than the others: `assets` ran 17 times and caught 0; `mockup-parity` ran 12 times and caught 6; `perf` ran once and caught 0; `test-users` (added 2026-11-01) has not run yet. A gate that was not there cannot have caught anything, so these shares undercount the late gates.

**Most common failure classes:** `assert-fail` ×102 (the acceptance test failed), `other` ×17, `overlap` ×10, `mockup-drift` ×6.

Two of the six escapes are this run's: the posts-list crash on TrBlazeUI 2.2.1 (TR-077) and the editor opening any post for any Author, both found by reading and running the app, not by a gate.

---

## 3. Escape rate

*What fraction of defective REQs reached UAT/production instead of a gate.*

| Provenance | project_type | REQs with any failure | Escaped to UAT/prod | Rate |
|---|---|---|---|---|
| **Live** | app | 147 failures over the scored rows | 6 | 6% |

Escapes are the `gate:"escaped"` records written by triage. Of the two from 2026-11-05, REQ-UI-016 had `prior_verdict: Verified` (from 2026-10-28), the strongest signal on this page: a passing verify never asked who may open which post. REQ-UI-017 was already `Needs re-verify` when it escaped, because the DevGuide run of 2026-11-02 had flagged the crash by hand before any triage logged it.

---

## 4. Throughput and rework — poolable

| Metric | Value |
|---|---|
| Runs total | 69 (verify-phase=20, fix-issues=22, build-phase=13, triage-and-fix=4, amend-docs=3, handoff-phase=2, triage-issues=2, devguide=1, metrics-report=1, refresh-status=1) |
| Rework ratio (fix-mode ÷ build-phase runs) | 285% |
| Batch size — median REQs per `build-phase` run | 4 |
| REQ throughput — median REQs/hour | 5.42 |
| Sessions / total tokens | 30 / 10 743 074 |
| Tokens per `Verified` REQ | 29 433 |
| Commit cadence | 1.17 commits/active day over 90 days |

**Cost in USD is not reported as a measurement.** Every run here is Claude Code on a subscription: `cost_usd` is null on all of them. The list-price figure the tool prints (787.53 USD over 39 records, 2.16 USD per Verified REQ) is a rate-card product, not money paid, and is shown only so the reader knows it exists.

The rework ratio counts fix-issues and triage-and-fix runs against build-phase runs; the 2026-11-05 run was a fix (a package upgrade), which is why it moved from 269% to 285% since the last report.

---

## 5. Misses — what was missed, who missed it, what the fix cost

| Metric | Value |
|---|---|
| Misses logged | 110 (2 open, 96 resolved, 12 wont-fix) |
| Design-miss share (`unspecified-gap`) | 1% |
| Found by a human (`owner` / `production`) | 5% (2 owner, 3 production) |

*Reported beside the escape rate in §3, never merged with it: different records, different definition.*

*`wont-fix` is a decision, not a backlog item, so it is not counted as open. The duplicate check still treats it as a live defect, which is why the editor-ownership defect found on 2026-11-05 got no miss record of its own (TechieFlow feedback TF-003).*

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

**Why it was missed** — *which practice failed* (6 of 110 misses assessed)

| Practice | n | Share of assessed |
|---|---|---|
| insufficient-verify-method | 4 | 67% |
| instruction-ignored | 2 | 33% |

0 misses predate the field. 0 escapes lack it. The other 104 misses were found by a gate, where the field is optional and was not assessed.

**Whose gap it was** (`sort`, 7 of 22 eligible misses carry it; 88 predate the field)

| Sort | n |
|---|---|
| weak-check (a check existed and let it through) | 5 |
| spec (the specification never said it) | 2 |

Both readings point at verification, not specification: the checks that exist are too narrow for the defects that occur (the 2.2.1 upgrade check drove two components, not the posts-list search).

### 5a. Attribution — `linked` records only

**40 of 110 misses are attributed; 70 are excluded** because they name a phase no `runs.jsonl` record backs, so the model that produced them is unknown.

| By | Counts |
|---|---|
| Origin phase | build-phase=35, fix-issues=5 |
| Origin agent | general-purpose=31, flow-master=6, trblazeui=3 |
| Origin model | unknown=33, claude-opus-5=3, claude-opus-5-5=3, claude-sonnet-5-5=1 |

**These are observational, not causal.** Which model gets the hard work is not random; a model at the top may be doing the hardest building rather than the worst. Read it as a question, never as a ranking to route on. 33 of the 40 attributed misses have no model recorded at all, so the per-model line rests on 7 records: insufficient data for any conclusion.

### 5b. Rework cost — measured and apportioned never combine

| | Fix records | Tokens out per miss |
|---|---|---|
| **Measured** (`sole` — the run fixed only this REQ) | 0 | insufficient data (n=0) |
| Apportioned (`shared:n` — divided equally, **not a measurement**) | 10 | 88 537 |
| Unattributable (`none` — no usable token window) | 174 | — |

**Dollars.** No measured dollars: every record is Claude Code with `cost_usd: null`, and pricing tokens from a rate card would be an estimate presented as a measurement. Tokens are the honest figure.

174 of 184 fix records carry no token window at all, so the apportioned figure stands on 10 records, 4 of them from the 2026-11-05 run (one window shared over four rows). A miss fixed inline inside a longer run cannot be costed, which is why the unattributable row is printed rather than dropped.

---

## 6. Effort per phase — time, tokens, model, fan-out

Aggregated over **69 live run records**. Token-window coverage: `tree 16` · `main 21` · `none/absent 30`. Duration: 54 of 67 earlier records usable, 13 discarded (start after end), 4 taken from the timestamps where the stored figure disagreed. The two newest records (2026-11-05) were written after the `--phases` table below was computed and are not in it.

| Phase (`cmd`) | Runs | Wall clock (total / median) | Tokens out | % of all output | Tokens measured on |
|---|---|---|---|---|---|
| `verify-phase` | 20 | 45h12m / — | 3.3M | 41% | 14 of 20 runs |
| `build-phase` | 13 | 40h05m / — | 1.8M | 22% | 6 of 13 runs |
| `fix-issues` | 22 | 7h55m / — | 1.8M | 22% | 9 of 22 runs |
| `devguide` | 1 | 17m02s / 17m02s | 572.9k | 7% | 1 of 1 run |
| `triage-and-fix` | 2 | 45m09s / — | 217.8k | 3% | 1 of 2 runs |
| `amend-docs` | 3 | 43m43s / 6m38s | 178.2k | 2% | 3 of 3 runs |
| `handoff-phase` | 2 | 36m25s / — | 145.4k | 2% | 1 of 2 runs |
| `triage-issues` | 2 | 39m14s / — | 109.7k | 1% | 1 of 2 runs |
| `metrics-report` | 1 | 1m32s / 1m32s | 7.3k | 0% | 1 of 1 run |
| `refresh-status` | 1 | — | 0 | 0% | 0 of 1 run |

Runs whose token window could not be computed are excluded from the token columns, never averaged in as zero; the last column says how many that is per phase. A median is printed only where the tool printed one.

`verify-phase` costing more than `build-phase` is a fact about what those phases are (twenty verify runs over 159 rows, each booting the app and driving every screen), not a finding about either.

### 6a. Which model did the work

| Phase | Model | Output tokens | Share of the phase | Runs |
|---|---|---|---|---|
| `amend-docs` | `claude-opus-5-5` | 178.2k | 100% | 3 |

The per-model split is printed by `tf-metrics.sh --phases` only for phases whose every measured run names a model; for the other phases the tool prints no split, and none is invented here. **This ranking is observational, not causal.**

### 6b. Subagent fan-out — measured, on its own denominator

| Phase | Runs observed | Spawns (total / median / max) | Runs that fanned out | Output tokens in subagents | Subagent share |
|---|---|---|---|---|---|
| `verify-phase` | 4 of 20 | 19 / 0 / 19 | 1 | 2.1M | 84% |
| `build-phase` | 6 of 13 | 10 / 0 / 8 | 2 | 1.2M | 68% |
| `devguide` | 1 of 1 | 4 / 4 / 4 | 1 | 461.7k | 81% |
| `handoff-phase` | 1 of 2 | 0 / 0 / 0 | 0 | 0 | 0% |
| `fix-issues` | 0 of 22 | not observed | — | — | — |
| `triage-and-fix` | 0 of 2 | not observed | — | — | — |

**Read the `observed` column before anything else.** Fan-out is visible only on a `tokens_scope: "tree"` record; a `main`-scope window never read the subagent transcripts, so 0 on such a run means *not looked*, not *none ran*. Excluded: `fix-issues` 18 runs not tree scope and 4 written before `subagent_runs` existed (2026-08-31); `triage-and-fix` 2, `triage-issues` 2, `amend-docs` 3, `metrics-report` 1, `refresh-status` 1, all not tree scope.

**Declared vs measured.** The 2026-11-05 triage-and-fix run declared no subagents and its window is `main` scope, so its fan-out is unobserved, not zero; it did in fact spawn none.

---

## 7. What is missing

- **Dollars.** No run on this project carries a measured cost; the harness does not report one. The list-price line in §4 is a rate card, not a bill.
- **Fan-out on most runs.** 53 of 69 runs have no `tree` window, so §6b sees 12 runs. The framework's own session hook decides the scope; nothing here can widen it after the fact.
- **`sort` and `why_missed` on old records.** 88 of 110 misses predate the `sort` field and 104 were found by a gate, where `why_missed` is optional. The practice table in §5 therefore rests on 6 records.
- **Per-model attribution.** 33 of 40 attributed misses carry no origin model. Nothing on this page can say which model misses more.
- **Commit telemetry.** The pre-commit hook is installed (`commit_hook: true`); the stream ends at the owner's last commit on 2026-11-02 and lags reality by one commit by design.
- **Cycle time per feature** is not reported and never will be: the unit of work in this framework is the run, not the ticket.
