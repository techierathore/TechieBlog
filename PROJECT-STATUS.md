---
project: TechieBlog
stack: .NET 10 / Blazor Server / TrBlazeUI / PostgreSQL + Dapper + DbUp / Serilog / BlogApp MAUI (Windows)
last_updated: 2026-08-16
current_phase: Handoff — READY FOR UAT; 4 rows open, ALL owner-gated or not agent-observable
last_verified_build: PASS
last_verified_date: 2026-08-14
---

# TechieBlog — Status

## Where I am

**All agent-actionable REQs are terminal; awaiting UAT per `docs/TechieBlog-UsageGuide.md`.**
`*build-phase` (FIX) + a chained scoped `*verify` + `*handoff-phase`. Terminal **159 → 160**; open
**5 → 4**. Build GREEN rung #4 `0 Error(s)` 7/7; suite **1 482 → 1 490** (1 487 pass / 0 fail / 3 skip).
**REQ-NFR-041 CLOSED (`Verified`) — the three-occurrence regex-blindness cycle is over.** Pattern 2
was blind a third time (one underscore only). Both halves fixed: the pattern allows N underscores
plus generics, **and patterns 1-4 are gated at BUILD time** by `tests/unit/Ops/SourceConventionTests.cs`,
whose fifth Fact asserts each regex matches a synthetic violation — so a dead regex fails the build
instead of reading as a pass. Proved by **mutation test**: 3 violations injected into
`source/BlogModel/` failed 3 of 4 scans, each naming file, line, text.

**REQ-NFR-038 — five manual owner steps moved into `deploy.yml`** (`chown 1654`, a writability probe,
the DDL-rights probe as a *gate*, server-side GHCR login, a hard `/_framework/blazor.web.js` check),
**then the REAL VPS was inspected** via an owner-run write-free recon script. **Three would-be
first-deploy failures found and closed:** (1) the server was never authenticated to GHCR, so
`compose pull` would have been denied; (2) **PostgreSQL was not listening on `172.17.0.1`** — runbook
Step 7 edits 1-2 never applied, so the container could not have reached the database and `/healthz`
would have sat at 503; owner fixed it, `pg_isready` now answers `accepting connections`;
(3) **`SEQ_API_KEY` was required but no `seq` container exists** — now optional, `SeqUrl` rendered
blank, both branches tested. Also confirmed `ensure-db` makes `appuser` the DB **owner**, so on
PG 18.4 the §8 GRANTs are very unlikely to be needed. Stays `Implemented`: server *state* is known,
but no pipeline step has *executed* there.

**2026-08-16 — two of those findings CORRECTED, and the deploy docs merged into one file.**
(a) **Seq IS running** at `https://seq.techierathore.com` (Seq 2026.1.17083, same VPS IP as the
apex, same Caddy) — probed directly over HTTPS. The 2026-08-14 remedy therefore over-corrected:
blanking `SeqUrl` whenever `SEQ_API_KEY` was unset would have **silently disabled log shipping on
every deploy**, since nothing gave the owner a reason to set that secret. `SeqUrl` is now rendered
**unconditionally** (default `http://seq:5341`, at the time overridable by a repo **variable**
`SEQ_URL` — **that override was removed hours later, see 2026-08-16b**), and a new warning-only
deploy step *Report the Seq endpoint* observes on the server which address is right. `SEQ_API_KEY` stays optional and genuinely is: that Seq accepts anonymous ingestion
(`POST /ingest/clef` → 201; reads 401). All three render branches executed, `docker compose config`
valid. (b) **`appuser` DDL/database-creation rights owner-confirmed** — no longer an inference from
`ensure-db`'s text; the §8 gate stays as a guard, not an expected step. (c) **`docs/Server-Setup.md`
+ `.html` merged into `docs/Prod-Deploy-Checklist.md` §2 and DELETED** — two files described one
procedure and the short one had already drifted stale on both facts above.

**2026-08-16b — SERVER BUILD COMPLETE. Deploy docs realigned to runbook v5 + deployment-brief v3.**
The owner finished `docs/bluehost-vps-runbook-v5.md` end to end and its acceptance script
`sudo /srv/checkup.sh` (Part 9) reports **0 failed** — swap, ufw, fail2ban, cron, Postgres +
pgvector, Docker, the `web` network, Caddy valid, **`seq` container up and answering**, the whole
`/srv` tree, and backups landing on **both** OneDrive and Google Drive. The server is also **logged
in to GHCR** with a stored PAT. **Only the app container is left to deploy — that is CI's job.**
Consequences: the last open unknown (`http://seq:5341` — is the container named `seq` on `web`?) is
**closed**, so the `SEQ_URL` repo-variable override added earlier the same day was **removed** —
brief v3 §0 forbids the public Seq URL in app config, and it existed only while the address was
unconfirmed. `SeqUrl` is now the hardcoded contract constant. The GHCR credential check will now
always take its **leave-it-alone** branch, which is what the brief requires. `SEQ_API_KEY` was
promoted from "optional, skip it" to **§2 step 3** — one of the two genuinely manual per-app tasks.
**Two upstream contradictions found and adjudicated in the doc rather than silently picked:**
(1) runbook §6.3 names the secret `SEQ_APIKEY_BLOG`, brief §2 names it `SEQ_API_KEY` — **this repo
follows the brief** (the workflow reads `SEQ_API_KEY`; a mis-named secret fails silently, so it is
called out three times); (2) brief §0 mandates a **root** container with no `USER` directive, while
this repo runs **non-root UID 1654** — **deviation kept deliberately**, because the pipeline already
chowns the bind mounts to 1654 and probes them, which is exactly the premise the brief's rule
assumes absent. Both are documented in Prod-Deploy-Checklist §0 with the reasoning and the exact
change needed to switch. `sudo /srv/checkup.sh` added to §7 (post-deploy) and §11 (routine ops).
Evidence: `deploy.yml` parses (3 jobs / 21 steps), 14/14 shell blocks pass `bash -n`, the render
step **executed** in both Seq branches → `SeqUrl: "http://seq:5341"` in each, 237 lines, zero
surviving placeholders, `docker compose config` **valid**; HTML re-rendered, **58/58 anchors
resolve**, no unbalanced tags.

**2026-08-16c — `ciuser` CI account (runbook v5.2 / brief v3.2). THREE DEFECTS FOUND, all in the
pipeline, all fixed and measured.** The owner created a dedicated CI account and supplied the two
updated specs. Auditing this pipeline against them found: **(1) THE GHCR CREDENTIAL DOES NOT COVER
`ciuser` — this would have failed the first deploy.** Brief v3.2 §0 still asserts "the server is
already authenticated… the workflow must not run `docker login` over SSH", but that answer predates
the account change: registry logins are **per-user** (`~/.docker/config.json`), the `docker` group
grants daemon access and *nothing* at the registry, and runbook Step 12 ran as `ravi` before
`ciuser` existed. So `docker compose pull` of a private image as `ciuser` would fail `unauthorized`.
**The deploy survives only because the ephemeral-login fallback was deliberately retained on
2026-08-16b** — it is now the LIVE path, not a dormant one. Documented, with the one-command fix
(`sudo -u ciuser -H … docker login`) that restores spec compliance. **(2) The ownership fix would
have cut `ciuser` out of its own directories.** `chown -R 1654:1654` replaces group `webops` with
`1654`, silently breaking §8's documented `rsync` uploads-migration path, which connects as
`VPS_USER` (= `ciuser`). Now `chown -R 1654` (owner only) + `chmod g+ws` on the directory, restoring
the setgid/group-write model runbook v5.2 §2.1 sets on `/srv`. **Measured on both a Debian base (the
app image) and Alpine:** mode `drwxrwsr-x`, owner 1654, group unchanged; container UID 1654 writes;
a `webops` user writes; setgid inheritance confirmed; idempotent over 3 runs; parent untouched;
files stay `-rw-r--r--`. **(3) A silent-false-pass removed:** the old `find … ! -uid 1654 | wc -l`
guard reports "nothing to do" on any BusyBox/musl base — `-uid` is GNU-only, and a failed `find`
piped to `wc -l` yields `0`, which `set -e` never sees. Reproduced on `alpine:latest`; latent today
(Debian image), gone now. **Two quoting bugs in the new inline comments were caught by the
`bash -n` gate before they shipped** — one stray apostrophe inside the `sh -c '…'` inner script
(step dies `syntax error near unexpected token done`), then an *even* number of them, which
re-balances so `bash -n` passes while exposing the enclosed text to the outer shell. An
editors-note now states the rule in the file. **Compliance audit:** the workflow executes exactly
one `sudo` — `/usr/local/bin/ensure-db`, the only command in ciuser's allowlist — uses no
`sudo docker`, and never hardcodes the SSH username. **Evidence:** `deploy.yml` parses (21 steps);
**14/14** blocks pass `bash -n`; 0 apostrophes in the inner script; render executed → 245 lines,
zero placeholders, `docker compose config` VALID; checklist HTML **60/60 anchors**, no unbalanced
tags. Zero files under `source/` touched.

## Next command to run

```
Manual UAT per docs/TechieBlog-UsageGuide.md smoke checklist.
```
Handoff ran 2026-08-14; the UAT bundle is current. Set `current_phase: Released` yourself once UAT
passes. Nothing remains that an agent can build or verify.

## Open requirements

- [ ] **Implemented — not agent-observable (1)** — REQ-NFR-038 *(needs a real VPS)*
- [ ] **PARTIAL (2)** — REQ-NFR-017 *(owner: CI repo secret)*, REQ-NFR-026 *(stage 4 deferred)*
- [ ] **In Progress (1)** — REQ-NFR-025 *(owner: git-history decision)*

Counts: 164 rows. Terminal 160 (148 `Verified` + 4 `N/A` + 8 `N/A removed`); open 4 — all owner-gated.

## Known blockers

- **⚠⚠ OWNER — ROTATE A LEAKED PAT, TODAY.** A live TrBlazeUI PAT sits in **clear text** at
  `C:\Users\srkra\AppData\Roaming\NuGet\NuGet.Config` and was exposed in a 2026-08-14 transcript. Not
  used after discovery. Rotate, then re-store.
- **⚠ OWNER — six GitHub items gate a real deploy:** `TrBlazeUiPackagesToken`, `JWT_SIGNING_KEY`,
  `APP_ENCRYPTION_KEY`, `ANALYTICS_VISITOR_SALT`, **the per-app `SEQ_API_KEY`** (created in the Seq
  UI — note the name, NOT runbook §6.3's `SEQ_APIKEY_BLOG`) — **owner reports all secrets added
  2026-08-16**. DNS A records are **already live** (apex + www → 50.6.45.249, verified). Server-side
  steps (ownership, DDL probe, GHCR auth) need nothing from you. See `docs/Prod-Deploy-Checklist.md`
  §2 — now the ONLY deploy setup doc; `docs/Server-Setup.md` was merged into it and deleted
  2026-08-16.
- **⚠ OWNER — OPTIONAL BUT RECOMMENDED: give `ciuser` its own GHCR login.** Registry credentials are
  per-user; runbook Step 12 logged in as `ravi`, so `ciuser` has none and every pull currently relies
  on the pipeline's ephemeral `GITHUB_TOKEN` login. It works, but it ties each pull to
  `packages: read` on the workflow. One command on the server as `ravi`:
  `sudo -u ciuser -H bash -c 'echo <PAT> | docker login ghcr.io -u techierathore --password-stdin'`
- **`deploy.yml` triggers on `main` only** — one production branch, no second name to keep in sync.
  **Correction 2026-08-16:** an intermediate revision claimed this repo used `master` and widened the
  trigger to both names; that came from a stale session snapshot, not the repository. Owner confirmed
  `main` and removed the fallback. No deploy was ever at risk — both names were listed at the time —
  but the written instructions pointed at the wrong branch for part of a day.
- **✅ The VPS build is COMPLETE (2026-08-16)** — the whole of `docs/bluehost-vps-runbook-v5.md`,
  confirmed by its own acceptance script `sudo /srv/checkup.sh` reporting **0 failed**. The earlier
  "runbook was NOT fully applied" warning is retired. Historical note worth keeping: two recon
  conclusions from 2026-08-14 had to be reversed (Postgres reachability, and "no Seq container") —
  **do not treat a one-off recon as ground truth when a repeatable acceptance script exists.**
  Verified state: `docs/Prod-Deploy-Checklist.md` §0.
- **⚠ `/srv/caddy/sites/techieblog.caddy` already exists and is AUTHORITATIVE** — the pipeline is
  create-only and will never replace it. Content checked and correct; it just lacks the repo
  version's compression directive.
- **⚠ A green deploy is still not proof** — every probe has been green while the site was broken three
  times. Two classes are now gated (`_framework` 404, empty schema); the Caddy hop is not.
- **⚠ `dotnet run` ignores `ASPNETCORE_ENVIRONMENT`** unless `--no-launch-profile` is passed.
  **`CLAUDE.md` says "No TrBlazeUI" — stale**; 2.0.2 is referenced by three projects.
- **⚠ OWNER — dead PAT still in git history.** Revoked (401). Recommendation: accept the history.
- **⚠ Admin WCAG rests on a workaround** (TR-054/063/064); no screen-reader pass ever run. *(The
  `mustchangepassword` contradiction is RESOLVED — dev-DB drift, not a doc error. See UsageGuide.)*

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-02…08-06 | Day-1 docs, amendment, mockups | Docs only — BRD-92…97, 38 screens | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-07…08-08 | *build-phase x2 + build repair | 383 tests; PASS 7/7 · no verifier run | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-09 | *verify all* (first executed run) | 131 graded · 98 Verified, 17 FAIL, 8 Needs re-verify | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-09…08-10 | *build-phase FIX + *build-phase | 1055 → 1291 tests · coverage 82.5% | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-11 | *build-phase + local Docker verification | 1355 tests · BLOCKING `_framework` defect found and fixed | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-11 | *build-phase (7 clusters) + chained *verify all* | **51 rows graded: 45 Verified** · terminal 108 → 149 | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-14 | *build-phase (REQ-FN-061) + chained *verify* | 1411 → 1482 tests · **11 graded, 9 → `Verified`** · REQ-NFR-041 raised · terminal 149 → 158 | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-14 | *verify* REQ-NFR-001/038/041 (scoped) | **REQ-NFR-001 → `Verified`** (both perf budgets, 0 errors in 30 000 req @ c100); REQ-NFR-041 → `Needs re-verify` (pattern 2 blind, 3rd time) · ⚠ leaked PAT found · terminal 158 → 159 | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-14 | ***handoff-phase*** (owner-invoked; a prior handoff ran 2026-08-11) | **READY FOR UAT.** UsageGuide finalised: test-user table **reconciled against the live DB** (all 4 exist, ids 1-4), and the long-open `MustChangePassword` contradiction RESOLVED — `019-SampleData.sql` seeds users 2-4 `TRUE`, so the doc was right and the **dev DB had drifted** (re-arm step skipped after a smoke). Stale sections corrected against evidence: "solution does not build", "no tests / no CI", "reset tokens in memory", "seeded admin password is plaintext" were all false. DevGuide re-map skipped — **zero files under `source/` changed this session**. TrBlazeUI feedback re-confirmed, no new entries (next id TR-067) | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-14 | ***build-phase* FIX + chained *verify* REQ-NFR-041** | PASS 0 errors 7/7 · 1482 → **1490** tests · **REQ-NFR-041 → `Verified`** (pattern 2 fixed AND patterns 1-4 promoted to a build-time gate; mutation-tested by the verifier) · **REQ-NFR-038: 5 manual server steps moved into CI, then the REAL VPS inspected (owner-run read-only recon) — 3 would-be first-deploy failures found and closed: server-side GHCR auth, Postgres not listening on 172.17.0.1 (runbook Step 7 never applied), `SEQ_API_KEY` required with no Seq container** · deploy docs rewritten · terminal **159 → 160**, open **5 → 4** | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- **TrBlazeUI:** next free **TR-067**. Nothing new — this pass was app-side. Prior: TR-057…066.
  **TechieRag:** not used (no AI/RAG features).

## Standards compliance (2026-08-14)

- **All six enforcement checks are now gated by the build.** Patterns 1-4 joined 5-6 via
  `SourceConventionTests`; the exposure flagged on every prior pass is closed.
- **Source and tests clean**, each zero validated by a control in the same invocation: underscore
  fields 0 (4/4 synthetic, liveness **259**) · test-method underscores 0 (4/5 — correctly rejecting
  the non-violation; liveness **1 228**) · Hungarian 0 (2/2) · `a`/`v` 0 (both caught) · `ex.Message` 0.
- **Mutation-tested, not merely green** — 3 injected violations failed 3 of 4 scans; a passing suite
  alone cannot tell a live gate from a dead one. Run the doc greps with `command grep` (this shell
  aliases `grep` to `ugrep -G`).

## Deferred / future

- **The 137 rows `Verified` on 2026-08-11 were NOT re-confirmed** — a full `*verify all` re-sweep is owed.
- **No pipeline step has EXECUTED on the real VPS.** Server *state* is verified (§0); its behaviour
  under the pipeline is not — SSH-from-CI, GHCR push/pull, TLS issuance, the Caddy hop. §13 is the ledger.
- **Optional on the server:** delete `/srv/caddy/sites/techieblog.caddy` to adopt the repo's
  compression directive. Swap and Seq are both **done** — the whole runbook is complete and
  `checkup.sh` passes.
- **Not re-tested** — `/category/{slug}` featured-image `renders-empty`. **Not re-exercised** —
  REQ-NFR-005, REQ-NFR-023 (both keep `Verified` on unit coverage).
- **Never driven** — profile save (REQ-FN-011 update half, REQ-FN-053), newsletter Send, subscriber
  toggles, comment/rating/subscribe submits. **BlogApp (MAUI) is build-verified only** —
  REQ-UI-051/052, REQ-FN-046/047 have no runtime coverage.
- REQ-UI-059 needs a back-dated seed post; `newsletter` has 0 rows (REQ-UI-054 / REQ-FN-050
  unexercised); a real screen-reader pass; Firefox + WebKit for REQ-NFR-009; macOS BlogApp head.
- `.buildout/*/logs` ~133 MB of leftover agent logs (gitignored, safe to delete). Docker cleanup
  2026-08-14 reclaimed 18.05 GB; `WinPostgre` untouched.
