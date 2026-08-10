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

- **TR-049 — `SelectValue` renders the raw bound value instead of the matching `SelectItem`'s
  `Text`.** *Severity:* **Medium** — repo-wide cosmetic-but-user-facing defect.
  *Repro:* bind a `Select<T>` to an id/enum and give each `SelectItem` a human `Text`; the closed
  trigger shows the raw bound value.
  *Expected:* the selected item's `Text`. *Actual:* the underlying value.
  *Encountered in:* every user, role and category picker in the admin area (reported by cluster L).
  *Workaround:* render the display text yourself inside `SelectTrigger`.
  *Suggested fix:* have `SelectValue` resolve the selected `SelectItem` and render its `Text`,
  falling back to the raw value only when no item matches.

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

TR-063 / TR-064 recorded 2026-08-09 by *build-phase (Cluster M fix pass — REQ-NFR-007).
**Next free ID: TR-065.**
