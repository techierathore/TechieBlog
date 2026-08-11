# TechieBlog — Production Deployment Checklist

**Everything you must set up on GitHub and on the VPS before `git push` deploys this blog, in the
order you should do it.** Written for the repository owner, not for an agent.

Once this checklist is complete, deployment is: **push to `main` → watch Actions go green → the site
is live.** Nothing else, ever, per deploy.

| | |
|---|---|
| App identity (`APP_NAME`) | `techieblog` |
| Public domain | `techierathore.com` (apex, plus a `www` → apex redirect) |
| Database | `techieblog` on the VPS's native Postgres, user `appuser`, no pgvector |
| Image | `ghcr.io/techierathore/techieblog:latest` and `:<short-sha>` |
| Compose file on the server | `/srv/apps/techieblog/docker-compose.yml` |
| Uploads on the server | `/srv/data/techieblog/uploads` → `/app/uploads` in the container |
| DataProtection keys | `/srv/data/techieblog/dp-keys` → `/home/app/.aspnet/DataProtection-Keys` |
| Health endpoint | `https://techierathore.com/healthz` |
| Container runs as | UID/GID **1654** (`app`) — matters for uploads ownership, §5 |
| Logs | stdout only — `docker logs techieblog`, plus Seq. No log files, no log bind mount. |
| Pipeline | `.github/workflows/deploy.yml` |

> **Read this before trusting any "verified" language below.** The pipeline files were validated
> offline — YAML parsing, `docker compose config`, `caddy validate`, the secret-guard script, and (as
> of 2026-08-11) a **complete image build and a running container** that was signed into and uploaded
> to through its real admin UI. **No part of this has run against the real VPS**, because there is no
> VPS reachable from the build environment, and the image build substituted a local package feed for
> the private one. §11 states exactly which parts are now proven, with their deviations, and which
> remain unproven.

## Table of Contents

1. [Before you start](#before-you-start)
2. [GitHub secrets](#github-secrets)
3. [NuGet / TrBlazeUI package feed authentication](#nuget-trblazeui-package-feed-authentication)
4. [DNS — the one manual infrastructure step](#dns-the-one-manual-infrastructure-step)
5. [Uploads and persistent storage](#uploads-and-persistent-storage)
   — including [5a. `ensure-db` privileges](#5a-ensure-db-privileges--verify-once-before-you-trust-the-first-deploy)
6. [First deploy — ordered runbook](#first-deploy-ordered-runbook)
7. [Verifying success](#verifying-success)
8. [Rolling back](#rolling-back)
9. [Routine operations](#routine-operations)
10. [Troubleshooting](#troubleshooting)
11. [What is NOT verified yet](#what-is-not-verified-yet)

---

## Before you start

The VPS-side prerequisites are already done (from the VPS runbook) and are **not** your job here:
Docker with the external `web` network, native Postgres 18 with the `ensure-db` helper, the `caddy`
container importing `/srv/caddy/sites/*.caddy`, the `seq` container, and the five org-level GitHub
secrets.

What is left is on this page, and it is roughly 20 minutes of clicking:

1. Add four new GitHub secrets (§2) — one of which needs a personal access token (§3).
2. Add two DNS records (§4).
3. Push to `main`, then fix the uploads directory ownership once (§5) and verify the database
   privileges once (§5a).
4. Add an UptimeRobot monitor (§7).

**Two things to decide before you generate any key** (§2 explains why): whether this deployment
starts from an **empty** database, or restores an **existing** TechieBlog database. If it restores an
existing one, you must reuse the *existing* `AppEncryptionKey` — a fresh one makes the stored SMTP
password and cloud storage key permanently undecryptable.

---

## GitHub secrets

Repository secrets live at **Settings → Secrets and variables → Actions → New repository secret**.
Organisation secrets live at the organisation's equivalent page and are inherited by this repository.

GitHub secret names may contain only letters, digits and underscores, so the three app secrets are
named in `SCREAMING_SNAKE_CASE` here even though the *configuration keys* they feed are PascalCase.
The workflow does the mapping; the middle column below shows it.

### The five that already exist (organisation level)

| Secret | Feeds | What it is | What breaks without it |
|--------|-------|-----------|------------------------|
| `VPS_HOST` | ssh/scp target | VPS hostname or IP | Every `deploy` step fails to connect. |
| `VPS_USER` | ssh/scp user | The deploy user (has passwordless `sudo` for `ensure-db` only) | Same. |
| `VPS_SSH_KEY` | ssh/scp key | Private half of the deploy key | Same. |
| `DB_PASSWORD` | `AppDbConString` | Password for the Postgres role `appuser` | Container starts, then fails every request; `/healthz` returns 503. |
| `SEQ_API_KEY` | `SeqApiKey` → `Seq:ApiKey` | Seq ingestion API key | Events reach Seq unauthenticated and are rejected if the Seq server requires a key. |

> **Seq works.** `Program.cs` registers `Serilog.Sinks.Seq` **conditionally on `Seq:Url` being
> non-blank** (`SeqSettings.Resolve`), and the compose file sets `SeqUrl: http://seq:5341`, so events
> ship to the shared Seq container on the internal `web` network from the first boot. Every event is
> enriched with the application name, so one Seq server can separate this app from the others on the
> VPS. Leaving `SeqUrl` blank is a supported state — console only, no connection errors — which is why
> a clone of this repository runs with no Seq anywhere.
>
> **Seq is the durable log store, because the container writes no log files.** `LogFileEnabled=false`
> in both the `Dockerfile` and the compose file: a rolling file sink inside a container writes into an
> ephemeral layer that the next redeploy discards. `docker logs techieblog` is the other copy, and it
> is a daemon-side buffer, **not** long-term storage.

### The four you must add now (repository level, all NEW)

| Secret | Feeds config key | What it is / how to obtain | What breaks without it |
|--------|------------------|----------------------------|------------------------|
| `JWT_SIGNING_KEY` | `JwtSigningKey` | The HMAC key session tokens are signed with. **≥ 32 characters.** Generate: `openssl rand -hex 32` | **The container refuses to start.** `AppSecrets.Initialise` (REQ-NFR-027) throws — there is deliberately no fallback default. |
| `APP_ENCRYPTION_KEY` | `AppEncryptionKey` | Passphrase that AES-encrypts stored credentials (SMTP password, cloud storage key) in Site Settings. **≥ 16 characters.** Generate: `openssl rand -hex 24` | **The container refuses to start** (same gate). |
| `ANALYTICS_VISITOR_SALT` | `Analytics:VisitorSalt` | Salt in `SHA-256(salt \| ip \| userAgent)`, the only thing making a stored visitor record pseudonymous. **≥ 32 characters**, and must not equal the built-in development salt. Generate: `openssl rand -hex 32` | **The container refuses to start.** `DeploymentConfiguration.Enforce` (REQ-NFR-030) throws outside Development. |
| `TrBlazeUiPackagesToken` | *(build only)* | Classic PAT with `read:packages`, for the private TrBlazeUI NuGet feed. Full steps in §3. **Name it exactly this — it is case-sensitive.** | **The image never builds.** `dotnet restore` fails `NU1301 / 403 Forbidden` inside the Docker build. |

`SiteSettings:BaseUrl` is **not** a secret — the compose template hard-codes `https://techierathore.com`.

### Key-rotation rules — read once, then never rotate casually

These three values are effectively **write-once for the life of the deployment**:

| Rotating… | Consequence |
|-----------|-------------|
| `JWT_SIGNING_KEY` | Every existing session cookie becomes invalid. Every signed-in user is silently signed out on their next request. Recoverable, but visible to everyone. |
| `APP_ENCRYPTION_KEY` | Every value already encrypted under the old key is **permanently undecryptable**: the SMTP password and cloud storage access key in Site Settings must be re-entered by hand. Worse, they *look* present in the admin UI while failing at use. |
| `ANALYTICS_VISITOR_SALT` | Every stored visitor pseudonym stops matching. Unique-view counts jump, de-duplication restarts from zero, and the old and new digests can never be reconciled. **Treat as write-once.** |

Store all three in a password manager the moment you generate them. They exist nowhere else — GitHub
will not show you a secret's value again after you save it.

---

## NuGet / TrBlazeUI package feed authentication

This blog depends on `TrBlazeUI.Components` and `TrBlazeUI.Icons.Lucide`, which are published to a
**private, user-scoped GitHub Packages feed** (`https://nuget.pkg.github.com/techierathore/index.json`).
Anonymous restore is impossible. Both CI (`.github/workflows/ci.yml`) and the deploy image build fail
without credentials, and the raw failure is ~60 lines of NuGet retry noise ending in an `NU1301` that
never names the real cause — which is why both workflows now run a one-line preflight probe first.

> **The token that used to be committed in `NuGet.Config` was invalidated by GitHub secret scanning
> on 2026-08-09 and CANNOT be reused** (REQ-NFR-025). Do not try to recover it, and never put a token
> back into `NuGet.Config` — that file is published to every clone and fork of the repository.

Do **one** of the following two remedies. They are alternatives, not steps.

### Remedy 1 — a classic PAT stored as `TrBlazeUiPackagesToken` (recommended)

1. Sign in to GitHub as **`techierathore`**.
2. Go to **Settings → Developer settings → Personal access tokens → Tokens (classic)**
   (`https://github.com/settings/tokens`).
3. Click **Generate new token → Generate new token (classic)**.
4. **Note:** `TechieBlog CI + deploy — TrBlazeUI packages`.
5. **Expiration:** pick a date you will actually remember, or *No expiration* if you accept the
   trade-off. An expired token reappears as the same `NU1301 / 403` failure.
6. **Select scopes:** tick **`read:packages`** only. Nothing else is needed — not `repo`, not
   `write:packages`. A fine-grained token will *not* work here; GitHub Packages for NuGet still
   requires a **classic** token.
7. Click **Generate token** and copy the value immediately (`ghp_…`). GitHub never shows it again.
8. In the **TechieBlog repository**, go to **Settings → Secrets and variables → Actions**.
9. Click **New repository secret**.
10. **Name:** `TrBlazeUiPackagesToken` — exactly this, character for character. Secret names are
    case-sensitive and both workflows reference this spelling.
11. **Secret:** paste the token. Click **Add secret**.

The workflows use the username `techierathore` together with this token. Nothing else needs changing.

### Remedy 2 — grant this repository package access, and use the built-in `GITHUB_TOKEN`

No PAT, no secret to rotate — but it must be repeated for **each** TrBlazeUI package.

1. Go to `https://github.com/users/techierathore/packages`.
2. Open the **`TrBlazeUI.Components`** package.
3. Click **Package settings** (right-hand side).
4. Scroll to **Manage Actions access**.
5. Click **Add repository**, choose **TechieBlog**, and set the role to **Read**.
6. Repeat steps 2–5 for **`TrBlazeUI.Icons.Lucide`** and any other TrBlazeUI package the solution
   references.

With this in place the built-in `GITHUB_TOKEN` is sufficient and the `TrBlazeUiPackagesToken` secret
can be left unset. Both workflows already fall back to `GITHUB_TOKEN` automatically (CI emits a
warning saying so; the deploy build passes it as the BuildKit secret instead).

### How the token reaches the build — and why it is not a build ARG

The image build receives the token as a **BuildKit secret**, id **`nuget_pat`**:

```yaml
      - name: Build and push
        uses: docker/build-push-action@v6
        with:
          secrets: |
            nuget_pat=${{ secrets.TrBlazeUiPackagesToken || secrets.GITHUB_TOKEN }}
```

The Dockerfile consumes it with `RUN --mount=type=secret,id=nuget_pat …`. A BuildKit secret is
mounted into a single layer's filesystem for the duration of that `RUN` and is never written into the
image. A **build `ARG` would be**: `docker history` prints every ARG value, so anyone who can pull the
public-ish image could read the PAT. That is why this pipeline never uses an ARG for it.

> **The two ids must be spelled identically.** Until 2026-08-10 the workflow sent
> `trblazeui_token` while the Dockerfile mounted `nuget_pat`. BuildKit does not treat that as an
> error — it simply mounts nothing, `/run/secrets/nuget_pat` is empty, the generated NuGet config
> gets no credentials, and the build dies deep inside `dotnet restore` with an `NU1301` that never
> mentions a secret. If you ever rename one half, rename the other in the same commit.

### Local development (for completeness)

Your own machine restores against the same feed using your **user-level** NuGet config, which lives
outside the repository:

```bash
dotnet nuget add source https://nuget.pkg.github.com/techierathore/index.json \
  --name TrBlazeUI \
  --username techierathore \
  --password <your PAT with read:packages> \
  --store-password-in-clear-text
```

That writes to `~/.nuget/NuGet/NuGet.Config` (Linux/macOS) or `%APPDATA%\NuGet\NuGet.Config`
(Windows) and merges with the repository's credential-free `NuGet.Config`.

---

## DNS — the one manual infrastructure step

The pipeline never touches DNS, by design. Add both records **before the first push**, because Caddy
provisions the Let's Encrypt certificate on the first request and cannot do so for a name that does
not resolve to the VPS.

| Type | Name | Value | TTL | Note |
|------|------|-------|-----|------|
| A | `@` | *your VPS IPv4 address* | Auto / 300 | The apex record for `techierathore.com`. If the domain is on Cloudflare: **DNS only — grey cloud.** |
| A | `www` | *your VPS IPv4 address* | Auto / 300 | Needed for the `www` → apex redirect in `deploy/techieblog.caddy`. A `CNAME www → techierathore.com` works equally well. **DNS only — grey cloud** if on Cloudflare. |

**Why grey cloud matters:** an orange-cloud (proxied) Cloudflare record terminates TLS at Cloudflare.
Caddy's `HTTP-01` certificate challenge then fails, and Cloudflare's proxy adds its own WebSocket and
buffering behaviour in front of a Blazor Server circuit. Keep both records DNS-only.

Verify before pushing — both must print the VPS IP:

```bash
dig +short techierathore.com
dig +short www.techierathore.com
```

---

## Uploads and persistent storage

**The rule: uploaded images must never live inside the container.** A container's filesystem is
discarded and recreated on every `docker compose pull && up -d`. An image written inside it survives
until the next deploy and then silently disappears — the post still references `/uploads/blog/foo.jpg`
and the browser shows a broken image.

### How it is wired

| Where | Path |
|-------|------|
| On the server (persistent, backed up) | `/srv/data/techieblog/uploads` |
| Inside the container | `/app/uploads` |
| On the wire | `https://techierathore.com/uploads/…` |

There is a **second** bind mount alongside it, for the DataProtection key ring — see
"The second bind mount" below.

The bind mount and the setting that points the app at it are both in
`deploy/docker-compose.template.yml`, deliberately next to each other:

```yaml
    environment:
      UploadsPath: "/app/uploads"

    volumes:
      - /srv/data/techieblog/uploads:/app/uploads
```

**Why `/app/uploads` and not `/app/wwwroot/uploads`.** `wwwroot` lives *inside the image*, so the old
web-root location put uploaded images on the same disposable layer as the binaries. `BlogEngine`'s
`UploadsLocation` now resolves both halves of the problem from the single `Uploads:Path` setting
(`UploadsPath` in the container):

- `Uploads:Path` **names the directory served at `/uploads`** — here `/app/uploads`.
- Because its last segment is already `uploads`, the **storage root** is its parent, `/app`.
  `BlogImageService` composes storage-relative paths of the form `uploads/<category>/<file>`, so a
  save lands in `/app/uploads/<category>/<file>` — inside the bind mount.
- `UploadsRootPath` is *always* `StorageRootPath + "/uploads"` by construction, so "written here,
  served from there" is not a state this can get into.

With `Uploads:Path` unset the old behaviour is unchanged — `wwwroot/uploads` — which is what a fresh
clone on a developer machine gets, with nothing to configure.

> **Leave the Site Settings → Storage → "Local root path" field EMPTY** in the admin UI. An explicit
> value there overrides the deployment default and points `LocalFileStorage` somewhere the
> static-file mapping does not cover — uploads would then succeed and be unservable.

### The second bind mount — the DataProtection key ring

Uploads are not the only state the container generates and would otherwise throw away. ASP.NET Core
creates a **DataProtection key ring** on first use, and with nothing mounted it writes it to
`~/.aspnet/DataProtection-Keys` *inside* the container. The runtime says so on every single start:

```text
[WRN] Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository
  Storing keys in a directory '/home/app/.aspnet/DataProtection-Keys' that may not be persisted
  outside of the container. Protected data will be unavailable when container is destroyed.
```

Every `docker compose pull && up -d` therefore mints a brand-new key. It is the same class of bug the
uploads mount exists to prevent, so it gets the same treatment:

```yaml
    volumes:
      - /srv/data/techieblog/uploads:/app/uploads
      - /srv/data/techieblog/dp-keys:/home/app/.aspnet/DataProtection-Keys
```

**What actually depends on it in this app — audited, so the severity is honest.** TechieBlog has *no*
ASP.NET Identity cookie, no `ITicketStore`, no `TicketDataFormat`, no session state, no `TempData`, no
OAuth correlation cookie and no `ProtectedBrowserStorage`. Sign-in is a JWT signed with
`JwtSigningKey` and resolved against the database by `SessionCookieAuthenticationHandler`, so **losing
the key ring does not sign anyone out** — the thing that does that is rotating `JwtSigningKey` (§2).
The two genuine consumers are framework-implicit and both live in the page-load handshake:

1. **Blazor Server component descriptors and the `StartCircuit` payload** — the `CfDJ8…` blobs
   embedded in prerendered HTML.
2. **The antiforgery token** `UseAntiforgery()` emits inside every prerendered `EditForm`.

Both only fail for a document that was rendered by the **outgoing** container and then talks to the
**incoming** one — a seconds-wide window per deploy, which a page reload clears. So this mount is
correctness hygiene rather than an outage fix: it removes the "Attempting to reconnect…" banner some
visitors catch mid-deploy and makes the behaviour deterministic. It becomes genuinely load-bearing the
moment a second replica or a real server-side form post is added, and it costs one line, so do it now.

### Directory creation and ownership — the one thing you must do by hand

The pipeline creates the directories on every run (`mkdir -p`), so **creation** is automated:

```bash
mkdir -p /srv/apps/techieblog
mkdir -p /srv/data/techieblog/uploads
mkdir -p /srv/data/techieblog/dp-keys
```

There is no logs directory. The container writes no log files (§2, "Seq works").

**Ownership is not automated, and it does need fixing.** The `Dockerfile` ends with
`USER $APP_UID`, and in the .NET 10 runtime image `$APP_UID` is **1654** — the `app` user those
images ship. `mkdir -p` on the server creates the host directories owned by `root`, which the
container user cannot write to, so every image upload fails with an access-denied error while the site
otherwise works perfectly.

Run this once, immediately after the first deploy:

```bash
docker exec techieblog id
# expected:  uid=1654(app) gid=1654(app) groups=1654(app)
sudo chown -R 1654:1654 /srv/data/techieblog/uploads
sudo chown -R 1654:1654 /srv/data/techieblog/dp-keys
```

(1654 was read out of the runtime image itself — `docker run --rm mcr.microsoft.com/dotnet/aspnet:10.0
id 1654` prints `uid=1654(app)`. Earlier drafts of this document said 64198, which is the UID some
older .NET images used; it is wrong for this one and the `chown` would have had no effect.)

> **This failure is INVISIBLE server-side — do not wait for a log line.** A root-owned uploads
> directory produces no `[ERR]`, no `[WRN]`, and no stack trace anywhere in `docker logs`. The
> container is `Up`, `/healthz` answers 200, and the startup line still reports the uploads directory
> as `configured: True` — because it *is* configured; it is merely not writable. The only signal is a
> generic red toast in the admin UI. **Probe for it instead of expecting to be told:**
>
> ```bash
> docker exec techieblog touch /app/uploads/probe   # silence = fine, "Permission denied" = chown it
> docker exec techieblog rm -f /app/uploads/probe
> ```
>
> §10 has the full symptom set. **No restart or redeploy is needed after the `chown`** — the next
> upload succeeds immediately.

Then confirm by uploading one image in the admin UI (**Admin → Images**) and reloading the page.

Confirm the key ring the same way: `ls -l /srv/data/techieblog/dp-keys` shows a `key-<guid>.xml` owned
`1654:1654`, that **same file** is still present after the next deploy, and the
"may not be persisted outside of the container" warning is gone from `docker logs techieblog`.

### 5a. `ensure-db` privileges — verify once, before you trust the first deploy

**DbUp creates its own `schemaversions` table** the first time the app boots, then runs every script
in `source/BlogDb/PostgresScripts`. That needs **DDL rights** on the `techieblog` database, not just
`CONNECT`. The pipeline runs `sudo /usr/local/bin/ensure-db techieblog`, which is idempotent — but
**what it grants `appuser` is not documented anywhere this repository can see, so this checklist will
not claim it is sufficient.** Check it yourself; it takes ten seconds.

```bash
# On the VPS. Connect as the application's own role, not as postgres.
psql "host=127.0.0.1 dbname=techieblog user=appuser password=<DB_PASSWORD>" \
  -c 'CREATE TABLE ensure_db_probe (id int); DROP TABLE ensure_db_probe;'
```

- **`CREATE TABLE` / `DROP TABLE` printed** → nothing to do. `ensure-db` granted enough.
- **`ERROR: permission denied for schema public`** (or similar) → run the grants below **as a
  superuser**, once:

```bash
sudo -u postgres psql -d techieblog <<'SQL'
GRANT ALL PRIVILEGES ON DATABASE techieblog TO appuser;
GRANT ALL ON SCHEMA public TO appuser;
ALTER SCHEMA public OWNER TO appuser;
SQL
```

> **The symptom if you skip this** is deceptively mild: the container starts, `docker compose up`
> reports success, and the site *answers* — but DbUp threw during startup, so there are no tables.
> Depending on where the failure lands you get either a crash-looping container or a site that is up
> and completely empty. `docker logs techieblog` names the failing script.

> **This step is no longer the only defence (REQ-NFR-039, 2026-08-11).** It used to be: `/healthz`
> only ran `SELECT 1`, which a database with no tables answers perfectly, so a *permissions* failure
> left the process alive, the probe at 200 and the `verify` job green — and a runbook step is not a
> gate. **`/healthz` now carries a third readiness check, `schema`**, which reads DbUp's
> `schemaversions` journal and compares it against the migration scripts the host was pointed at. A
> missing journal or a journal behind the script set makes `/healthz` return **503**, so the deploy
> goes red instead of shipping an empty site.
>
> Two consequences worth knowing before you read a failure:
>
> - The check names the cause. `schemaversions` absent and "journalled but behind" are reported
>   differently, and the behind case lists the outstanding script file names — that is the file to
>   look for in `docker logs techieblog`.
> - The expectation is **derived**, not hardcoded. It comes from the scripts folder itself, so
>   adding `026-…​.sql` extends what the gate demands with no code change. There is no constant to
>   bump and therefore no way for the gate to silently fall behind the migration set.
>
> **Still run the probe below.** The gate tells you *after* a deploy that migrations did not apply;
> this check tells you *before* the first one that they will.

### Migrating existing local images to the server

If you already have images under your development copy's `source/TechieBlog/wwwroot/uploads`, copy the
tree up **before or right after the first deploy** — the database rows reference these paths, so
without them every existing post shows broken images.

From the Windows machine (WSL), preserving the sub-folder structure the app writes
(`uploads/<category>/<file>`):

```bash
# 1. Look at what you have.
ls -R /mnt/c/1MyCode/TechieBlog/source/TechieBlog/wwwroot/uploads

# 2. Copy it up. Note the trailing slash on the source: it copies the CONTENTS.
rsync -av --progress \
  /mnt/c/1MyCode/TechieBlog/source/TechieBlog/wwwroot/uploads/ \
  <VPS_USER>@<VPS_HOST>:/srv/data/techieblog/uploads/

# 3. Re-apply ownership — rsync writes the new files as the SSH user, not as 1654.
ssh <VPS_USER>@<VPS_HOST> 'sudo chown -R 1654:1654 /srv/data/techieblog/uploads'
```

If `rsync` is not available on the server, `scp -r` works the same way. Copying while the site is
live is safe — the files are only ever read by the static-file handler.

### Backups

**Nothing to do.** Per the environment contract, `pg_dumpall` picks up the new `techieblog` database
automatically, and the `/srv/data` archive picks up `/srv/data/techieblog/uploads` and
`/srv/data/techieblog/dp-keys` automatically because that is precisely why they live there. If you
want to prove it, list the archive after the next scheduled run and confirm both folders appear.

---

## First deploy — ordered runbook

Do these in order. Steps 1–3 are the ones that make the difference between a green first run and a
confusing red one.

| # | Step | Where | Done when |
|---|------|-------|-----------|
| 1 | Add `TrBlazeUiPackagesToken` (or complete Remedy 2) — §3 | GitHub repo settings | Re-running CI gets past "Preflight — TrBlazeUI feed authentication" |
| 2 | Add `JWT_SIGNING_KEY`, `APP_ENCRYPTION_KEY`, `ANALYTICS_VISITOR_SALT` — §2 | GitHub repo settings | All three listed under Actions secrets |
| 3 | Add the two DNS A records — §4 | Registrar dashboard | `dig +short techierathore.com` returns the VPS IP |
| 4 | Confirm the org secrets are visible to this repo | GitHub org settings | `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `DB_PASSWORD`, `SEQ_API_KEY` all listed |
| 5 | Merge the deployment work to `main` and push | Your machine | Actions shows the **Deploy** workflow running |
| 6 | Watch `build` → `deploy` → `verify` | GitHub Actions | All three green |
| 7 | `chown -R 1654:1654` on **both** `/srv/data/techieblog/uploads` and `/srv/data/techieblog/dp-keys` — §5 | SSH | `docker exec techieblog touch /app/uploads/probe` is silent, and an image uploads and displays |
| 8 | Verify `appuser` has DDL rights — §5a | SSH | The `CREATE TABLE` probe succeeds |
| 9 | Migrate existing images if you have any — §5 | SSH / rsync | Old posts render their images |
| 10 | Add the UptimeRobot monitor — §7 | UptimeRobot | Monitor shows "Up" |

**What the pipeline does on push, in order** (`.github/workflows/deploy.yml`):

```text
build   preflight feed probe -> buildx -> GHCR login
        -> docker build (BuildKit secret nuget_pat) -> push :latest and :<sha7>
deploy  check every secret is present and long enough
        -> envsubst renders deploy/docker-compose.template.yml
        -> assert no placeholder survived
        -> mkdir -p /srv/apps/techieblog, /srv/data/techieblog/{uploads,dp-keys}
        -> scp rendered compose to /srv/apps/techieblog/docker-compose.yml
        -> Caddy snippet: install ONLY if /srv/caddy/sites/techieblog.caddy is absent, then reload
        -> sudo /usr/local/bin/ensure-db techieblog
        -> docker compose pull && docker compose up -d && docker image prune -f
verify  sleep 20 -> curl https://techierathore.com/healthz, 3 attempts 10s apart, fail on no 200
```

Everything is idempotent: the hundredth run does the same thing as the first.

---

## Verifying success

**1. The pipeline is green.** All three jobs. A green `verify` means the whole chain worked —
DNS resolved, Caddy answered, TLS was valid, the app started, and it returned 200. That is a
genuinely reachable site, not a guess.

**2. Check the endpoints by hand:**

```bash
curl -i https://techierathore.com/healthz       # 200 -> process AND database are healthy
curl -i https://techierathore.com/health        # 200, liveness only
curl -i https://techierathore.com/health/ready  # 200 -> same checks as /healthz
curl -I https://www.techierathore.com           # 308 redirect to https://techierathore.com/
curl -I https://techierathore.com/              # 200, the blog home page
```

> **`/healthz` is the deployment probe.** `Program.cs` maps it at
> `DeploymentHealthProbe.Path` with the readiness predicate, `AllowAnonymous()` and
> `DisableRateLimiting()`, so it needs no credential and a monitor polling every minute is never
> 429'd. It carries the **PostgreSQL check**, so 200 means the database answered and **503** means it
> did not — both directions were exercised in test. That is the whole point of using it as the gate: a
> probe that stayed green while the database was unreachable would report a broken deploy as a good
> one.
>
> `/health` (liveness — answers while the process is alive) and `/health/ready` are still mapped and
> still work; `/healthz` is an alias of the readiness set, not a rename. Use `/health` by hand when
> you want to know whether the *process* is up independently of the database.

**3. Click through the running site.** Open `https://techierathore.com`, then interact — open a post,
use the search box, sign in. **Interaction is the real test**: TechieBlog is Blazor *Server*, so
everything after the first render travels over a SignalR WebSocket. A site that renders and then
ignores every click has a proxy problem, not an app problem (see §10).

**4. Add the uptime monitor.** UptimeRobot → **Add New Monitor** → type **HTTP(s)** → URL
`https://techierathore.com/healthz` → interval 5 minutes → alert after 2 consecutive failures. This
is the same URL the pipeline's `verify` job probes — one URL, one meaning.

---

## Rolling back

**Normal rollback — revert the commit:**

```bash
git revert <bad-commit-sha>
git push origin main
```

The pipeline rebuilds the previous code and redeploys it. This is the preferred route because the
repository stays the source of truth.

**Emergency rollback — pin the image tag on the server.** Use when the site is down and you need it up
in 60 seconds, without waiting for a build:

```bash
ssh <VPS_USER>@<VPS_HOST>

# 1. Find a good image tag. Every deploy pushed one named after its commit.
docker images ghcr.io/techierathore/techieblog

# 2. Point the compose file at it.
sudo nano /srv/apps/techieblog/docker-compose.yml
#    change:  image: ghcr.io/techierathore/techieblog:latest
#    to:      image: ghcr.io/techierathore/techieblog:<good-sha7>

# 3. Bring it up.
cd /srv/apps/techieblog && docker compose up -d
```

> **This is temporary by construction.** The next push overwrites
> `/srv/apps/techieblog/docker-compose.yml` from the template and restores `:latest`. Follow an
> emergency pin with a real `git revert` as soon as you can, or the next unrelated push will silently
> roll you forward onto the broken image again.

**What rollback does *not* undo:** database migrations. DbUp applies `source/BlogDb/PostgresScripts`
forward-only on every boot; reverting the code does not un-apply a schema change. If a release
included a destructive migration, restore the database from the `pg_dumpall` backup as well.

---

## Routine operations

**Changing a secret.** Update it in GitHub, then push any commit to `main` (or run the workflow
manually from the Actions tab — it has `workflow_dispatch`). The compose file is re-rendered and the
container restarts with the new value. Re-read the rotation warnings in §2 first.

**Changing the site's routing / Caddy config.** The pipeline installs
`/srv/caddy/sites/techieblog.caddy` **only when it does not already exist**, and never overwrites it.
That is deliberate: manual server edits are authoritative. To change routing:

```bash
sudo nano /srv/caddy/sites/techieblog.caddy
docker exec caddy caddy reload --config /etc/caddy/Caddyfile
```

To *reset* it to the repository version, delete the server copy and push — the next deploy recreates
it from `deploy/techieblog.caddy` and reloads Caddy.

**Reading logs.** The container writes **no log files** — `LogFileEnabled=false` in the `Dockerfile`
and in the compose file, because a rolling file sink inside a container writes into the ephemeral
layer that the next redeploy discards. There are exactly two places to look:

```bash
docker logs -f techieblog                       # stdout — startup gates print here
docker compose -f /srv/apps/techieblog/docker-compose.yml ps
```

…and **Seq**, which is the durable copy: filter on the application-name property the logger enriches
every event with. `docker logs` is a daemon-side buffer subject to the log driver's rotation — treat
it as "what happened in the last little while", **never** as an archive. If an incident needs
history, it needs Seq.

**Manual restart.**

```bash
cd /srv/apps/techieblog && docker compose restart
```

---

## Troubleshooting

### `NU1301` / `403 Forbidden` during restore — the image will not build

**Symptom.** The `build` job fails at "Preflight — TrBlazeUI feed authentication" with a boxed message
naming HTTP 401 or 403, or (if the preflight is bypassed) `docker build` fails deep inside `dotnet
restore` with:

```text
error NU1301: Unable to load the service index for source
https://nuget.pkg.github.com/techierathore/index.json.
```

**Cause.** No usable credential for the private TrBlazeUI feed.

**Fix.** §3, either remedy. Then re-run the workflow. Most common specific causes, in order:

- The secret is spelled `TRBLAZEUI_PACKAGES_TOKEN` or `TrBlazeUIPackagesToken`. It must be
  **`TrBlazeUiPackagesToken`**.
- The PAT is *fine-grained*. GitHub Packages for NuGet requires a **classic** token.
- The PAT lacks `read:packages`, or has expired.
- You are relying on `GITHUB_TOKEN` without having done Remedy 2 for **every** TrBlazeUI package.

### The container will not start: `SiteSettings:BaseUrl` / `Analytics:VisitorSalt`

**Symptom.** `deploy` goes green (compose brought the container "up"), then `verify` fails with no
200. `docker logs techieblog` shows the process exiting immediately, repeatedly, with an
`InvalidOperationException` shaped like this:

```text
Unhandled exception. System.InvalidOperationException: Deployment configuration is invalid for
environment 'Production'. Fix the following and restart:
  * SiteSettings:BaseUrl is not configured. Every unsubscribe, verification and feed link is built
    from it, so mail sent without it cannot be acted on. ...
  * Analytics:VisitorSalt is not configured, so visitor digests would fall back to the built-in
    development salt. ... TREAT IT AS WRITE-ONCE — rotating it resets every stored visitor pseudonym.
```

**Cause.** `DeploymentConfiguration.Enforce` (REQ-NFR-030) refuses to start any non-Development host
when either value is absent, blank, loopback (`localhost`, `127.0.0.1`, `[::1]`, `0.0.0.0`),
unparseable as a URL, equal to the built-in development salt, or — for the salt — shorter than 32
characters. This is intended: both settings fail *silently* at runtime otherwise.

**Fix.** Confirm what actually reached the container:

```bash
docker exec techieblog printenv | grep -Ei 'SiteSettings|Analytics|JwtSigningKey|AppEncryptionKey'
```

- If the values are **absent**: `ANALYTICS_VISITOR_SALT` is missing from GitHub secrets. Add it (§2)
  and re-run. (The workflow now pre-checks this, so an absent secret should fail in `deploy` with a
  named error before it ever reaches the server.)
- If the values are **present but the app still complains**: this is an environment-variable naming
  mismatch. The compose file uses the **PascalCase** spellings (`SiteSettingsBaseUrl`,
  `AnalyticsVisitorSalt`, `SeqUrl`, `UploadsPath`, …), which `AppEnvironmentVariables` translates to
  the `:`-nested paths the app reads. That provider is registered in `Program.cs` and added **last**,
  so PascalCase outranks both the JSON files and the framework's `__` form. If a name is set and the
  gate still fires, the name is not in the map: open
  `source/TechieBlog/Configuration/AppEnvironmentVariables.cs` and compare its `Map` table — **that
  table is the contract** — against the comment block at the top of
  `deploy/docker-compose.template.yml`. A variable not in `Map` is ignored by design, so that an
  unrelated CI token can never become application configuration.
- The two `ForwardedHeaders__…` entries are the deliberate exception: they stay double-underscore
  because `KnownNetworks` is a configuration *array* and an array index has no PascalCase spelling.
  The framework's provider handles them.

The sibling failure has the same shape and the same fix, from `AppSecrets.Initialise` (REQ-NFR-027),
when `JwtSigningKey` or `AppEncryptionKey` is missing or too short.

### The site renders once, then every click does nothing

**Symptom.** The home page loads and looks right. Clicking a link, typing in search, or signing in
does nothing. After a few seconds a "Attempting to reconnect to the server…" overlay appears, or the
page just sits there. The browser console shows a failed WebSocket to `/_blazor`.

**Cause.** TechieBlog is **Blazor Server**: after the first render, every interaction travels over a
long-lived SignalR WebSocket. Something between the browser and Kestrel is not passing the upgrade
through.

**Fix, in order of likelihood:**

1. **Cloudflare is proxying (orange cloud).** Set both DNS records to **DNS only / grey cloud** (§4).
   This is by far the most common cause on an apex domain.
2. **The Caddy snippet was hand-edited** and lost the plain `reverse_proxy`. Caddy v2's `reverse_proxy`
   handles the WebSocket upgrade *transparently with no extra directives* — there is no equivalent of
   nginx's `proxy_http_version 1.1` + `Upgrade`/`Connection` headers to add, so if someone "fixed" it
   by adding directives, revert to the repository version:

   ```bash
   cat /srv/caddy/sites/techieblog.caddy   # compare with deploy/techieblog.caddy
   ```

3. **Compression was applied to the circuit.** The shipped snippet compresses everything *except*
   `/_blazor*` for exactly this reason. If the exclusion was removed, restore it.
4. **A corporate proxy or browser extension** on the client side is blocking WebSockets. Blazor falls
   back to long polling, which is slow but functional; if the site is unusable only on one network,
   this is why.

**Before blaming any of those four, check cause zero — the image itself.** See the next entry. If
`/_framework/blazor.web.js` 404s, the proxy is innocent: the browser never received the script that
would have opened the WebSocket in the first place.

### Nothing is clickable and `/_framework/blazor.web.js` returns 404 — a Dockerfile defect

**This one is written down because the site passes every automated check while being unusable**, and
because it is invisible to anyone who only looks at the deploy pipeline.

**Symptom.** Identical to the entry above from a user's point of view — the page renders, nothing
responds — but with one decisive difference:

```bash
curl -o /dev/null -w '%{http_code}\n' https://techierathore.com/_framework/blazor.web.js   # 404
```

Meanwhile `/healthz` returns 200, `/` returns 200, the container is `Up`, the pipeline is green and
the logs are clean. `docker exec techieblog ls /app/wwwroot` shows **no `_framework` directory**.

**Cause.** A `Dockerfile` in which `dotnet restore` runs while **only the `.csproj` files exist** (the
layer-caching trick) and the later `dotnet publish` uses `--no-restore`. That restore writes an `obj/`
state with no Blazor framework static web assets, publish faithfully reuses it, and the published
`wwwroot` ships without `_framework`. `App.razor`'s `@Assets["_framework/blazor.web.js"]` then emits a
URL that 404s, so `blazor.web.js` never loads, the circuit never starts, and **every** interaction on
the site is dead. It is **not** caused by `--no-restore` itself.

**Confirm which image you have.** The published route count is the cheap fingerprint:

```bash
docker exec techieblog sh -c 'grep -o "\"Route\"" /app/TechieBlog.staticwebassets.endpoints.json | wc -l'
# 606 = healthy.   586 = the broken build (no _framework).
docker exec techieblog ls /app/wwwroot/_framework/blazor.web.js
```

**Fix.** The `Dockerfile` in this repository was corrected on **2026-08-11**: it now runs a **second
`dotnet restore`** immediately after `COPY source/ source/`, keeping the BuildKit secret mount and
keeping `--no-restore` on the publish. Packages are already warm from the first restore, so it adds
seconds and no downloads. Rebuild and redeploy from the current `Dockerfile`.

**Do not delete that second restore as redundant** — it is commented in the `Dockerfile` for exactly
this reason. Removing it silently reintroduces a site that builds green, starts, answers `/healthz`
with 200 and cannot be clicked. Both states were measured back to back on the same source tree:
586 routes and a 404 without it, 606 routes and a working circuit with it.

### `verify` fails but the site works in a browser

Usually DNS propagation: the GitHub runner resolved the old record. Re-run the job. If it fails again,
check for an IPv6 `AAAA` record pointing somewhere else — `curl` on the runner may prefer it.

### `ensure-db` fails

The deploy user's passwordless `sudo` entry is scoped to `/usr/local/bin/ensure-db`. A "sudo: a
password is required" error means the sudoers entry is missing or the helper moved. This is VPS
runbook territory, not app configuration.

### The site is up but completely empty — no posts, no admin, no tables

**Symptom.** `docker logs techieblog` shows a DbUp failure — commonly `permission denied for schema
public` or a failure creating `schemaversions` — and the site has no content.

**Since REQ-NFR-039 the deploy tells you this itself.** `/healthz` returns **503** with a `schema`
check that names the cause, so the `verify` job fails rather than reporting success over an empty
site. Read the body:

```bash
curl -s https://techierathore.com/healthz | jq '.checks[] | select(.name=="schema")'
```

Two distinguishable descriptions, because the fixes differ:

| Description says | Means | Fix |
|---|---|---|
| `'schemaversions' journal table does not exist` | DbUp never got as far as creating its journal — almost always missing DDL rights | §5a — run the `CREATE TABLE` probe and apply the `GRANT`s |
| `journal is BEHIND the migration set: N of M scripts …` | DbUp ran and a **specific** script failed; the outstanding file names are listed | `docker logs techieblog` and search for the named script; fix the script or the grant, then restart |

**Cause of the first.** `appuser` lacks DDL rights on the `techieblog` database. DbUp creates its
own `schemaversions` table and then applies `source/BlogDb/PostgresScripts`; `CONNECT` alone is not
enough.

**Fix.** §5a — run the `CREATE TABLE` probe, and apply the `GRANT`s there if it fails. The host does
not need a rebuild afterwards, only a restart: DbUp re-runs at every startup and the journal check
re-evaluates per request, so `/healthz` goes green as soon as the scripts apply.

> **Historical note.** Before 2026-08-11 this section could only say "`/healthz` *may* even answer".
> It did: the readiness check tested the connection, not the schema, so the single most damaging
> deployment failure in this project was also the quietest one. That is closed.

### Uploads fail with a generic error and nothing in the logs

**Symptom.** Everything looks healthy and nothing anywhere tells you what is wrong:

- `docker ps` shows the container **Up**;
- `curl -i https://techierathore.com/healthz` returns **200**;
- the startup log is entirely normal and even reports the uploads directory as **`configured: True`**;
- there are **zero `[ERR]` and zero `[WRN]` lines** in `docker logs techieblog` — before, during or
  after the failed upload;
- the only evidence anywhere is a red toast in the admin UI:

  ```text
  Upload failed — An error occurred while uploading the file. Please try again.
  ```

**Cause.** The uploads directory on the host is still **root-owned**. `mkdir -p` in the pipeline
created `/srv/data/techieblog/uploads` as `root`, the container runs as UID **1654**, and a non-root
process cannot write into it. `configured: True` is not a contradiction — the path *is* configured
correctly; it simply is not writable, and nothing in the pipeline checks that.

**Diagnose** — one command, and it is the only one that gives a straight answer:

```bash
docker exec techieblog touch /app/uploads/probe
# touch: cannot touch '/app/uploads/probe': Permission denied
```

Silence means the mount is writable and the problem is elsewhere (see the next entry). "Permission
denied" is the confirmation.

**Fix.**

```bash
sudo chown -R 1654:1654 /srv/data/techieblog/uploads
```

**No restart and no redeploy are needed.** The permission is evaluated per write, so the very next
upload succeeds — re-try it in the admin UI and it works immediately.

> **UPDATE 2026-08-11 (REQ-NFR-040) — the failure is no longer silent.** The symptom above described
> the build up to 2026-08-10: `BlogImageService` caught the access-denied exception without logging
> it, so there was no log line, no non-200 response, no failed health check and no signal any
> monitor could see. **A refused upload now emits exactly one `[ERR]` line**, naming the storage
> provider, the target path, the uploading user and the underlying exception — whose own text
> carries the absolute server path:
>
> ```text
> [12:04:31 ERR] BlogEngine.Services.BlogImageService a1b2c3d4
>   Upload REFUSED: Local storage cannot write uploads/blog/blog-1-20260811120431-9f3c2e11.png for
>   user 1. The upload location is not writable by the account this process runs as — check the
>   directory's ownership and mode
> System.UnauthorizedAccessException: Access to the path '/app/uploads/blog/…' is denied.
> ```
>
> And the admin no longer sees the generic retry toast. A permissions refusal now reads **"The server
> cannot write to its upload location… Retrying will not help — the uploads directory needs to be
> made writable by the application"**, which is deliberately distinct from the message for a
> transient I/O failure, so nobody retries forever against a directory that will never become
> writable. The message names the *class* of problem without disclosing the server path or the
> exception text; both of those live in the log (REQ-NFR-033).
>
> `docker logs techieblog 2>&1 | grep 'Upload REFUSED'` is now the fastest diagnosis. The `touch`
> probe above remains the way to detect the condition **before** anyone tries to upload — and the
> same root-owned-`mkdir -p` hazard still applies to `/srv/data/techieblog/dp-keys`, which has no
> equivalent log line.

### Uploads succeed but the images 404

Check, in this order:

1. **The bind mount.** `docker inspect techieblog --format '{{json .Mounts}}'` must show
   `/srv/data/techieblog/uploads` → **`/app/uploads`**. A destination of `/app/wwwroot/uploads` is a
   stale compose file from before 2026-08-10 — re-run the deploy so the template is re-rendered.
2. **`UploadsPath`.** `docker exec techieblog printenv UploadsPath` must print `/app/uploads`. The
   startup log line reports the resolved uploads directory and whether it came from configuration; if
   it says it did not, the value never reached the container.
3. **Site Settings → Storage → Local root path** has been set to a directory outside the served
   tree. Empty that field (§5) — an explicit administrator value overrides the deployment default.

---

## What is NOT verified yet

Honest scope statement. The following **were actually executed** while these files were written:

- Every YAML file parses (`deploy.yml`, `ci.yml`, the compose template).
- The compose template renders through `envsubst` with dummy values leaving **zero** unsubstituted
  placeholders, and the rendered file passes `docker compose config` (services, the environment list,
  the single uploads bind mount, the external `web` network, `mem_limit` 512m → 536870912 bytes).
- The secret-presence and minimum-length guard was run in four scenarios: all present (renders), a
  missing secret (fails, names it), a too-short salt (fails, names the length), and bare `envsubst`
  with a variable unset — which **exits 0 and emits an empty value**, proving why the guard is needed.
- `deploy/techieblog.caddy` passes `caddy validate --adapter caddyfile` in the official `caddy:2`
  image ("Valid configuration"). *(That file is unchanged since; it was not re-validated.)*
- **A real `docker build` was run** with `--secret id=nuget_pat` and a dummy token. It restored from
  nuget.org, reached the private feed, and failed with `NU1301 / 401 Unauthorized` on
  `TrBlazeUI.Components` — the correct outcome for an invalid token, and proof that the secret is
  presented under the id the Dockerfile mounts.
- **The secret-id handshake was proved directly**, with a two-instruction probe image: built with
  `--secret id=nuget_pat`, `/run/secrets/nuget_pat` held the exact token (byte count and SHA-256
  prefix matched the source file); built with the old `--secret id=trblazeui_token`, the same mount
  was **silently empty**. That silent emptiness is the failure this reconciliation removed.
- **The container UID was read out of the runtime image**: `$APP_UID` is `1654`, and `id 1654`
  resolves to `uid=1654(app) gid=1654(app) groups=1654(app)`.

### Newly PROVEN on 2026-08-11 — three rows moved up from the unverified table

These were predictions when this document was first written. They are observations now. The
deviations from a true production run are stated with each one; read them, because two of the three
are not unconditional.

**1. Uploads actually writing through the bind mount — PROVEN, in both directions.**
A real PNG was uploaded through the real `/admin/images` UI of a **running container** and landed on
the host bind mount owned `1654:1654`, byte-identical to the source (md5 match). It was then served
back over HTTP, **survived `docker rm -f` plus a container recreate**, and decoded in a browser at
240×240. The failure direction was exercised too: with the host directory left root-owned, the same
upload failed — which is what produced the new §10 entry. The `chown` in §5 is therefore an
**observation in both directions**, no longer a prediction.
*Deviation:* the bind mount was a local path on the build machine, not `/srv/data` on the VPS. The
mechanism — a non-root container UID writing into a host directory — is identical.

**2. A successful image build, and the resulting running image — PROVEN, with one substitution.**
Restore, compile, publish, the runtime stage, and a **running container serving the real site** were
all executed end to end. **The substitution: the private GitHub Packages feed was replaced with a
local folder feed** of the three cached TrBlazeUI 2.0.1 packages, because **no valid PAT exists on
the build machine** (checked across the process environment, `~/.nuget/NuGet/NuGet.Config`,
`%APPDATA%\NuGet\NuGet.Config` and the repository config). The swap was made in a throwaway copy of
the `Dockerfile` under `tests/.artifacts/harness/`, differing from the real one **only** in the feed
URL; the real `Dockerfile` still points at `https://nuget.pkg.github.com/techierathore/index.json`.
*What remains unproven is specifically the **authenticated GitHub Packages restore** — the credential
handshake — and nothing else.* The build pipeline itself, the publish output and the finished image
are proven. Do not read this row as "the build is unverified"; read it as "the first `dotnet restore`
has never been run against the real private feed with a real token".

**3. The Blazor circuit works in-container — PROVEN.** Against the running image, headless Playwright
opened a **`/_blazor` WebSocket** (the upgrade responded `101`), `window.Blazor` initialised,
`/_framework/blazor.web.js` returned **200**, and genuinely interactive actions were driven over the
circuit end to end: signing in as the seeded admin landed on `/admin`, `/admin/images` rendered as the
authenticated user, and a real image upload completed through the interactive dialog. **Only the Caddy
hop remains untested** — the circuit's behaviour behind the reverse proxy, over TLS, on the real
domain. The app half of §10's "renders once, then every click does nothing" entry is now excluded by
observation; what is left there is a proxy question.

*(This is also how the `Dockerfile` defect in §10 was found and fixed: an image built the old way
served `/_framework/blazor.web.js` as **404** with 586 published routes, and the corrected one serves
it 200 with 606 — measured back to back on the same source tree and the same feed.)*

**Also proven while fixing it:** the DataProtection key-ring mount added to
`deploy/docker-compose.template.yml` behaves as §5 claims. With the mount absent, two successive
container recreates generated two **different** key GUIDs and the runtime logged the "may not be
persisted outside of the container" warning each time. With `/…/dp-keys` bind-mounted and writable by
1654, that warning disappeared entirely and a `docker rm -f` plus recreate reloaded the **same**
`key-<guid>.xml` — one file, unchanged.

The following are **UNVERIFIED** and cannot be verified from a development machine:

| Unverified | Why |
|-----------|-----|
| Any actual deploy | There is no VPS reachable from the build environment. No SSH, no scp, no `ensure-db`, no `docker compose up` has run against the real server. |
| The **authenticated GitHub Packages restore** | There is no valid TrBlazeUI PAT on the build machine. The build, publish, runtime stage and running image are all proven against a substituted **local folder feed** (see "Newly PROVEN", item 2) — what has never run is `dotnet restore` reaching `nuget.pkg.github.com` with a real token. The failure direction *is* proven: an invalid token yields `NU1301 / 401` at exactly that point. |
| GHCR push, tags and `type=gha` caching | Requires a real workflow run. |
| The `appleboy/ssh-action` and `appleboy/scp-action` steps | Never executed. Argument shapes follow the actions' documented inputs. |
| Caddy actually serving the site, TLS issuance, the `www` redirect | Needs live DNS and a running Caddy. The *config* is validated; its *behaviour* is not. |
| The Blazor circuit **through Caddy** | The circuit itself is proven in-container (see "Newly PROVEN", item 3). What is untested is the **Caddy hop**: the WebSocket upgrade through the reverse proxy, over TLS, on the real domain. The reasoning in §10 about proxies is still from Caddy's documented behaviour, not from an observed session. |
| Uploads through the bind mount **on the VPS specifically** | The mechanism is proven end to end on a local bind mount (see "Newly PROVEN", item 1). What has not happened is the same write against `/srv/data/techieblog/uploads` on the real server, under its ownership and its backup schedule. |
| What `ensure-db` grants | The helper lives on the VPS and is not in this repository. §5a is a *check to run*, not a claim that the grant is already sufficient. |
| Seq actually receiving events | The sink is registered and conditional on `Seq:Url`, which the compose file sets, and that logic is unit-tested — but no event has been shipped to the real Seq container on the VPS. |

---

*Companion documents: `deploy/SERVER-SETUP.md` (the short pre-flight summary),
`docs/claude-code-deployment-brief.md` (the portfolio-wide spec this implements),
`docs/deployment.md` (older, generic, non-VPS deployment options).*
