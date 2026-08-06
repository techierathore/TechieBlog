# TechieBlog → TrBlazeUI feedback

Gaps found while designing the 2026-08-06 mockup set (`docs/mockups/`, 41 screens) against the
TrBlazeUI catalog (read from github.com/techierathore/TrBlazeUI — the local
`.trblazeui/TrBlazeUI-AI-Reference.md` is not yet deployed because the package is not installed).
Re-validate each entry against the AI reference once the feed credentials are in `nuget.config`.

## Library gaps (no catalog component — mockups compose from primitives)

- **TR-001 — Charting.** No chart control. Analytics dashboard (34) mocks the views trend and
  category bars as styled divs on `--chart-1..5`. Build needs custom SVG or a chart lib — or
  TrBlazeUI could add a simple Bar/Line chart.
- **TR-002 — Sortable / orderable list.** No drag-to-reorder control. Series parts (24) and
  experience entries (36) mock ⋮⋮ handles with ↑↓ buttons + NumericInput order — that fallback is
  also the acceptable no-drag implementation path.
- **TR-003 — Anchor nav / scrollspy.** Resume (10) needs a sticky in-page section nav; mocked as
  chip row + `position:sticky`.
- **TR-004 — Timeline.** Resume experience (10) uses a CSS-only timeline; no Timeline component.
- **TR-005 — Password strength meter.** Register/reset (14, 16) show static hint text; a live
  strength meter would need TbProgress repurposing or a new control.
- ~~**TR-006** — Icon toggle (favourite ♥).~~ **Withdrawn 2026-08-06** — favourites left scope with
  reader accounts (BRD-43/44 retired), so no favourite toggle is needed.
- **TR-007 — Code block / syntax highlighting.** Post body code (02) mocked as mono card;
  build needs highlight.js or accepts monochrome.
- **TR-008 — Search-term highlight helper.** Results (07) use plain `<mark>`; an excerpt
  highlighter is app-level, noted for completeness.
- **TR-009 — Stepper / numbered steps.** Series view part numbers (06) reuse avatar circles.
- **TR-010 — `--success` design token / `alert-success` variant.** Confirmation and "subscribed"
  states (44) hard-code `#16a34a`, the same value `.badge-success` hard-codes. A success token
  alongside `--destructive` would make these theme-aware (44, 42).
- **TR-011 — Disabled TbButton style.** No disabled variant, so the first/last-issue prev/next in
  the newsletter view (43) renders as a normal link.
- **TR-012 — Centred single-panel page layout.** No utility for a vertically centred card page; the
  verification landing (44) uses inline flex. A `TbCenteredPanel` / `.center-page` would remove it.
- **TR-013 — TbEmpty icon slot.** `.empty` has no icon area; the archive empty state (42) fakes one
  with a centred inline SVG.
- **TR-014 — TbSkeleton.** Every screen's spec names TbSkeleton for loading states but the mockup
  stylesheet has no skeleton shape, so loading states are specified yet unrenderable in the contract.
- **TR-015 — Captcha control.** The self-hosted challenge (BRD-99) is composed from TbInput +
  TbButton + a server-rendered SVG. Not expected from the library — recorded so the build knows it
  is app-owned, and to note the deliberate avoidance of `System.Drawing.Common` (Windows-only).

## Not gaps (exist in the catalog; earlier drafts mis-flagged them)

- **Rating** — exists (form components); used read-only on cards and interactive on the post page.
- **DatePicker / TimePicker / DateRangePicker** — Date & Time pickers exist; the *mockup CSS* just
  renders them as plain inputs. **Exception:** the catalog lists Date **Range** Picker, so the
  analytics From/To (34) should use it rather than two inputs — mockup shows the fallback.
- **DropdownMenu** now carries the admin **topbar account menu** (avatar trigger → My Profile /
  Log Out) on every admin screen. The build must wire it as a real popover — focus trapping,
  `Escape` to close, click-outside dismissal; the mockups only toggle a `hidden` panel.
- **DropdownMenu, Combobox, MultiSelect, FileUpload, MarkdownEditor** — all exist; mockups show
  simplified static shapes only.

## App-level composites (not library asks)

- **ImagePicker** — TechieBlog's own composite (FileUpload + Dialog + media-library grid),
  rebuilt on TrBlazeUI primitives (21, 25, 35–38).
- **MarkdownEditor split preview** — assumed native to TrBlazeUI's MarkdownEditor; if it is
  edit-only, pair it with the existing Markdig render service for the preview pane (21, 33).

---
Logged: 2026-08-06 · by *mockups (TechieBlog)
