# TechieBlog — Misses

| | |
|---|---|
| App | TechieBlog |
| Count | 92 logged: 2 open, 3 fixed, 87 will not fix |
| Source | `docs/metrics/misses.jsonl`, one row per miss record. Rewritten by `tf-misses-md.sh` on every new record. Never edit it: a wrong row is corrected by a new record. |
| Updated | 2026-10-21 |

**Whose gap** answers the four questions of the miss protocol: **the app's spec** did not say it, so the checklist line is fixed; **the framework never said it**, so one requirement line and a check are added; **the check was too weak** (a review, or a script that did not fire), so the check is fixed; **said and ignored**, so the rule becomes a hook or is deleted. **not sorted** means the record predates the sort or nobody has answered yet; `bash .tfcore/utils/tf-emit.sh --amend <miss> sort <spec|unsaid|weak-check|ignored>` completes it.

## Open (2)

| Miss | Found | Whose gap | What went wrong |
|---|---|---|---|
| MISS-TechieBlog-20260907-01 | 2026-09-07 by gate | not sorted | triage edited 1 file(s) under src, source or tests, which a triage never does: source/TechieBlog/wwwroot/uploads/blog/blog-2-20260907002216-7bc38ddb.jpg |
| MISS-TechieBlog-20260906-79 (REQ-NFR-026) | 2026-09-06 by gate | not sorted | no sentence recorded (partial-implementation, src) |

## Fixed (3)

| Miss | Found | Closed | Whose gap | What went wrong |
|---|---|---|---|---|
| MISS-TechieBlog-20261021-03 (REQ-FN-062) | 2026-10-21 by production | 2026-10-21 by fix-issues | the check was too weak | BlogApp crashes while choosing an image to upload — native crash in WebView2's file chooser (EmbeddedBrowserWebView.dll 0x80000003) |
| MISS-TechieBlog-20261021-02 (REQ-NFR-018) | 2026-10-21 by production | 2026-10-21 by fix-issues | the check was too weak | Images set on posts from BlogApp do not appear on the live home page for up to 10 minutes — the site is not told to refresh (Settings save and Clear-cache never notify it; the notifier fails silently) |
| MISS-TechieBlog-20261021-01 (REQ-FN-040) | 2026-10-21 by production | 2026-10-21 by fix-issues | the check was too weak | Post pages split at 500 words although Settings say 2400 — the post page reads the word count from browser storage, never from the saved setting |

## Will not fix (87)

| Miss | Found | Closed | Whose gap | What went wrong |
|---|---|---|---|---|
| MISS-TechieBlog-20260906-88 (REQ-UI-064) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-87 (REQ-NFR-040) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-86 (REQ-UI-060) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-85 (REQ-NFR-033) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-84 (REQ-UI-059) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-83 (REQ-FN-058) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-82 (REQ-FN-057) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-81 (REQ-FN-053) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-80 (REQ-FN-052) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-78 (REQ-UI-057) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-77 (REQ-NFR-005) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-76 (REQ-NFR-001) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-75 (REQ-FN-050) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-74 (REQ-FN-049) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-73 (REQ-FN-048) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-72 (REQ-FN-038) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-71 (REQ-FN-037) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-70 (REQ-FN-036) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-69 (REQ-FN-035) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-68 (REQ-FN-034) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-67 (REQ-FN-032) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-66 (REQ-FN-031) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-65 (REQ-FN-029) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-64 (REQ-FN-028) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-63 (REQ-FN-027) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-62 (REQ-FN-025) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-61 (REQ-FN-023) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-60 (REQ-FN-022) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-59 (REQ-FN-021) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-58 (REQ-FN-020) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-57 (REQ-FN-019) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-56 (REQ-FN-018) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-55 (REQ-FN-017) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-54 (REQ-FN-016) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-53 (REQ-FN-015) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-52 (REQ-FN-014) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-51 (REQ-FN-013) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-50 (REQ-FN-012) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-49 (REQ-FN-011) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-48 (REQ-FN-009) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-47 (REQ-FN-008) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-46 (REQ-FN-006) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-45 (REQ-UI-056) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-44 (REQ-UI-054) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-43 (REQ-UI-053) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-42 (REQ-UI-052) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-41 (REQ-UI-049) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-40 (REQ-UI-048) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-39 (REQ-UI-047) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-38 (REQ-UI-046) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-37 (REQ-UI-045) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-36 (REQ-UI-044) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-35 (REQ-UI-043) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-34 (REQ-UI-040) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-33 (REQ-UI-039) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-32 (REQ-UI-038) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-31 (REQ-UI-037) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-30 (REQ-UI-036) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-29 (REQ-UI-035) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-28 (REQ-UI-034) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-27 (REQ-UI-033) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-26 (REQ-UI-032) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-25 (REQ-UI-030) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-24 (REQ-UI-029) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-23 (REQ-UI-027) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-22 (REQ-UI-026) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-21 (REQ-UI-025) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-20 (REQ-UI-024) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-19 (REQ-UI-023) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-18 (REQ-UI-022) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-17 (REQ-UI-021) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-16 (REQ-UI-020) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-15 (REQ-UI-019) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-14 (REQ-UI-018) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-13 (REQ-UI-017) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-12 (REQ-UI-016) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-11 (REQ-UI-012) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-10 (REQ-UI-011) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-09 (REQ-UI-010) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-08 (REQ-UI-009) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-07 (REQ-UI-008) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-06 (REQ-UI-007) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-05 (REQ-UI-006) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-04 (REQ-UI-005) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-03 (REQ-UI-004) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-02 (REQ-UI-003) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
| MISS-TechieBlog-20260906-01 (REQ-UI-001) | 2026-09-06 by gate | 2026-09-07 by log-miss | not sorted | no sentence recorded (regression, src) |
