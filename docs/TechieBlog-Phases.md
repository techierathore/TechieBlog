<!-- tf-schema
doc: phases
file: docs/TechieBlog-Phases.md
header: App, Kind, Size, Date
section: Phases | required
budget: S 300 600 | M 300 600 | L 300 600
rule: phases-table
-->
<!-- Authoring notes (agent only; never visible text).
     Large projects only. One row per phase; each phase fits Medium on its own (at most 20 screens,
     100 requirements). Phase 1 keeps the plain file names (docs/TechieBlog-BRD.md, -Checklist, -UIDesign,
     -DevGuide); phase 2 onward are docs/TechieBlog-P2-BRD.md and so on. BRD-N and REQ- ids run on across
     phases and are never reused, so the ranges never overlap. An item added to an earlier phase after a
     later phase exists takes the next free id and the row gets a second range: "BRD-1 to BRD-73, BRD-111 to BRD-118". Every screen sits in exactly one phase;
     the screens of a phase are exactly the "### Screen:" entries of that phase's UIDesign.
     Status is planned, building, done or kept-whole. kept-whole is only for a phase finished before the
     size cap that the owner chose to keep as one document: its cap and word maximum then WARN, not FAIL. The phase being worked is appPhase in core-config.yaml
     (bash .tfcore/utils/tf-day1-files.sh TechieBlog --phase N); handoff moves it on.
     tf-doc-check.sh --app TechieBlog refuses a screen in two phases, an id in two phases, a BRD id outside
     its phase's range, and a phase row without its BRD file. -->

# TechieBlog — Phases

| | |
|---|---|
| App | TechieBlog |
| Kind | app |
| Size | Large |
| Date | 2026-10-27 |

## Phases

| Phase | Name | Screens | BRD range | Status |
|---|---|---|---|---|
| 1 | Whole product (kept as one plan by owner decision, 2026-10-27) | Home — portfolio landing, Post view, Category archive, Tag archive, Series list, Series view, Search results, About, Not found, Resume, Sign in, Forgot password, Reset password, Access denied, Post editor, Posts list, Draft preview, Series admin, Media library, Admin Dashboard, User Management, Comment Moderation, Category Management, Tag Management, Subscribers, Site Settings, Newsletter Composer, Analytics Dashboard, Manage Profile, Manage Experience, Manage Skills, Manage Awards, BlogApp Connection Setup, BlogApp Login, BlogApp Shell, Newsletter archive, Newsletter issue, Email confirmation landing | BRD-1 to BRD-105 | kept-whole |
