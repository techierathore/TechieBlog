# [REQ-NFR-008] (BRD-85) — partition: `BlogEngine/Common` + `BlogEngine/Storage` + engine root

Cluster: engine-common-storage. 25 files. Documentation pass against
`docs/TechieBlog-Coding-Standards.md` §"XML Documentation (MANDATORY on public members)",
house voice matched to `source/BlogModel/Interfaces/RepoSyncBridge.cs`.

## Numbers

| Metric | Count |
|---|---|
| Files in partition | 25 |
| Files edited | 19 |
| `<summary>` blocks in partition — before | 198 |
| `<summary>` blocks in partition — after | 218 |
| Members newly documented (had no XML doc at all) | 20 |
| Members substantively upgraded to the standard's shape | 27 |
| Members already compliant, left unchanged | 171 |
| Public/protected members with no XML doc, after | 0 (verified structurally) |

Build: `~/.dotnet/dotnet build TechieBlog.slnx` → **0 errors**.
Tests: `dotnet test --filter "FullyQualifiedName!~Integration"` → **383 passed, 0 failed** (baseline).
Doc-comment health: forcing `-p:GenerateDocumentationFile=true` across the solution produces
**zero** CS1570/1572/1573/1574/1587/1591 in this partition (the two that exist solution-wide are
pre-existing and belong to other partitions — see "Reported out" below).

## Newly documented (20 members)

- `SvcUtils.cs` — 3 (type + both methods; the file had no XML documentation at all).
- `BlogSvcInitializer.Initialize` — 1.
- `SiteSettingsMapper.cs` — 16 private helpers (`ApplyGeneral`/`ApplyBlog`/`ApplyPresentation`/
  `ApplySmtp`/`ApplyStorage`, `GeneralRows`/`BlogRows`/`PresentationRows`/`SmtpRows`/`StorageRows`,
  `Row`, `ReadText`/`ReadNumber`/`ReadFlag`, `WriteNumber`/`WriteFlag`).

`AllUsings.cs` gained a file-header block (a global-usings file has no member to attach XML doc to).

## Security topics documented to the brief

- **Captcha image** (`CaptchaSvgRenderer`, `CaptchaGlyphSet`) — the "no machine-readable answer in
  the DOM" guarantee written as a rule with its defect history (the base64 `data:` URI that still
  carried `<text>A</text>`), an explicit prohibition on reintroducing `<text>`/fonts/ids/transforms,
  and the deliberate independence of markup vocabulary from the code, cross-referenced to
  `CaptchaSvcTests.CaptchaMarkupVocabularyIsIndependentOfCode`.
- **Accessible captcha** (`CaptchaQuestionSet`, `CaptchaQuestion`) — why every shape resolves to a
  *number* (a multiple-choice question prints its answer in the page source), and that a text
  question is inherently machine-**solvable** so the rate limiter is the other half of the design —
  weakening the limiter reduces this challenge to approximately nothing.
- **Captcha rate limiting** (`CaptchaRateLimiter`, `ICaptchaRateLimiter`, `CaptchaRateLimitOptions`,
  `CaptchaRateLimitedException`) — two independent per-client fixed windows (20/60 s issuance,
  5/300 s failures); missing/zero/negative/unparsable config falls back to the compiled default so a
  typo cannot switch a cap off; drop-on-read plus a bounded sweep so the limiter cannot become its
  own DoS; per-process counters spelled out with worked numbers (4 instances ⇒ 80/min and 20/5 min).
- **`SvcUtils`** — the JWT signature is **not** verified on read (`ReadJwtToken` is a decode);
  session validity is DB-backed against `UserLogin`; therefore key rotation does not by itself
  invalidate anything and had to be made to bite via key fingerprinting in storage-key names.
  Marked plainly as outstanding work, not as secure by design.
- **`LoginThrottle`** — partition key is the lower-cased login email (protects an account, not a
  caller; the deliberate account-lockout trade-off is stated), plus the forwarded-headers dependency
  and the empty-allow-list footgun that `ForwardedHeadersSetup` defuses with
  `ForwardedHeaders.None`.
- **`PasswordValidator`** — the four rules, all four password-setting paths including the forced
  first-login change (REQ-NFR-023), and honest limitations (no max length, no breached-corpus check;
  `Password1` passes — the throttle and PBKDF2 carry the real load).
- **`VisitorHasher`** — exactly what is hashed, salted (and why an unsalted IP hash is not a
  pseudonym at all), not reversible without the salt, and **linkable across posts by design** —
  documented as pseudonymous personal data, not anonymous data.
- **`MarkdownRenderer`** — XSS posture answered directly: user Markdown **is** sanitised, by this
  class and not by Markdig; raw HTML is **not** permitted (`DisableHtml`); the bar is set at the
  comment-body (anonymous, rendered to other visitors) level for all callers, and `ToPlainText`'s
  output is text and must never reach `MarkupString`.
- **Storage** — provider names and per-provider path semantics as a table; the two-layer traversal
  defence (reject-don't-sanitise, then re-check containment after `Path.GetFullPath`); the explicit
  note that the layer's contract is containment only and that extension/size/MIME policy lives in
  `BlogImageService`; and that misconfiguration silently degrades to local disk.
- **DI composition roots** — every lifetime with its reasoning, the known-and-deliberate captive
  dependency (singleton `SiteSettingsService` over transient `ISiteSettingRepo`), the fact that
  `SvcTokenRepo` (REQ-FN-052) was never registered anywhere, and that `RateLimitedCaptchaSvc` is
  registered as a **decorator** so no registration exists through which an unlimited captcha is
  reachable.
- **`ResiliencePipelines`** — a thresholds table for all three named pipelines and an explicit
  graceful-degradation contract for an open breaker.

## Reported out (not fixed here — outside this partition)

1. `source/BlogEngine/Services/CaptchaClientKeyProvider.cs:36-44` — stale security documentation.
   It states the host has no `UseForwardedHeaders` and no `ForwardedHeadersOptions`; both now exist
   (`source/TechieBlog/Program.cs:134,307`, `source/TechieBlog/Middleware/ForwardedHeadersSetup.cs`).
2. `source/BlogModel/Interfaces/IUserEventRepo.cs:19` — CS1574, unresolvable `cref` `CreateAsync`.
3. `source/BlogUI/Layouts/MainLayout.razor.cs:1` — CS1587, `///` block on no language element.
