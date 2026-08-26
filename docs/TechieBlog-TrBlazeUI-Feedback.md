# TechieBlog → TrBlazeUI feedback

## Summary
- **Current disposition through TR-074: all TrBlazeUI findings are closed in library source.** Historical numbering has gaps and sub-findings (`TR-072b`…`TR-072d`), so this document deliberately reports the highest assigned ID instead of repeating the stale “66 entries” count.
- **Open library defects:** none from this report. TR-066/TR-067 and TR-068…TR-074 (including TR-072b/c/d) are resolved and verified.
- **Ships in: TrBlazeUI 2.0.3** — published to the feed and **consumed by TechieBlog since 2026-08-25** (`TrBlazeUI.Components` + `TrBlazeUI.Icons.Lucide` 2.0.3 in all three csprojs; build 0 errors, `dotnet test` 1551/0).
- **Consumer action — DONE 2026-08-25.** Every row of the resolution table below was measured against a running TechieBlog host on 2.0.3 *before* its workaround was removed (`tests/verify/trblazeui-203-upgrade.spec.ts`, 8/8; evidence `tests/.artifacts/trblazeui-203/notes.json`). See "Consumer verification on 2.0.3" under the table for what was removed, what was kept, and why.
- **One new finding on 2.0.3 — TR-075** (2026-08-26, Low–Medium a11y): a styled `Select` inside `DialogContent` drops focus to `<body>` after a pick, so `Escape` cannot dismiss the dialog until the user Tabs back in; mouse paths unaffected. Detail at the bottom of this file.
- Last consolidated: **2026-08-26**. Next free ID **TR-076** (the bottom counter line is authoritative).

## ✅ Resolution — 2026-08-25, TrBlazeUI **2.0.3** (pending publish)

This section is the authoritative disposition for the post-2.0.2 findings. The detailed repros later
in the file are preserved as historical evidence; any “OPEN” wording inside those dated repro blocks
describes 2.0.1/2.0.2, not the 2.0.3 source state.

| Finding | 2.0.3 source resolution | Verified evidence / TechieBlog follow-up |
|---|---|---|
| TR-066 | Nested `DialogPortal` content renders reactively within an existing document-level portal, avoiding stored-fragment disposal while preserving focus containment. | `tests/verify/ui-ui004.spec.js` PASS at 1280×900 and 390×844. Replace sibling-dialog workaround only after TechieBlog verifies its literal nested-dialog compositions on 2.0.3. |
| TR-067 | A `Select` nested in `DialogContent` renders its options inline at the already-portalled overlay level; mouse and keyboard paths both work. | Same focused spec: three options render; mouse selects Preview and keyboard selects Nightly. TechieBlog may restore styled `Select` where it used `NativeSelect` solely for this defect, after consumer UAT. |
| TR-068 | Styled `Select` now treats a supplied one-way `Value` as its effective initial/default value when no callback is supplied. | `tests/verify/ui-techieblog.spec.js`: one-way trigger renders “Engineering”; first-paint cases pass. |
| TR-069 | Focused `Input`/`Textarea` synchronization rejects delayed stale parent echoes instead of clobbering newer DOM text. | `ui-techieblog.spec.js`: focused delayed-echo check passes. TechieBlog should retain any document-identity latch that also represents application reset semantics until its own editor UAT proves it redundant. |
| TR-070 | Rating keyboard navigation moves DOM focus with the roving selection. | `ui-techieblog.spec.js`: focus, roving index, and checked option agree. |
| TR-071 | `ItemContent` ships `min-w-0`. | `ui-techieblog.spec.js`: narrow-width content shrinks without page overflow. Consumer-only `min-w-0` copies may be removed after upgrade verification. |
| TR-072 | `DatePicker` and `TimePicker` forward unmatched attributes to their visible trigger buttons. | `ui-techieblog.spec.js`: both trigger `data-testid` hooks render. Wrapper-only test hooks may be removed after 2.0.3 UAT. |
| TR-072b | `StatTile` emits stable value and label slots. | Two addressable slots verified by `ui-techieblog.spec.js`. |
| TR-072c | Missing min-height, responsive negative-margin, state-opacity, and related standard utilities are included in the generated bundle. | Computed-style checks pass at desktop and 390px. Arbitrary utilities remain available only when present in the prebuilt bundle; the AI reference now states this explicitly. |
| TR-072d | The AI reference documents the `_Imports.razor` upgrade requirement for newly introduced component namespaces. | Documentation/package source updated; TechieBlog must still update its own imports when adopting a new namespace. |
| TR-073 | Semantic gradient stop utilities are generated alongside gradient directions. | Computed `linear-gradient` with both stops verified. Remove the local fallback rule only after visual comparison on 2.0.3. |
| TR-074 | `AnchorNav` performs route-preserving scroll/history handling instead of resolving bare fragments against `<base href>`. | Verified `/verify-techieblog` remains on the same route with the expected fragment. TechieBlog may remove `post-toc-rail.js` interception after its post-detail UAT passes. |

Verification baseline for the 2.0.3 source:

| Gate | Result |
|---|---|
| `dotnet build TrBlazeUI.sln -c Release -p:CI=true` | **0 errors / 0 warnings** |
| `tests/verify/ui-ui004.spec.js` | **PASS desktop + mobile** — nested Dialog, focus, mouse/keyboard Select, visual bounds |
| `tests/verify/ui-techieblog.spec.js` | **76/76 PASS** — desktop + 390px, zero page errors / horizontal overflow |

### Consumer verification on 2.0.3 — TechieBlog, 2026-08-25

All three "remaining actions" are closed: 2.0.3 is on the feed, TechieBlog restored it (no new
`_Imports.razor` namespaces were needed — no new component was adopted), and each workaround was
removed only after the matching claim held on a running host (headless Chromium, admin circuit,
1280 + 390). Measured values, not inspection:

| Finding | Measured on TechieBlog @ 2.0.3 | Workaround disposition |
|---|---|---|
| TR-066 | Not exercised — TechieBlog composes stacked dialogs as siblings and had no literal nested-dialog markup to restore. | Nothing to remove. |
| TR-067 | `/admin/images` upload dialog Select: **7** options; mouse → "Icons" moves the caption to *Max 200 KB, formats: png, svg, webp*; keyboard Enter/ArrowDown/Enter → "Blog". `/users` edit dialog: **5** options. `/admin/skills` add-skill dialog: **7** options. | **Removed** — `ManageImages.razor` is back on the styled `Select`; `verify-all-admin.spec.ts` now asserts 0 native `<select>` and drives the popover. |
| TR-068 | Not exercised — every TechieBlog call site binds `ValueChanged`. | No workaround existed; the bUnit probe comment updated. |
| TR-069 | Not re-tested — the resolution table itself says to keep the document-identity latch. | **Kept** — `PostMarkdownEditor` `ResetKey`/`hasLocalEdits` stays. |
| TR-070 | No app-side workaround ever existed. | — |
| TR-071 | `/admin` at 390px: rendered `ItemContent` class is `flex min-w-0 flex-1 flex-col gap-1`, computed `min-width: 0px`, document hScroll **0**. | **Removed** — `AdminDashboard.razor` `ItemContent Class="min-w-0"` dropped. |
| TR-072 | `publish-date-picker` / `publish-time-picker` (`/ManagePost`) and `experience-start-date` / `experience-end-date` (`/admin/experience`) all resolve to a `<button type="button">`. | **Removed** — all four `<span>` wrappers gone; `data-testid` rides on the components. |
| TR-072b | Home stats band: `stat-tile-value` and `stat-tile-label` slots **1:1** with `home-stat-card`. | No consumer change needed; specs may target the slots. |
| TR-072c | `min-h-28` → 112px, `min-h-36` → 144px, `md:-mx-6` → −24px at 1280, `hover:opacity-90` served by `trblazeui.css` (plus focus/disabled variants). | **Removed** — the four hand-written rules deleted from `utilities.css`; only arbitrary values remain there. |
| TR-073 | `bg-gradient-to-br from-muted to-card` on the no-banner post card computes to `linear-gradient(to right bottom, oklch(0.269 0 0) 0%, oklch(0.205 0 0) 100%)` — both stops resolved. | **Removed** — `.post-card-fallback` deleted; `PostCard.razor` + `SearchResults.razor` use the utilities. |
| TR-074 | Moot for this app — the post-page TOC rail and `post-toc-rail.js` were deleted in UAT-029. | Nothing to remove. |

Two probe notes for the next consumer: Chromium serialises `to bottom right` as `to right bottom`,
and Tailwind v4 nests utilities inside `@layer`, so a `selectorText` sweep over `document.styleSheets`
must recurse into grouping rules or it reports the shipped rule as missing.

---

## Historical 2.0.2 resolution snapshot — 2026-08-11

> **VERSION LABEL CORRECTED 2026-08-11 by TechieBlog's `*build-phase`.** This section was written as
> "2.1.0 (unreleased; pending owner-manual publish)". **The release actually published to the feed
> as `2.0.2`**, and TechieBlog is now on it. `2.1.0` exists on the feed only as the prerelease
> `2.1.0-ci.2`. Everywhere below, read "2.1.0" as **2.0.2**.
>
> Identification was empirical, not by label: `2.0.2`'s `trblazeui.css` is **908,018 bytes** against
> `2.0.1`'s 88,202 — matching this document's own "~906 KB, up from ~88 KB" note — and the new
> component types (`Prose`, `StatTile`, `SortableList`, `Timeline`, `Stepper`) are present in
> `2.0.2`'s assembly and absent from `2.0.1`'s.
>
> **Two of the "you can delete this workaround" rows below did NOT hold for this app, and the
> deletions were reverted after measurement** — see **TR-067** (dialog `Select` still renders zero
> options on 2.0.2) and **TR-072** (`DatePicker`/`TimePicker` silently swallow `data-testid`, which
> *deleted* three working test hooks on upgrade). A third, **TR-069**, holds only with the host's
> own document latch kept in place. Treat the migration table as a starting hypothesis to measure,
> not a checklist to apply.

**All 65 entries in this file are now closed.** Every defect was fixed in the library — no entry was
closed by asking TechieBlog to keep its workaround. The gap requests (charting aside, which was
already refuted) are shipped as new components.

**One new defect was found on our side while building the demo pages** and was recorded at the end of
this file as **TR-066** — a `Dialog` declared literally inside another `Dialog`'s `DialogContent`
did not open on 2.0.1/2.0.2. That historical finding and its sibling Select case are resolved in
the 2.0.3 source; see the authoritative resolution table above.

**Every fix and every new component has a live example in the demo app**, so you can see the
behaviour before you upgrade: each new component has its own page under `/components/*`, the fixes
are demonstrated in a highlighted section on the existing page for that component, and
**`/whats-new`** collects the whole release with runnable examples of the splatting sweep, the
utility bundle and the retuned tokens.

**Verified on a running build, not by inspection:**

| Gate | Result |
|------|--------|
| `dotnet build TrBlazeUI.sln -c Release -p:CI=true` | **0 errors / 0 warnings** |
| `tests/verify/ui-techieblog.spec.js` on `/verify-techieblog` (headless Chromium, 1280 + 390) | **65/65 checks** |
| All demo routes crawled for exceptions / error boundaries | **103/103 clean** |
| Regression: `ui-ui014.spec.js` (AstroLyfe TR-003) | **8/8** |
| Regression: `ui-ui016.spec.js` (AstroLyfe TR-010/011/012) | **23/23** |
| `tools/splat-audit` over the built assemblies | **344/344** Components, **59/59** Primitives declare `CaptureUnmatchedValues` |
| `tools/token-contrast.py` over the shipped tokens | **0 failing pairings** (was 18) |

### Read this first — behaviour that changes under you

1. **Design tokens moved.** `--input` and `--ring` are materially darker in light mode
   (`oklch(0.922)`/`oklch(0.708)` → `oklch(0.66)`), dark `--input` → `oklch(0.55)`,
   dark `--accent` → `oklch(0.30)`, dark `--destructive-foreground` → near-black. TechieBlog already
   overrides `--input`, so **your values still win** (the library declares its own through
   zero-specificity `:where()` selectors). You can now drop the override if you want the shipped
   defaults; if you keep yours, nothing changes.
2. **`Rating` markup changed** — options are `<button role="radio">`, not `<span role="radio">`, and
   a `ReadOnly` rating is `role="img"` with no radio semantics at all. Any CSS or Playwright
   selector matching `span[role=radio]` needs updating.
3. **`TabsContent` always renders its panel element** (hidden when inactive; child content is still
   only rendered for the active tab). That is what makes `aria-controls` resolve.
4. **`trblazeui.css` is ~906 KB minified / ~97 KB gzipped**, up from ~88 KB, because it now ships
   the full Tailwind utility scale.

### Workarounds you can delete after upgrading

| Your workaround | Why it is no longer needed |
|---|---|
| Wrapper `<span>`/`<div>` carrying `data-testid` around `Label`, `Typography*`, `Breadcrumb`, `TabsList`, `Rating`, `DropdownMenuContent`, `RadioGroup`, `Select*`, `Alert*`, `DataTableColumn` … | TR-048 — every public component splats now (344/344, 59/59) |
| `source/BlogUI/wwwroot/css/utilities.css` (107 hand-declared utilities, `min-w-[720px]`, `w-36`, `top-1`) | TR-019/043/050 — the standard scale ships. Keep only genuinely arbitrary values, e.g. swap `min-w-[720px]` for `DataTable MinWidth="720px"` |
| ~~`Header.razor` replacing `NavigationMenu*` with `<nav><ul><li>` + `NavLink`~~ **REMOVED 2026-08-11 (cluster G)** | TR-044 — the library's own links are Tab reachable and carry no orphan `menuitem` role |
| ~~`AdminDashboard.razor` hand-rolled `<ul>/<li>` in place of `ItemGroup`/`Item`~~ **REMOVED 2026-08-11 (cluster G)** — needs `ItemContent Class="min-w-0"`, see TR-071 | TR-061 — `Item` emits `role="listitem"` inside an `ItemGroup` |
| ~~`PostRatingPanel.razor` `aria-hidden` + hidden `<fieldset>` radio fallback, and the `.tb-keyboard-fallback` class~~ **REMOVED 2026-08-11 (cluster G)** — the `.tb-keyboard-fallback` CSS itself is now dead and is cluster F's `utilities.css` sweep | TR-031/045/052 — the stars are keyboard operable and correctly announced |
| ~~The `MutationObserver` in `source/TechieBlog/Components/App.razor` (`data-a11y-decorative` tabindex re-application, `data-state`→`aria-selected` transcription, orphan `role="tab"` stripping, dangling `aria-controls` removal)~~ **REMOVED 2026-08-11 (cluster G)** — axe 0 before, 0 after, over 9 public + 15 admin routes | TR-052/054/063/064 — all four are fixed in the component markup. `Rating` also gained `Focusable="false"` for the decorative case |
| ~~`PostMarkdownEditor.razor` raw `<textarea>`~~ **DELETED 2026-08-11 (cluster H)** | TR-057 — `Textarea` no longer loses keystrokes on a Server circuit. Verified against the 400 ms-latency counterfactual (9/9, vs 4/9 failures on 2.0.1). **Keep the host's own `ResetKey`/`hasLocalEdits` latch — see TR-069.** |
| ~~Plain `<h1>`/`<h2>` with hand-copied token classes in `Newsletters.razor`, `VerifyEmail.razor`, `Routes.razor`~~ **REMOVED 2026-08-11 (cluster G)** | TR-020/021 — `Typography*` splats attributes and takes a `Size` |
| ~~`WrapTablesInScrollContainer` string transform in `PostView.razor`~~ **DELETED 2026-08-11 (cluster H)** | TR-059 — the body is now `<Prose ConstrainWidth="false">`. Pass `ConstrainWidth="false"` when the page already caps its measure; the default `max-w-prose` is 65ch. |
| Local `HomeStats.razor` tile composite | TR-022 — `StatTile` / `StatGroup` ship |
| The scoped `z-index: 120` rule for the ImagePicker portals | TR-060 — portals render in open order, so the nested dialog is on top by construction |
| Rendering display text yourself inside `SelectTrigger` | TR-049/058 — `SelectValue` resolves the item's `Text` on first paint |
| `<div data-testid="breadcrumb">` around `BlogBreadcrumb`'s `<Breadcrumb>` | TR-021 — put it on `<Breadcrumb>`; `BreadcrumbList` forwards its attributes onto the `<ol>` |

### Still your side

- **`_Imports.razor`** — keep `@using TrBlazeUI.Primitives.Services`; it is now part of the
  documented import block (TR-017). Keep fully qualifying `TrBlazeUI.Primitives.Sheet.SheetSide`
  rather than importing that namespace — that is the sanctioned pattern and is now written down
  (TR-018).
- **The `Tb` prefix in `docs/TechieBlog-UIDesign.md` and the 38 mockups** is a defect in
  TechieBlog's own spec, not a library issue. Still needs an `*amend-docs` pass on your side.
- **The Coding-Standards conflict** (`_variables.css` vs Tailwind v4 OKLCH tokens) is an owner
  decision on your side; the library's position is unchanged and now documented — theme by
  overriding tokens in an application `theme.css`.
- **Captcha (TR-015)** remains app-owned, as you recorded.
- **`INewsletterService.SendAsync` progress** is an application observation, not a library ask.

### Per-entry disposition

| ID | Disposition | Where |
|----|-------------|-------|
| TR-001 | Already refuted by TR-042 — charts ship | `Components/Chart/*` |
| TR-002 | **Added** `SortableList` (button-driven reorder, keyboard + screen-reader operable) | `Components/SortableList/` |
| TR-003 | **Added** `AnchorNav` with IntersectionObserver scrollspy | `Components/AnchorNav/`, `wwwroot/js/anchor-nav.js` |
| TR-004 | **Added** `Timeline` / `TimelineItem` (real `<ol>`/`<li>`) | `Components/Timeline/` |
| TR-005 | **Added** `PasswordStrength` (score exposed as text, not colour alone) | `Components/PasswordStrength/` |
| TR-006 | Withdrawn by reporter | — |
| TR-007 | **Added** `CodeBlock` (language label + copy button; no bundled highlighter by design) | `Components/CodeBlock/` |
| TR-008 | Confirmed app-level; no library change | — |
| TR-009 | **Added** `Stepper` / `StepperItem` with `aria-current="step"` | `Components/Stepper/` |
| TR-010 | **Added** `--success` / `--success-foreground` tokens + `bg-success`/`text-success`; `--alert-*` families now have library defaults | `trblazeui-input.css`, `tailwind.config.js` |
| TR-011 | Withdrawn by reporter | — |
| TR-012 | **Added** `CenteredPanel` | `Components/CenteredPanel/` |
| TR-013, TR-014 | Withdrawn by reporter | — |
| TR-015 | App-owned, as recorded | — |
| TR-016 | Already present — `ResponsiveNavProvider` / `ResponsiveNavTrigger` / `ResponsiveNavContent` is the responsive top-bar shell (demo at `/components/responsive-nav`). Not a gap; the catalog listing you read was incomplete | `Components/ResponsiveNav/` |
| TR-017 | **Fixed (docs)** — `TrBlazeUI.Primitives.Services` and `ApexCharts` added to the §1 import block, with the `SheetSide` / `PopoverSide` namespace split spelled out | AI reference §1 |
| TR-018 | **Fixed (docs)** — the safe-import rule is stated explicitly, with the list of shadowing sub-namespaces | AI reference §1 |
| TR-019 | **Fixed** — full Tailwind utility scale shipped | `trblazeui-input.css` safelist, regenerated `trblazeui.css` |
| TR-020 | **Fixed** — `Typography*` gains `Size`; `ClassNames.cn` is variant-aware | `Components/Typography/*`, `Utilities/TailwindMerge.cs` |
| TR-021 | **Fixed** — see TR-048 | catalog-wide |
| TR-022 | **Added** `StatTile` / `StatGroup` | `Components/Stat/` |
| TR-030 | **Fixed** — `Rating` and `Label` splat | catalog-wide |
| TR-031 | **Fixed** — buttons, roving tabindex, literal `aria-checked`, unique gradient ids | `Components/Rating/` |
| TR-040 | **Fixed** — see TR-048 | catalog-wide |
| TR-041 | **Fixed (docs)** — §8 chart snippet replaced with a compiling `ApexPointSeries` example; the Blazor-ApexCharts relationship is stated | AI reference §8 |
| TR-042 | Correction of the record; no action | — |
| TR-043 | **Fixed** — `min-w-*` ships, and `DataTable.MinWidth` gives the table its own minimum | `Components/DataTable/` |
| TR-044 | **Fixed** — plain links at the top level; `menuitem` only inside a real `role="menu"` | `Components/NavigationMenu/` |
| TR-045 | **Fixed** — `ReadOnly` renders `role="img"`, no radio semantics | `Components/Rating/` |
| TR-046 | **Fixed** — see TR-048 | catalog-wide |
| TR-047 | **Fixed** — `Label` splats | `Components/Label/` |
| TR-048 | **Fixed** — 344/344 and 59/59 verified by reflection; the two no-DOM exception groups are documented | catalog-wide, `tools/splat-audit` |
| TR-049 | **Fixed** — `SelectValue` resolves the item `Text` | `Primitives/Select/*` |
| TR-050 | **Fixed** — responsive `basis-*` ships | safelist |
| TR-051 | **Fixed** — the claim is now true and measured; the exceptions are published in the AI reference and both agent personas | AI reference, `docs/skills/*` |
| TR-052 | **Fixed** — `ReadOnly` drops the tab stop; new `Focusable` parameter | `Components/Rating/` |
| TR-053 | **Fixed** — see TR-057 | `Components/Input/`, `Utilities/TextValueSync.cs` |
| TR-054 | **Fixed** — `aria-controls` only when a panel exists, and the panel element stays mounted | `Primitives/Tabs/*` |
| TR-055 | **Fixed** — superseded by TR-061 | `Components/Item/` |
| TR-056 | **Fixed** — palette validated as a matrix; 18 failing pairings corrected; `--input` now clears 3:1 | `trblazeui-input.css`, `tools/token-contrast.py` |
| TR-057 | **Fixed** — `Input`/`Textarea` keep the DOM value out of the server echo; optional `DebounceMilliseconds` | `Utilities/TextValueSync.cs` |
| TR-058 | **Fixed** — items register while the listbox is closed; registration keyed on value | `Primitives/Select/*` |
| TR-059 | **Added** `Prose` + `[data-slot="prose"]` reflow rules | `Components/Prose/`, `trblazeui-input.css` |
| TR-060 | **Fixed** — portals render in open order | `Primitives/Services/PortalService.cs`, `PortalHost.razor` |
| TR-061 | **Fixed** — `Item` emits `role="listitem"`; `ItemSeparator` leaves the a11y tree | `Components/Item/` |
| TR-062 | **Fixed (docs + API)** — `AriaLabel` documented on `Input`/`Textarea`, with the "placeholder is not a name" warning | AI reference |
| TR-063 | **Fixed** — `aria-selected` serialised as `"true"`/`"false"` (and the same class of bug swept across Calendar, Collapsible, Menubar, RangeSlider, Sidebar, Slider, Toggle) | `Primitives/Tabs/TabsTrigger.razor` + 9 others |
| TR-064 | **Fixed** — `MarkdownEditor` renders its Write/Preview pair through `TabsList` | `Components/MarkdownEditor/` |
| TR-065 | **Fixed** — `DataTable.Refresh()` | `Components/DataTable/` |

*Thank you — this is the most useful consumer report the project has had. The measured evidence in
TR-051 and TR-056 in particular was reproduced exactly by the two tools now committed under
`tools/`, which is why both are now permanent gates rather than one-off findings.*

---

## Original report (unchanged below this line)


Gaps found while designing the 2026-08-06 mockup set (`docs/mockups/`, 41 screens) against the
TrBlazeUI catalog (read from github.com/techierathore/TrBlazeUI — the local
`.trblazeui/TrBlazeUI-AI-Reference.md` is not yet deployed because the package is not installed).
Re-validate each entry against the AI reference once the feed credentials are in `nuget.config`.

## Library gaps (no catalog component — mockups compose from primitives)

- **TR-001 — Charting.** No chart control. Analytics dashboard (34) mocks the views trend and
  category bars as styled divs on `--chart-1..5`. Build needs custom SVG or a chart lib — or
  TrBlazeUI could add a simple Bar/Line chart.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — refuted by TR-042; charts shipped. Compiling example now in AI reference §8.
- **TR-002 — Sortable / orderable list.** No drag-to-reorder control. Series parts (24) and
  experience entries (36) mock ⋮⋮ handles with ↑↓ buttons + NumericInput order — that fallback is
  also the acceptable no-drag implementation path.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — `SortableList<TItem>` added — ↑/↓ buttons, `@bind-Items`, every move announced.
- **TR-003 — Anchor nav / scrollspy.** Resume (10) needs a sticky in-page section nav; mocked as
  chip row + `position:sticky`.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — `AnchorNav` added, with an IntersectionObserver scrollspy and `aria-current="location"`.
- **TR-004 — Timeline.** Resume experience (10) uses a CSS-only timeline; no Timeline component.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — `Timeline` / `TimelineItem` added (real `<ol>`/`<li>`).
- **TR-005 — Password strength meter.** Register/reset (14, 16) show static hint text; a live
  strength meter would need TbProgress repurposing or a new control.
- ~~**TR-006** — Icon toggle (favourite ♥).~~ **Withdrawn 2026-08-06** — favourites left scope with
  reader accounts (BRD-43/44 retired), so no favourite toggle is needed.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — `PasswordStrength` added; the score is exposed as text, not colour alone.
- **TR-007 — Code block / syntax highlighting.** Post body code (02) mocked as mono card;
  build needs highlight.js or accepts monochrome.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — `CodeBlock` added (language label + copy button). No highlighter is bundled by design — pass pre-highlighted markup via `Html`.
- **TR-008 — Search-term highlight helper.** Results (07) use plain `<mark>`; an excerpt
  highlighter is app-level, noted for completeness.
- **TR-009 — Stepper / numbered steps.** Series view part numbers (06) reuse avatar circles.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — `Stepper` / `StepperItem` added, current step marked `aria-current="step"`.
- **TR-010 — `--success` design token / `alert-success` variant.** Confirmation and "subscribed"
  states (44) hard-code `#16a34a`, the same value `.badge-success` hard-codes. A success token
  alongside `--destructive` would make these theme-aware (44, 42).
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — `--success` / `--success-foreground` added with `bg-success` / `text-success` utilities, and the `--alert-*` families now ship library defaults for light and dark.
- **TR-011 — Disabled TbButton style.** No disabled variant, so the first/last-issue prev/next in
  the newsletter view (43) renders as a normal link.
- **TR-012 — Centred single-panel page layout.** No utility for a vertically centred card page; the
  verification landing (44) uses inline flex. A `TbCenteredPanel` / `.center-page` would remove it.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — `CenteredPanel` added (`Width` = Small/Medium/Large).
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

## Validation pass — 2026-08-06, against the deployed AI reference (*build-phase)

`TrBlazeUI.Components` **2.0.1** installed from the GitHub Packages feed; `.trblazeui/TrBlazeUI-AI-Reference.md`
(79 KB) deployed. Every entry above was re-validated against the real catalog, as the header asked.

### New gap found

- **TR-016 — No public-site top-bar shell (`ResponsiveNav`).** *Severity:* Medium.
  *Repro:* `docs/TechieBlog-UIDesign.md` §Design system mandates **TbResponsiveNav** as the shell for
  every public page (sticky top bar, brand + nav links + theme toggle, drawer below 768 px).
  *Expected:* a navigation shell component in the catalog. *Actual:* the catalog ships `NavigationMenu`,
  `Menubar`, `Sidebar`, `Sheet` and `Drawer` — no responsive top-bar shell.
  *Encountered in:* every public screen (01–10, 42–44). *Workaround (adopted):* compose the public
  shell from `NavigationMenu` + a `Sheet`/`Drawer` mobile drawer + `Button` theme toggle inside
  TechieBlog's own `Header.razor`. *Suggested fix:* add a `ResponsiveNav` (or document the composition
  as the sanctioned pattern).
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — **not a gap** — `ResponsiveNavProvider` / `ResponsiveNavTrigger` / `ResponsiveNavContent` already ship and are exactly this shell (demo: `/components/responsive-nav`). The catalog listing consulted was incomplete; the components are now in the AI reference import block.

### Entries REFUTED by the real catalog (were mockup-era guesses; now corrected)

- ~~**TR-011** — Disabled TbButton style.~~ **Withdrawn** — `Button` has a `Disabled` bool parameter.
- ~~**TR-013** — TbEmpty icon slot.~~ **Withdrawn** — `Empty` ships an `Icon` render fragment
  (plus `Title` / `Description` / `Size`). Note its API is **flat** — there is no
  `EmptyIcon`/`EmptyTitle` sub-component family, and mixing an explicit fragment with loose child
  content is a compile error (RZ10012).
- ~~**TR-014** — TbSkeleton missing.~~ **Withdrawn** — `Skeleton` exists; only the *mockup stylesheet*
  lacked a skeleton shape, which is a mockup limitation, not a library gap.

Still-valid gaps after validation: TR-001 … TR-005, TR-007 … TR-010, TR-012, TR-015, TR-016.

### ⚠ Not a library gap — a defect in OUR design spec (blocks nothing, but every builder must know)

`docs/TechieBlog-UIDesign.md` and all 38 mockups name components with a **`Tb` prefix**
(`TbCard`, `TbButton`, `TbDataTable`, `TbEmpty`, … 291 occurrences, 36 distinct names).
**The real library uses no prefix** — `Card`, `Button`, `DataTable`, `Empty`. The UIDesign spec
predicted this risk ("re-check component names against the AI reference when the feed is wired").
Builders must read every `Tb{X}` in the spec as `{X}`. The spec itself should be corrected via
`*amend-docs`, not silently during a build.

### ⚠ Coding-standards conflict raised by the migration (owner decision needed)

Coding Standards §"CSS and theming" forbids hardcoded values in components and requires every value to
come from `source/BlogUI/wwwroot/Themes/_variables.css`. TrBlazeUI is **Tailwind CSS v4 + OKLCH CSS
variables**, and its own rules mandate utility classes via the `Class` parameter and forbid inline
styles. These are incompatible as written: the Fluent-era `_variables.css` contract is superseded by
TrBlazeUI's `theme.css` OKLCH token set. Recorded here; the standards doc needs an `*amend-docs` pass.

## Gaps found during the REQ-UI-048 migration build (2026-08-06)

- **TR-017 — The documented `_Imports` set in AI-Reference §1 does not compile the components §1
  itself tells you to use.** *Severity:* Medium (documentation / packaging).
  *Repro:* Copy the `_Imports.razor` block from `.trblazeui/TrBlazeUI-AI-Reference.md` §1 verbatim,
  then follow §1's own layout instructions and write `<PortalHost />` plus
  `<SheetContent Side="SheetSide.Right">`.
  *Expected:* both resolve — §1 presents that import block as the complete set, and prescribes
  `<PortalHost />` in every root layout.
  *Actual:* neither resolves. `PortalHost` is `TrBlazeUI.Primitives.Services.PortalHost` and
  `SheetSide` is `TrBlazeUI.Primitives.Sheet.SheetSide`, but §1's list contains only
  `@using TrBlazeUI.Primitives` (the root namespace) and no `Primitives.*` sub-namespace.
  Build fails with `CS0103: The name 'SheetSide' does not exist in the current context`.
  The same split affects `PopoverSide` / `PopoverAlign` (`TrBlazeUI.Primitives.Services`), which
  `DropdownMenuContent.Align` and `TooltipContent.Side` need.
  *Encountered in:* `Layouts/*.razor` (PortalHost), `Components/Header.razor` (Sheet drawer),
  `Layouts/AdminLayout.razor` (account DropdownMenu alignment).
  *Workaround (adopted):* added `@using TrBlazeUI.Primitives.Services` to `_Imports.razor` — safe,
  since it collides with nothing — and fully qualified `TrBlazeUI.Primitives.Sheet.SheetSide`
  inline rather than importing `TrBlazeUI.Primitives.Sheet`, whose primitive `Sheet`/`SheetContent`
  types would shadow the styled `TrBlazeUI.Components.Sheet` family.
  *Suggested fix:* either re-export these enums from the styled component namespaces (so
  `TrBlazeUI.Components.Sheet.SheetSide` resolves), or add `TrBlazeUI.Primitives.Services` and a
  guidance note about the enum split to the §1 import block.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — §1 now includes `@using TrBlazeUI.Primitives.Services` and `@using ApexCharts`, and spells out the `SheetSide` / `PopoverSide` namespace split. Your adopted workaround is now the documented pattern.

- **TR-018 — Styled and primitive component families share type names across namespaces, so the
  "import everything" pattern is unsafe.** *Severity:* Low (documentation).
  *Repro:* Add `@using TrBlazeUI.Primitives.Checkbox` (or `.Label`, `.Switch`, `.Select`,
  `.RadioGroup`, `.Collapsible`, `.Accordion`, `.DropdownMenu`) alongside the styled equivalents.
  *Expected:* a documented statement of which namespaces are safe to import together.
  *Actual:* `CS0104` ambiguity between e.g. `TrBlazeUI.Components.Checkbox.Checkbox` and
  `TrBlazeUI.Primitives.Checkbox.Checkbox`. The reference does not warn about this.
  *Encountered in:* composing `_Imports.razor` for BlogUI.
  *Workaround (adopted):* import only `TrBlazeUI.Components.*` plus `TrBlazeUI.Primitives` and
  `TrBlazeUI.Primitives.Services`; never the other `Primitives.*` sub-namespaces.
  *Suggested fix:* state the rule explicitly in §1.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — the safe-import rule and the full list of shadowing `Primitives.*` sub-namespaces are stated in AI reference §1.

- **TR-019 — The shipped `trblazeui.css` is tree-shaken, so the library's own "use Tailwind utility
  classes" guidance silently fails.** *Severity:* **High** — this is the single biggest friction
  point found in the whole migration, and it fails silently.
  *Repro:* Follow the AI reference's core rule ("ALWAYS use Tailwind CSS utility classes via the
  `Class` parameter — never inline `style`") and write `<div class="max-w-7xl gap-6 py-8">`.
  *Expected:* the utilities apply — the guidance presents Tailwind utilities as the sanctioned way
  to style application markup, and the package promises "no Tailwind CSS setup, Node.js, or build
  tools are required".
  *Actual:* `_content/TrBlazeUI.Components/trblazeui.css` is a **pre-compiled, tree-shaken** Tailwind
  v4 bundle containing only the ~777 utilities TrBlazeUI's *own components* happen to reference.
  Any other class is absent from the bundle and does nothing — **no error, no warning, no visual
  hint**; the element simply renders unstyled. Because the package also ships no Tailwind CLI or
  config, an application has no supported way to generate the classes it needs.
  *Measured on this project:* 107 distinct utilities used across BlogUI were missing, including
  extremely common ones — `max-w-7xl`, `max-w-2xl`…`max-w-5xl`, `gap-5/6/8`, `gap-x-*`, `gap-y-*`,
  `py-8/10/16`, `mb-6/8`, `mt-3/8/16`, `my-4/6/8`, `pt-3/6`, `pb-24`, `grid-cols-1`, `grid-cols-2`,
  **every** responsive grid variant (`sm:grid-cols-2`, `md:grid-cols-2`, `lg:grid-cols-3`,
  `xl:grid-cols-4`), `sm:px-6`, `lg:flex-row`, `lg:w-80`, `space-y-3/6`, `divide-y`, `divide-border`,
  `last:border-b-0`, `backdrop-blur`, opacity modifiers (`bg-background/95`, `bg-destructive/10`),
  `no-underline`, `leading-relaxed`, `list-disc`, `object-contain`, `aspect-video`.
  Note the asymmetry that makes this so easy to miss: `gap-4` ships but `gap-6` does not;
  `md:flex` ships but `md:grid-cols-2` does not.
  *Encountered in:* every migrated layout, page and component — i.e. the entire REQ-UI-048 surface.
  *Workaround (adopted):* `source/BlogUI/wwwroot/css/utilities.css` hand-declares exactly the missing
  utilities on the Tailwind v4 scale using theme tokens, loaded immediately after `trblazeui.css`.
  No build step, so the library's no-Node promise is preserved.
  *Suggested fix (in preference order):* (1) ship the **full** Tailwind utility layer, not a
  tree-shaken one — it is the single change that makes the documented guidance true; or (2) ship an
  optional `trblazeui.utilities.css` companion for consumers who style their own markup; or (3) if
  tree-shaking must stay, document the exact supported class list and state plainly that any other
  utility will silently no-op, so consumers can plan for a Tailwind build of their own.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fix option (1) taken — `trblazeui.css` now ships the standard Tailwind scale with the responsive variants (~906 KB minified, ~97 KB gzipped). All 107 utilities you listed were re-checked on a running build and are present. Arbitrary values (`min-w-[720px]`) still cannot be pre-generated — the AI reference says so, and `DataTable.MinWidth` covers the table case.

- **TR-021 — `Typography*` and `Breadcrumb*` declare no `CaptureUnmatchedValues` parameter, so a
  `data-testid` on them is a hard runtime failure, not a no-op.** *Severity:* **High** — it took
  every public page down.
  *Symptom:* `System.InvalidOperationException: Object of type
  'TrBlazeUI.Components.Typography.TypographyH1' does not have a property matching the name
  'data-testid'` thrown during render. In a Blazor app whose router wraps the route view in an
  `ErrorBoundary` (the standard template shape) the exception is swallowed into the boundary and
  the visitor gets "Something went wrong" **instead of the whole page** — with a 200 status, so
  neither a smoke curl nor an uptime check notices.
  *Affected types confirmed by IL inspection of 2.0.1:* `TypographyH1`…`H4`, `TypographyP`,
  `TypographyLead`, `TypographyMuted` (and siblings), `Breadcrumb`, `BreadcrumbList`,
  `AlertDescription`. Contrast `Card`, `CardContent`, `Badge`, `Empty`, `Skeleton`, `Separator`,
  `Alert`, `Button`, `Input`, `Field`, `FieldLabel`, which all capture unmatched values correctly —
  the inconsistency is what makes this a trap.
  *Encountered in:* `Newsletters.razor` (REQ-UI-053) and, pre-existing, in
  `BlogUI/Components/BlogBreadcrumb.razor`, which passed `data-testid="breadcrumb"` to
  `<Breadcrumb>` and therefore broke **every** page using it — `/categories`, `/tags`, `/post/{slug}`,
  `/series/{slug}` all rendered the error boundary. Found 2026-08-07 while smoking REQ-UI-053/054.
  *Workaround (adopted):* move the attribute onto a plain wrapper element — `BlogBreadcrumb` now
  wraps its `<Breadcrumb>` in `<div data-testid="breadcrumb">`, and headings in the newsletter pages
  are plain `<h1>`/`<h2>` carrying the same token classes the Typography component would apply.
  *Suggested fix:* add `[Parameter(CaptureUnmatchedValues = true)]` to every leaf component that
  renders a single element — it is already the documented library-wide promise ("All components
  support CaptureUnmatchedValues", AI-Reference §Rules), so today the docs and the assembly
  disagree. Failing that, the reference must list the exceptions explicitly.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed catalog-wide — see TR-048. `data-testid` on `<Breadcrumb>` and on every `Typography*` is asserted live in `ui-techieblog.spec.js`.

- **TR-020 — `Typography*` bakes in its font size with no `Size`/`Level` parameter, and a `Class`
  override loses on CSS source order.** *Severity:* Low-Medium — cosmetic, but silent.
  *Symptom:* `<TypographyH1 Class="text-2xl">` still renders at `text-4xl` / `lg:text-5xl`.
  `TypographyH1`'s baked class string is `scroll-m-20 text-4xl font-extrabold tracking-tight
  lg:text-5xl`; a consumer class is appended to the same `class` attribute, so both rules have
  identical specificity and the winner is whichever appears **later in `trblazeui.css`** — and
  Tailwind emits `.text-2xl` (offset ~32257) before `.text-4xl` (~32447). The override therefore
  never applies for any *smaller* size, while a *larger* one appears to work. That asymmetry is
  what makes it look like a random bug rather than a rule.
  *Related but distinct from TR-021:* that entry is about attributes throwing; this one is about
  a `Class` that is accepted, rendered into the DOM, and silently has no effect.
  *Encountered in:* `VerifyEmail.razor` (REQ-UI-055) — mockup 44 specifies a 24 px card heading,
  `TypographyH1` renders ~36-48 px, which dominated a 512 px-wide centred card and wrapped the
  expired-state headline onto two lines.
  *Workaround (adopted):* a plain `<h1 class="text-2xl font-bold tracking-tight">`, i.e. the same
  escape hatch TR-021 already forced on the newsletter pages and `Routes.razor`. Note the cost:
  two of the library's own core selling points — semantic Typography components and "style via the
  `Class` parameter" — are now both unusable for headings in this codebase.
  *Suggested fix (in preference order):* (1) give `Typography*` a `Size` parameter (or an `As` +
  `Size` pair) so the size is a component concern rather than a CSS-ordering race; or (2) emit the
  library's baked classes through a zero-specificity `:where()` wrapper — the same trick
  `trblazeui.css` already uses so well for its theme tokens — so any consumer utility wins
  deterministically; or (3) document plainly that `Class` on `Typography*` can only *add*
  properties the component does not already set, never override one.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fix option (1) taken — `Size` (`TypographySize.Xs`…`Xl6`) REPLACES the baked size, including `TypographyH1`'s `lg:text-5xl`. `ClassNames.cn` is now variant-aware too, so `lg:` utilities take part in conflict resolution. Measured: default 48 px vs `Size="TypographySize.Xl2"` 24 px.

- **TR-022 — No stat / metric tile component.** *Severity:* Low–Medium.
  *(Renumbered from a duplicate TR-020 by the orchestrator, 2026-08-07. Three clusters running in
  parallel each minted TR-020 independently — none could see the others' allocations. The
  `Typography` sizing entry above keeps TR-020; this one becomes TR-022. No content changed.)*
  *Repro:* Build the headline-statistics band a portfolio landing page or any dashboard needs — a
  row of tiles each showing a large value over a small caption (`20+` / "Years of experience").
  *Expected:* something like `<StatTile Value="20+" Label="Years of experience" />`, the way most
  shadcn-derived kits ship a `MetricCard`. `DataTable`, `Progress` and `Chart` all exist, so the
  catalog clearly targets data-heavy screens; the simplest data display of all is the one missing.
  *Actual:* the tile has to be hand-composed from `Card` + `CardContent` + two `<span>`s carrying
  the type-scale utilities, and every consumer re-invents the same markup, so value/caption sizing
  drifts between pages.
  *Encountered in:* `source/BlogUI/Components/Home/HomeStats.razor` (REQ-UI-049 home stats band).
  *Workaround (adopted):* a local `HomeStats` component wrapping `Card`, so at least the app has
  one definition. It also has to hand-roll the responsive grid (`grid-cols-2 md:grid-cols-4`) via
  the TR-019 utilities file.
  *Suggested fix:* ship a `StatTile` / `MetricCard` with `Value`, `Label`, optional `Icon`,
  `Trend` and `Description`, plus a `StatGroup` wrapper that lays them out responsively.
  *Related to TR-019:* unprefixed `grid-cols-3` is absent from the bundle even though
  `sm:grid-cols-3` ships — another instance of the tree-shaking asymmetry called out there.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — `StatTile` (`Value`, `Label`, `Trend`, `TrendDirection`, `Description`, `Icon`) and `StatGroup` (responsive `Columns`) added.

### Not a gap — confirmed working during this build

- `Empty`'s flat API, `Button`'s inline-icon composition and `Rating` behaved exactly as the
  validated reference describes. `SidebarProvider`/`Sidebar`/`SidebarInset` supported the grouped
  admin shell (Content · Taxonomy · Media · Resume · Audience · System) with no gap.
- REQ-UI-049 (portfolio home, 2026-08-07): `Card`, `Skeleton`, `Empty`, `Spinner`, `Badge`,
  `Rating` and `Button` (incl. `Href` + inline-icon composition) all behaved as documented, and
  `CaptureUnmatchedValues` accepted `data-testid` on every one of them — which is what made the
  page testable without wrapper divs.
- Overriding the library palette is friction-free: `trblazeui.css` declares its own tokens through
  zero-specificity `:where(:root)` / `:where(.dark)` selectors, so an app `theme.css` wins on
  ordinary `:root` / `.dark` rules without `!important` or layer juggling. Worth documenting as the
  sanctioned theming entry point — it is what made the three TechieBlog site themes (BRD-67) easy.

---
Logged: 2026-08-06 · by *mockups (TechieBlog); validated 2026-08-06 by *build-phase;
TR-017/TR-018 added 2026-08-06 by *build-phase (REQ-UI-048 TrBlazeUI migration)

---

## Gaps found building the post-page engagement surfaces (2026-08-07, Cluster A — REQ-UI-027/029/056)

- **TR-030 — `Rating` declares no `CaptureUnmatchedValues` parameter, so it cannot carry a
  `data-testid`.** *Severity:* Medium.
  *Repro:* `<Rating @bind-Value="score" Max="5" data-testid="post-rating-stars" />`.
  *Expected:* the attribute lands on the rendered radiogroup, as it does for `Input`, `Textarea`,
  `Button`, `Card`, `Alert`, `Empty`, `Field*` and `Spinner` — every one of which does declare
  `AdditionalAttributes`.
  *Actual:* compile error — `Rating` exposes only `Value`, `ValueChanged`, `Max`, `AllowHalf`,
  `AllowClear`, `ReadOnly`, `Disabled`, `Icon`, `ActiveColor`, `InactiveColor`, `IconTemplate`,
  `Class`, `AriaLabel`, `Size`. `Label` has the same omission (`For`, `Class`, `ChildContent` only).
  *Encountered in:* `source/BlogUI/Components/PostRatingPanel.razor`,
  `source/BlogUI/Components/StarRating.razor`.
  *Workaround (adopted):* wrap the control in a plain `<div data-testid="…">` and target the
  wrapper from Playwright. Costs one extra DOM node per rating.
  *Suggested fix:* add `[Parameter(CaptureUnmatchedValues = true)] Dictionary<string, object>
  AdditionalAttributes` to `Rating` and `Label` so the whole catalog is consistent.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed — `Rating` and `Label` both splat; see TR-048.

- **TR-031 — `Rating`'s stars are non-focusable `<span role="radio">` with an EMPTY
  `aria-checked`, so the control is not keyboard operable and screen readers cannot report the
  selection.** *Severity:* **High** (accessibility).
  *Repro:* render `<Rating @bind-Value="score" Max="5" AriaLabel="Rate this article" />` and
  inspect the DOM, or try to reach a star with Tab.
  *Expected:* an ARIA radiogroup whose options are individually reachable (roving `tabindex`, or
  real `<button>` elements as the project's own mockup uses) and each carrying
  `aria-checked="true"`/`"false"`.
  *Actual:* the wrapper renders `<div role="radiogroup" tabindex="0">` and each star renders as
  `<span role="radio" aria-checked="" aria-label="3 out of 5" class="relative inline-flex
  cursor-pointer">` — no `tabindex` on the options, and `aria-checked=""` is an invalid token that
  assistive technology reads as unset. A keyboard user can focus the group but cannot select a
  value. Each star's inline `<svg>` also re-declares the SAME `<linearGradient
  id="star-gradient-<guid>-1">`, so a five-star control emits four duplicate element ids.
  *Encountered in:* `source/BlogUI/Components/PostRatingPanel.razor` (REQ-UI-027, an anonymous
  public write surface where keyboard access is mandatory).
  *Workaround (adopted):* none is possible from the outside — the markup is owned by the library.
  The panel ships with mouse/touch selection working and the numeric average, the count, the
  email field, the captcha and the submit button all fully keyboard reachable, so the *rating
  flow* is not blocked, but the star selection itself is not keyboard operable today.
  *Suggested fix:* render each option as `<button type="button" role="radio" aria-checked="…">`
  (or add roving `tabindex` plus Arrow/Home/End handling on the group), always emit a literal
  `"true"`/`"false"` for `aria-checked`, and give the gradient a per-star unique id.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed exactly as suggested — each option is a `<button type="button" role="radio">` with a roving `tabindex`, `aria-checked` is a literal `"true"`/`"false"`, and each gradient id carries the star index. Measured live: 1 of 5 options focusable, `aria-checked=[false,false,true,false,false]`, 0 duplicate ids, ArrowRight changes the bound value.

- **Not a gap — confirmed working.** `Input`, `Textarea`, `Field`/`FieldLabel`/`FieldContent`/
  `FieldDescription`/`FieldError`, `Card*`, `Alert*` (inline-icon-first composition), `Empty`,
  `Avatar`, `Spinner` and `Button` all accepted arbitrary `data-*`, `aria-*`, `autocomplete`,
  `tabindex` and `spellcheck` attributes through `AdditionalAttributes` exactly as documented, and
  `Input`'s `AriaInvalid` drove the invalid ring without extra CSS.

---
TR-030/TR-031 added 2026-08-07 by *build-phase (Cluster A — REQ-UI-027/029/056 post-page engagement)

---

## Gaps found building the admin newsletter composer and analytics dashboard (2026-08-07, Cluster D — REQ-UI-043/044)

> Numbering note: TR-020 and TR-021 were each used twice by earlier passes and Cluster A took
> TR-030/031, so this pass starts at **TR-040** to stay unambiguous.
>
> **Resolved 2026-08-07 (orchestrator):** the duplicate TR-020 is fixed — the stat/metric-tile entry
> is now **TR-022**; TR-020 remains the `Typography` sizing/specificity gap. TR-021 was only ever one
> entry (`CaptureUnmatchedValues` throwing); its other mentions are cross-references, not duplicates.
> Allocated so far: TR-001…TR-022, TR-030/031, TR-040…TR-046. **Next free ID: TR-047.**
> (TR-044/045 = Cluster J accessibility; TR-046 = Cluster I `Tabs` splatting.)

- **TR-040 — `CaptureUnmatchedValues` is missing from a large, undocumented slice of the catalog,
  and passing `data-testid` to one of those components throws at render time.** *Severity:*
  **High** — it is a hard runtime failure, not a styling nit, and the reference states the
  opposite.
  *Repro:* `<DropdownMenuContent data-testid="account-menu">` (or `TabsList`, `TabsTrigger`,
  `TabsContent`, `RadioGroup`, `RadioGroupItem`, `Label`, `AlertDialog`, `AlertDialogHeader`,
  `AlertDialogFooter`, `Dialog`, `Drawer*`, `Breadcrumb*`, `DataTableColumn`, `Combobox`,
  `ContextMenu*`, `Carousel*`, `Command*`, `SidebarProvider`, `ToastProvider`, `AspectRatio`,
  `DateRangePicker`, `ColorPicker`, `CurrencyInput`, `AlertDescription`, `AlertTitle`,
  `ButtonIcon`, `DataTableToolbar` …).
  *Expected:* the AI reference's core principle — "All components support CaptureUnmatchedValues —
  arbitrary HTML attributes (id, style, data-*, aria-*) … can be passed directly to any component".
  *Actual:* `System.InvalidOperationException: Object of type
  'TrBlazeUI.Components.DropdownMenu.DropdownMenuContent' does not have a property matching the
  name 'data-testid'.` A reflection sweep of `TrBlazeUI.Components` 2.0.1 finds **~90** public
  `ComponentBase` types with no `[Parameter(CaptureUnmatchedValues = true)]` property at all. The
  split is arbitrary and unguessable: `Card`/`CardHeader`/`CardTitle` support it but
  `AlertDialogHeader` does not; `DropdownMenuTrigger` supports it but `DropdownMenuContent` and
  `DropdownMenuItem` do not; `Tabs` supports it but `TabsList`/`TabsTrigger`/`TabsContent` do not.
  *Encountered in:* `source/BlogUI/Layouts/AdminLayout.razor` — the pre-existing
  `<DropdownMenuContent … data-testid="account-menu">` was throwing on **every** admin page, so the
  entire `/admin` shell rendered the error boundary instead of the sidebar. Also hit in
  `NewsletterComposer.razor` (Tabs + RadioGroup).
  *Workaround (adopted):* every test id now rides on a plain inner `<span>`/`<div>` inside the
  component rather than on the component itself.
  *Suggested fix (in preference order):* (1) add `[Parameter(CaptureUnmatchedValues = true)]
  Dictionary<string, object> AdditionalAttributes` to every public component and splat it — it is
  what the documentation already promises; or (2) if some components genuinely cannot splat,
  publish the exact list in the AI reference and change the core principle from "any component" to
  the truthful statement, because today the failure mode is a page-killing exception.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed — see TR-048.

- **TR-041 — The chart section of the AI reference documents an API the chart components do not
  have.** *Severity:* Medium (documentation; costs a build cycle each time).
  *Repro:* copy §8's example verbatim —
  `<ChartContainer Class="h-[300px]"><BarChart TItem="SalesData" Items="@data"
  XValue="@(d => d.Month)" YValue="@(d => d.Revenue)" /></ChartContainer>`.
  *Expected:* it compiles.
  *Actual:* `BarChart`/`LineChart`/`AreaChart`/`PieChart`/`RadarChart`/`RadialChart` expose
  `Items`, `Config`, `Height`, `Width`, `ShowLegend`, `LegendPosition`, `ShowDataLabels`,
  `ShowTooltip`, `Title`, `EnableAnimations` and `ChildContent` — there is **no** `XValue` or
  `YValue` parameter. The series actually come from nested `ApexCharts.ApexPointSeries` children
  (the XML doc comment on `BarChart` itself shows the correct shape; only the AI reference is
  wrong). `@using ApexCharts` is required for that and is absent from the §1 import block, and the
  reference never mentions that the chart family is a Blazor-ApexCharts wrapper whose namespace a
  consumer must import.
  *Encountered in:* `source/BlogUI/Pages/AdminPages/AnalyticsDashboard.razor` (views trend).
  *Workaround (adopted):*
  `<BarChart TItem="ViewTrendPoint" Items="@TrendPoints" Height="280px" ShowLegend="false">
   <ApexPointSeries TItem="ViewTrendPoint" Items="@TrendPoints" Name="Views"
   SeriesType="SeriesType.Bar" XValue="@(p => p.Label)" YValue="@(p => (decimal?)p.TotalViews)" />
   </BarChart>` — renders correctly, themes off `--chart-1`, and `ChartContainer` turned out not to
  be required.
  *Suggested fix:* replace the §8 chart snippet with a compiling one, add `@using ApexCharts` to
  §1, and state the Blazor-ApexCharts relationship.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — §8 replaced with a compiling `BarChart` + `ApexPointSeries` example, `@using ApexCharts` added to §1, and the Blazor-ApexCharts relationship is stated. `--chart-1…5` now have library defaults.

- **TR-042 — REFUTES TR-001 ("no charting") and the mockup's two "GAP" notes on screen 34.**
  *Severity:* n/a — correction of the record.
  TrBlazeUI 2.0.1 **does** ship charts (`AreaChart`, `BarChart`, `LineChart`, `PieChart`,
  `RadarChart`, `RadialChart`, `ChartContainer`, `ChartConfig`, `--chart-1..5`) and **does** ship a
  `DateRangePicker`. `docs/mockups/34-analytics-dashboard.html` still carries
  `*** GAP: TrBlazeUI has no charting component ***` and `*** GAP: TrBlazeUI has no
  TbDateRangePicker ***`; both were mockup-era guesses made before the package was installed and
  are now false. The analytics dashboard therefore uses the real `BarChart` rather than placeholder
  divs. The date range is still composed from two `Input Type="InputType.Date"` controls, but for a
  different reason: `DateRangePicker` is one of the TR-040 components that cannot carry a
  `data-testid`, and this project's standards require a stable test id on every data-bound control.

- **TR-043 — `min-w-*` utilities are absent from the tree-shaken bundle, so the documented
  "wide table scrolls in its own container" pattern silently does nothing.** *Severity:* Low
  (a further instance of TR-019, recorded because it bites specifically on `DataTable`).
  *Repro:* wrap a five-column `DataTable` in `<div class="overflow-x-auto">` inside a `Card`.
  *Expected:* the table keeps its natural width and the wrapper scrolls.
  *Actual:* the table is `w-full`, shrinks to the card, and the right-hand columns are clipped away
  with no scrollbar; `min-w-[720px]` (or any `min-w-*` beyond `min-w-[200px]`) is not in the bundle,
  so the obvious fix no-ops.
  *Encountered in:* the popular-posts table on the analytics dashboard — the Rating column vanished
  at both 1280 px (half-width card) and 390 px.
  *Workaround (adopted):* `min-w-[720px]` hand-declared in `source/BlogUI/wwwroot/css/utilities.css`
  plus an `overflow-x-auto` wrapper; the card was also widened to the full content column.
  *Suggested fix:* ship the `min-w-*` scale, or have `DataTable` provide its own horizontal-scroll
  wrapper with a sensible minimum width.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — both suggested fixes taken — the `min-w-*` scale (including `min-w-3xl` = 768 px) ships, and `DataTable` gained `MinWidth` so it can give the table its own minimum without an arbitrary-value class.

### Not a library gap — an application-side observation worth recording

- `INewsletterService.SendAsync` is atomic and exposes no progress callback, so a composer cannot
  report per-recipient progress from the service contract alone. The composer gets **real**
  progress anyway by polling `GetSendHistoryAsync(newsletterId).Count` while the dispatch task is
  in flight, because `NewsletterSvc` writes one send-history row per recipient as it goes. Noted
  here so the next builder does not fake a timer-driven bar.

### Not a gap — confirmed working during this build

- `BarChart` + `ApexPointSeries` render correctly under Blazor Server with no extra script tag:
  Blazor-ApexCharts loads its own ES module from `_content/Blazor-ApexCharts/`, and the bars pick up
  `--chart-1` from `theme.css` in both light and dark.
- `Card*`, `Input` (including `InputType.Date`), `Button`, `Badge`, `Alert`, `Empty`, `Skeleton`,
  `Spinner`, `Progress`, `Separator`, `DataTable`/`CellTemplate`, `MarkdownEditor`, `Tabs` (the
  root), `AlertDialogContent`, `DropdownMenuTrigger` and every `Sidebar*` component accepted
  `data-testid` / `aria-*` through `AdditionalAttributes` exactly as documented.
- `MarkdownEditor` ships its own Write/Preview toggle and a formatting toolbar out of the box; the
  composer keeps a second, outer "Email preview" tab only because it previews the *delivered mail*
  (rendered body plus the mandatory unsubscribe footer), which is a different thing.

---
TR-040…TR-043 added 2026-08-07 by *build-phase (Cluster D — REQ-UI-043/044 newsletter composer + analytics dashboard)

---

## Gaps found running the WCAG 2.1 AA audit (2026-08-07, Cluster J — REQ-NFR-006/007)

> Numbering note: this pass continues from the allocation table above and starts at **TR-044**.
> Nothing above is renumbered. **Next free ID after this pass: TR-046.**

Method: headless Chromium + `@axe-core/playwright` with the `wcag2a, wcag2aa, wcag21a, wcag21aa`
rule tags, run over `/`, `/post/{slug}`, `/resume`, `/newsletters`, `/login` and `/search` at
1280×900 and 390×844, plus a manual 35-stop Tab traversal of the post page. **Every axe violation
found across all twelve page/viewport combinations traced back to a TrBlazeUI component — the
application's own markup produced only one violation (`link-in-text-block`), which was fixed in
app code.**

- **TR-044 — `NavigationMenu` renders an INCOMPLETE roving-tabindex menubar, making the whole
  navigation unreachable by keyboard.** *Severity:* **Critical** (accessibility — WCAG 2.1.1
  Keyboard, and 4.1.2 Name/Role/Value).
  *Repro:* render the documented composition and press Tab repeatedly:
  ```razor
  <NavigationMenu><NavigationMenuList>
      <NavigationMenuItem><NavigationMenuLink Href="/">Home</NavigationMenuLink></NavigationMenuItem>
  </NavigationMenuList></NavigationMenu>
  ```
  *Expected:* the navigation entries are reachable with Tab (a site nav is a list of links), or —
  if the menubar pattern is genuinely intended — exactly one entry carries `tabindex="0"` and the
  group handles Arrow/Home/End to move between the others.
  *Actual:* every entry renders as `<a href="…" role="menuitem" tabindex="-1">` inside a plain
  `<ul>` that carries **no** `role="menubar"`. No item ever receives `tabindex="0"` and no
  arrow-key handler is registered, so the roving-tabindex pattern is half-implemented: **0 of 6
  navigation links were reachable by Tab** in a 12-stop traversal of the home page. axe reports
  the orphan roles separately as `aria-required-parent` (critical, 6 nodes on every page that
  renders the header).
  *Encountered in:* `source/BlogUI/Components/Header.razor` (REQ-UI-048 public chrome).
  *Workaround (adopted):* replaced `NavigationMenu`/`NavigationMenuList`/`NavigationMenuItem`/
  `NavigationMenuLink` in `Header.razor` with plain `<nav aria-label="Primary"><ul><li>` +
  Blazor's own `<NavLink>`, styled with the same utility classes the mobile drawer already uses.
  Both violations disappear and all six links become keyboard reachable. There is no way to fix
  this from the outside — `tabindex` and `role` are emitted by the library's own markup.
  *Suggested fix:* drop the `menuitem` roles and the negative `tabindex` for the plain
  navigation case (this is what shadcn/ui's own NavigationMenu does — it renders ordinary links);
  keep the menubar pattern only for genuine application menus, and then complete it with a
  `role="menubar"` container, a `tabindex="0"` active item and Arrow/Home/End key handling.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed as suggested — top-level `NavigationMenuLink` renders an ordinary link with no `menuitem` role and no negative `tabindex`; the menu-item semantics apply only inside `NavigationMenuContent`, which is a real `role="menu"`. Measured live: 2 of 2 links Tab reachable, no orphan roles.

- **TR-045 — `Rating` is inaccessible even in `ReadOnly` mode: it emits interactive radio
  semantics with an invalid `aria-checked` for what is a pure display of a number.**

  *Severity:* **High** (accessibility — WCAG 4.1.2).
  *Repro:* `<Rating Value="4" Max="5" ReadOnly="true" Size="RatingSize.Small" />`, then run axe.
  *Expected:* a read-only rating is a *value*, not a control. It should render as text or an
  `<img role="img" aria-label="Rated 4 out of 5">`-style graphic with no radio semantics at all.
  *Actual:* identical markup to the interactive case — five `<span role="radio" aria-checked="">`
  elements. axe reports `aria-required-attr` (**critical**) with 10 nodes on the home page and 5
  on the post page, because `aria-checked=""` is not a valid token. Screen readers announce a
  radio group the user then cannot operate.
  *Encountered in:* `source/BlogUI/Components/StarRating.razor`, used by `PostCard` and
  `SearchResults` (REQ-UI-041/042/048).
  *Workaround (adopted):* wrap the `Rating` in `aria-hidden="true"` and expose the value as real
  `sr-only` text (`"Rated 4 out of 5 from 12 ratings"`). This clears the axe failure and gives
  assistive technology an accurate reading, at the cost of the library's own `AriaLabel`
  parameter becoming useless.
  *Suggested fix:* when `ReadOnly="true"`, render no `role="radio"` at all — emit
  `role="img"` + `aria-label` on the wrapper and mark the stars `aria-hidden`.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed exactly as suggested — `ReadOnly` renders `role="img"` + `aria-label="Rated 4 out of 5"` with the stars `aria-hidden` and no radio semantics. Your `aria-hidden` + `sr-only` wrapper can go, and `AriaLabel` is useful again.

### TR-031 — audit findings appended (2026-08-07, Cluster J)

The original TR-031 entry above is **confirmed in full** by the axe/keyboard pass, with these
measured specifics added:

- **Keyboard:** of the five `<span role="radio">` stars on `/post/{slug}`, **0 are focusable**
  (`tabIndex >= 0` count is zero). The 35-stop Tab traversal reaches the rating *group* wrapper
  and then jumps straight past it to the related-posts links — star selection is unreachable, so
  WCAG **2.1.1 Keyboard** fails. Confirmed library-caused; not fixable from application markup.
- **Duplicate ids:** measured **4 duplicate `<linearGradient id>` values** in a single five-star
  control (5 stars, 1 unique id), confirming the DOM-id collision noted originally — WCAG
  **4.1.1 Parsing** / general DOM validity.
- **`aria-checked`:** reported by axe as `aria-required-attr`, **critical**, on every page that
  renders a rating.
- **Workaround now adopted (change from the original entry, which said none was possible):** a
  keyboard-operable fallback *is* achievable from application code by not relying on the library
  widget for semantics. `PostRatingPanel.razor` now marks the `Rating` `aria-hidden="true"` and
  ships a real `<fieldset>` radio group bound to the same `ValueChanged` handler. The group uses
  the `.tb-keyboard-fallback` class (`source/BlogUI/wwwroot/css/utilities.css`): visually hidden
  but **in the tab order**, and it reveals itself on `:focus-within` so a sighted keyboard user
  both reaches and sees it. Mouse users keep the stars. This satisfies 2.1.1 and 4.1.2 for the
  *application*, but the library defect itself is unchanged and TR-031 stays open.

### Not a gap — confirmed working during this audit

- `Card*`, `Alert*`, `Empty`, `Input`, `Textarea`, `Field*`, `Button`, `Badge`, `Sheet*` and
  `Avatar` produced **zero** axe violations at either viewport.
- `/login` produced zero violations at both 1280 and 390 — the `Field`/`FieldLabel`/`Input`
  composition is correctly labelled out of the box.
- `Button` and `Input` apply a focus ring (`box-shadow`) on `:focus-visible` without extra CSS;
  only plain anchors needed the application-level 2 px outline added in `base.css`.

---
TR-044/TR-045 added, TR-031 extended, 2026-08-07 by *build-phase (Cluster J — REQ-NFR-006/007 security + accessibility audit)*

---

## Gap found during the maintainability sweep (2026-08-07, Cluster I — REQ-NFR-020/021/022)

- **TR-046 — `Tabs.TabsList` declares no `CaptureUnmatchedValues`, so `data-testid` on it throws at
  render and takes five admin pages into the ErrorBoundary.** *Severity:* **High** — a hard runtime
  failure, and it is invisible in a status-code check because the ErrorBoundary still returns HTTP 200.
  *Repro:* `<TabsList data-testid="anything">` — `InvalidOperationException` on first render; the first
  such page in a fresh circuit shows "Something went wrong".
  *Expected:* attribute splatting, per the reference's own guidance to put stable test ids on controls.
  *Actual:* throws. *Encountered in:* `BlogsList`, `SeriesList`, `CommentsList`, `UsersList`,
  `SubscribersList` (5 admin pages).
  *Workaround:* move the `data-testid` onto a wrapping element.
  *Suggested fix:* add `[Parameter(CaptureUnmatchedValues = true)]` to `TabsList` — and, given this is
  now the **fourth** component family hit by the same root cause (TR-021 `Breadcrumb`/`Typography*`,
  TR-030 `Rating`/`Label`, TR-040 the broader slice, TR-046 `Tabs`), please audit the **whole catalog**
  in one pass rather than fixing them one at a time. The coding standard here requires a stable
  `data-testid` on every interactive/data-bound element, so every such component is a latent crash.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed, and the whole-catalog audit you asked for was done in one pass — see TR-048.

TR-046 added 2026-08-07 by *build-phase (Cluster I), renumbered by the orchestrator from a
duplicate TR-044: Cluster J claimed TR-044/TR-045 concurrently. **Next free ID: TR-047.**

---

## Gaps found while building the accessible captcha challenge (2026-08-07, Cluster G — REQ-UI-057)

- **TR-047 — `Label` throws at render when given `data-testid`.** *Severity:* **High** — a hard
  runtime failure on a public write surface.
  *Repro:* `<Label For="someId" data-testid="captcha-prompt">Text</Label>`.
  *Expected:* the attribute splats onto the rendered `<label>`, as it does on `Input` and `Button`
  sitting one line away in the same form.
  *Actual:* `InvalidOperationException: Object of type 'TrBlazeUI.Components.Label.Label' does not
  have a property matching the name 'data-testid'.` The component render fails; on a Blazor Server
  circuit this surfaces as a broken control while the page still returns HTTP 200, so a status-code
  smoke check sails straight past it.
  *Encountered in:* `source/BlogUI/Components/CaptchaWidget.razor` — hence on the comment form, the
  rating step and the newsletter subscribe card, i.e. every public write surface.
  *Workaround applied:* wrap the `Label` in a plain `<span data-testid="…">` and hang the test hook
  off that. Costs a redundant element on every instance.
  *Suggested fix:* `[Parameter(CaptureUnmatchedValues = true)]` on `Label` — but see the matrix
  below; fixing this one component is not the fix.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed — see TR-048.

- **TR-048 — the splatting defect is catalog-wide, and the authoritative matrix has now been
  rediscovered independently five times (TR-021, TR-030, TR-040, TR-046, TR-047).** *Severity:*
  **High** — every rediscovery costs an agent a crashed page and a debugging cycle, and the repo's
  own coding standard *requires* a stable `data-testid` on every interactive or data-bound element,
  so every component in the "rejects" column is a latent crash rather than a limitation.
  *Evidence:* cluster E reflected over `TrBlazeUI.Components 2.0.1` and produced this:
  - **REJECTS splatting (throws on `data-testid`):** `TabsList`, `TabsTrigger`, `TabsContent`,
    `Label`, `Dialog`, `DialogHeader`, `DialogFooter`, `Select<T>`, `SelectContent<T>`,
    `SelectItem<T>`, `SelectValue`, `SelectGroup`, `SelectLabel`, `AlertTitle`, `AlertDescription`,
    `AlertIcon`, `ButtonIcon`, `DataTableColumn<T,V>`, `DataTableToolbar<T>`, and the whole
    `AlertDialog`, `Breadcrumb` and `Carousel` families.
  - **ACCEPTS splatting:** `Tabs`, `Alert`, `Badge`, `Button`, the `Card` family, `Checkbox`,
    `DataTable<T>`, `DialogContent`, `DialogTitle`, `DialogDescription`, `DialogTrigger`,
    `DialogClose`, `Empty`, `Input`, `SelectTrigger<T>`, `Spinner`.
  - **The trap:** `TrBlazeUI.Primitives` ships same-named types that DO splat, so whether a given
    page compiles-and-runs or crashes depends on which namespace `_Imports.razor` happens to
    resolve — the same markup behaves differently in two projects.
  *Suggested fix:* one pass over the whole catalog adding `CaptureUnmatchedValues` uniformly, and
  publish the matrix in the AI reference so it stops being rediscovered. Until then, treat the
  "rejects" list as the reference's missing appendix.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fix option (1) taken. `[Parameter(CaptureUnmatchedValues = true)]` added and splatted across the catalog in one pass. Reflection over the built assemblies now reports **344/344** public components in `TrBlazeUI.Components` and **59/59** in `TrBlazeUI.Primitives` — every name in your "rejects" column included. The audit is committed as `tools/splat-audit` so it stays a gate. Two groups accept the attributes without rendering them, because they own no element, and this is now stated in the reference rather than left to be discovered: the context roots (`Dialog`, `Sheet`, `Popover`, `HoverCard`, `DropdownMenu`, `ContextMenu`, `Drawer`, `TooltipProvider`, `ResponsiveNavProvider`, `PortalHost`) and the config-only `DataTableColumn`. `BreadcrumbList` forwards its attributes onto the `<ol>` that `Breadcrumb` renders.

- **TR-049 — `SelectValue` renders the raw bound value instead of the matching `SelectItem`'s
  `Text`.** *Severity:* **Medium** — repo-wide cosmetic-but-user-facing defect.
  *Repro:* bind a `Select<T>` to an id/enum and give each `SelectItem` a human `Text`; the closed
  trigger shows the raw bound value.
  *Expected:* the selected item's `Text`. *Actual:* the underlying value.
  *Encountered in:* every user, role and category picker in the admin area (reported by cluster L).
  *Workaround:* render the display text yourself inside `SelectTrigger`.
  *Suggested fix:* have `SelectValue` resolve the selected `SelectItem` and render its `Text`,
  falling back to the raw value only when no item matches.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed — see TR-058.

- **TR-050 — no responsive `basis-*` variants in the compiled utility set.** *Severity:* **Medium**
  — silent, which is the worst kind.
  *Repro:* `class="sm:basis-0"` — no rule is emitted, the class is inert, and the layout quietly
  keeps the base flex-basis. `sm:grid-cols-2` in the same file works, so the breakpoint machinery
  is present and only `basis-*` is missing from the responsive slice.
  *Expected:* `basis-*` participates in the responsive variants like the other sizing utilities.
  *Actual:* base `basis-*` compiles; every `sm:`/`md:`/`lg:` prefixed form no-ops.
  *Encountered in:* reported by cluster L.
  *Workaround:* use a grid or an explicit `sm:w-*`.
  *Suggested fix:* include `basis-*` in the responsive variant generation for the pre-built CSS.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed — `basis-*` is generated with the `sm:`/`md:`/`lg:`/`xl:`/`2xl:` variants.

- **TR-051 — the AI reference and the `trblazeui` persona both state the exact opposite of TR-048,
  which is *why* the same crash keeps being rediscovered.** *Severity:* **High** — this is the
  root cause of the repeat cost, not a cosmetic docs nit. Every agent onboards by reading the
  reference, is told the splatting is universal, writes the natural markup, and ships a page that
  returns HTTP 200 and renders nothing.
  *Repro:* `.trblazeui/TrBlazeUI-AI-Reference.md` (and the `trblazeui` agent persona's
  `core_principles`) assert: *"All components support CaptureUnmatchedValues — arbitrary HTML
  attributes (id, style, data-*, aria-*) and event handlers (@onkeydown, @onfocus, etc.) can be
  passed directly to any component."*
  *Expected:* the reference states which components splat and which throw.
  *Actual:* it guarantees the behaviour universally; the guarantee is false for **132 of the 334**
  component types in `TrBlazeUI.Components 2.0.1`.
  *Encountered in:* cluster B (REQ-UI-052) — measured, not inferred, by loading
  `TrBlazeUI.Components.dll` and `TrBlazeUI.Primitives.dll` in a `MetadataLoadContext` and
  listing every `IComponent` whose inheritance chain declares a
  `[Parameter(CaptureUnmatchedValues = true)]` property. This supersedes the hand-built list in
  TR-048: it is complete, and it confirms TR-048's "trap" — every one of the 14 ambiguous names
  (`Tabs*`, `Select*`, `Label`, `RadioGroup*`, `Tooltip*`, `DropdownMenu*`) splats in
  `TrBlazeUI.Primitives` and throws in `TrBlazeUI.Components`.
  *Workaround:* the only safe rule today is "put the test hook on a plain `<span>`/`<div>` you
  own, never on a TrBlazeUI component you have not personally verified".
  *Suggested fix:* ship the reflected matrix as an appendix to the AI reference and delete the
  universal claim from both the reference and the persona — until the catalog-wide fix in TR-048
  lands, the documentation is actively causing the defect.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed — the claim is now TRUE and measured, not asserted. The reference carries the numbers, the verification command, and the two exception groups; both agent personas (`docs/skills/claude-code-trblazeui.md`, `docs/skills/opencode-trblazeui.md`) carry the same correction. Your machine-generated 132/334 matrix was the decisive evidence — thank you.

TR-047 found and TR-048/TR-049/TR-050 recorded 2026-08-07 by *build-phase (Cluster G —
REQ-UI-057)*, consolidating cluster E's reflection matrix and cluster L's two findings at the
orchestrator's request.

TR-051 added 2026-08-08 by *build-phase (Cluster B — REQ-UI-052)*. Deliberately **not** a seventh
duplicate of the splatting bug: TR-048 already carries the catalog-wide audit request, so this
entry records the documentation defect that keeps causing the rediscoveries, and contributes the
complete machine-generated matrix (132/334 reject splatting) that TR-048 asked for.

- **TR-052 — `Rating` puts `tabindex="0"` on its radiogroup and offers no way to take it off, so a
  Rating that has to be marked `aria-hidden` becomes a SILENT tab stop.** *Severity:* **High** —
  it is an accessibility defect that the library forces on any application that works around
  TR-031. *Repro:* render `<Rating …/>` inside `<div aria-hidden="true">` — which TR-031 leaves as
  the only sane option, because the stars are not keyboard operable and the control announces a
  broken radio group. The library still emits
  `<div role="radiogroup" aria-label="…" tabindex="0">` inside that subtree.
  *Expected:* a `Rating` that is presented as decoration can be taken out of the tab order —
  e.g. a `Focusable`/`TabIndex` parameter, or `ReadOnly="true"` implying `tabindex="-1"`.
  *Actual:* nothing in the public API changes it. A keyboard user lands on a control the
  accessibility tree says is not there: focus is silent, the element has no accessible name, and
  axe reports `aria-hidden-focus` (**serious**). Confirmed on `TrBlazeUI.Components 2.0.1`.
  *Encountered in:* `PostRatingPanel.razor` on `/post/{slug}` — it was the LAST remaining axe
  violation on the whole site (2 nodes, one per viewport) after the 2026-08-07 audit, and the
  2026-08-07 pass recorded it as unfixable from application code.
  *Workaround (now shipped, REQ-NFR-007):* it turns out to be fixable from outside after all, but
  only with JavaScript. A wrapper opts in with `data-a11y-decorative`, and a `MutationObserver`
  installed in `source/TechieBlog/Components/App.razor` re-applies `tabindex="-1"` to every
  focusable descendant after each Blazor render (an attribute set once at first render is undone
  by the next one). Mouse operability is untouched — a click does not need a tab stop. Measured
  after the change: `groupTabindex="-1"`, `focusablesInsideAriaHidden: []`, and **0 tab stops
  inside an `aria-hidden` subtree across all four audited public pages**.
  *Suggested fix:* honour `ReadOnly` by dropping the tab stop, or expose `TabIndex`. Related:
  TR-031's second half is **re-confirmed** on 2.0.1 — one `Rating` still emits **5 `<linearGradient>`
  elements sharing a single `id`** (4 duplicates), so any second Rating on the page references the
  wrong gradient.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed both ways you suggested — `ReadOnly` implies no tab stop at all, and a new `Focusable` parameter takes a decorative rating out of the tab order without JavaScript. The `MutationObserver` in `App.razor` can go. Measured live: 0 focusable elements inside `ReadOnly` and inside `Focusable="false"`.

- **TR-053 — bound `Input` / `Textarea` LOSE AND REORDER CHARACTERS when text arrives faster than
  the Blazor Server circuit can echo it.** *Severity:* **High** — silent data corruption in the
  field the user is looking at, and an accessibility defect in its own right.
  *Repro:* on a Blazor Server circuit, type into `<Input @bind-Value="x" />` at ~12–16 characters
  per second. Measured on this build, at 60 ms/char:
  `cg-subscribe-cg0808c@techieblog.test` arrived as `c-usrb-g08eegt`;
  `cg-rating-…@techieblog.test` arrived as `cg-chieblog.test`;
  `Cluster G Auditor` arrived as `Cut Aur`. `Textarea` is worse — it still corrupted the value at
  **350 ms/char** and only survived when each keystroke was preceded by `End`, which is the
  signature of the server echo resetting the caret to position 0 between round trips.
  *Expected:* the field holds exactly what was typed, regardless of typing speed.
  *Actual:* characters are dropped and reordered, with no error anywhere.
  *Encountered in:* `CommentForm.razor` (name, email, comment), `PostRatingPanel.razor` (email),
  `NewsletterSubscribeCard.razor` (email) — i.e. every public write surface.
  *Why this belongs in an accessibility report:* the users most affected are the ones whose
  assistive technology INJECTS text rather than pressing keys — voice input, switch/AAC devices,
  screen-reader "type text" commands, braille displays, and anyone using a password manager or
  paste-and-tab workflow. A fast touch typist hits it too.
  *Workaround:* in tests, `typeAndVerify` in `tests/verify/cluster-g-keyboard.spec.ts` types,
  reads the value back, and retypes more slowly (finally `End`-prefixed per character) until it
  matches. **There is no workaround for a real user**, which is why this is filed as High.
  *Suggested fix:* do not re-render the input's `value` from the model on every `oninput` round
  trip — either debounce the binding, or preserve the caret/uncommitted text the way
  `Microsoft.AspNetCore.Components.Forms.InputText` does.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed — see TR-057. Your exact repro string now survives a 30 ms/char type in the harness, in both the DOM and the bound value.

- **TR-054 — `Tabs` emits `role="tab"` triggers whose `aria-controls` points at an element that is
  not in the document, and (intermittently) with no `role="tablist"` parent.** *Severity:*
  **High** — two separate axe **critical** rules, on every tabbed admin screen.
  *Repro:* render `<Tabs><TabsList><TabsTrigger Value="a">…` with more than one tab. Each trigger
  gets `aria-controls="tabs-N-content-a"`, but only the ACTIVE `TabsContent` is rendered, so every
  inactive trigger references a missing id.
  *Expected:* `aria-controls` names an element that exists (render inactive panels hidden, or drop
  the attribute for panels that are not present), and every `role="tab"` has a `role="tablist"`
  ancestor.
  *Actual:* measured 2026-08-08 on `TrBlazeUI.Components 2.0.1`:
  - `aria-valid-attr-value` (critical) — `/admin/images` **7 nodes** (6 triggers), `/comments`
    **4 nodes**, `/admin/newsletter` **2 nodes**, at both 1280 and 390.
  - `aria-required-parent` (critical) — `/admin/newsletter` **2 nodes**
    (`#tabs-…-trigger-write`, `#tabs-…-trigger-preview`), at both viewports.
  *Encountered in:* `ImagesPage`, `CommentsList`, the newsletter composer — i.e. everywhere the
  admin uses tabs. A screen reader announces "tab, 1 of 6" and then finds nothing to move into.
  *Workaround:* none from application code — the ids and the roles are emitted by the component.
  *Suggested fix:* keep inactive `TabsContent` in the DOM with `hidden`, which fixes
  `aria-controls` and is what the ARIA authoring practices assume; and make `TabsList` always
  render the `role="tablist"` element even when its children re-render.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — both halves fixed. `aria-controls` is emitted only when a `TabsContent` panel for that value exists — so the `Value`/`ValueChanged` screens (`ManageImages`, `CommentsList`, `BlogsList`) emit none at all — and the panel element now stays in the DOM, hidden, so the reference resolves. The missing-`tablist` half was `MarkdownEditor`; see TR-064.

- **TR-055 — the `ItemGroup` / `Item` pair emits `role="list"` with children that have no
  `role="listitem"`.** *Severity:* **Medium-High** — axe `aria-required-children`, **critical**.
  *Repro:* the recent-activity list on `/admin` renders
  `<div role="list" class="flex flex-col gap-0.5">` whose children are
  `<div data-slot="item" …>` with no role.
  *Expected:* an element with `role="list"` contains only `role="listitem"` children (or the
  wrapper does not claim `role="list"` at all).
  *Actual:* the children carry `data-slot="item"` and nothing else, so the list is announced as
  empty. **1 node** on `/admin` at both viewports.
  *Encountered in:* `/admin` dashboard, 2026-08-08.
  *Workaround:* none that is honest — a role could be injected from JavaScript, but inventing ARIA
  semantics from outside a component is a worse defect than the one it hides, so this was
  deliberately left reported rather than patched.
  *Suggested fix:* emit `role="listitem"` on `Item` when it is inside an `ItemGroup`, or drop
  `role="list"` from the group.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed — see TR-061.

TR-052 and TR-053 recorded 2026-08-08 by *build-phase (Cluster G — REQ-NFR-007)* during the WCAG
re-audit. TR-052 supersedes the "unfixable from application code" judgement on the REQ-NFR-007 row:
it IS fixable, with JavaScript, and the fix is shipped. TR-054 and TR-055 were found in the same
pass, on the admin routes — which no previous audit had actually reached, because a full page load
of an authorised route drops the rehydrating auth state and redirects to `/`, so earlier "admin"
runs were silently auditing the home page. **Next free ID: TR-056.**

---

## Gap found re-establishing dark mode on TrBlazeUI (2026-08-08, Cluster C — REQ-UI-033)

- **TR-056 — the shipped token set is tuned against `--background` only, so several defaults fail
  WCAG on the RAISED surfaces the same stylesheet defines (`--muted`, `--secondary`, `--accent`).**

  *Severity:* **High** — silent, palette-wide, and inherited by every consumer that trusts the
  shipped theme. It is not one bad colour; it is a missing axis in how the palette was validated.
  *Repro:* load `trblazeui.css` with its own defaults and compute sRGB contrast (the tokens are
  OKLCH, so the values must be read from `getComputedStyle`, not the CSS source) for each
  foreground token against each surface token, rather than against `--background` alone.
  *Expected:* a token intended for text clears 4.5:1 on every surface the same theme ships, and
  `--input` — which draws the visible boundary of every form control — clears 3:1 (WCAG 1.4.11).
  *Actual:* measured 2026-08-08 on `TrBlazeUI.Components 2.0.1`:
  - **`--input` fails 1.4.11 outright, in both modes, before any surface nesting:** the shadcn
    default `oklch(0.922 0 0)` gives **1.26:1** on the light page and the dark default gives
    **~1.6:1**. Every text box, select and textarea ships with an effectively invisible boundary.
    (TechieBlog already had to raise it to `0.66` / `oklch(1 0 0 / 36%)`, now measured at
    **3.27:1** — see REQ-NFR-007; recorded here because the *library* default is the defect and
    every other consumer will re-hit it.)
  - **On raised surfaces the shipped foregrounds slip under 4.5:1** even after that fix. Against
    `--muted` / `--secondary` / `--accent` rather than `--background`: `--destructive` **4.38:1**,
    and in the app's derived themes `--primary` **4.23:1** on `--muted` and **3.99:1** on
    `--accent`. This is not academic — TechieBlog hit it as a *rendered* failure: the post link on
    `/CommentsList` measured **4.44:1** against its own row.
  - The same axis is why a dark-mode-only defect survived: `--destructive` at `oklch(0.70 …)`
    measured **4.30:1** on `--accent` while measuring a comfortable 6.37:1 on `--background`.
  *Encountered in:* `source/BlogUI/wwwroot/css/theme.css` — all three site themes, light and dark.
  *Workaround applied:* the app overrides the tokens (easy, because the library declares its own
  through zero-specificity `:where(:root)` / `:where(.dark)` selectors, so an app `theme.css`
  always wins — that part of the design is genuinely good). Raised `--input`, and re-solved
  `--primary`/`--alert-info`/`--destructive`/`--alert-danger` against the raised surfaces.
  *Suggested fix:* validate the shipped palette as a MATRIX — every text token against every
  surface token, not just `--background` — and publish the resulting table in the AI reference so
  consumers know which pairings the library actually guarantees. `--input` in particular should
  ship at 3:1; it is a WCAG 1.4.11 obligation, not a stylistic choice.
  *Deliberately NOT raised:* `--border` remains at 1.26–1.33:1. It styles dividers and card edges,
  which are not "visual information required to identify UI components" under 1.4.11, so it is out
  of scope by the standard's own wording. Flagging it would be a false positive; `--input` is the
  token that carries the obligation.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed exactly as suggested — the palette is now validated as a MATRIX by `tools/token-contrast.py`, which is committed so it stays a gate. It reproduced your measurements to two decimals (`--input` 1.26:1 light, `--destructive` 4.37:1 on `--muted`, `--muted-foreground` 4.34:1) and found **18** failing pairings in total; all 18 now pass. `--input` ships at **3.11:1** light / **4.6:1** dark. Your "deliberately NOT raised" call on `--border` was followed — it is out of 1.4.11's scope and was left alone.

TR-056 recorded 2026-08-08 by *build-phase (Cluster C — REQ-UI-033 dark-mode corrections).

- **TR-057 — `Textarea` (and `Input`) are CONTROLLED inputs with no uncontrolled/debounced mode, so
  under Blazor Server they LOSE AND REORDER KEYSTROKES on a slow circuit.**

  *Severity:* **High** — silent data loss in the primary authoring surface of any Blazor **Server**
  consumer, and it only appears under load, which is exactly when nobody is testing.
  *Repro:* `<Textarea @bind-Value="text" />` on an InteractiveServer page; emulate 400ms of network
  latency (CDP `Network.emulateNetworkConditions`) and type a 15-character string at 0–15ms per
  key.
  *Expected:* the textarea holds exactly what was typed.
  *Actual:* measured 2026-08-09 on `TrBlazeUI.Components 2.0.1`, typing `## Live heading`:
  `#ve he`, `## ng`, `## Living`, `## Lie edg` — **4 of 9 runs wrong**. TechieBlog's verifier had
  already seen the same signature at 120ms and 1000ms per key under 7-way agent concurrency
  (`## Li`, `#ve`, `## ivehading`, `#L ag` — 3 of 4 runs).
  *Cause:* `Textarea.razor` renders `<textarea value="@Value" @oninput="HandleInput">` and
  `HandleInput` assigns `Value` before raising `ValueChanged`. Every keystroke therefore
  round-trips, and the render that comes back writes a `value` into the DOM that is already one or
  more keystrokes behind what the user has typed since. `Input` has the identical shape and the
  identical latent defect.
  *Encountered in:* `source/BlogUI/Components/PostMarkdownEditor.razor` (REQ-UI-016).
  *Workaround applied:* replaced `<Textarea>` with a RAW `<textarea>` carrying the library's own
  resolved classes, seeded once via child content and re-keyed only on programmatic writes, so the
  server never rewrites the DOM value. This deliberately breaks the AI-reference rule "NEVER use
  raw `<textarea>` elements" (§6) — there is currently no supported way to obey that rule and keep
  the user's keystrokes. Proven by counterfactual: the pre-fix build fails the stress above 4/9,
  the fixed build passes 9/9 (`tests/verify/cluster-c-authoring.spec.ts`).
  *Suggested fix:* one of — (a) an `Uncontrolled="true"` / `SeedOnly` mode that renders the value
  once and never diffs it again; (b) a `DebounceMilliseconds` parameter that also suppresses the
  value write-back while the control has focus; or (c) at minimum, stop assigning `Value` inside
  `HandleInput` and skip the `value` attribute in the render tree while the element is focused.
  Whichever is chosen, the AI reference should carry an explicit Blazor **Server** warning next to
  every text-entry component.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed with a variant of option (c), plus (b) as an option. `TextValueSync` tracks the DOM value, the render-tree value and the value the parent last supplied as three separate things, so the echo of the user's own typing never re-enters the render tree and Blazor's diff produces no DOM write; a genuine programmatic change still writes, and re-keys the element in the one case where the new value equals what the tree already holds (clearing a field back to empty). `DebounceMilliseconds` is available for traffic, not correctness. You can put `<Textarea>` back in `PostMarkdownEditor.razor`.

- **TR-058 — `SelectValue` shows the RAW bound value instead of the item's `Text` until the
  dropdown has been opened once.**

  *Severity:* Medium — cosmetic but pervasive and it looks like a data defect.
  *Repro:* a `Select` whose items live inside `SelectContent` (a popover), bound to `"0"` with
  `<SelectItem Value="0" Text="-- Select Category --">`. Render the page and do not open it.
  *Expected:* the trigger reads `-- Select Category --`.
  *Actual:* the trigger reads `0`. `SelectValue.GetSelectedText()` falls back to
  `SelectContext.Value?.ToString()` when no display text has been registered, and items only
  register when `SelectContent` first renders — which is when the popover opens.
  *Encountered in:* `source/BlogUI/Pages/AdminPages/ManagePost.razor` — the Category and Series
  pickers both show a bare number on first paint (screenshot
  `test-results-cluster-c/ui016-managepost-1280.png`). Pre-existing; not introduced by REQ-UI-016.
  *Workaround applied:* none — left as-is rather than distorting the binding, since the fix
  belongs in the library.
  *Suggested fix:* let `SelectItem` register its `Value`/`Text` pair with the context at
  `OnInitialized` regardless of whether the popover content is currently rendered, or expose an
  `ItemsSource`-style registration the trigger can read before first open.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed exactly as suggested — `SelectItem` now registers its `Value`/`Text` pair at `OnInitialized` whether or not the popover is rendered, and registration is keyed on the value, which also fixes a latent bug where reopening the listbox duplicated every keyboard-navigation entry. Measured live: the trigger reads `-- Select Category --` on first paint where the bound value is `0`.
  >
  > **CONSUMER-SIDE ADOPTION CONFIRMED — 2026-08-11 (Cluster E), on the published `2.0.2` package**
  > (the label "2.1.0" above is the pre-release name of what shipped as 2.0.2). TechieBlog's whole
  > workaround is **deleted**: `source/BlogUI/Common/SelectFirstPaintLabel.cs`, all 18
  > `DisplayTextSelector="@…"` attributes, every cached `…LabelSelector` member, the three
  > page-private label resolvers they fed, and the source-scan test that required the attribute.
  > Proved by bUnit against the real component before removal (`"1"` on 2.0.1, `"Ravi Rathore
  > (Ravi@techieblog.com)"` on 2.0.2) and then re-proved at runtime: **all 16 `Select` triggers in
  > `source/` render a human-readable label on the FIRST paint, with no application help.**
  > Sentinels resolve too, because each is a declared `SelectItem` — `"0"` → *All Users*
  > (`/admin/images`), `""` → *-- My Experience --* (`/admin/experience`), `"0"` → *-- Select
  > Category --* and *-- Not part of a series --* (`/ManagePost`), `""` → *All Categories* /
  > *Any Date* (`/search`). The one behaviour a consumer still has to respect is the fallback you
  > documented under TR-068: a value with **no** matching item renders `Value.ToString()`, not the
  > placeholder — pinned here by
  > `tests/TechieBlog.Tests/Components/BlogUi/SelectPreselectedValueRenderTests.cs`, which is now
  > the solution's regression alarm for this defect class.

- **TR-059 — no "prose" / rendered-HTML container, so arbitrary Markdown output has no
  responsive story.**

  *Severity:* Low–Medium — every content-driven site hits it, and the failure is a page-level
  horizontal scroll, which is a WCAG 1.4.10 (Reflow) problem rather than a cosmetic one.
  *Repro:* render Markdig output into a plain `<div>` inside a TrBlazeUI page and view it at
  390px. A three-column pipe table measures ~420px at its minimum content width and pushes the
  whole document sideways; `<pre>` blocks behave the same way unless the app has already styled
  them. TrBlazeUI ships `Card`, `Separator`, `TypographyH2` and friends for markup the developer
  writes, but nothing for markup the developer *receives* — there is no `Prose`/`RichText`
  component and no documented utility class that constrains an opaque HTML blob to its container.
  *Expected:* something like `<Prose>@((MarkupString)html)</Prose>` that gives tables and pre
  blocks their own overflow context and caps image widths, the way a typography plugin does.
  *Actual:* each app re-derives it. `DataTable` cannot help here because the content is HTML, not
  a bound collection.
  *Encountered in:* `source/BlogUI/Pages/BlogPages/PostView.razor` (REQ-UI-007) — the post body is
  one `MarkupString`, so the fix had to be a string transform that wraps every `<table>` in a
  scroll container before the markup reaches the DOM. A CSS rule alone cannot do it: the extra
  scrolling box has to exist as an element.
  *Workaround applied:* `WrapTablesInScrollContainer` in `PostView.razor`; page-level horizontal
  scroll at 390px went from 46px to 0px.
  *Suggested fix:* ship a `Prose` component (or a documented `trb-prose` class) covering
  `table`/`pre`/`img`/`iframe` overflow, and mention it in the AI reference next to the Markdown
  guidance.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed as suggested — `<Prose>` added, with `[data-slot="prose"]` rules in `trblazeui.css` giving `table`/`pre`/`img`/`iframe` their own overflow context. Measured live at 390 px: the table scrolls inside itself and page horizontal overflow is 0 px. `WrapTablesInScrollContainer` can go.

TR-057 / TR-058 recorded 2026-08-09 by *build-phase (Cluster C fix pass — REQ-UI-016 / REQ-UI-017).
TR-059 recorded 2026-08-09 by *build-phase (Cluster B fix pass — REQ-UI-007).

- **TR-060 — a `Dialog` opened from INSIDE another `Dialog`'s content is not guaranteed to stack
  above its parent, so the child renders but cannot be clicked.**

  *Severity:* High — the nested dialog looks correct in a screenshot and is completely dead to the
  user, which is the worst possible failure shape.
  *Repro:* put a control that owns its own `Dialog` inside another `DialogContent` — here the
  `ImagePicker` (gallery + upload dialogs) inside the add/edit dialog on `/admin/experience`. Open
  the outer dialog, then open the inner one, then click an item in the inner one.
  *Expected:* the inner dialog and its overlay sit above the outer dialog.
  *Actual:* both dialogs' overlay AND panel are hard-coded `z-50`, and the portal host does not
  guarantee that a later-opened portal lands later in document order. Measured on this page: the
  gallery's portal was inserted at **DOM index 0** and the parent experience dialog's portal sat at
  **index 1**, so with equal z-index the PARENT painted over the child. `document.elementFromPoint`
  at the centre of a gallery tile returned the parent picker's 144px preview frame, and Playwright
  reported `<div class="flex h-36 w-36 …"> from <div class="trblazeui-portal" data-portal-id=
  "dialog-25-portal"> subtree intercepts pointer events`. It is order-dependent, not constant —
  an earlier run of the same page produced the opposite order and worked, which is why this reads
  as a flaky product bug rather than a layout bug.
  *Encountered in:* `source/BlogUI/Pages/AdminPages/ManageExperience.razor` and
  `ManageAwards.razor` (REQ-UI-037 / REQ-UI-039 — the acceptance requires a picker inside the
  add/edit dialog, so nesting is not avoidable).
  *Workaround applied:* a scoped rule in `source/BlogUI/wwwroot/css/utilities.css` raises both
  children of the ImagePicker's own portals to `z-index: 120`:
  `.trblazeui-portal:has([data-testid="image-gallery-dialog"]) > *`. Verified by re-measuring the
  hit test — it now resolves to the gallery tile — and by a full create/edit/delete round trip.
  *Suggested fix:* give each opened dialog a z-index derived from a monotonically increasing open
  counter (a stack), or append every newly opened portal to the END of the portal host. Either
  makes "last opened wins" true by construction instead of by luck.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed with your second suggestion — `PortalService` now preserves registration order (it was a `ConcurrentDictionary`, whose enumeration order is arbitrary — that is the whole root cause) and `PortalHost` renders through the new `GetOrderedPortals()`. The most recently opened portal is last in the DOM, so "last opened wins" is true by construction. Your scoped `z-index: 120` rule can go. ⚠ **But note TR-066 at the end of this file:** a `Dialog` declared literally inside another `Dialog`'s `DialogContent` does not open at all — a separate, pre-existing defect. Compose stacked dialogs as siblings, each with its own `@bind-Open`; that is the pattern the `/components/dialog` demo now shows and the one the ordering fix is verified against.

- **TR-019 — two more instances of the pre-compiled-bundle gap** (not new IDs; recording the
  specimens because they cost a run each). `trblazeui.css` ships `.top-0`, `.top-1\.5`, `.top-1\/2`,
  `.top-2`, `.top-3\.5`, `.top-4` but **no `.top-1`** — `ImagePicker`'s clear-selection button used
  `absolute right-1 top-1`, so it kept a STATIC vertical position, fell below the `h-full` image,
  out of the `overflow-hidden` frame, and landed on the action row at 390px (this was the recorded
  REQ-UI-040 visual defect). And `w-36` was missing from BOTH the bundle and TechieBlog's own
  gap-fill file even though `h-36` was present, so every `h-36 w-36` preview square rendered 144px
  tall and FULL WIDTH. Both fixed locally. The general point stands: a missing utility is silent,
  and the failure it produces looks like an application bug every time.

TR-060 recorded 2026-08-09 by *build-phase (Cluster D fix pass — REQ-UI-037 / REQ-UI-039 / REQ-UI-040).

- **TR-061 — `ItemGroup` declares `role="list"` but `Item` never emits `role="listitem"`, so every
  list built from the pair is announced as EMPTY.** (Supersedes the informal note filed as TR-055 in
  the REQ-NFR-007 checklist row; this is the reproducible write-up.)
  *Severity:* High — the failure is invisible in a screenshot and total for a screen-reader user.
  *Repro:* render any `<ItemGroup>` with `<Item>` children and read the DOM.
  *Expected:* `role="list"` on the group and `role="listitem"` (or a real `<li>`) on each item —
  ARIA requires `list` to own `listitem` children and treats any other child as breaking the
  relationship.
  *Actual (measured 2026-08-09 on `/admin`, `tests/verify/cluster-l-probe.spec.ts`):*
  `<div role="list" class="flex flex-col gap-0.5">` whose five children are
  `<div data-slot="item" class="group relative flex items-center gap-3 rounded-lg px-4 py-3">` —
  **no role at all**. axe-core reports `aria-required-children` (**critical**), and a screen reader
  announces "list, 0 items" on the admin landing page's Recent Activity feed. There is no parameter
  on `Item` to supply the role, and no documented alternative.
  *Encountered in:* `source/BlogUI/Pages/AdminPages/AdminDashboard.razor` (REQ-NFR-007).
  *Workaround applied:* the block was rebuilt on a real `<ul>`/`<li>` carrying the exact class
  strings captured from the library's own rendered output, so the visuals are unchanged and the
  semantics come from HTML rather than from an ARIA claim. Same remedy as TR-044 (`NavigationMenu`
  → `<nav><ul><li>`). Re-measured: `/admin` axe nodes 1 → 0, light and dark.
  *Suggested fix:* emit `role="listitem"` from `Item` whenever its ancestor `ItemGroup` emits
  `role="list"` — or drop the `role="list"` and let the consumer choose the semantics. Either is
  correct; the current pair is the one combination that is actively wrong.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed with your first suggestion — `Item` emits `role="listitem"` when its ancestor `ItemGroup` declares `role="list"` (an explicit `role` you pass still wins), and `ItemSeparator` takes `role="none"` inside a group so it does not break the ownership either. Measured live: `role="list"` with `[listitem, listitem]` children.

- **TR-062 — `Input` has no way to supply an accessible name, so a control with no placeholder and
  no visible label ships nameless.**

  *Severity:* Medium — one control, but it is a WCAG 4.1.2 **critical** axe node and it hid for
  three audits behind axe's `non-empty-placeholder` check.
  *Repro:* `<Input Value="@x" readonly />` with no `Placeholder`. Run axe.
  *Expected:* an `AriaLabel` parameter, documented, the way `Button`, `Toggle`, `Toolbar`,
  `ToolbarButton` and `Spinner` all have one (AI reference §Button, §Toggle, §Toolbar, §Spinner).
  *Actual:* the `Input` parameter table lists `AriaInvalid` and `AriaDescribedBy` but **no
  `AriaLabel`** — the two ARIA attributes an author is least likely to need, and not the one they
  always need. Existing app code (`BlogSidebar.razor`) passes `AriaLabel` to `Input` anyway and it
  is accepted at runtime, which makes the omission from the reference doubly misleading: authors
  cannot tell whether it is a supported parameter or an unmatched attribute that happens not to
  throw. Every other `Input` in this application passes axe only because it carries a
  `Placeholder` — which is a *hint*, not a name, and disappears the moment the field has content.
  *Encountered in:* `source/BlogUI/Pages/BlogPages/RssFeed.razor` (REQ-NFR-007) — the readonly feed
  URL box, the one field in the app that cannot use a placeholder because it is never empty.
  *Workaround applied:* `Id` on the `Input` plus a `<Label For=… Class="sr-only">`. Verified in the
  served HTML: `<label for="rss-url-input" … sr-only>RSS feed URL</label>` +
  `<input id="rss-url-input" … readonly>`. axe `label` node on `/rss`: 1 → 0, light and dark.
  *Suggested fix:* add `AriaLabel` to `Input`/`Textarea` and document it; and state explicitly in
  the reference that `Placeholder` is not an accessible name.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — `AriaLabel` did exist on `Input` and `Textarea` but was missing from the parameter table — which, as you say, made it impossible to tell a supported parameter from an unmatched attribute that happens not to throw. Both tables now list it, and the reference states plainly that `Placeholder` is a hint, not an accessible name.

TR-061 / TR-062 recorded 2026-08-09 by *build-phase (Cluster L fix pass — REQ-NFR-007 / REQ-NFR-010).

---

## Gaps found closing the residual `Tabs` violations (2026-08-09, Cluster M — REQ-NFR-007)

Three findings from taking the admin area's last 48 axe nodes to 0. All three are in `Tabs`
(`TrBlazeUI.Components 2.0.1`), all three were measured on a running build, and two of them
**correct earlier guesses recorded against TR-054** — which should be read together with these.

- **TR-063 — the active tab is marked `aria-selected=""` (EMPTY) and the inactive ones carry no
  `aria-selected` at all, so NO tab is announced as selected.**

  *Severity:* **High** — WCAG 4.1.2 Name/Role/**Value**. It affects every tab set in every
  consuming application and it is invisible to automated tooling, which is why three previous
  audits of this repository missed it.
  *Repro:* render any `Tabs` and read `document.querySelector('[role="tab"]').getAttribute('aria-selected')`.
  *Expected:* `aria-selected="true"` on the active trigger and `aria-selected="false"` on the rest —
  the state is already computed; only its serialisation is wrong.
  *Actual (measured 2026-08-09 on `/settings`, `tests/verify/cluster-m-probe.spec.ts`):* the DOM is
  `aria-selected=""` on the active trigger and the attribute is **absent** on the other five. ARIA
  resolves an empty token value to the attribute's default, and the default for `aria-selected` is
  *undefined*, so nothing is exposed. Confirmed against **Chrome's own accessibility tree** read over
  CDP (`Accessibility.getFullAXTree`), not merely inferred from the markup: **6 tab nodes, 0 with a
  `selected` property**; the same probe on `/admin/images` gave **7 tabs, 0 selected**. A screen
  reader announces "General, tab" with no indication that it is the current one.
  *Why axe never reported it:* the `aria-valid-attr-value` rule skips empty attribute values by
  design, so `aria-selected=""` passes every audit while conveying nothing. **An axe-clean tab set
  is not evidence that this works.**
  *Workaround applied:* an observer in `source/TechieBlog/Components/App.razor` transcribes the
  library's own `data-state="active|inactive"`, on the same element, into
  `aria-selected="true|false"`. Nothing is guessed — the mapping is 1:1 with a value the component
  already renders. Re-measured over CDP after the change: **13 tabs across the two routes, 13 with
  the state exposed, exactly 1 selected per route** (0 before).
  *Suggested fix:* write `aria-selected` as the string `"true"`/`"false"` on every trigger. A Blazor
  `bool` interpolated into an attribute renders as `True`/`False`, and a `bool?`/conditional that
  yields `null` renders as an empty attribute — one of those two is almost certainly the cause.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed, and your diagnosis of the cause was exactly right — a Blazor `bool` interpolated into an attribute renders empty. `aria-selected` is now written as the literal string. The same class of bug was swept across the catalog: `Calendar`, `CollapsibleContent`/`CollapsibleTrigger`, `MenubarTrigger`, `RangeSlider`, `SidebarMenuButton`, `Slider`, `Toggle`, `ToggleGroupItem`. Measured live: `aria-selected=[true,false]`, exactly one selected.

- **TR-064 — `MarkdownEditor` renders its own Write/Preview `TabsTrigger` pair inside a plain
  `<div>`, with no `role="tablist"`, and no roving arrow-key navigation.** (Supersedes the
  "intermittently … no `role="tablist"` parent" half of **TR-054**, which guessed at a *nested*
  `Tabs` in the consuming page. It is not the consumer's nesting — it is inside the library
  component, and it is not intermittent; it is constant.)
  *Severity:* **High** — axe `aria-required-parent`, **critical**, on every screen that hosts a
  `MarkdownEditor`.
  *Repro:* place a `<MarkdownEditor>` anywhere and read the DOM around its view-mode toggle.
  *Actual (measured 2026-08-09 on `/admin/newsletter`, `cluster-m-probe.spec.ts`):* the two buttons
  are `#tabs-N-trigger-write` and `#tabs-N-trigger-preview` — the `Tabs` id scheme, so they ARE
  `TabsTrigger`s — and their parent is
  `<div class="inline-flex items-center rounded-md border border-input bg-muted/60 p-0.5">` with **no
  role**, whose only two children are those buttons. The composer's own outer `Tabs` on the same page
  renders its `role="tablist"` correctly, which is what made this look like a nesting problem.
  *Also measured, and the reason a DOM workaround had to be chosen carefully:* **arrow keys do not
  work on this pair.** Focusing `#tabs-N-trigger-write` and pressing `ArrowRight`, then `End`, leaves
  focus on the same button in all three reads. So the pair is not merely missing a `tablist`
  element — it is missing the tablist *behaviour* as well.
  *Workaround applied:* the App.razor observer **removes `role="tab"`** (and the `aria-selected` that
  only the tab role permits) from any `role="tab"` with no `role="tablist"` ancestor, parking the
  value so it is restored the moment a tablist appears. Injecting `role="tablist"` onto the wrapper
  was considered and **rejected**: it would promise assistive technology a roving-focus contract the
  measurement above shows the widget does not honour. What is left is two correctly-named, fully
  operable `<button>`s — everything that actually reaches a user today, minus a broken promise.
  *Suggested fix:* render that toolbar with `TabsList` so it emits `role="tablist"` and inherits the
  keyboard handling the outer `Tabs` already has.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed exactly as suggested — the toolbar is rendered through `TabsList`, so it emits `role="tablist"` and inherits the arrow/Home/End handling the outer `Tabs` already had. The editor's boxed-tab styling is unchanged. Your App.razor role-stripping observer can go.

- **TR-054 addendum — the `aria-controls` half is worse than recorded, because three of the five
  affected screens render NO `TabsContent` at all.** `ManageImages`, `CommentsList` and `BlogsList`
  drive `Tabs` through `Value`/`ValueChanged` and paint the filtered result set themselves, which is
  a documented and reasonable use of the component. On those screens **every** trigger's
  `aria-controls` dangles, not just the inactive ones — measured 7/7, 4/4 and 4/4. Any fix that only
  keeps inactive panels mounted would still leave these three broken; the attribute needs to be
  omitted when the `Tabs` has no `TabsContent` children.
  *Workaround applied:* the App.razor observer removes an `aria-controls` whose target id is not in
  the document, parking it for restoration if the target ever appears. Node counts over the five
  affected admin routes, light **and** dark, same spec before and after
  (`tests/verify/cluster-m-tr054-tabs.spec.ts`): **48 -> 0**.

- **TR-065 — `DataTable` does not repaint a `CellTemplate` when a bound item's property is mutated
  in place.** The row keeps rendering the value it was first painted with, so a grid that edits its
  own view models shows a stale cell until the whole data set is replaced.
  *Severity:* **Medium** — the write succeeds and the status banner is correct, so nothing is lost;
  the grid simply contradicts the action the administrator just took, which reads as a failed
  operation.
  *Repro:* `/CommentsList` as Admin. Approve a comment from its row action. `CommentsList.razor`
  sets `comment.Status = "Approved"` on the `CommentViewModel` the `DataTable` is bound to.
  *Actual (measured 2026-08-10 on `/CommentsList`, `cluster-e-async-tail.spec.ts`):* the success
  banner "Comment approved." appears, PostgreSQL shows `moderationstatus = 'Approved'` and
  `published = true` — and the row's Status badge still reads **Pending**. Navigating away and back
  renders **Approved** correctly, so the data and the template are both right; only the in-place
  update is missed.
  *Not caused by the async conversion:* the handler was `void` before REQ-NFR-026 stage 3 and is
  `async Task` after, and an async handler triggers *more* re-renders (once at the first await,
  once on completion), not fewer. The behaviour is identical on both.
  *Workaround applied:* none in the page — the smoke re-reads the grid instead of asserting on the
  mutated row, and the banner plus the database row carry the evidence. A page-side
  `StateHasChanged()` does not help, which is what points at the component.
  *Suggested fix:* have `DataTable` re-render its rows when the bound collection's items change, or
  document that `Data` must be reassigned (not mutated) and expose a `Refresh()` the consumer can
  call.
  > ✅ **RESOLVED 2.1.0 (2026-08-11)** — fixed with your second suggestion — the cause is `ShouldRender()` returning false while the `Data` reference is unchanged, which is why a page-side `StateHasChanged()` could not help. `DataTable.Refresh()` is now public: capture the grid with `@ref` and call it after an in-place edit. Documented in the AI reference next to the `Data` parameter.

TR-063 / TR-064 recorded 2026-08-09 by *build-phase (Cluster M fix pass — REQ-NFR-007).
TR-065 recorded 2026-08-10 by *build-phase (Cluster E — REQ-NFR-026 stage 3 tail).
**Next free ID: TR-066.**

---

## Found while building the 2.1.0 demo pages (2026-08-11, TrBlazeUI side)

- **TR-066 — a `Dialog` declared INSIDE another `Dialog`'s `DialogContent` never opens.**
  *Severity:* **High** for anyone composing a picker inside an edit dialog — which is exactly the
  shape TR-060 describes. *Not a regression:* reproduced against the 2.0.1 `PortalService` source as
  well as 2.1.0, so it predates this release.
  *Repro:* put `<Dialog><DialogTrigger …/><DialogContent>…</DialogContent></Dialog>` inside another
  dialog's `DialogContent` and click the inner trigger.
  *Expected:* the inner dialog opens on top of the outer one.
  *Actual (measured 2026-08-11 on a running Blazor Server demo):* nothing happens. The inner
  trigger keeps `aria-expanded="false"`, no second portal is registered, and only one
  `.trblazeui-portal` exists. It is not an interception problem — dispatching `.click()` directly on
  the element through `page.evaluate`, bypassing hit testing, has no effect either, and no exception
  or console error is raised. The inner component instance is stable across clicks (its context id
  does not change), and an ordinary `<Button OnClick>` in the same portal content works, so events
  reach the portal subtree; it is specifically the nested dialog's own state change that does not
  propagate. Two candidate mechanisms were tried and did NOT fix it, and were reverted rather than
  shipped unproven: re-rendering `PortalHost` when the registry changes during its own render, and
  having `DialogPortal`/`SheetPortal` register from `OnStateChanged` instead of `OnParametersSet`.
  *Workaround (supported pattern, demonstrated on `/components/dialog`):* compose stacked dialogs as
  **siblings**, each with its own `@bind-Open`, and open the second from a handler inside the first.
  Verified live: both portals register, the later-opened one is last in DOM order, and its tiles are
  clickable — which is the TR-060 fix working as intended.
  *Suggested fix:* investigate why a state change on a component living inside a portal's
  `RenderFragment` does not reach its own `DialogPortal` child. The likely culprit is the
  `CascadingValue` in `Dialog.razor` skipping its subtree because `objContext` is the same instance
  mutated in place, leaving the nested portal with no parent render to ride on.

TR-066 recorded 2026-08-11 by *fix-issues (TrBlazeUI side) while building the 2.1.0 demo pages.

---

> **ID note (2026-08-11):** **TR-067 is claimed concurrently** by the cluster working REQ-FN-025
> (`Select` inside `DialogContent` renders zero `SelectItem` nodes, so the listbox cannot be opened;
> `ManageImages`'s upload-category picker was swapped to `NativeSelect`). This cluster took **TR-068**
> rather than risk the duplicate-TR-044 collision recorded earlier in this file. Cluster D also
> confirms that TR-067 reproduces on `/users` — the change-role dialog's `user-role-select` opens
> with **zero** `[role="option"]` nodes — and on `/admin/skills`'s add-skill dialog.

- **TR-068 — `Select` silently IGNORES its bound `Value` unless a `ValueChanged` delegate is also
  supplied, and the styled component exposes no `DefaultValue`, so a one-way pre-selection is
  impossible.** *Severity: minor (API/documentation).* Measured on **2.0.1** with bUnit against the
  real component (`tests/TechieBlog.Tests/Components/BlogUi/SelectFirstPaintLabelRenderTests.cs`):
  `<Select TValue="string" Value="1" DisplayTextSelector="…">` with matching items renders its
  **placeholder**, not the label and not "1" — the value never lands. Adding `ValueChanged` renders
  the resolved label. Root cause is visible in `TrBlazeUI.Primitives.Select.Select<TValue>`:
  `OnInitialized` builds `UseControllableState` with `IsControlled = ValueChanged.HasDelegate`, and
  `OnParametersSet` re-syncs `Context.State.Value` **only** `if (objState.IsControlled)`. So without
  a callback the component stays uncontrolled and uses `DefaultValue` — which
  `TrBlazeUI.Components.Select.Select<TValue>` does not surface as a parameter, leaving no way to
  express "pre-select this, read-only". *Why it costs time:* the failure is silent and looks exactly
  like the TR-049/TR-058 first-paint bug, so an author chasing a trigger that "won't show the value"
  cannot tell which of the two they have hit. *Suggested fix:* forward `DefaultValue` from the styled
  `Select`, or adopt the `Value` parameter in uncontrolled mode as the initial state, and say so in
  the AI reference's Select table.
  *Also worth a line in that table:* when `DisplayTextSelector` returns null/empty, `SelectValue`
  falls back to `Value.ToString()` — **not** to `Placeholder`. That is correct behaviour, but it
  means a caller must map its sentinels (`"0"` = "All Users") explicitly; returning empty to "get the
  placeholder" leaves the raw value on screen.

TR-068 recorded 2026-08-11 by *build-phase (Cluster D fix pass — REQ-UI-034 / REQ-UI-038 / REQ-UI-039).

---

## Gap found fixing the per-category upload limits (2026-08-11, Cluster C — REQ-FN-025)

- **TR-067 — a `Select` inside `DialogContent` opens but renders ZERO `SelectItem` nodes, so the
  listbox is empty and the control cannot be used at all.**
  *Severity:* **High.** Not cosmetic and not a test-only artefact — the control is inoperable by
  mouse *and* by keyboard, so any value a dialog asks for through a `Select` is unreachable for
  every user. Measured on **2.0.1**.
  *Repro (this is the isolation, and it is a clean one — same page, same component, same circuit):*
  `/admin/images` renders two `Select<string>`s. The owner filter sits on the page; the upload
  dialog's category picker sits inside `DialogContent`. Click each and count `[role="option"]`.
  *Expected:* both open a listbox with their options.
  *Actual (headless Chromium against a running Blazor Server host, 2026-08-11):*

  | Where | `data-state` after click | `aria-expanded` | `[role="option"]` count |
  |---|---|---|---|
  | Owner filter — **outside** any dialog | `open` | `true` | **5** |
  | Upload category — **inside** `DialogContent` | `open` | `true` | **0** |

  The trigger's own state is correct in both cases; it is only the content that never mounts. No
  element with the trigger's `aria-controls` id (`select-NNN-content`) exists anywhere in the
  document, `[role="listbox"]` and `[data-slot="select-content"]` both match nothing, and no
  exception or console error is raised. Mouse click, `Enter`, `Space` and `ArrowDown` all flip the
  trigger to `open` and all render nothing, so it is not a hit-testing or focus-trap problem.
  *Relationship to TR-066:* almost certainly the same root cause one component over — a portalled
  popover whose state change does not reach its own portal child when it lives inside another
  portal's `RenderFragment`. TR-066 records it for a nested `Dialog`; this is the `Select` case, and
  it is the one that bites in ordinary forms rather than in exotic composition.
  *Confirmed elsewhere by Cluster D:* `/users` (the change-role dialog's `user-role-select`) and
  `/admin/skills` (the add-skill dialog) reproduce it identically, so this is not specific to the
  media library.
  *Workaround (shipped in `ManageImages.razor`):* use **`NativeSelect`**, which renders a real
  `<select>` and needs no popover. It is keyboard operable, it is what a phone shows its own picker
  for, and it costs only the styled trigger. Verified live: all seven categories selectable, and the
  per-category constraint caption, the dropzone ceiling and the `accept` filter all update on change.
  *Suggested fix:* whatever resolves TR-066 should be checked against `SelectContent` too — and,
  since the popover components share `PortalService`, against `DropdownMenu`, `Combobox` and
  `Popover` inside a `Dialog` as well. Until then the AI reference's `Select` section should carry a
  one-line warning to prefer `NativeSelect` inside a dialog.

  > **STILL REPRODUCES ON 2.0.2 — re-measured 2026-08-11 (Cluster E), headless Chromium against a
  > running Blazor Server host on :5421, seeded Admin `Ravi@techieblog.com`.** The 2.0.2 upgrade
  > closed TR-049/TR-058 (see below) but did **not** close this one, which is consistent with the
  > release note's own warning that one dialog-stacking defect (TR-066) remains open upstream. The
  > workaround therefore **stays**: `ManageImages.razor` keeps its `NativeSelect`, and no dialog in
  > this solution may use the popover `Select` yet.
  >
  > | Where | `data-state` after click | `aria-expanded` | `[role="option"]` | `[role="listbox"]` | `[data-slot="select-content"]` |
  > |---|---|---|---|---|---|
  > | `/admin/skills` `skills-user-select` — **outside** a dialog | `open` | `true` | **4** | — | — |
  > | `/admin/stats` `stats-user-select` — outside | `open` | `true` | **4** | — | — |
  > | `/admin/experience` `experience-user-select` — outside | `open` | `true` | **5** | — | — |
  > | `/admin/images` `user-filter-select` — outside | `open` | `true` | **5** | — | — |
  > | `/users` `user-role-select` — **inside** `DialogContent` | `open` | `true` | **0** | **0** | **0** |
  > | `/admin/skills` `skill-category-select` — **inside** `DialogContent` | `open` | `true` | **0** | **0** | **0** |
  >
  > The trigger now emits **no `aria-controls` at all** in the dialog case (2.0.1 emitted a dangling
  > `select-NNN-content` id), so the content element is not merely unmounted — it is never announced.
  > Keyboard is equally dead: focusing the trigger and pressing `Enter` flips it to `open` and yields
  > **0** options, and `ArrowDown`+`Enter` leaves the bound value untouched. Six click attempts with a
  > 900 ms poll between them never produced an option, so this is not a timing artefact — the same
  > polling loop finds 4–5 options on every page-level `Select` in the table above.
  >
  > The `NativeSelect` workaround was re-verified on 2.0.2 in the same run: `/admin/images` upload
  > dialog renders a real `<select>` with **7** `<option>`s, `selectOption(index 3)` moves the bound
  > value `profiles` → `icons`, and the constraint caption follows to
  > *"Max 200 KB, formats: png, svg, webp"*. Screenshots:
  > `tests/.artifacts/cluster-e/tr067-{users,skills}-dialog.png`, `images-upload-dialog.png`.

TR-067 recorded 2026-08-11 by *build-phase (Cluster C — REQ-FN-025).
Re-tested on 2.0.2 and confirmed **OPEN** 2026-08-11 by *build-phase (Cluster E — REQ-UI-034/038/039).

---

## Gap found ADOPTING the 2.0.2 fixes for TR-057 / TR-059 (2026-08-11, Cluster H — REQ-UI-007 / REQ-UI-016)

**Both fixes were adopted, and both hold up.** `<Prose>` replaced `WrapTablesInScrollContainer` in
`PostView.razor` and `<Textarea>` replaced the raw uncontrolled `<textarea>` in
`PostMarkdownEditor.razor`. The TR-057 fix in particular was checked against the counterfactual that
made the original defect visible, not just against a fast local circuit: 400 ms emulated latency
(CDP `Network.emulateNetworkConditions`) with burst typing at 0/15/40 ms per key, which the **2.0.1**
build failed 4 of 9 runs — **2.0.2 passed 9 of 9**, twice, before and after a document switch. The
entry below is the one thing the release note over-promises.

- **TR-069 — `TextValueSync` protects against the echo it recognises, not against a stale supplied
  value, so "you do not need to debounce for correctness" is stronger than what the code guarantees.**

  *Severity:* Low (documentation / hardening). Nothing in this app regressed, because the app keeps
  its own document-scoped latch — but that latch had been written as a TR-057 workaround, and the
  release note reads as though it can now be deleted. It cannot.
  *Measured on:* `TrBlazeUI.Components 2.0.2`, `TrBlazeUI.Components.Utilities.TextValueSync`.
  *Behaviour:* `OnValueSupplied(v)` short-circuits only when `v` equals `objSuppliedValue`, and
  `OnUserInput` overwrites `objSuppliedValue` with the value of the keystroke just processed. So the
  helper recognises exactly ONE echo — the most recent. A supplied value that is neither the current
  DOM value nor that single last echo is classified as a programmatic change and written into the
  DOM, caret reset included.
  *Why that is not academic:* it is only safe because, on a Blazor Server circuit, the parent's echo
  is produced synchronously inside the same event dispatch as the keystroke. Any host that puts a
  layer between the control and the parent — a debounced parent, an `await` before the write-back, a
  composite editor like ours, a component that replays a value — can supply a value one keystroke
  behind, and the control will happily write it. That is the original TR-053/TR-057 clobber arriving
  through a different door.
  *Proven by counterfactual, not by argument:* `PostMarkdownEditor` sits between `ManagePost` and
  `Textarea` and keeps a `hasLocalEdits` latch that drops any value arriving under an UNCHANGED
  document identity. With the latch in place, `EditorIgnoresEchoedValueWhenResetKeyUnchanged`
  (bUnit, `tests/TechieBlog.Tests/Components/BlogUi/PostEditorRouteReloadTests.cs`) passes: a stale
  `"## Live headin"` supplied after the user typed `"## Live heading"` never reaches the DOM. With the
  latch disabled and nothing else changed, that test **fails** — the stale value lands in the
  rendered `value` attribute. The library did not stop it; the app did.
  *Suggested fix:* one of — (a) keep a short window of recently-sent values rather than only the
  last, so any of them is recognised as an echo; (b) expose the guard the app has to write anyway as
  a parameter (`DocumentKey`/`ResetKey`: adopt `Value` unconditionally when it changes, ignore it
  while unchanged and focused); or (c) at minimum, soften the AI reference's Blazor Server note to
  say what is actually guaranteed — the control ignores the echo of its own last keystroke — and
  point hosts that wrap it at (b).
  *Not a blocker:* `Input` shares `TextValueSync` and therefore the same characteristic; nothing in
  this app wraps `Input` the way `PostMarkdownEditor` wraps `Textarea`, so it was not exercised.

  *Also worth a line in the reference, found while adopting `Prose`:* `ConstrainWidth` defaults to
  **true** (`max-w-prose`, i.e. 65ch ≈ 600px). A page that already caps its own measure — this one
  is `max-w-[820px]` — narrows visibly on adoption unless `ConstrainWidth="false"` is passed. The
  parameter table documents the default correctly; the usage snippet in the Prose section leads with
  the constrained form, which is the one most Markdown bodies do NOT want.

TR-069 recorded 2026-08-11 by *build-phase (Cluster H fix pass — REQ-UI-007 / REQ-UI-016).

---

## Gaps found deleting the 2.0.1 accessibility workarounds (2026-08-11, Cluster G — REQ-NFR-007)

Both found while removing the `App.razor` accessibility `MutationObserver` and the five markup
workarounds the 2.0.2 notes said could go. **The headline is that they could: axe
(wcag2a/2aa/21a/21aa) over 9 public + 15 admin routes reported 0 violation nodes before the
removal and 0 after**, and the four DOM properties the observer used to repair were counted
directly on every route — 0 dangling `aria-controls`, 0 orphan `role="tab"`, 0 empty
`aria-selected`, 0 focusable nodes inside an `aria-hidden` subtree. TR-020/021/031/044/045/052/
054/061/063/064 are all confirmed closed against this application's markup.

Two residuals, neither a blocker.

- **TR-070 — `Rating`'s roving `tabindex` moves on Arrow/Home/End but DOM focus does not follow
  it, so the focus ring and the screen-reader cursor stay on option 1 while the selection is
  somewhere else.**

  *Severity:* **Medium** — WCAG 2.4.7 Focus Visible, and 4.1.2 in spirit. The control is
  *operable* (2.1.1 passes), which is the part TR-031 was about; this is the next layer down.
  *Repro:* `<Rating @bind-Value="v" Max="5" />` on a Blazor **Server** circuit. Tab into the group,
  then press ArrowRight, ArrowRight, ArrowLeft, End, Home, reading after each press:
  ```js
  const o = [...document.querySelectorAll('[role="radio"]')];
  ({ domFocus: o.indexOf(document.activeElement),
     roving:   o.findIndex(x => x.getAttribute('tabindex') === '0'),
     checked:  o.findIndex(x => x.getAttribute('aria-checked') === 'true') })
  ```
  *Expected:* `domFocus === roving` after every press — that is the whole point of a roving
  tabindex; the component must call `.focus()` on the option it just made current.
  *Actual (measured 2026-08-11, `tests/.artifacts/harness/g-rating-keys.mjs`, one trace):*
  ```
  after Tab         focus0  roving0  checked-none
  after ArrowRight  focus0  roving0  checked0
  after ArrowRight  focus0  roving1  checked1
  after ArrowLeft   focus0  roving0  checked0
  after End         focus0  roving4  checked4
  after Home        focus0  roving0  checked-none
  ```
  `roving` and `checked` track each other perfectly — the state machine is right — but `domFocus`
  never leaves 0, and after the first move option 0 is `tabindex="-1"`, so DOM focus is parked on
  an option that is no longer the current one. A sighted keyboard user sees the ring stay on star
  1 while the fill moves; a screen-reader user is told "1 out of 5" while star 4 is selected. A
  further consequence: the next `Tab` leaves the group from option 0, not from the current option.
  *Also seen:* `Home` sets the value to *none* rather than to option 1. With `AllowClear` defaulting
  to `true` that may be intended, but ARIA's radiogroup pattern has Home mean "first option".
  *Encountered in:* `source/BlogUI/Components/PostRatingPanel.razor` (REQ-UI-027).
  *Not worked around.* An application-side `.focus()` would need exactly the render-batch-scoped
  observer this cluster just deleted, and the control is operable without it. Recorded instead.
  *Suggested fix:* after the roving index changes, `await ElementReference.FocusAsync()` on the new
  current option in `OnAfterRenderAsync` (the re-render has to land first on a Server circuit).

- **TR-071 — `ItemContent` sets no `min-width: 0`, so an `Item`'s text sets a hard min-content
  floor and the whole containing grid track overflows on a narrow viewport.**

  *Severity:* **Low** (layout ergonomics; a one-class workaround exists).
  *Repro:* put an `ItemGroup`/`Item` list with realistic sentence-length `ItemTitle` /
  `ItemDescription` text inside a `Card` in a `grid` column, and render at 390px.
  *Expected:* the row shrinks and the text wraps or clamps — every other library surface that
  holds long text (`CardDescription`, `DataTable` cells) already behaves this way.
  *Actual (measured 2026-08-11 on `/admin` at 390px):* `ItemContent` is a flex child with the
  default `min-width: auto`, so the card's own min-content width was **419px against 358px of
  room**, and because grid tracks are floored by their items' min-content contributions the entire
  dashboard column — including an untouched sibling card — overflowed the admin pane by 45px.
  *Workaround (adopted):* `<ItemContent Class="min-w-0">` in
  `source/BlogUI/Pages/AdminPages/AdminDashboard.razor`, which took this card's min-content from
  419px to 262px.
  *Suggested fix:* ship `min-w-0` on `ItemContent`'s own class list (and consider a line-clamp on
  `ItemDescription`, which is what the hand-rolled markup this replaced used). Worth a sentence in
  the reference either way, because the failure is invisible at desktop width and shows up as a
  *sibling* component overflowing.

  *Not a library issue, recorded because it cost this cluster an hour:* `min-w-0` is the wrong tool
  when the child carries `truncate`. `truncate` implies `white-space: nowrap`, so the box's
  min-content size is the full string; `min-width: 0` only lets flexbox shrink the item *after* the
  track has already been sized that wide. `w-0 flex-1` is what actually reduces the contribution.

TR-070 / TR-071 recorded 2026-08-11 by *build-phase (Cluster G — REQ-NFR-007 workaround removal).

- **TR-072 — `DatePicker` and `TimePicker` ACCEPT a splatted `data-testid` and then never render
  it. Upgrading 2.0.1 → 2.0.2 silently DELETED three working test hooks.**

  *Severity:* **High** — it is a regression, it is silent in all three places a consumer would
  look (build, browser console, component API), and it contradicts the release's own headline
  claim.
  *Repro:* render `<DatePicker @bind-Value="d" data-testid="x" />` and query `[data-testid=x]`
  → **0 elements**. No throw, **0 build errors, 0 warnings**. Same for `TimePicker`.
  *Expected:* the attribute reaches the popover trigger `<button>`, per TR-048's audited claim
  that 344/344 Components and 59/59 Primitives declare `CaptureUnmatchedValues`. The documented
  exception list names only the context roots and `DataTableColumn` — not these two.
  *Actual, measured on TechieBlog 2026-08-11:* three `data-testid`s that had ridden directly on
  `<DatePicker>` and **worked on 2.0.1** were gone after the upgrade — `publish-date-picker`
  (`ManagePost.razor`), `experience-start-date` and `experience-end-date`
  (`ManageExperience.razor`). Nothing in the build or the browser said so. They were found only
  by a census that compared 554 component-borne test ids in the markup against 427 actually
  observed in the DOM.
  *Workaround (adopted):* all three moved back onto `<span>` wrappers — i.e. the TR-048 wrapper
  pattern the migration guide tells consumers to delete. `publish-time-picker` was reverted to
  its wrapper for the same reason.
  *Suggested fix:* spread `AdditionalAttributes` onto the trigger element in both components —
  **and re-run `tools/splat-audit` against RENDERED output rather than declared parameters.** A
  declared `CaptureUnmatchedValues` property that is never spread is precisely what an audit of
  declarations cannot see, so the 344/344 figure cannot presently be relied on.

- **TR-072b — `StatTile` exposes no way to address its value or its caption.**

  *Severity:* **Medium** (testability).
  *Actual:* `Value` and `Label` are string parameters with no `ValueTemplate`/`LabelTemplate` and
  no `data-slot` on the rendered parts, so a consumer can only put a test id on the tile root.
  Adopting `StatTile` therefore **cost** the `home-stat-value` / `home-stat-label` hooks outright;
  two verify specs had to be re-pointed at the tile's own elements.
  *Suggested fix:* emit `data-slot="stat-tile-value"` / `data-slot="stat-tile-label"`, or accept
  optional `ValueTemplate` / `LabelTemplate` fragments.

- **TR-072c — "2.0.2 ships the standard Tailwind scale" is not literally complete.**

  *Severity:* **Low-Medium.** Measured against the shipped bundle, these are used by TechieBlog
  and absent: `min-h-28`, `min-h-36` (the `h-*` scale HAS 28 and 36; `min-h-*` jumps 24 → 32 → 40,
  so the asymmetry TR-019 raised survives in smaller form), `hover:opacity-90` (only
  `hover:opacity-100` ships), and `md:-mx-6` (negative margins ship unprefixed, with no responsive
  variant). These four are the only non-arbitrary rules left in `utilities.css` after the sweep.
  *Also worth stating in the AI reference:* an arbitrary value happens to work **if the library
  itself uses it** — `w-[200px]`, `h-[150px]`, `max-h-[70vh]` are all in the bundle. That makes
  "arbitrary values never work" a trap in the other direction: a consumer can test one, see it
  work, and be wrong about the next one.

- **TR-072d — adopting a new 2.0.2 component needs an `_Imports.razor` change the compiler will
  not ask for, and the failure mode is silent.** *(Packaging/docs note, not a defect.)*

  The nine namespaces new in 2.0.2 — `TrBlazeUI.Components.{Stat,Timeline,Stepper,CenteredPanel,
  AnchorNav,CodeBlock,PasswordStrength,SortableList}` — are correctly listed in AI-Reference §1 but
  are not in a consumer's `_Imports.razor` after an upgrade. `<StatTile Value="20+" />` then
  **compiles with 0 errors** and renders as a literal, empty `<stattile value="20+">` element in
  the DOM. Only a browser smoke catches it. Worth calling out in the upgrade notes.

TR-072 recorded 2026-08-11 by *build-phase (Cluster F — CSS/layout workaround removal), written up
by the orchestrator: the cluster measured and reported all four findings but returned them without
writing them to this file.

- **TR-073 — the prebuilt bundle ships gradient DIRECTION utilities (`bg-gradient-to-br` etc.) but
  NO gradient colour-STOP utilities at all, for any colour, custom token or base palette.**

  *Severity:* Low-Medium (documentation/trap, same family as TR-072c). Not a functional defect —
  Tailwind's own architecture makes stop utilities and direction utilities separable generators —
  but the AI reference's "Tailwind utilities work in application markup … ships the standard
  Tailwind scale" guidance reads as though the whole gradient family is available, and it silently
  is not.
  *Repro (measured 2026-08-24 on the 2.0.2 bundle, REQ-UI-049/UAT-025):*
  `grep -o '\.bg-gradient-to-[a-z]*' trblazeui.css` → 8 direction classes present
  (`to-t/tr/r/br/b/bl/l/tl`). `grep -o '\.from-[a-zA-Z0-9-]*'` and `grep -o '\.to-[a-zA-Z0-9-]*'`
  → **zero matches, for every colour name tried, including base-palette names the library itself
  uses elsewhere** (not just custom theme tokens like `muted`/`card`). `bg-gradient-to-br` alone
  therefore sets `background-image: linear-gradient(to bottom right, var(--tw-gradient-stops))`
  with `--tw-gradient-stops` never defined by anything in the bundle, i.e. an invisible gradient —
  the exact "resolves to nothing, silently" trap `utilities.css`'s own header warns about for
  arbitrary values, but here it is a whole utility *family*, not a bracket syntax.
  *Workaround (adopted):* a hand-written rule in `utilities.css` —
  `.post-card-fallback { background-image: linear-gradient(135deg, var(--muted) 0%, var(--card) 100%); }`
  — direction and both stops in one declaration, tokens only, no bracket syntax at all.
  *Suggested fix:* either generate `from-*`/`via-*`/`to-*` for the same semantic-colour set the
  bundle already emits solid-background utilities for (`bg-muted`, `bg-card`, …), or state
  explicitly in the AI reference that the gradient family ships direction-only and stop colours
  are always a hand-written rule — the current wording ("ships the standard Tailwind scale … not
  just the utilities the library's own components happen to use") reads as a guarantee that does
  not hold for this one family.

TR-073 recorded 2026-08-24 by *trblazeui (UAT-025 / REQ-UI-049 — PostCard no-banner fallback).

- **TR-074 — `AnchorNav`'s `<a href="#id">` links silently navigate the whole app away from the
  current page on any app with `<base href="/">` and a non-root current path — a real, reproducible
  loss of the page being viewed, not a cosmetic bug.**

  *Severity:* **High.** Not a rendering glitch — clicking a TOC entry made the entire article
  disappear with no error and no console warning.
  *Repro (measured 2026-08-24 on the 2.0.2 bundle, REQ-UI-045/UAT-027, post detail page rebuild):*
  `AnchorNav` renders each `AnchorNavSection` as a plain `<a href="#{Id}">` (confirmed via the
  rendered DOM — `<a href="#where-i-was-wrong" ...>`). This host declares `<base href="/">` in
  `App.razor`, standard for a Blazor Web App with client-side routing. Per the HTML spec, a bare
  fragment `href` is resolved **against the base URI**, not the current document path — so on
  `/post/{slug}` a click on `href="#where-i-was-wrong"` resolves to `/#where-i-was-wrong`
  (the post's whole path silently dropped). Playwright reproduction: `window.scrollY` stayed `0`
  after the click (no scroll happened at all), and `page.url()` became
  `http://host/#where-i-was-wrong` — origin only, no `/post/{slug}`. The article's own DOM was
  found to still be technically present in one run but the browser had genuinely navigated
  (`framenavigated` fired for the bad URL); in an adjacent run the SPA fully swapped to the site's
  Home page instead (its `<a>` click interception performs the *same* base-relative URL resolution
  to decide whether a click is a "same page, just the hash changed" no-op, computes the same wrong
  path, and — since the path now differs from the current one — treats it as a genuine internal
  navigation to `/`). Either outcome fails the one job a TOC link has: reliably reaching its own
  heading.
  *Root cause, restated precisely:* `AnchorNav` has no parameter for a base path/prefix, and always
  renders exactly `"#" + Id` with no way for a consumer to supply a full relative URL through the
  public API (`AnchorNavSection(string Id, string Label)` — no path field, and the `#` is
  hard-baked into the template, so passing a longer `Id` just produces a longer, still-fragment-only,
  still-broken `href`).
  *Workaround (adopted, app-side):* `source/BlogUI/wwwroot/js/post-toc-rail.js` intercepts the click
  on an **ancestor** of the `AnchorNav` links (this app's own rail wrapper) in the bubble phase.
  Bubble-phase dispatch always visits an ancestor element before it reaches `document`, regardless of
  when each listener was attached, so this reliably runs ahead of Blazor's own document-level click
  listener and can call `preventDefault()` before Blazor's handler — which itself bails out
  immediately once `event.defaultPrevented` is already true — ever sees the click. The workaround
  then performs the scroll itself (compensating for the app's sticky header) and records the fragment
  with `history.replaceState` using `location.pathname` explicitly, sidestepping the same base-href
  trap for the address bar. Confirmed fixed: `window.scrollY` moves correctly and `page.url()`
  correctly retains `/post/{slug}#{id}`.
  *Suggested fix:* either (a) have `AnchorNav` do its own `preventDefault()` + `scrollIntoView` +
  `history.replaceState(..., location.pathname + '#' + id)` internally in `anchor-nav.js`, so the
  component is correct out of the box regardless of the host's `<base href>`, or (b) add an optional
  `Href`/`BasePath` parameter to `AnchorNavSection`/`AnchorNav` so a consumer on a nested route can
  supply the full relative target instead of a bare fragment. Given this bug reproduces on **any**
  Blazor Web App using the (default, documented) global-interactivity setup with a non-root current
  page — which is most of them — this is not an edge case.
  *Also worth stating in the AI reference:* `AnchorNavSection` has no heading-level field, so a
  consumer wanting to distinguish h2/h3 entries visually has no lever except baking indentation
  characters into the label string (which is what this app did — see `PostTocRail.razor`). Worth
  either adding a `Level`/`Depth` parameter or documenting the label-prefix workaround directly.

TR-074 recorded 2026-08-24 by *trblazeui (UAT-027 / REQ-UI-045 — post detail page rebuild, TOC rail).

---

## Found verifying the 2.0.3 upgrade (2026-08-26, *verify REQ-UI-048 · REQ-FN-025 · REQ-UI-049)

- **TR-075 — after an option is picked in a styled `Select` nested in `DialogContent`, focus is
  dropped to `<body>` instead of returning to the `SelectTrigger`, so `Escape` no longer dismisses
  the dialog until the user Tabs back in.**

  *Severity:* **Low–Medium** (accessibility — WCAG 2.4.3 Focus Order; not a keyboard trap).
  *Measured on:* `TrBlazeUI.Components 2.0.3`, TechieBlog 2026-08-26, headless Chromium against a
  running Blazor Server host, three dialogs (`/admin/images` upload category, `/users` edit-user
  role, `/admin/skills` add-skill category). Identical result on all three, by mouse and by keyboard.
  *Repro (keyboard):* open the dialog → focus the trigger (`document.activeElement` is the trigger,
  inside the dialog) → `Enter` opens the listbox (7 options) → `ArrowDown`, `Enter`.
  *Expected:* focus returns to the trigger (the WAI-ARIA listbox/combobox pattern), so `Escape`
  still reaches the dialog and closes it.
  *Actual:* `document.activeElement === document.body`; `Escape` ×2 leaves the dialog open
  (`data-state="open"`, no `inert`, no `aria-hidden`, circuit alive). `Tab` #1 re-enters the
  dialog (lands on its first focusable — the file input / the Cancel button), and `Escape` then
  closes it. Mouse `Cancel` / `X` / backdrop work throughout. Baseline control: the same dialog
  opened WITHOUT touching the Select closes on the first `Escape`, so this is the Select's focus
  hand-back, not the dialog.
  *Also observed:* Playwright's `locator.click()` on `Cancel` stalls in its actionability wait in
  this state while a raw coordinate click lands — harness-side symptom of the same focus/overlay
  bookkeeping, recorded so the next consumer does not misread it as an app hang.
  *Relationship:* this is the residue of TR-066/TR-067 — the nested content now renders and
  selects correctly; what it does not yet do is restore focus to its trigger after closing inside a
  portalled dialog.
  *Workaround (not adopted):* none needed by TechieBlog — no acceptance requires Escape-dismissal
  after a pick, and the dialogs' Cancel buttons are one Tab away. Recorded, not demoted.
  *Suggested fix:* on item selection (mouse or keyboard) `await trigger.FocusAsync()` after the
  listbox closes, the same `OnAfterRenderAsync`-timed focus return TR-070 added for `Rating`; and
  add the nested-in-Dialog case to `tests/verify/ui-ui004.spec.js` with an `Escape`-closes-dialog
  assertion after a pick.

TR-075 recorded 2026-08-26 by *verify (REQ-UI-048 / REQ-FN-025 / REQ-UI-049 — 2.0.3 re-verification).
**Next free ID: TR-076.**

> **Note on this file's own "next free ID" bookkeeping:** the task brief that led to TR-073 named
> **TR-067** as the next free id, following the SUMMARY section at the top of this file rather than
> this counter line — the summary was not kept in step with TR-067 … TR-072d being added below it.
> This entry used **TR-073**, matching this line, which is the authoritative counter. The top
> summary needs its own pass to reconcile the count (66 → at least 73 entries) — out of scope for
> a single-defect UAT fix, flagged here so the next agent that reads only the summary is not misled
> the same way.
