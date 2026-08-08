# REQ-NFR-008 — `source/BlogEngine/Services/` A–L

Partition: 20 files, `AnalyticsSvc.cs` … `LoggingVerificationEmailSender.cs`.

## Numbers

| Metric | Count |
|---|---|
| Files in partition | 20 |
| Files edited | 6 |
| Documented declarations in partition (final) | 162 |
| Newly written / substantially rewritten by this pass | 43 |
| Already compliant, left alone | 119 |
| Undocumented public members remaining | 0 |

Build: 0 errors, no `CS1570`/`CS1573`/`CS1591` XML-doc warnings introduced.
Tests: 383 passed / 0 failed (`FullyQualifiedName!~Integration`), unchanged from baseline.

## Files edited

- **`AuthSvc.cs`** — added the session-model limitation (JWT signature is never verified on read;
  `UserLogin` is the real authority), the configuration-sourced signing key and UTF-8 rationale, the
  two-expiry divergence, the forced-password-change propagation path, and an explicit statement that
  the attempted password is never recorded.
- **`BlogSvc.cs`** — class `<remarks>` rewritten (purpose, the "this service enforces no
  authorization" statement, the read-degrades / write-reports failure convention); constructor and
  all 22 legacy synchronous members given full `Business Logic` / `Flow` / `Side Effects` blocks.
  Standards: `PostRepo` → `postRepo`, `aPostRepo` → `postRepo`, `aUserId`/`aIsAdmin`/`aSingleId` →
  `userId`/`isAdmin`/`postId`, `vReturnVal` local removed.
- **`CategorySvc.cs`** — class `Purpose` rewritten; constructor and all 8 legacy synchronous members
  documented. Standards: `CategoryRepo` → `categoryRepo`.
- **`ConsoleEmailService.cs`** — replaced `<inheritdoc/>` on both members. They were inheriting
  `IEmailService`'s "**Side Effects:** Sends email", which is false for this implementation.
- **`LoggingVerificationEmailSender.cs`** — same correction; it was inheriting "Sends an email".
- **`DatabaseHealthProbe.cs`** — added what a green probe proves and, explicitly, the four things it
  does not (schema, correct database, capacity, write availability).

## Files already compliant — reviewed, not edited

`AnalyticsSvc`, `BlogImageService`, `CaptchaClientKeyProvider`, `CaptchaSvc`, `CommentSpamGuard`,
`CommentSvc`, `DashboardSvc`, `EmailVerificationSvc`, and the six interfaces
(`ICaptchaClientKeyProvider`, `ICaptchaService`, `ICommentSpamGuard`, `IEmailService`,
`IEmailVerificationService`, `IVerificationEmailSender`).

Verified against the current code rather than assumed: the captcha five-minute absolute expiry and
burn-before-compare single use, the `X-Forwarded-For` refusal and IPv6 /64 masking rationale, and
the anonymous engagement state machine (pending → token → redemption → moderation queue) are all
present and accurate. `CaptchaChallenge`, the DTO that reaches the browser, carries no answer field.
