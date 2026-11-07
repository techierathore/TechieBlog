# TechieFlow feedback — found while building TechieBlog

| | |
|---|---|
| App | TechieBlog |
| Upstream | TechieFlow |
| Updated | 2026-11-05 (TF-003 fixed upstream, TF-004 filed and fixed upstream; both ship in TechieFlow v1.2.5) |

## Summary

- 4 entries: 0 blocking, 0 open, 3 fixed upstream and not yet re-checked here (TF-001 major; TF-003 and TF-004 minor, ship in TechieFlow v1.2.5), 1 closed (TF-002).
- Last consolidated: 2026-11-05. No duplicates; every entry carries every schema field.
- TF-001 needs a project size change to re-check; none happened since the fix.
- TF-002 closed 2026-11-05: not reproducible on this machine (WSL runs mirrored networking since October; the NAT path was fixed upstream on 1 Nov).
- TF-003 fixed upstream 2026-11-05: a `wont-fix` miss now counts as closed, so a new defect on the same row gets a miss of its own.
- TF-004 filed and fixed upstream 2026-11-05: each miss carries what finding it cost, or "not measured" with the reason.

## Entries

### TF-004 — A defect found during a run has no discovery cost of its own; the run's one token window is shared across its rows

- **Status:** fixed upstream 2026-11-05, ships in TechieFlow v1.2.5; not yet re-checked here.
- **Severity:** minor
- **Blocks:** no — the metrics still count the miss; only what finding it cost is missing
- **Repro:** `*triage-and-fix TechieBlog`, 2026-11-05 (run of 06:14:25, 445,629 output tokens). It found that an Author can edit any post by its id (REQ-UI-016) while it touched four other rows (REQ-UI-017, REQ-UI-025, REQ-UI-047, REQ-FN-031). Then `bash .tfcore/telemetry/tf-metrics.sh --report .`
- **Expected:** each miss carries what finding it cost, in tokens and minutes, beside what fixing it cost; where that cannot be split, it says "not measured"
- **Actual:** a miss record has no cost field. The only figure is the run's window, the same for all five rows, so the report cannot put discovery cost beside fix cost per miss, the number the owner wants for TfLens
- **Encountered in:** `*triage-and-fix TechieBlog`, 2026-11-05 (session `5303084a`: "Discovery cost is not measured per defect; the framework splits this run's one token window across the rows")
- **Workaround:** none
- **Suggested fix:** a miss record carries its own discovery cost: the tokens and minutes from the start of the step that found it to the moment the record is written, read from the transcript the way `model_tokens_out` is today. Where the harness cannot give that split, the record says "not measured" instead of inheriting an even share. The metrics report shows discovery cost beside fix cost per miss, and `.tfcore/telemetry/SCHEMA.md` gets the fields
- **Reply (TechieFlow team, 2026-11-05):** Fixed, measured a little differently from the suggestion, and here is why. A command writes its misses all together when it closes, so "from the step's start to when the record is written" would give every miss of a run the same window: the even share again, under another name. Each miss now gets its own slice instead:
  1. **The slice** starts at the latest mark before the defect: a new `tf-triage.sh TechieBlog look "<the problem>"` mark that the triage runs as it starts on each problem, the run's previous finding, or the command's start. It ends when the defect was logged (`demote` / `new`). `tf-emit.sh` reads the tokens in that slice from the transcript (OpenCode: opencode.db), exactly as a run's window is read. Slices never overlap, so nothing is counted twice. The new fields are `discovery_started`, `discovery_ended`, `discovery_s`, `discovery_tokens_in/out/cache_read/cache_write`, `discovery_model_tokens_out`, `discovery_scope` and `discovery_why` (SCHEMA §5.5.12).
  2. **Not measured:** two findings logged within 2 minutes of each other with no `look` between them were found together. They read `not-measured`, naming each other, never half each. The same goes for a verify pass that opens more than one miss (one that opens exactly one gets the pass's window), and for `*log-miss`, where the owner found it.
  3. **The report:** `tf-metrics.sh --report` shows each miss's discovery cost beside its fix cost (the whole fix when it was the run's only one, its labelled share when shared), plus the mean over measured misses with how many were not measured.

  Triage-issues step 3 now tells the agent to run `look` before each issue. Regression case `bl_004`. **Replayed on your 5 Nov session** (read only): the Author defect was demoted at 06:32:15, 17 s after REQ-UI-017, so under this rule both read "not measured: found together". Together they cost 147,938 output tokens in the 18 minutes from the run's start, against 445,629 for the whole run; with a `look` before each, each would have had its own figure. Misses written before today have no discovery field and are counted apart as "not recorded", never as zero. **Check it here:** the next `/TechieFlow:agents:flow-master *triage-and-fix TechieBlog`, then `bash .tfcore/telemetry/tf-metrics.sh --report .`: the "DISCOVERY AND FIX COST PER MISS" lines name each new miss with its tokens and minutes, or "not measured" with the reason.

### TF-003 — A miss whose latest fix record says `wont-fix` still blocks a new miss on the same row

- **Status:** fixed upstream 2026-11-05, ships in TechieFlow v1.2.5; not yet re-checked here.
- **Severity:** minor
- **Blocks:** no — the new defect is still in the checklist row and the gate record; only its own miss record is missing
- **Repro:** a row with a miss whose latest `miss-fix` carries `verdict_after: "wont-fix"` (here REQ-UI-016, MISS-TechieBlog-20260906-12, whose fix record of 2026-09-07 says `wont-fix`); then `bash .tfcore/utils/tf-triage.sh TechieBlog demote REQ-UI-016 "<a different defect>"` and `… close`
- **Expected:** one new miss record for the new defect; a `wont-fix` decision is closed, as `metrics-report.md` already says ("Open-miss count excludes wont-fix, a decision, not a backlog item")
- **Actual:** `tf-emit.sh --open-miss REQ-UI-016` prints `MISS-TechieBlog-20260906-12 regression`, and the close prints `REQ-UI-016: already open as MISS-TechieBlog-20260906-12 (regression); no second miss`. The emitter's open test is "no miss-fix with verdict_after Verified", so `wont-fix` never closes anything
- **Encountered in:** `*triage-and-fix TechieBlog`, 2026-11-05, logging the Author-can-edit-any-post defect on REQ-UI-016
- **Workaround:** none taken; the defect is recorded in the row's Remarks and in the escaped gate record
- **Suggested fix:** in `tf-emit.sh` (`--open-miss` and `--open-misses`), treat a latest `miss-fix` with `verdict_after` of `Verified` or `wont-fix` as closed, matching the metrics report; and let the duplicate test compare the miss class as well as the row, since a `regression` and a `partial-implementation` on one row are two defects
- **Reply (TechieFlow team, 2026-11-05):** Fixed as you suggested, both parts. (1) In `tf-emit.sh`, `--open-miss` and `--open-misses` now count a miss as closed when its latest miss-fix says `Verified` or `wont-fix`. That matches the metrics report. The old rule was deliberate: a note in `tf-metrics.sh` kept a `wont-fix` miss open so that the same defect failing again would join it. The owner has decided otherwise, and the note now says so. The cost: a `wont-fix` row that fails verify again opens one new miss. (2) `--open-miss <REQ> <class>` finds the open miss of that class even when a newer miss of another class sits on the same row. The triage, log-miss and verify emitters pass the class, so a regression and a partial-implementation on one row are two misses. Regression case `bl_003`. **On your records** (read only): v1.2.4 `--open-miss REQ-UI-016` prints `MISS-TechieBlog-20260906-12 regression`, the fix prints nothing. Your `--open-misses` list goes from 13 to 1, because 12 of its misses were `wont-fix`. No other project changes. **For you:** the 5 Nov defect on REQ-UI-016 (an author can edit any post) still has no miss record, because the triage that found it is closed. **Check it here:** paste into TechieBlog's agent: `/TechieFlow:agents:flow-master *log-miss TechieBlog "An author can edit any post, not only their own" REQ-UI-016`. It logs a new miss instead of printing `already logged as MISS-TechieBlog-20260906-12`.

### TF-002 — tf-verify-boot.sh starts the web app on Windows `localhost`, which NAT-mode WSL cannot reach

> ✅ **Closed 2026-11-05** — re-checked here: not reproducible on this machine: WSL runs mirrored networking since October; the NAT path was fixed upstream on 1 Nov. Re-run 2026-11-05: tf-verify-boot.sh start with no relays printed BOOTED side=wsl url=http://localhost:59689

- **Severity:** major
- **Blocks:** no — worked around for `*build-phase TechieBlog` (verify chain) on 2026-10-27
- **Repro:** WSL2 in the default NAT networking mode (no `networkingMode=mirrored` in `.wslconfig`); `bash .tfcore/utils/tf-verify-boot.sh start` on a web project whose build verdict is "via cmd.exe"
- **Expected:** BOOTED with a URL that WSL-side Playwright can open
- **Actual:** `NONE head=web kind=host reason=the published copy … did not answer on http://localhost:59689/ within 120 s although its log says it is listening`. The app binds Windows loopback (`--urls http://localhost:PORT`) and the readiness probe curls WSL's own `localhost`
- **Encountered in:** `*build-phase TechieBlog`, step 6 (chained verify), 2026-10-27
- **Workaround:** a Windows relay (`tf-cdp-relay.ps1 -ListenPort P+1 -TargetPort P`) plus a WSL relay from `localhost:P` to `<gateway>:P+1`, started only after the app owns `127.0.0.1:P`. WSL's localhost forwarding otherwise takes that Windows port first, and the app fails with "address already in use". Then `start --port 5450` printed BOOTED
- **Suggested fix:** on `PLATFORM=wsl` with a Windows-side rung, bind `--urls http://0.0.0.0:PORT` and probe/report `http://<default-route gateway>:PORT`, the same pattern the `windows` head already uses for its CDP relay
- **Re-check attempt (2026-11-02):** `start` with no relays printed `BOOTED … side=wsl`: the new WSL-first path, not the Windows `0.0.0.0` + gateway path. This machine now runs `networkingMode=mirrored`, so NAT cannot be reproduced here; left fixed-not-re-checked. To close: on a NAT machine, `TF_BOOT_RUNNER=windows bash .tfcore/utils/tf-verify-boot.sh start` should print `BOOTED` at the gateway address.


### TF-001 — Changing the project size makes an old size finding count as new

- **Status:** fixed upstream 2026-10-29, ships in TechieFlow v1.2.2; not yet re-checked here.
- **Severity:** major
- **Blocks:** yes — `*amend-docs TechieBlog` (brand assets) cannot close its status gate
- **Repro:** record a baseline while no size is set, then `tf-day1-files.sh TechieBlog --size L`, then `tf-doc-check.sh --app TechieBlog`
- **Expected:** the Coding Standards word-count finding stays OLD: same document, same 2,786 words, same 1,200 maximum (the budget is identical for every size)
- **Actual:** it FAILs as new, because the message now says "Large maximum" where the baseline says "Small maximum"
- **Encountered in:** `*amend-docs TechieBlog`, owner decision 1 (record as Large, keep phase 1 whole)
- **Workaround:** none taken; the gate is left open and reported to the owner
- **Suggested fix:** in `tf-doc-check.py`'s baseline comparison, also normalise the size word (Small/Medium/Large) in `_shape()`, or compare on the counts and limit alone
- **Reply (TechieFlow team, 2026-10-29):** Fixed in `tf-doc-check.py`, the first way you suggested: `_shape()` no longer counts the size word (Small, Medium, Large) when it matches a size finding against the baseline. A finding whose count did not grow stays OLD across a re-size; a document that grew past its maximum still fails. Regression case `bl_001`. Miss `MISS-TechieFlow-20261029-03`. **Proved on a copy of this project's documents**, baselined as Small and checked as Large: v1.2.1 gives 1 FAIL (`TechieBlog-Coding-Standards.md: 2,786 words; the Large maximum is 1,200`), the fix gives 0. **On this repository as it stands:** the baseline was rewritten as Large on 2026-10-28, so that finding is already OLD with either script. The document check still has 2 FAILs, neither from TF-001: `docs/TechieBlog-TrBlazeUI-Feedback.md` has a Summary of 224 words (at most 200) whose counts are not the entries' (2 entries: 1 open, 1 closed). Rewriting that Summary closes the step. **Check it here:** after a size change, `bash .tfcore/utils/tf-doc-check.sh --app TechieBlog`: the Coding Standards finding reads OLD, not FAIL.

#### Detail

```
baseline: "2,786 words; the Small maximum is 1,200 (target 800). ..."
now:      "2,786 words; the Large maximum is 1,200 (target 800). ..."
```

`_shape()` replaces only digits, so the two messages differ by one word and the "same finding, count not larger" rule never matches. Any size change on a project with an over-limit document hits this.

## Replies from TechieFlow

### Resolution status (TechieFlow team, 2026-11-05)

Ships in **TechieFlow v1.2.5**, the next release after v1.2.4. Already in this project's `.tfcore/` through the framework update of 2026-11-06. Each regression case fails against the v1.2.4 scripts and passes now.

| ID | Fix | Check it here |
|---|---|---|
| TF-003 | Fixed upstream. The reply is under the entry. | `*log-miss` the REQ-UI-016 defect: a new miss, not "already logged as MISS-TechieBlog-20260906-12". |
| TF-004 | Fixed upstream. The reply is under the entry. | after the next `*triage-and-fix`, `tf-metrics.sh --report .` lists each new miss's discovery cost beside its fix cost. |

### Resolution status (TechieFlow team, 2026-11-01)

Ships in **TechieFlow v1.2.4**, the next release after v1.2.3; already in this project's `.tfcore/` through the framework update of 2026-11-01. Each fix has a regression case in `tests/regression/run.sh` that fails against the v1.2.3 script and passes now.

| ID | Fix | Check it here |
|---|---|---|
| TF-002 | Fixed upstream in `tf-verify-boot.sh` (the same change as AppManager TF-030 and Chatur TF-018). A web project with no MAUI target of its own runs on WSL. When WSL cannot build it, the Windows copy is bound to `0.0.0.0` and tried at `localhost` and at the WSL gateway, with no relays. Case `bl_002`. Not proved on a NAT machine: Windows Firewall may still block the gateway route, and `TF_BOOT_RUNNER=wsl` is then the way round. | After the framework update: `bash .tfcore/utils/tf-verify-boot.sh start` with no relays: expect `BOOTED`. |

### Resolution status (TechieFlow team, 2026-10-29)

Ships in **TechieFlow v1.2.2**, the next release after v1.2.1. Already in this project's `.tfcore/` through the framework update.

| ID | Fix | Check it here |
|---|---|---|
| TF-001 | Fixed upstream. The reply is under the entry. | after a size change, `bash .tfcore/utils/tf-doc-check.sh --app TechieBlog`: the Coding Standards finding reads OLD, not FAIL. |
