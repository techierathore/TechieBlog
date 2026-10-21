# TechieBlog — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md.

     THE ONE RULE FOR THIS DOCUMENT: no combined first-pass rate, gate catch
     distribution, or escape rate across live/backfilled or across project_type. -->

**Snapshot as of 2026-10-21** · project_type `app` · schema v1 · every figure from `tf-metrics.sh --report`

| Stream | Records |
|---|---|
| `runs.jsonl` | 50 |
| `gates.jsonl` | 328 (0 backfilled) |
| `sessions.jsonl` | 26 |
| `commits.jsonl` | 102 |
| `misses.jsonl` | 92 |

## 1. Quality — live records, project_type `app` only

All gate records are live; no backfilled column exists.

| Figure | Value |
|---|---|
| REQs scored | 155 |
| First-pass rate | 81% |
| Escape rate (no gate caught it) | 4% |

### Gate catch distribution (130 failures)

| Gate | Failures | Share |
|---|---|---|
| acceptance | 107 | 82% |
| render | 7 | 5% |
| visual | 5 | 4% |
| build | 4 | 3% |
| escaped | 4 | 3% |
| mockup-parity | 2 | 2% |
| standards | 1 | 1% |

Per-gate catch rates (not the share above): mockup-parity caught 2 of 7 records it ran on (29%); assets 0 of 8 (0%); perf insufficient data (n=1).

## 2. Throughput and effort (poolable)

- Runs: 50 — build-phase 7, fix-issues 22, verify-phase 15, triage-and-fix 2, triage-issues 2, handoff-phase 1, refresh-status 1.
- Rework ratio: 414% (fix-mode runs / build-phase runs). Median batch 4 REQs per build-phase run; 4.47 REQs/hour.
- Sessions: 26, 9,100,457 tokens in total. List price $578.42 over 20 priced runs — the published rate applied to measured tokens; a price, not a bill. Claude Code records carry no billed dollars.
- Commit cadence: 1.17 commits per active day (102 commits over 87 days).

### Effort per phase

Token figures cover only runs with a measured window. Fan-out was not observed on any run (no `tree`-scope windows in these phases), so a 0 there means *not looked*, not *none ran*. Phase costs reflect what each phase is, not how well it ran.

| Phase | Runs | Wall clock | Tokens out | Out share | Time share |
|---|---|---|---|---|---|
| build-phase | 7 | 32h38m | not measured (0 of 7) | — | 44% |
| verify-phase | 15 | 31h28m | 783.5k | 27% | 43% |
| fix-issues | 22 | 7h55m | 1.8M (9 of 22 measured) | 61% | 11% |
| triage-and-fix | 2 | 45m09s | 217.8k | 8% | 1% |
| triage-issues | 2 | 39m14s | 109.7k | 4% | 1% |
| handoff-phase | 1 | 25m30s | not measured | — | 1% |

## 3. Misses

- Logged: 92 (2 open, 3 resolved, 87 wont-fix); 90 fix records.
- Found by: gate 89, production 3 (3% found by a human — reported beside the escape rate, never merged with it).
- Classes: regression 90, other 1, partial-implementation 1. Design-miss share 0%.
- Why missed (4 of 92 assessed): insufficient-verify-method 3, instruction-ignored 1. Whose gap (3 of 4 sorted): weak-check 3.
- **This run (2026-10-21 production report):** 3 misses on REQ-FN-040 (post pagination ignored the saved word count), REQ-NFR-018 (the live site was not told to refresh after BlogApp saves), REQ-FN-062 (BlogApp crashed in WebView2's file dialog). All three were fixed and re-verified `Verified`.

### Attribution — linked records only (34 of 92; 58 excluded as inferred/unknown)

By origin phase: build-phase 29, fix-issues 5. By agent: general-purpose 29, flow-master 3, trblazeui 2. By model: unknown 31, claude-opus-5 3. A per-model rate is observational, not causal: harder work is not assigned at random.

### Rework cost

| Basis | Records | Tokens out per miss |
|---|---|---|
| sole (measured) | 0 | insufficient data (n=0) |
| shared (apportioned, equal division — not a measurement) | 3 | 72,605.7 |
| unattributable (no token window) | 87 | — |

No measured dollars: Claude Code records carry `cost_usd: null`, and no rate card is applied.

## 4. What is missing

- Token windows on build-phase and handoff-phase runs were never measured.
- 88 misses predate the `sort` field and are unsorted.
- No run had a `tree`-scope window, so subagent fan-out is unobserved everywhere.
