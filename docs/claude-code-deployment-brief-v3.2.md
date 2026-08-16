# App Deployment Spec — VPS Production Pipeline (v3.2)

Purpose: the single reusable spec for taking any app in the portfolio from repo → live on the VPS, with **zero manual server work per app**. Designed to be absorbed into a spec-driven development framework: Section 0 is the answer key (facts Claude Code must never have to ask about), Section 2 the environment contract, Section 3 the per-app variables, Section 4 the master prompt, Section 5 the pipeline contract, Section 6 the human checklist.

*v3.2 changes: CI account renamed to `ciuser`. Previous v3.1 changes: pipeline SSH account is now the dedicated `ciuser` user (runbook v5.2 §2.1), not `ravi` — new Section 0 block, contract row, prompt constraints, and prohibited entries covering sudo and permissions. Previous v3 changes: new **Section 0** answering the questions Claude Code kept stopping to ask; Seq API keys reconciled to **per-app repo-level secrets** (runbook v5 §6.3) — this replaces the org-level `SEQ_API_KEY` in v2; GHCR pull authentication, container filesystem ownership for uploads, and Postgres reachability made explicit; prohibited-actions list added to the prompt; Section 5 numbering fixed; Section 6 gains the Seq key step and a post-deploy checkup.*

Companion doc: **VPS Runbook v5.2** — the server build. This brief assumes that build is complete and `sudo /srv/checkup.sh` (runbook Part 9) reports `0 failed`.

Per app, exactly TWO manual tasks exist: adding the DNS A record, and creating the app's Seq API key. Both take about two minutes. Everything else is pipeline-automated.

---

## Section 0 — Answer key: facts Claude Code must never ask about

Every question that has interrupted a generation run, answered here. If a run still stops to ask something, add the answer to this section rather than answering it in chat — that's how this document stops costing you round-trips.

**Seq / logging**

| Question | Answer |
|---|---|
| What is Seq's address from an app container? | `http://seq:5341` — plain HTTP, container name `seq`, ingestion-only port, resolved over the shared `web` Docker network. |
| Do I need the Seq UI URL in app config? | No. `https://seq.techierathore.com` is for humans in a browser only. No app ever references it. |
| HTTPS or a certificate for Seq ingestion? | Neither. Traffic never leaves the Docker network. |
| What authenticates the app to Seq? | An API key in config key `Seq:ApiKey`. One key per app (see below). |
| Is Seq already running? | Yes — container `seq`, on network `web`, started by the runbook. The pipeline never creates, starts, or configures it. |
| What if Seq is down when an app starts? | The app must start normally. The Serilog Seq sink buffers and retries; logging must never be a startup dependency. |
| Seq config on my Mac during local dev? | None. `Seq:Url` is unset locally, so the sink is not registered and Serilog writes to console only. |

**Database**

| Question | Answer |
|---|---|
| Postgres host from inside a container? | `172.17.0.1` port `5432` — the Docker bridge address of the host. Postgres runs natively on Ubuntu, not in Docker. Never `localhost`, never `postgres`, never a container name. |
| Which user? | `appuser`, password from the `DB_PASSWORD` secret. Never the `postgres` superuser. |
| Who creates the database? | The pipeline, via `sudo /usr/local/bin/ensure-db <db> [vector]`. It is idempotent and already passwordless for the deploy user (sudoers entry `/etc/sudoers.d/ensure-db`). The workflow runs no other SQL. |
| How does pgvector get enabled? | Pass `vector` as the second argument to `ensure-db`. Nothing else. |
| Is Postgres reachable from containers already? | Yes — `listen_addresses` includes `172.17.0.1` and `pg_hba.conf` allows the Docker subnet. Configured in runbook Steps 6–7; do not modify either file. |

**SSH / deploy account**

| Question | Answer |
|---|---|
| Which user does the pipeline SSH in as? | `ciuser` — a dedicated CI account created in runbook v5.2 §2.1. Never `ravi`. The value is in the org secret `VPS_USER`; the workflow just uses the secret and never hardcodes a username. |
| What can `ciuser` do? | Run Docker (it is in the `docker` group), write to `/srv/apps`, `/srv/data`, and `/srv/caddy/sites` (via the `webops` group), and run exactly one sudo command: `/usr/local/bin/ensure-db`. |
| What can it NOT do? | Any other sudo command, and anything in `/srv/backups` or the main `Caddyfile` — both are `ravi`-owned by design. If a workflow step needs either, the step is wrong. |
| Do I need `sudo` before `docker` commands? | No. `ciuser` is in the `docker` group. Adding `sudo docker` will fail the sudoers allowlist. |
| Does the pipeline create folders under `/srv`? | Yes, its own subfolders only — `/srv/apps/{APP_NAME}` and `/srv/data/{APP_NAME}/uploads`. Group inheritance is already handled by the setgid bit; never `chown` or `chmod` from a workflow. |

**Registry / images**

| Question | Answer |
|---|---|
| Does the pipeline need to log in to GHCR on the server? | No. The server is already authenticated as `techierathore` via a stored PAT (runbook Step 12). The workflow must not run `docker login` over SSH, and must not put a PAT in a secret for that purpose. |
| GHCR login in the build job? | Yes — with the built-in `GITHUB_TOKEN` and `permissions: packages: write`. That is separate from the server's pull credentials. |
| Public or private packages? | Private is fine; the server's stored login covers it. If `docker compose pull` ever fails with `unauthorized`, the PAT has expired — a human re-runs the login from runbook Step 12. Not a pipeline concern. |

**Filesystem / permissions**

| Question | Answer |
|---|---|
| Do `/srv/apps` and `/srv/data` exist? | Yes, both created in runbook Step 8 and owned by `ravi` (the deploy user). The pipeline creates only its own subfolders under them. |
| Which user runs inside the container? | Root — the default for the `aspnet` base image. **Do not add a `USER` directive to the Dockerfile.** A non-root container user cannot write to the `ravi`-owned uploads bind mount, and the failure surfaces at runtime as a permission error on first upload, not at deploy time. |
| Does the pipeline need `chown` on the uploads folder? | No, given the above. |

**Caddy / routing**

| Question | Answer |
|---|---|
| Where do snippets go? | Host `/srv/caddy/sites/{APP_NAME}.caddy` = container `/etc/caddy/sites/`. The main `Caddyfile` contains `import sites/*.caddy` and is never edited by anything. |
| How is Caddy reloaded? | `docker exec caddy caddy reload --config /etc/caddy/Caddyfile`. |
| What if the snippet already exists? | Skip it and skip the reload. Server-side files are authoritative — see the create-only rule in Section 5. |
| Who creates the TLS certificate? | Caddy, automatically, on first request to the new hostname. Nothing to configure, provided the DNS A record exists first. |

**Scope**

| Question | Answer |
|---|---|
| Should I add tests, linting, staging, or a second environment? | No. Build, deploy, verify. Nothing else. |
| Should I add monitoring, alerts, or a healthcheck service? | No. UptimeRobot is configured by hand, once per app (Section 6). |
| Should I touch DNS? | Never. The pipeline has no DNS credentials by design. It prints the record for a human to add. |

---

## Section 1 — What the pipeline does on every push to main

```
push to main
 ├─ build:    Docker image → GHCR (:latest + commit SHA)
 ├─ deploy:   over SSH —
 │             1. mkdir -p /srv/apps/APP_NAME  and  /srv/data/APP_NAME/uploads (if uploads)
 │             2. sudo ensure-db DB_NAME [vector]        (idempotent DB + extension)
 │             3. place rendered docker-compose.yml       (secrets injected from GitHub)
 │             4. place Caddy snippet → /srv/caddy/sites/APP_NAME.caddy
 │                (create-only: skipped if INTERNAL or if the file already exists on the server)
 │             5. docker compose pull && up -d && image prune
 │             6. reload Caddy config (only if the snippet was newly created)
 └─ verify:   curl https://DOMAIN/healthz (or internal healthz if INTERNAL) — fail loudly

(Not in the pipeline by design: the DNS A record and the Seq API key — two once-per-app
manual steps; the generated SERVER-SETUP.md prints exactly what to do for both.)
```

Everything is idempotent: first deploy and hundredth deploy run the same steps.

---

## Section 2 — Environment contract (constants — never change per app)

| Constant | Value |
|---|---|
| Registry | `ghcr.io/techierathore` — server already authenticated; pipeline never logs in server-side |
| Docker network | `web` (external) |
| App port inside container | `8080` |
| Container user | root (image default — **no `USER` directive**) |
| Compose path on server | `/srv/apps/{APP_NAME}/docker-compose.yml` |
| Uploads host path | `/srv/data/{APP_NAME}/uploads` → container `/app/uploads` |
| DB from containers | host `172.17.0.1`, port `5432`, user `appuser` |
| DB creation helper | `sudo /usr/local/bin/ensure-db <db> [vector]` (passwordless for deploy user) |
| Seq ingestion (internal) | `http://seq:5341` — container `seq` on network `web`, plain HTTP |
| Seq UI (humans only) | `https://seq.techierathore.com` — never referenced by app config |
| Caddy snippets dir | host `/srv/caddy/sites/*.caddy` = container `/etc/caddy/sites`; reload via `docker exec caddy caddy reload --config /etc/caddy/Caddyfile` |
| Org-level secrets | `VPS_HOST`, `VPS_USER` (= `ciuser`), `VPS_SSH_KEY`, `DB_PASSWORD` |
| SSH account | `ciuser` — docker group, `webops` group on `/srv/apps` `/srv/data` `/srv/caddy/sites`, sudo limited to `ensure-db`, no access to `/srv/backups` |
| Repo-level secrets | `SEQ_API_KEY` — **per-app value**, created in Seq per runbook v5 §6.3. Same secret name in every repo, different value. |
| Health endpoint | `/healthz`, anonymous, excluded from any auth middleware |
| DNS | manual: one A record per app (`DOMAIN` → VPS IP) added in the registrar before first push; **DNS only** (grey cloud) if the domain sits on Cloudflare |
| Internal service-to-service calls | `http://{container-name}:8080` — apps on the VPS call sibling APIs by container name, never via public URL |
| Post-deploy verification | `sudo /srv/checkup.sh` on the server (runbook Part 9) |

**Why `SEQ_API_KEY` is repo-level and not org-level (changed in v3):** a per-app key stamps `App` server-side so events stay attributable even if the app's enrichment is wrong, can be revoked without redeploying the other seventeen apps, and lets one noisy app be throttled at the key. Keeping the *secret name* identical across repos means the workflow YAML stays byte-identical — only the value differs.

---

## Section 3 — Per-app variables (fill per app)

| Variable | Value for THIS app | Notes |
|---|---|---|
| `APP_NAME` | e.g. `appmgrapi` | **the app's single infra identity** — lowercase, no spaces (GHCR requires lowercase). Becomes container/image/folder/snippet name and the Seq `App` property |
| `DOMAIN` | e.g. `appmgrapi.techierathore.com` — or `INTERNAL` | `INTERNAL` = no DNS, no Caddy; reachable only as `http://APP_NAME:8080` by siblings |
| `DB_NAME` | e.g. `blog` — or `NONE` | one database per app |
| `NEEDS_PGVECTOR` | `yes` / `no` | |
| `HAS_UPLOADS` | `yes` / `no` | user/admin file uploads (banners, images, docs) |
| `DOTNET_VERSION` | e.g. `10` | |
| `PROJECT_PATH` | e.g. `src/Blog/Blog.csproj` | |
| `MEM_LIMIT` | `384m` default; `512m` heavier apps | total across all apps must stay under ~6 GB |
| `INTERNAL_APIS` | e.g. `appmgrapi` — or none | sibling APIs this app consumes; config gets `http://name:8080` base URLs |
| `HAS_EF_MIGRATIONS` | `yes` / `no` | `yes` → `Database.Migrate()` on startup |

**Naming rule (removes all ambiguity):** an app has four names — C# project name (`AppManagerApi`), repo name, `APP_NAME`, and `DOMAIN` — and only `APP_NAME` must be consistent across infrastructure. `PROJECT_PATH` is the bridge to the .NET world, so PascalCase project names never leak into infra. Convention: **`APP_NAME` = the subdomain label** (`appmgrapi.techierathore.com` → `appmgrapi`); apex-domain apps pick a short lowercase name once (blog → `techieblog`). `INTERNAL_APIS` entries and Caddy snippet filenames always refer to `APP_NAME` values, never project names.

---

## Section 4 — Master prompt for Claude Code

Paste into Claude Code inside the app's repo after replacing `{{PLACEHOLDERS}}`. Paste **Section 0 above it** in the same message the first few times — it is the reference that stops the run from pausing to ask.

```
You are preparing this Blazor/.NET app for fully automated production deployment to my VPS.
Every environment fact you need is stated below. Do not ask me to confirm any of them; if
something is genuinely absent, state the assumption you made in your final summary instead
of stopping.

INFRASTRUCTURE ALREADY RUNNING (do not create, modify, or verify any of it):
- Ubuntu 24.04. Native Postgres 18 + pgvector on the host. Containers reach it at
  172.17.0.1:5432 as user appuser. Never localhost, never a container name.
- Idempotent DB helper on the server: "sudo /usr/local/bin/ensure-db <db> [vector]",
  already passwordless for the deploy user. This is the ONLY SQL the workflow may run.
- Docker with external network "web".
- Caddy container named "caddy", loading host /srv/caddy/sites/*.caddy (= /etc/caddy/sites
  in the container), reloaded with:
  docker exec caddy caddy reload --config /etc/caddy/Caddyfile
- Seq container named "seq", on network "web". Apps send logs to http://seq:5341 — plain
  HTTP, no TLS, no port mapping needed, resolved by container name. The Seq web UI at
  https://seq.techierathore.com is for humans only and must NOT appear in app config.
  Seq being unavailable must never prevent the app from starting.
- GHCR under ghcr.io/techierathore. The server is ALREADY logged in to GHCR with a stored
  PAT — the workflow must NOT run docker login over SSH. The build job logs in to GHCR
  separately using the built-in GITHUB_TOKEN.
- The pipeline connects over SSH as a dedicated CI user (secret VPS_USER, value "ciuser"),
  NOT my personal account. It is in the docker group, so docker commands need no sudo. It has
  exactly one sudo permission: /usr/local/bin/ensure-db. It can write to /srv/apps, /srv/data
  and /srv/caddy/sites and nothing else — /srv/backups and the main Caddyfile are off limits.
- /srv/apps and /srv/data exist and are group-writable by that CI user, with setgid set, so
  never chown or chmod anything from the workflow.
- DNS is managed manually by me. The pipeline must NEVER touch DNS; instead print the
  record I need to add (item 6).

SECRETS AVAILABLE: VPS_HOST, VPS_USER, VPS_SSH_KEY, DB_PASSWORD (org-level);
SEQ_API_KEY (repo-level, unique to this app). Never echo a secret value in logs.

APP VARIABLES:
- APP_NAME: {{APP_NAME}}
- DOMAIN: {{DOMAIN}}                 (INTERNAL = skip DNS job and Caddy snippet entirely)
- DB_NAME: {{DB_NAME}}               (NONE = skip all database wiring)
- NEEDS_PGVECTOR: {{yes/no}}
- HAS_UPLOADS: {{yes/no}}
- HAS_EF_MIGRATIONS: {{yes/no}}
- DOTNET_VERSION: {{10}}
- PROJECT_PATH: {{src/App/App.csproj}}
- MEM_LIMIT: {{384m}}
- INTERNAL_APIS: {{none | list of sibling container names this app calls}}

PRODUCE ALL OF THE FOLLOWING:

1. DOCKERFILE (repo root): multi-stage — sdk:{{DOTNET_VERSION}} publish of PROJECT_PATH,
   aspnet:{{DOTNET_VERSION}} runtime, ASPNETCORE_URLS=http://+:8080, EXPOSE 8080.
   Do NOT add a USER directive — the container must run as root so it can write to the
   host-owned uploads bind mount.

2. APP CODE WIRING:
   a. Health: /healthz via AddHealthChecks; include AspNetCore.HealthChecks.NpgSql DB check
      unless DB_NAME=NONE. The endpoint must be anonymous — exclude it from any auth
      middleware, since the deploy pipeline curls it with no credentials.
   b. Logging: Serilog.AspNetCore + Serilog.Sinks.Seq. Console always; Seq sink registered
      only when config "Seq:Url" is non-empty (apiKey from "Seq:ApiKey").
      Enrich.WithProperty("App","{{APP_NAME}}"). Local dev must run with no Seq configured
      and must not warn or fail because of it.
   c. Config-driven everything: ConnectionStrings:Default; if HAS_UPLOADS, uploads dir from
      "Uploads:Path" (local fallback folder) with static file serving at /uploads via
      PhysicalFileProvider; for each entry in INTERNAL_APIS, a typed HttpClient whose
      BaseAddress comes from config key "Services:<name>:Url". No hardcoded paths, hosts,
      or secrets anywhere.
   d. If HAS_EF_MIGRATIONS=yes: Database.Migrate() on startup.

3. deploy/docker-compose.template.yml — production compose with ${DB_PASSWORD} and
   ${SEQ_API_KEY} placeholders (rendered by the workflow via envsubst; real values never
   enter the repo). Service: image ghcr.io/techierathore/{{APP_NAME}}:latest, container_name
   {{APP_NAME}}, restart unless-stopped, mem_limit {{MEM_LIMIT}}, networks [web] (external),
   environment: ASPNETCORE_ENVIRONMENT=Production; ConnectionStrings__Default=
   "Host=172.17.0.1;Port=5432;Database={{DB_NAME}};Username=appuser;Password=${DB_PASSWORD}"
   (omit if NONE); Seq__Url=http://seq:5341; Seq__ApiKey=${SEQ_API_KEY};
   Uploads__Path=/app/uploads plus volume bind /srv/data/{{APP_NAME}}/uploads:/app/uploads
   (only if HAS_UPLOADS); Services__<name>__Url=http://<name>:8080 per INTERNAL_APIS entry.

4. deploy/{{APP_NAME}}.caddy — Caddy snippet: "{{DOMAIN}} { reverse_proxy {{APP_NAME}}:8080 }".
   If DOMAIN is an apex like example.com, also handle www redirect to apex. Omit this file
   entirely if DOMAIN=INTERNAL.

5. .github/workflows/deploy.yml implementing exactly this pipeline:
   - Trigger: push to main. Concurrency group "deploy-{{APP_NAME}}", cancel-in-progress: false.
   - Job build: checkout, buildx, GHCR login with GITHUB_TOKEN (permissions packages:write),
     build+push tags :latest AND ${GITHUB_SHA::7}, cache-from/cache-to type=gha.
   - Job deploy (needs build): render deploy/docker-compose.template.yml with envsubst
     (DB_PASSWORD, SEQ_API_KEY from secrets) → scp rendered file to
     /srv/apps/{{APP_NAME}}/docker-compose.yml (appleboy/scp-action; ensure dirs first via
     appleboy/ssh-action: mkdir -p /srv/apps/{{APP_NAME}} and, if HAS_UPLOADS,
     /srv/data/{{APP_NAME}}/uploads).
     Caddy snippet — CREATE-ONLY, never overwrite (skip entirely if INTERNAL): over SSH,
     test [ -f /srv/caddy/sites/{{APP_NAME}}.caddy ]. If it EXISTS: log
     "caddy entry already present — skipped", do NOT copy, do NOT reload Caddy.
     Only if ABSENT: scp deploy/{{APP_NAME}}.caddy to /srv/caddy/sites/{{APP_NAME}}.caddy,
     then docker exec caddy caddy reload --config /etc/caddy/Caddyfile.
     Then over SSH:
       sudo /usr/local/bin/ensure-db {{DB_NAME}} {{"vector" if NEEDS_PGVECTOR else ""}}  (skip if NONE)
       cd /srv/apps/{{APP_NAME}} && docker compose pull && docker compose up -d && docker image prune -f
   - Job verify (needs deploy): if public — sleep 20 then curl -sf https://{{DOMAIN}}/healthz,
     3 retries 10s apart, fail workflow on no 200. If INTERNAL — over SSH:
     docker exec {{APP_NAME}} curl -sf http://localhost:8080/healthz.

6. deploy/SERVER-SETUP.md — a short generated summary containing: (a) unless DOMAIN=INTERNAL,
   the exact DNS record I must add manually BEFORE the first push, as a table (Type A /
   Name = subdomain part or @ / Value = my VPS IP / note "DNS only" if on Cloudflare);
   (b) the Seq API key step: create a key in Seq titled "{{APP_NAME}}" with applied property
   App={{APP_NAME}}, then save it as the repo secret SEQ_API_KEY; (c) the UptimeRobot monitor
   URL https://{{DOMAIN}}/healthz; (d) anything else needing one-time human action.

PROHIBITED — do not do any of these, and do not propose them:
- docker login on the server; any PAT in a workflow secret for server-side pulls
- sudo in front of any docker command (the CI user is in the docker group; sudo docker is
  blocked by the sudoers allowlist and will fail the deploy)
- any sudo command other than /usr/local/bin/ensure-db
- chown, chmod, or any write to /srv/backups or /srv/caddy/Caddyfile
- hardcoding the SSH username anywhere — always use the VPS_USER secret
- any docker compose down -v, docker volume rm, or docker system prune -a
- any SQL beyond the ensure-db call; any edit to postgresql.conf or pg_hba.conf
- creating, editing, or deleting Caddy snippets belonging to other apps, or the main Caddyfile
- any DNS operation
- a USER directive in the Dockerfile
- test jobs, staging environments, extra infrastructure, or additional monitoring services

Finish with a summary of every file created/changed and any assumptions you made.
```

---

## Section 5 — Pipeline contract (what a correct implementation guarantees)

1. **Idempotent**: re-running any deploy on an unchanged commit changes nothing and breaks nothing.
2. **Caddy routing is create-only**: an existing `/srv/caddy/sites/<APP_NAME>.caddy` — whether created manually or by an earlier pipeline run — is never overwritten or deleted. Routing changes are deliberate manual edits on the server; the pipeline only fills gaps.
3. **Secrets never in the repo**: compose is a template; real values injected at deploy time from GitHub secrets; the rendered server-side file is readable only on the server.
4. **INTERNAL apps** have zero public surface: no DNS record, no Caddy snippet, no open port — verify runs inside the container.
5. **Public apps** are verified end-to-end through the real URL (DNS → Caddy → TLS → app → DB), so a green pipeline means a genuinely reachable site. This requires the A record to exist before the first push.
6. **Log attribution survives misconfiguration**: the per-app Seq key stamps `App` server-side, so events are attributable even if the app's own enrichment is missing.
7. **Rollback** = `git revert && git push`, or emergency manual: edit the image tag to a previous SHA on the server and `docker compose up -d`.

---

## Section 6 — Human checklist per app (~10 minutes, no SSH needed)

1. Fill the Section 3 table → replace placeholders in the Section 4 prompt → run in Claude Code → review its summary → commit.
2. **Seq API key**: in Seq → Settings → API Keys → Add API Key, title `APP_NAME`, applied property `App` = `APP_NAME` → copy the key → add it as the repo secret `SEQ_API_KEY` on this app's GitHub repo. (Per runbook v5 §6.3 — never reuse another app's key.)
3. **DNS A record** exactly as printed in the generated `deploy/SERVER-SETUP.md` (registrar dashboard, 2 minutes; skip if INTERNAL). Do this before the first push or the verify job fails.
4. `git push` → watch Actions go green → open `https://DOMAIN/healthz`.
5. Add one UptimeRobot HTTP monitor for the new `/healthz` URL (skip if INTERNAL).
6. Confirm the app appears in Seq (filter `App = 'APP_NAME'`) and run `sudo /srv/checkup.sh` — `no container restart-looping` and `caddy config valid` should still pass with the new container running.

Backups: nothing to do — `pg_dumpall` picks up the new database and `/srv/data` archiving picks up the new uploads folder automatically.

---

## Section 7 — Filled examples (note how project names and APP_NAMEs differ freely)

**Blog** (apex domain, no subdomain — short name chosen once):
`APP_NAME=techieblog · DOMAIN=techierathore.com · DB_NAME=techieblog · NEEDS_PGVECTOR=no · HAS_UPLOADS=yes · HAS_EF_MIGRATIONS=yes · DOTNET_VERSION=10 · PROJECT_PATH=src/Blog/Blog.csproj · MEM_LIMIT=512m · INTERNAL_APIS=appmgrapi`

**AppManager API** (C# project `AppManagerApi` — infra name from the subdomain):
`APP_NAME=appmgrapi · DOMAIN=appmgrapi.techierathore.com · DB_NAME=appmanager · NEEDS_PGVECTOR=no · HAS_UPLOADS=no · HAS_EF_MIGRATIONS=yes · DOTNET_VERSION=10 · PROJECT_PATH=src/AppManagerApi/AppManagerApi.csproj · MEM_LIMIT=384m · INTERNAL_APIS=none`
Public clients (desktop/MAUI) call `https://appmgrapi.techierathore.com`; sibling apps on the VPS call `http://appmgrapi:8080`.

**AppManager UI:**
`APP_NAME=appmanager · DOMAIN=appmanager.techierathore.com · DB_NAME=NONE (uses the API) · HAS_UPLOADS=no · HAS_EF_MIGRATIONS=no · PROJECT_PATH=src/AppManager/AppManager.csproj · INTERNAL_APIS=appmgrapi`

---

## Section 8 — When a deploy fails, look here first

| Symptom | Cause | Fix |
|---|---|---|
| SSH step fails: permission denied | `VPS_SSH_KEY` holds the `.pub` file instead of the private key, or `VPS_USER` is stale | Re-paste the full private key incl. BEGIN/END; confirm `VPS_USER` = `ciuser` |
| `sudo: a password is required` | Workflow used sudo for something outside the allowlist | Remove the sudo — only `ensure-db` is permitted |
| Verify job fails, 502 in browser | Container crash-looping | `docker logs {{APP_NAME}}` — read the actual error before changing anything (runbook §6.6) |
| Verify job fails, DNS error | A record missing or not propagated | Add it, re-run the job |
| `docker compose pull` → `unauthorized` | Server's GHCR PAT expired | Re-run the login from runbook Step 12 — not a pipeline change |
| App starts, no logs in Seq | Wrong or missing `SEQ_API_KEY` repo secret | Recreate the key in Seq, update the secret, redeploy |
| Permission denied writing uploads | A `USER` directive crept into the Dockerfile | Remove it; container must run as root |
| DB connection refused | Connection string using `localhost` or a container name | Must be `172.17.0.1` |
| New app's Caddy snippet ignored | File already existed on the server | Intended — edit `/srv/caddy/sites/{{APP_NAME}}.caddy` by hand and reload |
