# REQ-NFR-008 — BlogEngine/Services, files M–Z

Partition: `source/BlogEngine/Services/` files starting M–Z (14 files). Documentation pass against
`docs/TechieBlog-Coding-Standards.md` §"XML Documentation (MANDATORY on public members)", in the
house `<para><b>Purpose:</b> …</para>` voice.

## Per-file counts

| File | Members newly documented / substantially expanded | Already adequate, left alone |
|---|---:|---:|
| MemoryCacheService.cs | 3 | 5 |
| NewsletterSvc.cs | 12 | 8 |
| PostViewTracker.cs | 2 | 4 |
| RateLimitedCaptchaSvc.cs | 2 | 5 |
| RatingSvc.cs | 8 | 6 |
| ScheduledPostPublisher.cs | 4 | 0 |
| SeriesSvc.cs | 13 | 0 |
| SiteSettingsService.cs | 9 | 7 |
| SitemapSvc.cs | 7 | 0 |
| SmtpEmailService.cs | 3 | 8 |
| SubscriberSvc.cs | 12 | 0 |
| TagSvc.cs | 16 | 0 |
| UserStatsSvc.cs | 1 | 13 |
| VerificationEmailSender.cs | 2 | 3 |
| **Total** | **94** | **59** |

## Standards fixes applied (this partition only)

- `TagSvc.cs` — `private readonly IBlogTagRepo TagRepo` → `tagRepo` (bare camelCase, no PascalCase
  fields). All 14 usages updated; field is private, no ripple outside the file.
- `SeriesSvc.cs` — `SeriesRepo` → `seriesRepo`, `PostRepo` → `postRepo`. Same rationale.
- `SitemapSvc.cs` — `<lastmod>` was interpolated as `{lastmod.Value:yyyy-MM-dd}`, which renders in
  the server's **current culture**; under a non-Gregorian calendar that emits a date no crawler can
  parse. Now `ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)`. Small, safe, obviously correct.

No `_underscore`, Hungarian, or `a`/`v`-prefixed identifiers were found in this partition. All 14
files already use file-scoped namespaces. No method exceeded the size/nesting limits badly enough to
warrant a refactor.

## Verification

- `~/.dotnet/dotnet build TechieBlog.slnx` → **0 errors**. BlogEngine rebuild: 11 warnings, all
  pre-existing (`MarkdownRenderer.cs` ×5, `CaptchaSvc.cs` ×2, NU1510 ×4) — none from this partition.
- `~/.dotnet/dotnet test --filter "FullyQualifiedName!~Integration"` → **383 passed, 0 failed**
  (baseline). `RateLimitedCaptchaSvcTests` and `CaptchaRateLimiterTests` unaffected.

## Defects found (reported, not fixed — outside this partition)

1. **`source/BlogEngine/Services/SmtpEmailService.cs:69-80`** — the sender reads `IConfiguration`
   only, once at construction. The SMTP settings an administrator saves on the Settings screen are
   validated, encrypted at rest and persisted, but **never read by anything**:
   `ISiteSettingsService.GetSmtpSettingsAsync` has zero consumers outside its own class. Impact:
   changing SMTP configuration through the admin UI has no effect on mail delivery; only a
   configuration change plus a restart does. Contradicts the contract documented on
   `source/BlogModel/Models/SmtpSettings.cs:15` ("the sender calls
   `ISiteSettingsService.GetSmtpSettingsAsync` per send").

2. **`source/BlogUI/Components/BlogSidebar.razor:238`** — the sidebar subscribe box calls
   `SubscriberSvc.Subscribe`, which inserts with `IsConfirmed = true`
   (`source/BlogEngine/Services/SubscriberSvc.cs:71`), bypassing the double opt-in that
   `NewsletterSubscribeCard` enforces. Impact: any visitor can subscribe a third party's address
   without their consent, and the distinct "This email is already subscribed" message lets an
   anonymous caller enumerate which addresses are on the list. The divergence is acknowledged in
   `NewsletterSubscribeCard.razor.cs:31` as retained REQ-FN-030 behaviour, so this is a known gap
   rather than an accident — but it is a live one.

3. **`source/BlogEngine/Services/MemoryCacheService.cs` (whole class)** — the tag-aware cache is
   registered as `ICacheService` for settings, taxonomy and listings (REQ-NFR-018), but no
   production write path calls `EvictTag` and the only in-tree consumer of `GetOrCreate` is
   `source/TechieBlog/HealthChecks/CriticalServicesHealthCheck.cs:72`. Impact: today, none — the
   caching layer is inert, so there is no staleness bug. The risk is forward-looking: the first
   service to start caching through it must add the matching eviction in the same change, or
   administrators will silently see stale settings.

## Notes (not defects)

- `ScheduledPostPublisher` publishes through `BlogSvc.UpdatePost` and evicts no output-cache tag, so
  a newly published scheduled post can take up to 15 minutes to appear in `/sitemap.xml` (the `Feed`
  policy window) and 5 minutes in public listings. Consistent with the caching design; documented on
  the class rather than raised.
- `SeriesSvc.GetSeriesBySlug` intentionally returns unpublished parts so `SeriesView.razor` can
  render them as unlinked "Coming Soon" rows. A draft part's **title, abstract and featured image
  are therefore visible to anonymous visitors** — deliberate, now documented explicitly on the
  method so nobody "fixes" the missing filter and silently breaks the page.
- `TagSvc.SetTagsForPost` returns `void` and swallows repository failures, so a post save can report
  success while the author's tag changes are lost. Documented as a trap; converting it to `Result`
  would ripple into the post editor (outside this partition).
