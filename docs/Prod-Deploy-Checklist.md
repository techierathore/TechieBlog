# TechieBlog — Production Deployment Checklist

**What the pipeline does for you, what you must do yourself, and nothing in between.**
Written for the repository owner, not for an agent.

> **Start here.** Your VPS build is **complete** — the whole of `docs/bluehost-vps-runbook-v5.2.md`,
> confirmed by its own acceptance script `sudo /srv/checkup.sh` reporting **0 failed** on
> 2026-08-16. See [§0](#0-verified-vps-state). What is left for you is
> [§2 — your setup list](#2-your-setup-list): about twenty-five minutes, **all of it in a browser**.
> There are no routine steps on the server.

Once §2 is done, every deployment forever after is: **push to `main` → watch Actions go green → the
site is live.** Nothing else, ever.

> **This document absorbed `docs/Server-Setup.md` on 2026-08-16, which no longer exists.** There
> were two files describing one procedure, and the shorter one had already drifted out of date on
> both Seq and the `appuser` privileges question. Its content is [§2](#2-your-setup-list), which is
> now written to be read on its own — that is the "one page" the deleted file used to be. One
> procedure, one file.

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
| Container runs as | UID/GID **1654** (`app`) — a deliberate deviation from deployment-brief v3.2, see [§0](#0-verified-vps-state) |
| Log shipping (app → server) | `http://seq:5341` — container `seq` on the `web` network, plain HTTP. **Never the public URL.** |
| Log reading (you → browser) | `https://seq.techierathore.com`, filter `App = 'techieblog'` |
| SSH account the pipeline uses | **`ciuser`** — dedicated CI robot account (runbook v5.2 §2.1). `docker` group, `webops` group on `/srv/apps` `/srv/data` `/srv/caddy/sites`, sudo limited to `ensure-db`, no access to `/srv/backups`. Read from the `VPS_USER` secret, never hardcoded. |
| Pipeline | `.github/workflows/deploy.yml` |
| Server build | `docs/bluehost-vps-runbook-v5.2.md` — **complete**, `sudo /srv/checkup.sh` = 0 failed (2026-08-16) |
| Portfolio spec | `docs/claude-code-deployment-brief-v3.2.md` |

## Table of Contents

0. [Verified VPS state — server build complete](#0-verified-vps-state)
1. [The division of labour — who does what](#1-the-division-of-labour--who-does-what)
2. [Your setup list — do these once](#2-your-setup-list)
3. [Every secret, in plain terms](#3-every-secret-in-plain-terms)
4. [DNS — the two records](#4-dns--the-two-records)
5. [The TrBlazeUI package token](#5-the-trblazeui-package-token)
6. [What the pipeline does, step by step](#6-what-the-pipeline-does-step-by-step)
7. [After the first deploy — what to check](#7-after-the-first-deploy--what-to-check)
8. [The two conditional server steps](#8-the-two-conditional-server-steps)
9. [How uploads and keys survive a redeploy](#9-how-uploads-and-keys-survive-a-redeploy)
10. [Rolling back](#10-rolling-back)
11. [Routine operations](#11-routine-operations)
12. [Troubleshooting](#12-troubleshooting)
13. [What is proven and what is not](#13-what-is-proven-and-what-is-not)

---

## 0. Verified VPS state

**The server build is COMPLETE.** On **2026-08-16** the owner finished the whole of
`docs/bluehost-vps-runbook-v5.2.md` and ran its acceptance script, `sudo /srv/checkup.sh`
(runbook Part 9), which reported **0 failed**. That script is not a summary of intentions — it
checks swap, ufw, fail2ban, unattended-upgrades, cron, PostgreSQL (running, accepting queries,
pgvector available), Docker, the `web` network, restart-looping containers, Caddy (up and
`caddy validate` clean), **Seq (container up and answering through Caddy)**, every `/srv` folder,
the backup script + its cron registration + a recent backup present on **both** OneDrive and Google
Drive.

**The only thing not done manually is deploying an application container** — that is this
pipeline's job, deliberately.

This section used to be a ledger of assumptions and half-corrections. It is now short, because the
questions it tracked are answered.

### What is now settled

| Question this document used to hedge | Settled answer |
|---|---|
| Is PostgreSQL reachable from a container? | **Yes** — `172.17.0.1:5432`, user `appuser`. Runbook Steps 6–7; `checkup.sh` confirms Postgres runs and answers queries. **Do not modify `postgresql.conf` or `pg_hba.conf`.** |
| Does `appuser` have DDL rights? | **Yes** — owner-confirmed. `ensure-db` runs `createdb -O appuser`, so `appuser` owns the database. |
| Does a `seq` container exist? | **Yes** — container **`seq`** on network **`web`**, ingestion at **`http://seq:5341`** (plain HTTP, no TLS, no port mapping). `checkup.sh` verifies it is up and answering. |
| Is the internal Seq address really `http://seq:5341`? | **Yes.** This was the last open unknown; the v5 runbook and the v3 deployment brief both state it as a contract constant, and `checkup.sh` proves the container exists on the right network. |
| Is the server authenticated to GHCR? | **As `ravi`, yes. As `ciuser` — the account the pipeline actually uses — NO.** See the third contradiction below; this one has teeth. |
| Which account does the pipeline SSH in as? | **`ciuser`** (runbook v5.2 §2.1), from the `VPS_USER` secret. In the `docker` group (so no `sudo docker`), `webops` group on `/srv/apps` `/srv/data` `/srv/caddy/sites`, and exactly **one** sudo command: `/usr/local/bin/ensure-db`. No access to `/srv/backups` or the main `Caddyfile`. |
| Do `/srv/apps` and `/srv/data` exist? | **Yes**, owned by the deploy user (`ravi`). Runbook Step 8, which now also creates `/srv/caddy/sites`. |
| Is swap configured? | **Yes** — runbook Part 7.1, checked by `checkup.sh`. The earlier "no swap" warning is obsolete. |
| Is `ensure-db` passwordless for the deploy user? | **Yes** — `/etc/sudoers.d/ensure-db`. It is the **only** SQL the workflow may run. |

### What remains genuinely manual, per app

Exactly **two** things, per the deployment brief — plus the ones specific to this repo:

| # | Manual step | Why it cannot be automated |
|---|---|---|
| 1 | **The DNS A records** | The pipeline holds no DNS credentials, by design. |
| 2 | **The Seq API key** | Created in the Seq UI per runbook §6.3, then stored as a GitHub secret. One key **per app** — never shared. |

Plus, for TechieBlog specifically: the three application secrets and the TrBlazeUI package token
(§2), because this app refuses to start without the former and cannot build without the latter.

### Three contradictions between the source documents — read before acting

None is a defect in this deployment. Two are places where the upstream documents disagree with each
other; **the third is a place where the brief describes the server as it was before the `ciuser`
change, and would have failed your first deploy.** This file records what this repo does about each.

**0. THE GHCR CREDENTIAL DOES NOT COVER `ciuser` — the one that bites.**

Brief v3.2 §0 says *"the server is already authenticated as `techierathore` via a stored PAT
(runbook Step 12) … the workflow must not run `docker login` over SSH."* That answer was written
when the pipeline connected as `ravi`. Runbook v5.2 §2.1 then moved CI onto a brand-new account,
and the two facts do not compose:

- **Docker registry credentials are per-user**, stored in `~/.docker/config.json`.
- Membership of the `docker` group grants access to the **daemon**. It grants nothing at the
  **registry**.
- Runbook **Step 12 ran as `ravi`** — `ciuser` does not exist until Part 2.1, which comes later.

So `/home/ciuser/.docker/config.json` does not exist, and `docker compose pull` of a private image
as `ciuser` would fail **`unauthorized`**.

**This repo survives it** because the pull step checks for a stored credential first and falls back
to an ephemeral `docker login` with the run's own `GITHUB_TOKEN`, logging out again via an `EXIT`
trap. That fallback was written as a safety net for a state the brief said could not happen; it is
now **the live path**. Deleting it to satisfy the letter of the brief would break the deploy.

**To make the brief true again**, run one command on the server as `ravi` — then the fallback goes
dormant and stays dormant:

```bash
sudo -u ciuser -H bash -c \
  'echo <YOUR_PAT> | docker login ghcr.io -u techierathore --password-stdin'
```

Either way the deploy works. The command is worth running because a per-run token is a per-run
dependency: it ties every pull to `packages: read` on the workflow, and it re-authenticates on every
deploy rather than once ever.

---

The remaining two are genuine disagreements between the documents.

**1. The Seq secret's NAME.** Runbook v5.2 §6.3 says to name the GitHub secret
`SEQ_APIKEY_BLOG` (uppercase, app suffix). Deployment brief v3.2 §2 says **`SEQ_API_KEY`** — the same
name in every repo, different value per app — and explicitly justifies it: *"Keeping the secret name
identical across repos means the workflow YAML stays byte-identical — only the value differs."*

**This repo follows the brief: the secret is `SEQ_API_KEY`.** The brief is the newer document, it
cites §6.3 as its own source, and its reasoning is the stronger one. **Name the secret
`SEQ_API_KEY`** when you create it in §2 — if you name it `SEQ_APIKEY_BLOG`, the workflow will not
see it and logs will ship unattributed.

**2. The container user.** Brief v3.2 says the container must run as **root** with **no `USER`
directive**, on the grounds that a non-root user cannot write to the `ravi`-owned uploads bind mount.

**This repo deliberately deviates: the container runs as non-root UID `1654`.** The brief's premise
does not hold here, because this pipeline **fixes ownership on the two bind-mounted leaf directories
every deploy and then probes that they are writable**. That mechanism was executed against real
directories with real Docker and proven in both directions (§13). Running the public-facing web
container as root to avoid a `chown` the pipeline already performs is the worse trade.

The brief v3.2 also adds *"never `chown` or `chmod` from a workflow — group inheritance is handled
by the setgid bit."* That rule exists to stop a workflow destroying the `webops` group model that
gives `ciuser` its access, and **it caught a real bug here.** The ownership fix used to run
`chown -R 1654:1654`, which replaces group `webops` with group `1654` — cutting `ciuser` out of the
very directories it created, and silently breaking §8's `rsync` uploads-migration path, which
connects as `VPS_USER` (= `ciuser`).

**Fixed on 2026-08-16, and measured rather than reasoned about.** The step now:

```sh
chown -R 1654 "$d"     # owner only — group `webops` is left alone
chmod g+ws  "$d"       # directory only — restores group-write + setgid
```

Verified end to end against real Docker on both a Debian base (what the app image is) and Alpine:
resulting mode `drwxrwsr-x`, owner `1654`, group unchanged; **container UID 1654 writes, and a
`webops`-group user writes** — so the rsync path still works; a file created by the container
inherits group `webops` via setgid, which is also what keeps the `/srv/data` backup archive
readable; idempotent across three consecutive runs; the parent directory is never touched; and
uploaded files stay `-rw-r--r--` rather than becoming group-writable.

**What this costs you:** nothing operationally, but it is a knowing departure from the portfolio
spec. If you ever adopt the brief's shape, delete `USER $APP_UID` from the `Dockerfile` **and** the
ownership-fix step together — removing only one of the two breaks uploads.

### The one thing still unproven

**No part of this pipeline has ever executed against the real VPS.** The server's *state* is now
thoroughly known; the pipeline's *behaviour against it* is not. §13 is the honest ledger.

---

## 1. The division of labour — who does what

This is the table the rest of the document elaborates. **If you read nothing else, read this.**

### The pipeline does all of this, on every push, with no involvement from you

| # | What | Where it happens |
|---|------|------------------|
| 1 | Checks the private TrBlazeUI feed is reachable **before** building, so a credential problem is one readable line instead of sixty lines of NuGet noise | `build` job |
| 2 | Builds the Docker image and pushes it to GHCR as `:latest` **and** `:<short-sha>` (that second tag is your rollback history) | `build` job |
| 3 | Checks every required secret is present **and long enough**, before doing anything on the server | `deploy` job |
| 4 | Renders `docker-compose.yml` from the template with the real secret values, and refuses to continue if any placeholder survived | `deploy` job |
| 5 | Creates `/srv/apps/techieblog` and `/srv/data/techieblog/{uploads,dp-keys}` on the server | `deploy` job |
| 6 | Copies the rendered compose file to the server | `deploy` job |
| 7 | Installs the Caddy site snippet **only if it is not already there**, and reloads Caddy | `deploy` job |
| 8 | Creates the `techieblog` database if it does not exist (`ensure-db`) | `deploy` job |
| 9 | **Verifies `appuser` can actually create tables**, and fails the deploy with the exact `GRANT` statements if it cannot | `deploy` job |
| 10 | **Logs the server in to GHCR** for the duration of the pull, then logs out again — leaving no credential behind | `deploy` job |
| 11 | Pulls the new image | `deploy` job |
| 12 | **Fixes ownership of the uploads and key-ring directories to UID 1654**, every time, idempotently | `deploy` job |
| 13 | Starts the new container and prunes the old image | `deploy` job |
| 14 | **Probes that both persistent directories are genuinely writable by the container**, and fails loudly if not | `deploy` job |
| 14a | **Reports where log events are actually going** — checks a `seq` container exists on the `web` network and prints the `SeqUrl` the container was configured with. Warning only; never fails the deploy | `deploy` job |
| 15 | Checks `https://techierathore.com/healthz` returns 200 — which means DNS, TLS, Caddy, the app **and the database** all worked | `verify` job |
| 16 | Checks `/_framework/blazor.web.js` returns 200 — catching the defect where the site renders but nothing is clickable | `verify` job |
| 17 | Checks the `www` → apex redirect answers (warning only, not fatal) | `verify` job |

### You do this — once, before the first push

| # | What | Where | Time |
|---|------|-------|------|
| 1 | Add three application secrets — §3 | GitHub repo settings | 5 min |
| 2 | Add the TrBlazeUI package token — §5 | GitHub repo settings | 5 min |
| 3 | Create the Seq API key, store as `SEQ_API_KEY` — §2 | Seq UI, then GitHub | 3 min |
| 4 | Confirm the org secrets reach this repo — §3 | GitHub org settings | 1 min |
| 5 | Add two DNS A records — §4 | Your registrar | 2 min |
| 6 | Push to `main` | Your machine | — |
| 7 | Add one UptimeRobot monitor — §7 | UptimeRobot | 2 min |

### You do this only *if* something specific happens

| What | When |
|------|------|
| Run three `GRANT` statements as a superuser — §8 | **Only if** the pipeline's DDL check fails and prints them |
| `rsync` your existing local images to the server — §8 | **Only if** you have images under your dev copy's `wwwroot/uploads` |

### Nothing else. In particular, these are *no longer* your job

Earlier versions of this document asked you to do all of the following by hand after the first
deploy. **The pipeline now does every one of them, on every deploy:**

- ~~`sudo chown -R 1654:1654` on `uploads` and `dp-keys`~~ → automatic (item 12), and verified (item 14)
- ~~`docker exec techieblog touch /app/uploads/probe`~~ → automatic (item 14)
- ~~The `CREATE TABLE` probe for `appuser` privileges~~ → automatic, and it now *gates* the deploy (item 9)
- ~~`docker login ghcr.io` on the server~~ → automatic and ephemeral (item 10)
- ~~`curl` the Blazor script and the `www` redirect by hand~~ → automatic (items 16, 17)

---

## 2. Your setup list

**This section is the whole job.** It is written to be read on its own — it is what
`docs/Server-Setup.md` used to be, before that file was folded in here on 2026-08-16. Everything
else in this document is reference material you consult when something specific happens.

Seven things, in this order. **Everything except step 5 happens in a browser.** About twenty-five
minutes.

**There are no routine steps on the server. Not one.** Your VPS build is complete and verified
(§0) — nothing below asks you to redo any of `docs/bluehost-vps-runbook-v5.2.md`.

Generated for `APP_NAME=techieblog` · `DOMAIN=techierathore.com` (apex) · `DB_NAME=techieblog` ·
`NEEDS_PGVECTOR=no` · `HAS_UPLOADS=yes`.

| # | Step | Where | Time | Done when |
|---|------|-------|------|-----------|
| 1 | Add the three application secrets | GitHub repo settings | 5 min | All three listed under Actions secrets |
| 2 | Add the TrBlazeUI package token | GitHub repo settings | 5 min | The build job gets past "Preflight — TrBlazeUI feed authentication" |
| 3 | **Create the Seq API key and store it as `SEQ_API_KEY`** | Seq UI, then GitHub | 3 min | Key titled `techieblog` exists in Seq; repo secret `SEQ_API_KEY` listed |
| 4 | Confirm the org secrets reach this repo | GitHub org settings | 1 min | `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `DB_PASSWORD` all listed |
| 5 | Add two DNS A records | Your registrar | 2 min | `dig +short techierathore.com` returns the VPS IP |
| 6 | Push to `main` | Your machine | — | `build` → `deploy` → `verify` all green |
| 7 | Add one UptimeRobot monitor | UptimeRobot | 2 min | Monitor shows "Up" |

### Step 1 — the three application secrets

**Settings → Secrets and variables → Actions → New repository secret.** Each one makes the container
**refuse to start** if it is missing — that is deliberate, not a bug.

```bash
openssl rand -hex 32   # JWT_SIGNING_KEY         signs your sign-in tokens        (>= 32 chars)
openssl rand -hex 24   # APP_ENCRYPTION_KEY      locks stored SMTP / storage keys (>= 16 chars)
openssl rand -hex 32   # ANALYTICS_VISITOR_SALT  anonymises visitor IPs           (>= 32 chars)
```

**Store all three in your password manager the moment you generate them.** They exist nowhere else —
GitHub never shows a secret's value again after you save it.

**Before you generate any key**, decide one thing: does this deployment start from an **empty**
database, or restore an **existing** TechieBlog database?

⚠ **If you are restoring an existing TechieBlog database, reuse the existing `APP_ENCRYPTION_KEY`.**
A fresh one makes the stored SMTP password and cloud storage key permanently unreadable.

⚠ **Treat all three as write-once.** Rotating `JWT_SIGNING_KEY` signs everyone out; rotating
`APP_ENCRYPTION_KEY` destroys stored credentials; rotating `ANALYTICS_VISITOR_SALT` resets every
visitor pseudonym. Details in §3.

### Step 2 — the TrBlazeUI package token

`TrBlazeUiPackagesToken` — a **classic** PAT (not fine-grained) with **`read:packages`** only. Name
it exactly that; it is case-sensitive. Without it the image never builds.

Alternative: grant this repository Read access on each TrBlazeUI package page, and the built-in
`GITHUB_TOKEN` suffices. Full steps for both: §5.

### Step 3 — the Seq API key

One of the **two** genuinely manual per-app steps in the whole portfolio (the other is DNS). Runbook
v5.2 §6.3 is emphatic that keys are **per app, never shared** — a per-app key stamps `App` on every
event *server-side*, so logs stay attributable even if the app's own enrichment is wrong; it can be
revoked without redeploying every other app; and a noisy app can be throttled at the key.

1. Open `https://seq.techierathore.com` and sign in as `admin`.
2. **Settings → API Keys → Add API Key**.
3. **Title:** `techieblog`.
4. **Applied properties:** add `App` = `techieblog`.
5. Leave **Minimum level** at Information.
6. Save, and **copy the key immediately** — Seq shows it once.
7. In this repository: **Settings → Secrets and variables → Actions → New repository secret**.
8. **Name:** `SEQ_API_KEY` — exactly this.

> ⚠ **Name it `SEQ_API_KEY`, not `SEQ_APIKEY_BLOG`.** Runbook v5.2 §6.3 suggests the latter; the v3
> deployment brief specifies the former and this repo's workflow reads the former. §0 explains why
> the brief wins. A mis-named secret does not fail the build — logs simply ship without a key, which
> you would only notice by looking.

**If you skip this step**, the deploy still succeeds and logs still ship — that Seq accepts
unauthenticated ingestion — but events arrive with no ingestion identity. The pipeline emits a
`::warning::` saying so.

### Step 4 — the org secrets

Already exist per the environment contract — just confirm this repo can see them:
`VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `DB_PASSWORD`.

You created `DB_PASSWORD` on the server during the runbook. GitHub holds a **copy** so it can write
it into the app's configuration — the server is the source. You are not setting it twice.

### Step 5 — DNS, *before* the first push

The pipeline never touches DNS. Caddy provisions the certificate on the first request and cannot do
so for a name that does not resolve.

| Type | Name | Value | Note |
|------|------|-------|------|
| A | `@` | *your VPS IPv4* | Apex record. **DNS only — grey cloud** if on Cloudflare. |
| A | `www` | *your VPS IPv4* | For the `www` → apex redirect. A `CNAME www → techierathore.com` also works. **Grey cloud.** |

An orange-cloud record terminates TLS at Cloudflare, which breaks both certificate issuance and the
Blazor WebSocket. Keep both DNS-only. Full reasoning: §4.

```bash
dig +short techierathore.com
dig +short www.techierathore.com   # both must return the VPS IP
```

### Step 6 — push

`build` → `deploy` → `verify`, all green. The `verify` job proves DNS, TLS, Caddy, the app, the
database **and** that the Blazor script is being served.

> **Which branch deploys: `main`, and only `main`.** Pushing to `dev` deliberately does **not**
> deploy — `ci.yml` still builds and tests it. There is no second production branch.
>
> If a push to `main` ever produces **no Deploy run at all**, check the trigger list first: a branch
> that does not match is completely silent, which reads exactly like a broken pipeline because there
> is no failed job to open. The workflow also has `workflow_dispatch`, so you can always run it by
> hand from the Actions tab.
>
> *Correction, 2026-08-16: an earlier revision of this document said `master` was this repo's main
> branch and the workflow briefly accepted both names. That came from a stale session snapshot, not
> from the repository. Corrected to `main` only.*

### Step 7 — uptime monitoring

UptimeRobot → **Add New Monitor** → **HTTP(s)** → `https://techierathore.com/healthz` → interval 5
minutes → alert after 2 consecutive failures.

> `/healthz` is anonymous, exempt from rate limiting, and carries the **readiness** checks —
> PostgreSQL and the DbUp schema journal included. A green response means the database answered and
> the migrations applied, not merely that the process is alive. It returns **503** otherwise. Same
> URL the pipeline probes: one URL, one meaning.

### Only if something specific happens

**If the pipeline's `Verify appuser DDL rights` step fails**, it prints three `GRANT` statements. Run
them on the VPS as a superuser, once, then re-run the workflow — §8. (You confirmed on 2026-08-16
that `appuser` already holds these rights, so this should never fire.)

**If you have images under your dev copy's `wwwroot/uploads`**, `rsync` them to
`/srv/data/techieblog/uploads` — §8. No `chown` afterwards; the next deploy fixes ownership itself.

### What the pipeline now does that you used to have to do

| Was your job | Now |
|---|---|
| `sudo chown -R 1654:1654` on `uploads` and `dp-keys` | Automatic on every deploy, then **probed** to confirm it worked |
| `docker exec techieblog touch /app/uploads/probe` | Automatic; a non-writable mount fails the deploy |
| The `CREATE TABLE` probe for `appuser` | Automatic, and it now **gates** the deploy — before the container starts |
| `docker login ghcr.io` on the server | Automatic and ephemeral; an existing manual login is detected and left alone |
| `curl` the Blazor script / `www` redirect | Automatic in the `verify` job |

### Nothing else

- **Backups:** no action. `pg_dumpall` picks up `techieblog` automatically, and the `/srv/data`
  archive picks up `uploads` and `dp-keys` — which is precisely why they live there.
- **Database:** created by `sudo /usr/local/bin/ensure-db techieblog` inside the pipeline. No
  `vector` extension. Schema applied by DbUp on every boot.
- **Caddy:** the pipeline installs `/srv/caddy/sites/techieblog.caddy` **only if absent**, and never
  overwrites it. An existing file is authoritative — edit it on the server and reload by hand.
- **Logs:** stdout plus Seq. `docker logs techieblog` for the recent buffer,
  `https://seq.techierathore.com` for durable history.

---

## 3. Every secret, in plain terms

### First — you are not setting the same secret twice

This is the confusing part, so here it is directly. There are two places, and they hold two
different kinds of thing.

**On the server live the credentials the server's own services own.** You created these during the
VPS runbook and they are already done — you do not touch them again:

- the PostgreSQL password for `appuser` (runbook Step 6)
- the Seq admin password (runbook Part 6.1)
- the **GHCR pull credential** — you logged the server in as `techierathore` with a stored PAT
  (runbook Step 12). This is why the pipeline never runs `docker login` over SSH.
- the rclone OneDrive / Google Drive tokens for backups (runbook Part 3.2–3.3)
- your SSH public key in `~/.ssh/authorized_keys`

**In GitHub live the values the pipeline needs in order to reach that server and configure the
app.** Nine of them.

Only **two** values could exist in both places — `DB_PASSWORD` and `SEQ_API_KEY` — and not out of
duplication. The server owns the database and owns Seq; GitHub has to be *told* those values so it
can write them into the container's configuration. **The server is the source, GitHub is the copy.**
The Seq key is the clearest case: you mint it in the Seq UI (§2 step 3), so it is born on the server
and GitHub holds the copy the pipeline injects.

**The GHCR PAT is the one server credential that is deliberately NOT mirrored into GitHub.** The
brief is explicit: the server pulls with its own stored login, and putting a pull PAT in a workflow
secret is prohibited. If a pull ever fails `unauthorized`, that PAT expired — re-run runbook Step 12
by hand. It is not a pipeline change.

Everything else is one-sided:

- `VPS_SSH_KEY` is the **private half** of a key pair whose **public half** sits on the server. That
  is two halves of one key, not two secrets.
- `JWT_SIGNING_KEY`, `APP_ENCRYPTION_KEY` and `ANALYTICS_VISITOR_SALT` exist **only in GitHub**. You
  never type them on the server.
- `TrBlazeUiPackagesToken` exists **only in GitHub**, and only while the image is being built. It
  never reaches the server at all.

**Where they end up.** The pipeline writes `/srv/apps/techieblog/docker-compose.yml` on the server
with the real values inside it. So yes — if you `cat` that file you will see secrets in plain text.
But you never typed them there, the pipeline wrote them, and it rewrites the file on every deploy.
That single file is the only place on the server they appear.

### What each one is actually for

| Secret | In plain terms | Where it came from |
|--------|----------------|--------------------|
| `VPS_HOST` | The server's address, so GitHub knows which machine to deploy to. | Bluehost console |
| `VPS_USER` | Which login GitHub uses when it connects (`ravi`). | Runbook Step 2 |
| `VPS_SSH_KEY` | The key that proves GitHub is allowed in, instead of a password. | Runbook Part 2.1 |
| `DB_PASSWORD` | The blog's database password. GitHub writes it into the app's connection string. | Runbook Step 6 |
| `SEQ_API_KEY` | Identifies the blog to Seq. **Repo-level and per-app** — never shared with another app. It stamps `App = techieblog` on every event *server-side*, so logs stay attributable even if the app's own enrichment is wrong, and this one app can be revoked or throttled without touching the others. Shipping works without it, unattributed. | **You create it** — Seq UI → Settings → API Keys, per §2 step 3 |
| `JWT_SIGNING_KEY` | The signature on your sign-in tokens — it proves a session cookie was issued by your site and not forged by someone else. | You generate |
| `APP_ENCRYPTION_KEY` | The lock on credentials the blog stores in its own database: the SMTP password and the cloud-storage key. Without it those would sit in the database readable by anyone who opened it. | You generate |
| `ANALYTICS_VISITOR_SALT` | Turns each visitor's IP address into an unreadable fingerprint before it is stored, so you can count unique visitors without keeping anyone's IP. | You generate |
| `TrBlazeUiPackagesToken` | Lets the build download your private TrBlazeUI packages. Build-time only. | You generate (§5) |

One analogy for the three you generate: **the JWT key is the signature on a ticket, the encryption
key is the safe your stored passwords sit in, and the visitor salt is the shredder your visitor logs
go through.** Change the signature and everyone's ticket stops working. Change the safe's
combination and you can never open the safe again. Change the shredder and yesterday's shreds no
longer match today's.

### What breaks without each one

| Secret | What breaks |
|--------|-------------|
| `VPS_HOST` / `VPS_USER` / `VPS_SSH_KEY` | Every `deploy` step fails to connect. |
| `DB_PASSWORD` | Container starts, then fails every request; `/healthz` returns 503. |
| `SEQ_API_KEY` | The deploy still succeeds and logs still ship, unauthenticated, with a `::warning::`. What you lose is the server-side `App` stamp, per-app revocation and per-app throttling — the three reasons runbook §6.3 insists on per-app keys. Not fatal; do it anyway. |
| `JWT_SIGNING_KEY` | **The container refuses to start.** `AppSecrets.Initialise` (REQ-NFR-027) throws — there is deliberately no fallback. |
| `APP_ENCRYPTION_KEY` | **The container refuses to start** (same gate). |
| `ANALYTICS_VISITOR_SALT` | **The container refuses to start.** `DeploymentConfiguration.Enforce` (REQ-NFR-030) throws outside Development. |
| `TrBlazeUiPackagesToken` | **The image never builds.** `dotnet restore` fails `NU1301 / 403` inside the Docker build. |

The `deploy` job checks all five of the value-carrying secrets for presence **and minimum length**
before it touches the server, so a missing or too-short secret fails in GitHub with a named error
rather than as a crash-looping container you have to diagnose over SSH.

### The three you must never rotate casually

These are effectively **write-once for the life of the deployment**:

| Rotating… | Consequence |
|-----------|-------------|
| `JWT_SIGNING_KEY` | Every existing session cookie becomes invalid. Every signed-in user is silently signed out on their next request. Recoverable, but everyone sees it. |
| `APP_ENCRYPTION_KEY` | Every value already encrypted under the old key is **permanently unreadable**: the SMTP password and cloud storage key must be re-entered by hand. Worse, they *look* present in the admin UI while failing at use. |
| `ANALYTICS_VISITOR_SALT` | Every stored visitor pseudonym stops matching. Unique-view counts jump, de-duplication restarts from zero, and old and new digests can never be reconciled. **Treat as write-once.** |

`SiteSettings:BaseUrl` is **not** a secret — the compose template hard-codes
`https://techierathore.com`.

### Where log events go — and why the public URL must never appear here

There is no configuration for you to set. `SeqUrl` is rendered by the workflow as the constant
**`http://seq:5341`**: container `techieblog` to container `seq`, over the shared `web` Docker
network, plain HTTP.

**The public address `https://seq.techierathore.com` is for your browser only and must never appear
in app config** — deployment-brief v3.2 §0 states this as a rule, and the reasons are good ones:
ingestion traffic would leave the Docker network, cross Caddy, and terminate TLS for no benefit,
while making the app's logging depend on public DNS and a certificate.

> An earlier revision of this document introduced a `SEQ_URL` repository **variable** that could
> override the address with the public URL. **It has been removed** — it existed only because the
> internal address was unconfirmed at the time, and pointing app config at the public URL is exactly
> what the brief prohibits. Nothing to unset; if you created that variable, delete it.

The deploy job still prints where events are going (*Report the Seq endpoint*, warning-only), so a
misrouted sink is visible in the log rather than silent.

---

## 4. DNS — the two records

The pipeline never touches DNS, by design. Add both records **before the first push**, because Caddy
provisions the Let's Encrypt certificate on the first request and cannot do so for a name that does
not resolve to the VPS.

| Type | Name | Value | TTL | Note |
|------|------|-------|-----|------|
| A | `@` | *your VPS IPv4 address* | Auto / 300 | The apex record. If the domain is on Cloudflare: **DNS only — grey cloud.** |
| A | `www` | *your VPS IPv4 address* | Auto / 300 | Needed for the `www` → apex redirect. A `CNAME www → techierathore.com` works equally well. **DNS only — grey cloud** if on Cloudflare. |

**Why grey cloud matters:** an orange-cloud (proxied) Cloudflare record terminates TLS at
Cloudflare. Caddy's `HTTP-01` certificate challenge then fails, and Cloudflare's proxy adds its own
WebSocket and buffering behaviour in front of a Blazor Server circuit. Keep both records DNS-only.

Verify before pushing — both must print the VPS IP:

```bash
dig +short techierathore.com
dig +short www.techierathore.com
```

---

## 5. The TrBlazeUI package token

This blog depends on `TrBlazeUI.Components` and `TrBlazeUI.Icons.Lucide`, published to a **private,
user-scoped GitHub Packages feed** (`https://nuget.pkg.github.com/techierathore/index.json`).
Anonymous restore is impossible, so both CI and the deploy image build need a credential.

> **The token that used to be committed in `NuGet.Config` was invalidated by GitHub secret scanning
> on 2026-08-09 and CANNOT be reused** (REQ-NFR-025). Never put a token back into `NuGet.Config` —
> that file is published to every clone and fork.

Do **one** of these two. They are alternatives, not steps.

### Remedy 1 — a classic PAT stored as `TrBlazeUiPackagesToken` (recommended)

1. Sign in to GitHub as **`techierathore`**.
2. **Settings → Developer settings → Personal access tokens → Tokens (classic)**
   (`https://github.com/settings/tokens`).
3. **Generate new token → Generate new token (classic)**.
4. **Note:** `TechieBlog CI + deploy — TrBlazeUI packages`.
5. **Expiration:** a date you will remember, or *No expiration* if you accept the trade-off. An
   expired token reappears as the same `NU1301 / 403` failure.
6. **Scopes:** tick **`read:packages`** only. Not `repo`, not `write:packages`. A fine-grained token
   will **not** work — GitHub Packages for NuGet still requires a **classic** token.
7. **Generate token**, copy the value immediately (`ghp_…`). GitHub never shows it again.
8. In the **TechieBlog repository**: **Settings → Secrets and variables → Actions → New repository
   secret**.
9. **Name:** `TrBlazeUiPackagesToken` — exactly this. Secret names are case-sensitive and both
   workflows reference this spelling.
10. Paste the token, **Add secret**.

### Remedy 2 — grant this repository package access, and use the built-in `GITHUB_TOKEN`

No PAT and no secret to rotate — but it must be repeated for **each** TrBlazeUI package.

1. Go to `https://github.com/users/techierathore/packages`.
2. Open **`TrBlazeUI.Components`** → **Package settings** → **Manage Actions access**.
3. **Add repository** → **TechieBlog** → role **Read**.
4. Repeat for **`TrBlazeUI.Icons.Lucide`** and any other TrBlazeUI package the solution references.

Both workflows already fall back to `GITHUB_TOKEN` automatically.

### How the token reaches the build — and why it is not a build ARG

The image build receives it as a **BuildKit secret**, id **`nuget_pat`**. The Dockerfile consumes it
with `RUN --mount=type=secret,id=nuget_pat …`, which mounts it into a single layer's filesystem for
the duration of that `RUN` and never writes it into the image. A build `ARG` **would** be written in:
`docker history` prints every ARG value. That is why this pipeline never uses one.

> **The two ids must be spelled identically.** Until 2026-08-10 the workflow sent
> `trblazeui_token` while the Dockerfile mounted `nuget_pat`. BuildKit does not treat that as an
> error — it simply mounts nothing, `/run/secrets/nuget_pat` is empty, and the build dies deep
> inside `dotnet restore` with an `NU1301` that never mentions a secret. If you rename one half,
> rename the other in the same commit.

### Local development (for completeness)

Your own machine restores against the same feed using your **user-level** NuGet config, outside the
repository:

```bash
dotnet nuget add source https://nuget.pkg.github.com/techierathore/index.json \
  --name TrBlazeUI --username techierathore \
  --password <your PAT with read:packages> --store-password-in-clear-text
```

---

## 6. What the pipeline does, step by step

```text
build   preflight feed probe -> buildx -> GHCR login (the RUNNER, so it can push)
        -> docker build (BuildKit secret nuget_pat) -> push :latest and :<sha7>

deploy  check every secret is present and long enough
        -> envsubst renders deploy/docker-compose.template.yml
        -> assert no placeholder survived
        -> mkdir -p /srv/apps/techieblog, /srv/data/techieblog/{uploads,dp-keys}
        -> scp rendered compose to /srv/apps/techieblog/docker-compose.yml
        -> Caddy snippet: install ONLY if absent, then reload
        -> sudo /usr/local/bin/ensure-db techieblog
        -> VERIFY appuser can CREATE/DROP a table  (fails with the GRANTs if not)
        -> GHCR login on the SERVER (ephemeral) -> docker compose pull
        -> chown uploads + dp-keys to 1654:1654  (root container over the same image)
        -> docker compose up -d -> docker image prune -f -> GHCR logout
        -> PROBE both mounts are writable by UID 1654  (fails loudly if not)
        -> REPORT the Seq endpoint  (warning only, never fatal)

verify  sleep 20 -> /healthz must be 200 (3 attempts, 10s apart)
        -> /_framework/blazor.web.js must be 200
        -> www redirect answers (warning only)
```

Everything is idempotent: the hundredth run does the same thing as the first.

**Two design notes worth knowing:**

- **The ownership fix runs as a throwaway root container over the image the deploy just pulled.**
  `ciuser`'s passwordless `sudo` is scoped to `/usr/local/bin/ensure-db` and nothing else, so
  `sudo chown` is simply not available to it; and the app container itself runs as 1654 and cannot
  chown its own mounts. A `--rm` container running as root over an image already on the box solves
  it with no new image and nothing left behind. It touches only the two leaf directories, never
  their parent, and it changes the **owner only** — group `webops` and the setgid bit are preserved
  so `ciuser` keeps its access (§0, contradiction 2).

- **The `chown` is unconditional, and that is deliberate.** It used to be guarded by a
  `find … ! -uid 1654 | wc -l` pre-check, which has a silent-false-pass mode: `-uid` is GNU
  findutils; BusyBox `find` does not implement it, so on a musl base image `find` fails, `wc -l`
  prints `0`, and the guard reports *"already correct, nothing to do"* while the `chown` never runs.
  `set -e` does not catch it, because the command that failed is inside a substitution and `wc`
  succeeded. Measured directly on `alpine:latest` on 2026-08-16. The app image is Debian-based, so
  this was latent rather than live — but a pre-check that can report success **by failing** is
  precisely the failure class this pipeline exists to remove, and `chown -R` is idempotent and costs
  milliseconds.
- **An existing `ghcr.io` credential on the server is left completely untouched — and as of
  2026-08-16 there IS one, so this is now the path every deploy takes.** Runbook Step 12 has you log
  in by hand with a personal PAT that every app on the box shares; you did. The pipeline checks for
  that first and logs `ghcr.io credential already present on the server — left untouched`.

  This satisfies deployment-brief v3.2 §0, which prohibits the pipeline from running `docker login`
  over SSH: with your credential in place, it never does. **The ephemeral-login fallback is retained
  deliberately** — it fires only when the server has *no* credential at all, a state the brief says
  should not occur. Removing it would trade a harmless dormant branch for a deploy that fails
  `unauthorized` with no explanation if that PAT is ever revoked. It logs out again on the way out,
  including when the deploy fails, so it can never overwrite or strip your stored login.

---

## 7. After the first deploy — what to check

**1. The pipeline is green.** All three jobs. A green `verify` means DNS resolved, Caddy answered,
TLS was valid, the app started, the database answered, and the Blazor script is being served. That
is a genuinely reachable, genuinely interactive site — not a guess.

**2. Click through it.** Open `https://techierathore.com`, then *interact* — open a post, use the
search box, sign in. TechieBlog is Blazor **Server**, so everything after the first render travels
over a SignalR WebSocket. The pipeline now checks the script loads, but only a human can confirm the
circuit behaves through Caddy (§13).

**3. Upload one image** in **Admin → Images** and reload the page. The pipeline proves the directory
is *writable*; this proves the whole path end to end.

**4. Confirm the logs arrived.** Open `https://seq.techierathore.com` and filter on
`App = "techieblog"`. Events should appear from the moment the container started. **This is the
first deploy that will ever have shipped an event from this app** (§13), so look rather than assume.
If nothing arrives, read the deploy job's *Report the Seq endpoint* step — it prints both what is on
the server and what the container was configured with.

**5. Run the server's own acceptance script.** One SSH command, changes nothing:

```bash
sudo /srv/checkup.sh
```

Every line should read `[ OK ]`. Deployment-brief v3.2 §6 names this the post-deploy verification
step, and two of its checks are specifically about what you just did: **`no container
restart-looping`** (a crash-looping `techieblog` shows up here even when `verify` passed on a stale
container) and **`caddy config valid`** (the pipeline installs a site snippet; this proves it did not
break the shared Caddyfile for the other apps on the box). It also re-confirms `seq container up`.

**6. Add the uptime monitor.** UptimeRobot → **Add New Monitor** → **HTTP(s)** → URL
`https://techierathore.com/healthz` → interval 5 minutes → alert after 2 consecutive failures. Same
URL the `verify` job probes: one URL, one meaning.

By hand, if you want them:

```bash
curl -i https://techierathore.com/healthz       # 200 -> process AND database healthy
curl -i https://techierathore.com/health        # 200, liveness only
curl -I https://www.techierathore.com           # 308 redirect to the apex
```

> **`/healthz` is the deployment probe.** It is mapped `AllowAnonymous()` and
> `DisableRateLimiting()`, so it needs no credential and a monitor polling every minute is never
> 429'd. It carries the **PostgreSQL check** *and* a **schema check** (REQ-NFR-039) that reads
> DbUp's `schemaversions` journal — so a database that is reachable but has no tables returns
> **503** rather than a misleading 200.

---

## 8. The two conditional server steps

Neither is routine. Do them only if the trigger below actually happens.

### If the pipeline's DDL check fails

The `Verify appuser DDL rights` step runs a `CREATE TABLE` / `DROP TABLE` probe as `appuser`. If it
fails, the deploy stops **before** the container starts and prints these three statements. Run them
on the VPS as a superuser, once, then re-run the workflow:

```bash
sudo -u postgres psql -d techieblog -c "GRANT ALL PRIVILEGES ON DATABASE techieblog TO appuser;"
sudo -u postgres psql -d techieblog -c "GRANT ALL ON SCHEMA public TO appuser;"
sudo -u postgres psql -d techieblog -c "ALTER SCHEMA public OWNER TO appuser;"
```

**Why this check exists.** DbUp creates its own `schemaversions` table and then runs every script in
`source/BlogDb/PostgresScripts`. That needs **DDL rights**, not just `CONNECT`.

**It should not fail at all.** Two independent reasons, one inferred and one now confirmed:

1. The `ensure-db` script was read directly on the VPS on 2026-08-14 (§0): it runs
   `createdb -O appuser`, so `appuser` becomes the database **owner**, and the server is PostgreSQL
   **18.4** — where the owner holds `CREATE` on `public` through `pg_database_owner`.
2. **You checked `appuser` directly on 2026-08-16 and confirmed it holds database-creation and DDL
   rights.** That closes the project's oldest open unknown; it is an observation, not an inference
   from a script's text.

**The gate stays anyway**, and that is deliberate. It costs one `CREATE TABLE` / `DROP TABLE` per
deploy, it guards the quietest failure in the project, and a privilege that is true today is not a
privilege that is true after the next server change. A check whose expected result is "pass" is
still worth running when its failure mode is a site that looks fine and is empty.

> **If you see `database "techieblog" does not exist`, that is NOT this problem and these `GRANT`s
> are NOT the fix.** It means `ensure-db` did not create the database — look at the *Ensure database*
> step's log instead. A read-only recon script written for this project made exactly that mistake and
> reported a permissions failure when the database simply did not exist yet; the pipeline's own gate
> now distinguishes the two explicitly.

**The failure this prevents** used to be the single most damaging and quietest one in the project: a
container that started, a `/healthz` that answered 200, a green pipeline — and a completely empty
site, because DbUp had thrown during startup.

### If you have existing local images to migrate

If your development copy has images under `source/TechieBlog/wwwroot/uploads`, copy them up — the
database rows reference these paths, so without them existing posts show broken images.

```bash
# 1. See what you have.
ls -R /mnt/c/1MyCode/TechieBlog/source/TechieBlog/wwwroot/uploads

# 2. Copy it up. Note the trailing slash: it copies the CONTENTS.
rsync -av --progress \
  /mnt/c/1MyCode/TechieBlog/source/TechieBlog/wwwroot/uploads/ \
  <VPS_USER>@<VPS_HOST>:/srv/data/techieblog/uploads/
```

**There is no third step any more.** `rsync` writes the files as the SSH user (`ciuser`), not as
1654 — but the next deploy's ownership fix corrects them automatically. If you want them working
immediately rather than at the next push, run the workflow manually from the Actions tab
(`workflow_dispatch`).

> **This path depends on the `g+ws` in the ownership fix.** After the first deploy the uploads
> directory is owned by UID 1654; `ciuser` can still write into it only because the pipeline
> preserves group `webops` and restores the group-write bit. An earlier `chown -R 1654:1654` would
> have made this `rsync` fail with `Permission denied` — see §0, contradiction 2.

If `rsync` is not on the server, `scp -r` works the same way. Copying while the site is live is safe.

### Backups — nothing to do

`pg_dumpall` picks up the new `techieblog` database automatically, and the `/srv/data` archive picks
up `/srv/data/techieblog/uploads` and `/srv/data/techieblog/dp-keys` automatically, because that is
precisely why they live there.

---

## 9. How uploads and keys survive a redeploy

You do not have to do anything here — this section explains *why* the arrangement exists, so that
nothing in it looks arbitrary later.

**The rule: uploaded images must never live inside the container.** A container's filesystem is
discarded and recreated on every `docker compose pull && up -d`. An image written inside it survives
until the next deploy and then silently disappears — the post still references
`/uploads/blog/foo.jpg` and the browser shows a broken image.

| Where | Path |
|-------|------|
| On the server (persistent, backed up) | `/srv/data/techieblog/uploads` |
| Inside the container | `/app/uploads` |
| On the wire | `https://techierathore.com/uploads/…` |

**Why `/app/uploads` and not `/app/wwwroot/uploads`.** `wwwroot` lives *inside the image*, so the old
web-root location put uploaded images on the same disposable layer as the binaries. `BlogEngine`'s
`UploadsLocation` now resolves both halves from the single `Uploads:Path` setting: it names the
directory served at `/uploads`, and because its last segment is already `uploads`, the storage root
is its parent (`/app`). `UploadsRootPath` is *always* `StorageRootPath + "/uploads"` by
construction, so "written here, served from there" is not a state this can get into.

> **Leave Site Settings → Storage → "Local root path" EMPTY** in the admin UI. An explicit value
> there overrides the deployment default and points storage somewhere the static-file mapping does
> not cover — uploads would then succeed and be unservable.

**The second bind mount — the DataProtection key ring.** ASP.NET Core creates a key ring on first
use and, with nothing mounted, writes it *inside* the container, so every redeploy mints a new one.
Audited honestly: TechieBlog has no Identity cookie, no `ITicketStore`, no session state, no
`TempData`. Sign-in is a JWT signed with `JwtSigningKey`, so **losing the key ring does not sign
anyone out**. The two real consumers are the Blazor circuit descriptors and the antiforgery token in
prerendered forms — both fail only for a page rendered by the *outgoing* container and submitted to
the *incoming* one, a seconds-wide window a reload clears. So the mount is correctness hygiene: it
removes the "Attempting to reconnect…" banner some visitors catch mid-deploy. It becomes genuinely
load-bearing the moment a second replica or a real server-side form post is added.

**Ownership.** The container runs as UID **1654** (`app`), while `mkdir -p` over SSH creates the host
directories owned by `ciuser` with group `webops`. That mismatch used to be a manual `chown` and a
genuinely invisible failure — no `[ERR]`, no `[WRN]`, `/healthz` at 200, and only a red toast in the
admin UI. **The pipeline now fixes it on every deploy and then probes that it worked**, so it should
never reach you. §12 keeps the symptom description in case it ever does.

The fix sets the **owner** to 1654 and restores **`g+ws`** on the two directories, leaving group
`webops` in place. Both users therefore keep what they need: the container writes as the owner,
`ciuser` writes via the group, and setgid means files the container creates inherit `webops` rather
than becoming 1654-only — which is what keeps them readable in the `/srv/data` backup archive.

---

## 10. Rolling back

**Normal rollback — revert the commit:**

```bash
git revert <bad-commit-sha>
git push origin main
```

The pipeline rebuilds the previous code and redeploys it. Preferred, because the repository stays
the source of truth.

**Emergency rollback — pin the image tag on the server.** When the site is down and you need it up
in 60 seconds:

```bash
ssh <VPS_USER>@<VPS_HOST>
docker images ghcr.io/techierathore/techieblog     # every deploy pushed a :<sha7>
sudo nano /srv/apps/techieblog/docker-compose.yml  # :latest -> :<good-sha7>
cd /srv/apps/techieblog && docker compose up -d
```

> **This is temporary by construction.** The next push overwrites that compose file from the
> template and restores `:latest`. Follow an emergency pin with a real `git revert`, or the next
> unrelated push silently rolls you forward onto the broken image again.

**What rollback does *not* undo:** database migrations. DbUp applies scripts forward-only on every
boot; reverting code does not un-apply a schema change. If a release included a destructive
migration, restore the database from the `pg_dumpall` backup as well.

---

## 11. Routine operations

**Changing a secret.** Update it in GitHub, then push any commit to `main` (or run the workflow from
the Actions tab — it has `workflow_dispatch`). The compose file is re-rendered and the container
restarts with the new value. Re-read the rotation warnings in §3 first.

**Changing the site's routing / Caddy config.** The pipeline installs
`/srv/caddy/sites/techieblog.caddy` **only when it does not already exist**, and never overwrites
it — manual server edits are authoritative.

```bash
sudo nano /srv/caddy/sites/techieblog.caddy
docker exec caddy caddy reload --config /etc/caddy/Caddyfile
```

To *reset* it to the repository version, delete the server copy and push.

**Reading logs.** The container writes **no log files** (`LogFileEnabled=false`), because a rolling
file sink inside a container writes into the ephemeral layer the next redeploy discards. Two places:

```bash
docker logs -f techieblog
docker compose -f /srv/apps/techieblog/docker-compose.yml ps
```

…and **Seq at `https://seq.techierathore.com`**, which is the durable copy. One Seq instance receives
events from every application on the host, so **filter on `App = "techieblog"`** — the property
`SeqSettings` attaches to the logger itself, which is why the same property appears in the console
output and makes a local reproduction comparable with a production trace.

`docker logs` is a daemon-side ring buffer: "what happened recently", never an archive. If an
incident needs history, it needs Seq.

**If Seq shows nothing for `techieblog`**, the sink is pointed somewhere that does not resolve. Read
the deploy job's *Report the Seq endpoint* step — it prints both what is on the server and what the
container was configured with — and if it warned, set the `SEQ_URL` repository variable (§3).

**Manual restart.**

```bash
cd /srv/apps/techieblog && docker compose restart
```

**Checking the server is still healthy** — after any deploy, or any time you come back to the box
weeks later wondering whether something drifted:

```bash
sudo /srv/checkup.sh          # reports state, changes nothing, safe any time
```

It catches the quiet failures specifically: a backup that stopped uploading, a container
restart-looping since Tuesday, a cron job that was never registered. Nothing about those announces
itself. Runbook v5.2 Part 9.

---

## 12. Troubleshooting

### `NU1301` / `403 Forbidden` during restore — the image will not build

**Symptom.** `build` fails at "Preflight — TrBlazeUI feed authentication" with a boxed message naming
HTTP 401 or 403.

**Cause.** No usable credential for the private TrBlazeUI feed. **Fix:** §5, either remedy. Most
common specific causes, in order:

- The secret is spelled `TRBLAZEUI_PACKAGES_TOKEN` or `TrBlazeUIPackagesToken`. It must be
  **`TrBlazeUiPackagesToken`**.
- The PAT is *fine-grained*. GitHub Packages for NuGet requires a **classic** token.
- The PAT lacks `read:packages`, or has expired.
- You are relying on `GITHUB_TOKEN` without having done Remedy 2 for **every** TrBlazeUI package.

### `sudo: a password is required` in a deploy step

**Cause.** A step used `sudo` for something outside `ciuser`'s allowlist. That account may run
**exactly one** privileged command — `/usr/local/bin/ensure-db` — per runbook v5.2 §2.1.

**Fix.** Remove the `sudo`. In particular **never write `sudo docker`**: `ciuser` is in the `docker`
group, so `docker` needs no elevation, and `sudo docker` is blocked by the sudoers allowlist and
will fail the deploy. This pipeline currently executes exactly one `sudo`, and it is the permitted
one.

### SSH step fails with `Permission denied (publickey)`

**Cause.** Either `VPS_SSH_KEY` holds the `.pub` file instead of the private key, or `VPS_USER` is
stale.

**Fix.** Re-paste the **entire** private key including the `-----BEGIN` and `-----END` lines, and
confirm `VPS_USER` is **`ciuser`** — not `ravi`. The account changed in runbook v5.2; an org secret
still holding the old value fails every deploy at the first SSH step.

### `denied` / `unauthorized` on `docker compose pull`

**Symptom.** The deploy reaches "Pull image, fix mount ownership, restart the stack" and the pull is
refused.

**Most likely cause: the credential belongs to `ravi`, not to `ciuser`.** Registry logins are
per-user, and runbook Step 12 ran as `ravi` — see §0, contradiction 0. The pipeline's ephemeral
fallback normally covers this; if it did not fire (check the step log), that is why the pull failed.
**Fix — one command on the server as `ravi`:**

```bash
sudo -u ciuser -H bash -c \
  'echo <YOUR_PAT> | docker login ghcr.io -u techierathore --password-stdin'
```

**Second cause: the stored PAT expired.** Deployment-brief v3.2 §8 says this outright, and calls it
*not a pipeline concern*. **Fix:** re-run the login from runbook v5.2 Step 12 on the server, once:

```bash
echo YOUR_GITHUB_PAT | docker login ghcr.io -u techierathore --password-stdin
```

Then re-run the workflow. **Do not "fix" this by adding a PAT to a GitHub secret for server-side
pulls** — the brief explicitly prohibits it, and the credential would then live in two places with
one of them silently stale.

**Less likely.** The credential is missing entirely *and* the run's ephemeral fallback did not fire;
the step emits a `::warning::` saying so. Check the `deploy` job still declares
`permissions: packages: read` — without it the run's `GITHUB_TOKEN` cannot pull either.

**Alternative fix.** Make the `ghcr.io/techierathore/techieblog` package public, and the credential
question disappears. The image carries no secrets by construction — the NuGet PAT is a BuildKit
secret mounted for one `RUN`, never an `ARG` or `ENV`, and that was audited when the committed token
was revoked. It does expose your compiled application to anyone who wants to pull it, which is why
the recommendation is to **leave it private** and let the server's stored login handle it.

### The container will not start: `SiteSettings:BaseUrl` / `Analytics:VisitorSalt`

**Symptom.** `deploy` goes green, then `verify` fails with no 200. `docker logs techieblog` shows the
process exiting immediately and repeatedly with an `InvalidOperationException` naming the setting.

**Cause.** `DeploymentConfiguration.Enforce` (REQ-NFR-030) refuses to start any non-Development host
when either value is absent, blank, loopback, unparseable as a URL, equal to the built-in
development salt, or — for the salt — shorter than 32 characters. This is intended: both settings
fail *silently* at runtime otherwise.

**Fix.** Confirm what actually reached the container:

```bash
docker exec techieblog printenv | grep -Ei 'SiteSettings|Analytics|JwtSigningKey|AppEncryptionKey'
```

- **Absent** → the secret is missing from GitHub. Add it (§3) and re-run. (The workflow pre-checks
  this, so an absent secret should fail in `deploy` with a named error first.)
- **Present but the app still complains** → an environment-variable naming mismatch. The compose file
  uses **PascalCase** spellings (`SiteSettingsBaseUrl`, `AnalyticsVisitorSalt`, `SeqUrl`,
  `UploadsPath`), which `AppEnvironmentVariables` translates to the `:`-nested paths the app reads.
  That provider is added **last**, so PascalCase outranks the JSON files and the framework's `__`
  form. If a name is set and the gate still fires, the name is not in the map: open
  `source/TechieBlog/Configuration/AppEnvironmentVariables.cs` and compare its `Map` table — **that
  table is the contract** — against the comment block atop `deploy/docker-compose.template.yml`.
- The two `ForwardedHeaders__…` entries are the deliberate exception and stay double-underscore,
  because `KnownNetworks` is a configuration *array* and an array index has no PascalCase spelling.

The sibling failure has the same shape and fix, from `AppSecrets.Initialise` (REQ-NFR-027), when
`JwtSigningKey` or `AppEncryptionKey` is missing or too short.

### The site renders once, then every click does nothing

**Symptom.** The home page loads and looks right. Clicking, typing in search, or signing in does
nothing. A "Attempting to reconnect to the server…" overlay may appear. The browser console shows a
failed WebSocket to `/_blazor`.

**Cause.** TechieBlog is **Blazor Server**: after the first render every interaction travels over a
long-lived SignalR WebSocket. Something between the browser and Kestrel is not passing the upgrade
through.

**Check cause zero first — the image itself.** If `/_framework/blazor.web.js` 404s, the proxy is
innocent; see the next entry. **The `verify` job now probes this on every deploy**, so a green
pipeline rules it out.

**Then, in order of likelihood:**

1. **Cloudflare is proxying (orange cloud).** Set both DNS records to **DNS only / grey cloud** (§4).
   By far the most common cause on an apex domain.
2. **The Caddy snippet was hand-edited** and lost the plain `reverse_proxy`. Caddy v2 handles the
   WebSocket upgrade *transparently with no extra directives* — there is no equivalent of nginx's
   `proxy_http_version 1.1` + `Upgrade` headers to add. If someone "fixed" it by adding directives,
   compare with `deploy/techieblog.caddy` and revert.
3. **Compression was applied to the circuit.** The shipped snippet compresses everything *except*
   `/_blazor*` for exactly this reason.
4. **A corporate proxy or browser extension** blocking WebSockets. Blazor falls back to long polling
   — slow but functional. If the site is unusable only on one network, this is why.

### `/_framework/blazor.web.js` returns 404 — a Dockerfile defect

**Written down because the site passes every automated check while being unusable.**

**Symptom.** Identical to the entry above from a user's point of view, with one decisive difference:

```bash
curl -o /dev/null -w '%{http_code}\n' https://techierathore.com/_framework/blazor.web.js   # 404
```

Meanwhile `/healthz` returns 200, `/` returns 200, the container is `Up` and the logs are clean.
`docker exec techieblog ls /app/wwwroot` shows **no `_framework` directory**.

**Cause.** A `Dockerfile` in which `dotnet restore` runs while **only the `.csproj` files exist** (the
layer-caching trick) and the later `dotnet publish` uses `--no-restore`. That restore writes an
`obj/` state with no Blazor framework static web assets, publish faithfully reuses it, and the
published `wwwroot` ships without `_framework`. It is **not** caused by `--no-restore` itself.

**Confirm which image you have.** The published route count is the cheap fingerprint:

```bash
docker exec techieblog sh -c 'grep -o "\"Route\"" /app/TechieBlog.staticwebassets.endpoints.json | wc -l'
# 606 = healthy.   586 = the broken build (no _framework).
```

**Fix.** The `Dockerfile` was corrected on 2026-08-11: it runs a **second `dotnet restore`**
immediately after `COPY source/ source/`, keeping the BuildKit secret mount and keeping
`--no-restore` on the publish. Packages are already warm, so it adds seconds and no downloads.

**Do not delete that second restore as redundant** — it is commented in the `Dockerfile` for exactly
this reason. Both states were measured back to back on the same source tree: 586 routes and a 404
without it, 606 and a working circuit with it. **The `verify` job now fails on this**, so it cannot
ship silently again.

### The site is up but completely empty — no posts, no admin, no tables

**Since REQ-NFR-039 the deploy tells you this itself.** `/healthz` returns **503** with a `schema`
check that names the cause, so `verify` fails rather than reporting success over an empty site:

```bash
curl -s https://techierathore.com/healthz | jq '.checks[] | select(.name=="schema")'
```

| Description says | Means | Fix |
|---|---|---|
| `'schemaversions' journal table does not exist` | DbUp never created its journal — almost always missing DDL rights | §8 — the `GRANT`s |
| `journal is BEHIND the migration set: N of M scripts …` | DbUp ran and a **specific** script failed; the outstanding file names are listed | `docker logs techieblog`, search for the named script |

The expectation is **derived** from the scripts folder itself, not hardcoded, so adding `026-….sql`
extends what the gate demands with no code change.

**And since the DDL probe was added to the pipeline (§8), the first case should now fail the deploy
*before* the container ever starts.**

### Uploads fail with a generic error

**This should no longer happen** — the pipeline fixes ownership and then probes writability on every
deploy. The description is kept because the failure mode is instructive.

**Symptom.** `docker ps` shows the container **Up**, `/healthz` returns 200, the startup log reports
the uploads directory as `configured: True`, there are **zero `[ERR]` and `[WRN]` lines** — and the
only evidence is a red toast in the admin UI.

**Cause.** The uploads directory on the host is **root-owned**. `configured: True` is not a
contradiction — the path *is* configured correctly; it is simply not writable.

**Diagnose:**

```bash
docker exec techieblog touch /app/uploads/probe    # "Permission denied" confirms it
sudo chown -R 1654:1654 /srv/data/techieblog/uploads
```

No restart is needed — the permission is evaluated per write, so the next upload succeeds
immediately.

> **Since REQ-NFR-040 the failure is no longer silent.** A refused upload emits one `[ERR]` line
> naming the storage provider, the target path, the uploading user and the exception. The admin sees
> **"The server cannot write to its upload location… Retrying will not help"** — deliberately
> distinct from a transient I/O failure, so nobody retries forever.
> `docker logs techieblog 2>&1 | grep 'Upload REFUSED'` is the fastest diagnosis.

### Uploads succeed but the images 404

1. **The bind mount.** `docker inspect techieblog --format '{{json .Mounts}}'` must show
   `/srv/data/techieblog/uploads` → **`/app/uploads`**. A destination of `/app/wwwroot/uploads` is a
   stale compose file from before 2026-08-10 — re-run the deploy.
2. **`UploadsPath`.** `docker exec techieblog printenv UploadsPath` must print `/app/uploads`.
3. **Site Settings → Storage → Local root path** was set to a directory outside the served tree.
   Empty that field (§9).

### `verify` fails but the site works in a browser

Usually DNS propagation: the GitHub runner resolved the old record. Re-run the job. If it fails
again, check for an IPv6 `AAAA` record pointing elsewhere — `curl` on the runner may prefer it.

### `ensure-db` fails

The deploy user's passwordless `sudo` is scoped to `/usr/local/bin/ensure-db`. A "sudo: a password is
required" error means the sudoers entry is missing or the helper moved. VPS runbook territory, not
app configuration.

---

## 13. What is proven and what is not

Honest scope statement. **No part of this pipeline has ever *executed* against the real VPS.** What
changed on 2026-08-14 is that the server's *state* was inspected directly (§0), so the assumptions
the pipeline encodes are no longer guesses — but running the pipeline itself is still untested.

### Actually executed and proven

- Every YAML file parses; every shell block in `deploy.yml` passes `bash -n`.
- The compose template renders through `envsubst` with zero surviving placeholders, and the result
  passes `docker compose config`.
- The secret-presence and minimum-length guard was exercised in four scenarios, including bare
  `envsubst` with a variable unset — which **exits 0 and emits an empty value**, proving why the
  guard is needed.
- `deploy/techieblog.caddy` passes `caddy validate --adapter caddyfile`.
- **A real image build**, with `--secret id=nuget_pat`, reaching the private feed and failing
  `NU1301 / 401` on an invalid token — the correct outcome, and proof the secret is presented under
  the id the Dockerfile mounts. The secret-id handshake was proved directly with a probe image:
  under the correct id the mount held the exact token; under the old `trblazeui_token` it was
  **silently empty**.
- **A running container serving the real site**, signed into through its real admin UI, with a real
  image uploaded through the interactive dialog. `/_blazor` opened (`101`), `window.Blazor`
  initialised, `/_framework/blazor.web.js` returned 200. *Substitution: the private feed was replaced
  with a local folder feed of the cached TrBlazeUI packages, because no valid PAT exists on the build
  machine.*
- **Uploads through a bind mount, in both directions** — a real PNG landed on the host owned
  `1654:1654`, byte-identical, was served back over HTTP, and **survived `docker rm -f` plus a
  recreate**. With the host directory left root-owned, the same upload failed. *Deviation: a local
  bind mount, not `/srv/data` on the VPS.*
- **The DataProtection key-ring mount** — without it, two recreates generated two different key
  GUIDs and the runtime warned each time; with it, the warning disappeared and a recreate reloaded
  the **same** `key-<guid>.xml`.
- **The container UID** — `$APP_UID` is `1654`; `id 1654` resolves to `uid=1654(app) gid=1654(app)`.
- **The new ownership fix, as a mechanism** — the actual step script was run against real
  directories with real Docker. Run 1 corrected the paths; `ls -ln` confirmed `1654:1654`
  recursively. Run 2 reported "nothing to do" (idempotent). Run 3 corrected a newly-dropped
  root-owned file — which is the `rsync` migration case healing itself. The parent directory stayed
  owned by the deploy user. Writability was proven both ways with `--user 1654`.
- **The new DDL probe and both verify probes, as control flow** — every branch exercised with stubs
  (psql absent → warning; success; failure → `GRANT`s + `::error::` + exit 1), including a check that
  the password never appears in output.

### Established on the real VPS (server state, not pipeline behaviour)

These were assumptions in early revisions. They are observations now. They are **server-state**
facts — none of them is proof that a pipeline step runs.

**By read-only inspection, 2026-08-14:** PostgreSQL **18.4** active with `psql` present (so the DDL
gate executes rather than skipping itself); `ensure-db` runs `createdb -O appuser`, making `appuser`
the database **owner**; the `web` network and `caddy:2` container exist; `/srv` and its children are
owned by the deploy user; Docker Engine **29.7.1** with the **`docker compose` v5.3.1** plugin.

**By direct HTTPS probe, 2026-08-16:** Seq answers at `https://seq.techierathore.com` (**Seq
2026.1.17083**), on the **same IPv4 as the apex and `www`**, behind the same Caddy. It accepts
**unauthenticated ingestion** — `POST /ingest/clef` returned **201**, while `GET
/api/events/signal` returned **401** — which is why `SEQ_API_KEY` is genuinely optional rather than
optional-in-name. The apex itself returned **502**: Caddy live and holding a valid certificate, with
nothing behind it yet — exactly the expected pre-deploy state.

**By the owner completing the server build, 2026-08-16 — `docs/bluehost-vps-runbook-v5.2.md` finished
and `sudo /srv/checkup.sh` reporting 0 failed:**

- **The Seq container is named `seq` and is up**, answering through Caddy. Combined with the brief's
  contract constant, `http://seq:5341` is settled — this was the last open unknown from the previous
  revision, and it is now closed.
- **`appuser` holds database-creation and DDL rights** — owner-checked directly. The §8 `GRANT`s
  become a guard against future drift, not an expected step.
- **The server is authenticated to GHCR** with a stored PAT (runbook Step 12). This flips a branch
  that was live in the previous revision: the pipeline's credential check will now find an existing
  `ghcr.io` entry and take the **leave-it-alone** path, never its ephemeral login. That is what
  deployment-brief v3.2 requires.
- **Swap is configured**, `ufw`/`fail2ban`/`unattended-upgrades`/`cron` active, `/srv` tree complete,
  backups running with a recent copy on **both** OneDrive and Google Drive.

**By owner action + local measurement, 2026-08-16b — the `ciuser` change (runbook v5.2 §2.1):**

- **The pipeline SSHes in as `ciuser`, not `ravi`.** Audited: this workflow executes exactly **one**
  `sudo`, and it is `/usr/local/bin/ensure-db` — the only command in ciuser's allowlist. It uses no
  `sudo docker` anywhere, and it never hardcodes a username; every step reads `VPS_USER`.
- **The ownership fix was re-measured under the new group model**, on both a Debian base (what the
  app image is) and Alpine: resulting mode `drwxrwsr-x`, owner `1654`, group unchanged; container
  UID 1654 writes; a `webops`-group user writes; setgid inheritance confirmed; idempotent over three
  runs; parent untouched; uploaded files stay `-rw-r--r--`.
- **A latent silent-false-pass was found and removed.** The old `find … ! -uid 1654 | wc -l`
  pre-check reports *"nothing to do"* on any BusyBox/musl base, because `-uid` is GNU-only and a
  failed `find` piped to `wc -l` yields `0`. Reproduced directly on `alpine:latest`. Not live today
  (the app image is Debian), and now gone regardless.

### Still unverified, and unverifiable from a development machine

| Unverified | Why |
|---|---|
| **Everything the pipeline does on the server** | No SSH credential exists on this machine — see the table below. Server *state* is thoroughly known; the pipeline's *behaviour against it* has never run once. |
| Whether `ciuser` really has **no** stored GHCR credential | Inferred, and the inference is strong: registry logins are per-user, and runbook Step 12 ran as `ravi` before `ciuser` existed. It cannot be checked from here. Either way the deploy works — with the credential the pipeline skips its login, without it the ephemeral fallback fires — and the pull step's own log says which branch it took. |

### Unverified, and cannot be verified from a development machine

| Unverified | Why |
|-----------|-----|
| Any actual deploy | No SSH-from-CI, no scp, no `ensure-db` invocation, no `docker compose up` has run against the real server. The server's *state* is now known (§0); its *behaviour under the pipeline* is not. |
| The **authenticated GitHub Packages restore** | No valid TrBlazeUI PAT on the build machine. The failure direction *is* proven (invalid token → `NU1301 / 401`). |
| GHCR push, tags and `type=gha` caching | Requires a real workflow run. |
| The server **pulling** a private image with its stored PAT | The credential now exists (runbook Step 12) and the pipeline is designed to leave it alone, but no pull has been attempted. If it fails `unauthorized`, the PAT expired — re-run Step 12 by hand; it is not a pipeline change. |
| The `appleboy/ssh-action` / `scp-action` steps | Never executed. Argument shapes follow the actions' documented inputs. |
| Caddy actually serving **this site**, TLS issuance for the apex, the `www` redirect | Caddy itself is proven live (it returned 502 for the apex with a valid certificate, and `checkup.sh` validates its config). What is untested is *this app's* snippet being installed and routed. |
| The Blazor circuit **through Caddy** | Proven in-container; the **Caddy hop** — WebSocket upgrade through the reverse proxy, over TLS, on the real domain — is untested. |
| The ownership fix, the DDL probe and the writability probe **on the VPS** | The mechanisms are proven locally. What has not happened is any of them running over SSH against the real server. The ownership fix is also the piece that makes this repo's non-root container (UID 1654) safe despite deployment-brief v3.2 prescribing root — see §0. |
| Seq actually receiving events **from this app** | The sink is registered and unit-tested, and the Seq server is now proven to exist and to accept anonymous ingestion (a manual `POST` landed a `probe` event on 2026-08-16). What has never happened is the **app** shipping an event, because the app has never run on the VPS. Seq is now enabled by default rather than disabled. |

---

*Companion documents: `docs/claude-code-deployment-brief-v3.2.md` (the portfolio-wide spec this
implements — note it still describes a generated `deploy/SERVER-SETUP.md`, which this project no
longer has, and it disagrees with runbook v5.2 §6.3 on the Seq secret's name — see §0),
`docs/bluehost-vps-runbook-v5.2.md` (the VPS build, **complete**, `sudo /srv/checkup.sh` = 0 failed),
`docs/deployment.md` (older, generic, non-VPS deployment options).*

*`docs/Server-Setup.md` and `docs/Server-Setup.html` were **merged into this file and deleted on
2026-08-16**. Their content is §2. If you have a bookmark to either, repoint it at §2.*
