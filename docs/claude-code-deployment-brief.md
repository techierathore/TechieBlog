# App Deployment Spec — VPS Production Pipeline (v2)

Purpose: the single reusable spec for taking any app in the portfolio from repo → live on the VPS, with **zero manual server work per app**. Designed to be absorbed into a spec-driven development framework: Section 2 is the environment contract (constants), Section 3 the per-app variables, Section 4 the master prompt for Claude Code, Section 5 the pipeline contract it must produce, Section 6 the human checklist (short).

Prerequisite (one-time, already done via the VPS runbook): server hardened, Docker + `web` network, native Postgres 18 + pgvector + `ensure-db` helper, Caddy running with `import sites/*.caddy`, Seq running, org-level GitHub secrets set (`VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `DB_PASSWORD`, `SEQ_API_KEY`).

Per app, exactly ONE manual infrastructure task exists: adding the DNS A record (2 minutes, in the domain's registrar dashboard, before the first push). Everything else is pipeline-automated.

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
 │             6. reload Caddy config
 └─ verify:   curl https://DOMAIN/healthz (or internal healthz if INTERNAL) — fail loudly

(Not in the pipeline by design: the DNS A record — a once-per-app 2-minute manual step;
the generated SERVER-SETUP summary prints the exact record to add.)
```

Everything is idempotent: first deploy and hundredth deploy run the same steps.

---

## Section 2 — Environment contract (constants — never change per app)

| Constant | Value |
|---|---|
| Registry | `ghcr.io/techierathore` |
| Docker network | `web` (external) |
| App port inside container | `8080` |
| Compose path on server | `/srv/apps/{APP_NAME}/docker-compose.yml` |
| Uploads host path | `/srv/data/{APP_NAME}/uploads` → container `/app/uploads` |
| DB from containers | host `172.17.0.1`, port `5432`, user `appuser` |
| DB creation helper | `sudo /usr/local/bin/ensure-db <db> [vector]` (passwordless for deploy user) |
| Seq ingestion (internal) | `http://seq:5341` |
| Caddy snippets dir | `/srv/caddy/sites/*.caddy`; reload via `docker exec caddy caddy reload --config /etc/caddy/Caddyfile` |
| Org secrets | `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `DB_PASSWORD`, `SEQ_API_KEY` |
| DNS | manual: one A record per app (`DOMAIN` → VPS IP) added in the registrar before first push; **DNS only** (grey cloud) if the domain sits on Cloudflare |
| Internal service-to-service calls | `http://{container-name}:8080` — apps on the VPS call sibling APIs by container name, never via public URL |

---

## Section 3 — Per-app variables (fill per app)

| Variable | Value for THIS app | Notes |
|---|---|---|
| `APP_NAME` | e.g. `appmgrapi` | **the app's single infra identity** — lowercase, no spaces (GHCR requires lowercase). Becomes container/image/folder/snippet name |
| `DOMAIN` | e.g. `appmgrapi.techierathore.com` — or `INTERNAL` | `INTERNAL` = no DNS, no Caddy; reachable only as `http://APP_NAME:8080` by siblings |
| `DB_NAME` | e.g. `blog` — or `NONE` | one database per app |
| `NEEDS_PGVECTOR` | `yes` / `no` | |
| `HAS_UPLOADS` | `yes` / `no` | user/admin file uploads (banners, images, docs) |
| `DOTNET_VERSION` | e.g. `10` | |
| `PROJECT_PATH` | e.g. `src/Blog/Blog.csproj` | |
| `MEM_LIMIT` | `384m` default; `512m` heavier apps | |
| `INTERNAL_APIS` | e.g. `appmgrapi` — or none | sibling APIs this app consumes; config gets `http://name:8080` base URLs |

**Naming rule (removes all ambiguity):** an app has four names — C# project name (`AppManagerApi`), repo name, `APP_NAME`, and `DOMAIN` — and only `APP_NAME` must be consistent across infrastructure. `PROJECT_PATH` is the bridge to the .NET world, so PascalCase project names never leak into infra. Convention: **`APP_NAME` = the subdomain label** (`appmgrapi.techierathore.com` → `appmgrapi`); apex-domain apps pick a short lowercase name once (blog → `techieblog`). `INTERNAL_APIS` entries and Caddy snippet filenames always refer to `APP_NAME` values, never project names.

---

## Section 4 — Master prompt for Claude Code

Paste into Claude Code inside the app's repo after replacing `{{PLACEHOLDERS}}`:

```
You are preparing this Blazor/.NET app for fully automated production deployment to my VPS.

Infrastructure already running (environment contract): Ubuntu 24.04; native Postgres 18 +
pgvector on host (containers reach it at 172.17.0.1:5432, user appuser); idempotent DB helper
on server: "sudo /usr/local/bin/ensure-db <db> [vector]" (passwordless for the deploy user);
Docker with external network "web"; Caddy container ("caddy") loading /srv/caddy/sites/*.caddy
(host path) = /etc/caddy/sites inside the container, reloaded with
"docker exec caddy caddy reload --config /etc/caddy/Caddyfile"; Seq container ("seq") ingesting
at http://seq:5341; GHCR under ghcr.io/techierathore. DNS records are managed manually by me —
the pipeline must NOT touch DNS; instead you will print the record I need to add (see item 6).
Available GitHub secrets: VPS_HOST, VPS_USER, VPS_SSH_KEY, DB_PASSWORD, SEQ_API_KEY (org-level).

App variables:
- APP_NAME: {{APP_NAME}}
- DOMAIN: {{DOMAIN}}                 (INTERNAL = skip DNS job and Caddy snippet entirely)
- DB_NAME: {{DB_NAME}}               (NONE = skip all database wiring)
- NEEDS_PGVECTOR: {{yes/no}}
- HAS_UPLOADS: {{yes/no}}
- DOTNET_VERSION: {{10}}
- PROJECT_PATH: {{src/App/App.csproj}}
- MEM_LIMIT: {{384m}}
- INTERNAL_APIS: {{none | list of sibling container names this app calls}}

Produce ALL of the following:

1. DOCKERFILE (repo root): multi-stage — sdk:{{DOTNET_VERSION}} publish of PROJECT_PATH,
   aspnet:{{DOTNET_VERSION}} runtime, ASPNETCORE_URLS=http://+:8080, EXPOSE 8080.

2. APP CODE WIRING:
   a. Health: /healthz via AddHealthChecks; include AspNetCore.HealthChecks.NpgSql DB check
      unless DB_NAME=NONE.
   b. Logging: Serilog.AspNetCore + Serilog.Sinks.Seq. Console always; Seq sink only when
      config "Seq:Url" is set (apiKey from "Seq:ApiKey"). Enrich.WithProperty("App","{{APP_NAME}}").
      Local dev must run with no Seq configured.
   c. Config-driven everything: ConnectionStrings:Default; if HAS_UPLOADS, uploads dir from
      "Uploads:Path" (local fallback folder) with static file serving at /uploads via
      PhysicalFileProvider; for each entry in INTERNAL_APIS, a typed HttpClient whose BaseAddress
      comes from config key "Services:<name>:Url". No hardcoded paths, hosts, or secrets.
   d. If the app uses EF Core migrations: Database.Migrate() on startup.

3. deploy/docker-compose.template.yml — the production compose file with ${DB_PASSWORD} and
   ${SEQ_API_KEY} placeholders (rendered by the workflow via envsubst; real values never enter
   the repo). Service: image ghcr.io/techierathore/{{APP_NAME}}:latest, container_name
   {{APP_NAME}}, restart unless-stopped, mem_limit {{MEM_LIMIT}}, networks [web] (external),
   environment: ASPNETCORE_ENVIRONMENT=Production; ConnectionStrings__Default=
   "Host=172.17.0.1;Port=5432;Database={{DB_NAME}};Username=appuser;Password=${DB_PASSWORD}"
   (omit if NONE); Seq__Url=http://seq:5341; Seq__ApiKey=${SEQ_API_KEY};
   Uploads__Path=/app/uploads plus volume bind /srv/data/{{APP_NAME}}/uploads:/app/uploads
   (only if HAS_UPLOADS); Services__<name>__Url=http://<name>:8080 for each INTERNAL_APIS entry.

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
     Caddy snippet — CREATE-ONLY, never overwrite (unless INTERNAL, in which case skip
     entirely): over SSH, test [ -f /srv/caddy/sites/{{APP_NAME}}.caddy ]. If the file EXISTS:
     log "caddy entry already present — skipped" and do NOT copy and do NOT reload Caddy.
     Only if ABSENT: scp deploy/{{APP_NAME}}.caddy to /srv/caddy/sites/{{APP_NAME}}.caddy,
     then docker exec caddy caddy reload --config /etc/caddy/Caddyfile.
     (Rationale: manually-created entries on the server are authoritative; routing changes are
     made by editing/deleting the server file by hand, never by the pipeline.)
     Then over SSH:
       sudo /usr/local/bin/ensure-db {{DB_NAME}} {{"vector" if NEEDS_PGVECTOR else ""}}   (skip if NONE)
       cd /srv/apps/{{APP_NAME}} && docker compose pull && docker compose up -d && docker image prune -f
   - Job verify (needs deploy): if public — sleep 20 then curl -sf https://{{DOMAIN}}/healthz,
     3 retries 10s apart, fail workflow on no 200. If INTERNAL — over SSH:
     docker exec {{APP_NAME}} curl -sf http://localhost:8080/healthz.
   - Never echo secret values in logs.

6. deploy/SERVER-SETUP.md — a short generated summary containing: (a) unless DOMAIN=INTERNAL,
   the exact DNS record I must add manually before the first push, formatted as a table
   (Type A / Name = the subdomain part or @ / Value = my VPS IP / note: "DNS only" if the
   domain is on Cloudflare); (b) the UptimeRobot monitor URL https://{{DOMAIN}}/healthz;
   (c) anything else requiring one-time human action, if any.

Rules: no extra infrastructure, no test job, workflow never runs SQL beyond ensure-db,
workflow never touches DNS.
Finish with a summary of every file created/changed and any assumptions made.
```

---

## Section 5 — Pipeline contract (what a correct implementation guarantees)

1. **Idempotent**: re-running any deploy on an unchanged commit changes nothing and breaks nothing.
2. **Caddy routing is create-only**: an existing `/srv/caddy/sites/<APP_NAME>.caddy` — whether created manually or by an earlier pipeline run — is never overwritten or deleted by the pipeline. Routing changes are deliberate manual edits on the server (see manual-dns-caddy-setup.md); the pipeline only fills gaps.
2. **Secrets never in the repo**: compose is a template; real values injected at deploy time from GitHub secrets; server-side file contains rendered values, readable only on the server.
3. **INTERNAL apps** have zero public surface: no DNS record, no Caddy snippet, no open port — verify runs inside the container.
4. **Public apps** are verified end-to-end through the real URL (DNS → Caddy → TLS → app → DB), so a green pipeline means a genuinely reachable site. This requires the A record to exist before the first push — the one manual step, printed for you in the generated deploy/SERVER-SETUP.md.
5. **Rollback** = `git revert && git push` (pipeline redeploys previous code), or emergency manual: edit the image tag to a previous SHA on the server and `docker compose up -d`.

---

## Section 6 — Human checklist per app (~7 minutes, no SSH needed)

1. Fill Section 3 table → replace placeholders in the Section 4 prompt → run in Claude Code → review its summary → commit.
2. Add the DNS A record exactly as printed in the generated deploy/SERVER-SETUP.md (registrar dashboard, 2 minutes; skip if INTERNAL).
3. `git push` → watch Actions go green → open `https://DOMAIN/healthz`.
4. Add one UptimeRobot HTTP monitor for the new `/healthz` URL (skip if INTERNAL).

Backups: nothing to do — `pg_dumpall` picks up the new database and `/srv/data` archiving picks up the new uploads folder automatically.

---

## Section 7 — Filled examples (note how project names and APP_NAMEs differ freely)

**Blog** (apex domain, no subdomain — short name chosen once):
`APP_NAME=techieblog · DOMAIN=techierathore.com · DB_NAME=techieblog · NEEDS_PGVECTOR=no · HAS_UPLOADS=yes · DOTNET_VERSION=10 · PROJECT_PATH=src/Blog/Blog.csproj · MEM_LIMIT=512m · INTERNAL_APIS=appmgrapi`

**AppManager API** (C# project `AppManagerApi` — infra name from the subdomain):
`APP_NAME=appmgrapi · DOMAIN=appmgrapi.techierathore.com · DB_NAME=appmanager · NEEDS_PGVECTOR=no · HAS_UPLOADS=no · DOTNET_VERSION=10 · PROJECT_PATH=src/AppManagerApi/AppManagerApi.csproj · MEM_LIMIT=384m · INTERNAL_APIS=none`
Public clients (desktop/MAUI) call `https://appmgrapi.techierathore.com`; sibling apps on the VPS call `http://appmgrapi:8080`.

**AppManager UI:**
`APP_NAME=appmanager · DOMAIN=appmanager.techierathore.com · DB_NAME=NONE (uses the API) · HAS_UPLOADS=no · PROJECT_PATH=src/AppManager/AppManager.csproj · INTERNAL_APIS=appmgrapi`
