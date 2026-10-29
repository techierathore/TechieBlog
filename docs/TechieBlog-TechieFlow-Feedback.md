# TechieFlow feedback — found while building TechieBlog

| | |
|---|---|
| App | TechieBlog |
| Upstream | TechieFlow |
| Updated | 2026-10-29 (TF-001 fixed upstream, ships in TechieFlow v1.2.2) |

## Summary

2 entries: 0 blocking now, 1 open (TF-002), 1 fixed upstream and not yet re-checked here (TF-001, fixed 2026-10-29, ships in TechieFlow v1.2.2).

The document check after recording TechieBlog as Large cannot close, because an unchanged, already-known size finding reads as new. The verify boot script cannot reach the web app it starts on this machine (NAT-mode WSL); worked around with two port relays.

## Entries

### TF-002 — tf-verify-boot.sh starts the web app on Windows `localhost`, which NAT-mode WSL cannot reach

- **Severity:** major
- **Blocks:** no — worked around for `*build-phase TechieBlog` (verify chain) on 2026-10-27
- **Repro:** WSL2 in the default NAT networking mode (no `networkingMode=mirrored` in `.wslconfig`); `bash .tfcore/utils/tf-verify-boot.sh start` on a web project whose build verdict is "via cmd.exe"
- **Expected:** BOOTED with a URL that WSL-side Playwright can open
- **Actual:** `NONE head=web kind=host reason=the published copy … did not answer on http://localhost:59689/ within 120 s although its log says it is listening`. The app binds Windows loopback (`--urls http://localhost:PORT`) and the readiness probe curls WSL's own `localhost`
- **Encountered in:** `*build-phase TechieBlog`, step 6 (chained verify), 2026-10-27
- **Workaround:** a Windows relay (`tf-cdp-relay.ps1 -ListenPort P+1 -TargetPort P`) plus a WSL relay from `localhost:P` to `<gateway>:P+1`, started only after the app owns `127.0.0.1:P`. WSL's localhost forwarding otherwise takes that Windows port first, and the app fails with "address already in use". Then `start --port 5450` printed BOOTED
- **Suggested fix:** on `PLATFORM=wsl` with a Windows-side rung, bind `--urls http://0.0.0.0:PORT` and probe/report `http://<default-route gateway>:PORT`, the same pattern the `windows` head already uses for its CDP relay


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

### Resolution status (TechieFlow team, 2026-10-29)

Ships in **TechieFlow v1.2.2**, the next release after v1.2.1. Already in this project's `.tfcore/` through the framework update.

| ID | Fix | Check it here |
|---|---|---|
| TF-001 | Fixed upstream. The reply is under the entry. | after a size change, `bash .tfcore/utils/tf-doc-check.sh --app TechieBlog`: the Coding Standards finding reads OLD, not FAIL. |
