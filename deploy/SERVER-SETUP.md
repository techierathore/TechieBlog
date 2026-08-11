# TechieBlog — one-time human actions before the first deploy

Generated for `APP_NAME=techieblog` · `DOMAIN=techierathore.com` (apex) · `DB_NAME=techieblog`.

Everything else in the deployment is automated by `.github/workflows/deploy.yml`. The items below are
the ones a pipeline cannot or must not do. The full owner-facing walkthrough — every GitHub secret,
the NuGet PAT steps, uploads, rollback and troubleshooting — is
[`docs/Prod-Deploy-Checklist.md`](../docs/Prod-Deploy-Checklist.md).

---

## 1. DNS — add these records manually, BEFORE the first push

The pipeline never touches DNS. Add both records in the registrar dashboard (about two minutes),
then wait for them to resolve before pushing to `main`. Caddy provisions the Let's Encrypt
certificate on first request, so a missing record means no certificate and a failed `verify` job.

| Type | Name | Value | TTL | Note |
|------|------|-------|-----|------|
| A | `@` | *your VPS IPv4 address* | Auto / 300 | Apex record for `techierathore.com`. **DNS only — grey cloud** if the domain sits on Cloudflare; an orange-cloud proxy terminates TLS itself and breaks Caddy's certificate issuance and the Blazor WebSocket. |
| A | `www` | *your VPS IPv4 address* | Auto / 300 | Required because the app is on an APEX domain and `deploy/techieblog.caddy` serves a `www` → apex redirect. Without it, `www.techierathore.com` fails to resolve instead of redirecting. **DNS only — grey cloud** as above. |

A `CNAME www → techierathore.com` works equally well in place of the second A record.

Check before pushing:

```bash
dig +short techierathore.com
dig +short www.techierathore.com
```

Both must return the VPS IP.

---

## 2. GitHub secrets

Five org-level secrets already exist per the environment contract: `VPS_HOST`, `VPS_USER`,
`VPS_SSH_KEY`, `DB_PASSWORD`, `SEQ_API_KEY`.

**TechieBlog additionally needs four NEW repository secrets** — the deploy fails without them:

| Secret | Why |
|--------|-----|
| `JWT_SIGNING_KEY` | `AppSecrets.Initialise` aborts startup without it (≥ 32 chars). |
| `APP_ENCRYPTION_KEY` | Same gate (≥ 16 chars). |
| `ANALYTICS_VISITOR_SALT` | `DeploymentConfiguration.Enforce` aborts startup without it (≥ 32 chars). |
| `TrBlazeUiPackagesToken` | The image build restores TrBlazeUI from a private feed. |

Generation commands, exact scopes and the full "what breaks without it" table are in
[`docs/Prod-Deploy-Checklist.md`](../docs/Prod-Deploy-Checklist.md).

---

## 3. Uptime monitoring

Add one UptimeRobot HTTP(s) monitor:

```
https://techierathore.com/healthz
```

> **`/healthz` is the monitor and pipeline URL.** `Program.cs` maps it anonymously, exempt from rate
> limiting, carrying the **readiness** checks — PostgreSQL included — so a green response means the
> database answered, not merely that the process is alive. It returns **503** when the database is
> unreachable. It is the same endpoint the pipeline's `verify` job probes, which is the point: one
> URL, one meaning. `/health` (liveness) and `/health/ready` are still mapped and still work; they are
> useful by hand, they are just not what the monitor watches.

Suggested settings: interval 5 minutes, expect HTTP 200, alert after 2 consecutive failures.

---

## 4. Bind-mount ownership — do this once, after the first deploy

The pipeline creates **two** host directories and bind-mounts them into the container:

| Host directory | Container path | Holds |
|----------------|----------------|-------|
| `/srv/data/techieblog/uploads` | `/app/uploads` | Uploaded images (not `/app/wwwroot/uploads` — `wwwroot` lives inside the image and is discarded on redeploy). |
| `/srv/data/techieblog/dp-keys` | `/home/app/.aspnet/DataProtection-Keys` | The ASP.NET Core DataProtection key ring. Unmounted, it is regenerated on every redeploy. |

The container **does** run as a non-root user: the `Dockerfile` ends with `USER $APP_UID`, and
`$APP_UID` in the .NET 10 runtime image is **1654** (`app`). `mkdir -p` created both host directories
owned by `root`, so they must be handed to that UID. Uploads fail with an access-denied error while
the rest of the site works perfectly; the key ring falls back to the container-internal path and the
startup log warns about it:

```bash
sudo chown -R 1654:1654 /srv/data/techieblog/uploads
sudo chown -R 1654:1654 /srv/data/techieblog/dp-keys
```

Confirm the UID, then **probe the thing that actually breaks — write permission on the mount:**

```bash
docker exec techieblog id
# expected:  uid=1654(app) gid=1654(app) groups=1654(app)

docker exec techieblog touch /app/uploads/probe && docker exec techieblog rm /app/uploads/probe
# silence = writable.  "Permission denied" = the chown above has not been done.
```

`id` verifies who the process is; the `touch` verifies what it can do, and it is the second one that
fails when this goes wrong. A failed upload produces **no server-side log line at all** — only a
generic "Upload failed" in the admin UI — so this probe is the only cheap way to see it. There is
nothing to restart afterwards: fix the ownership and the next upload succeeds immediately.

Then upload one image from the admin UI as the end-to-end confirmation.

The key ring is correct when `ls /srv/data/techieblog/dp-keys` shows a `key-<guid>.xml` owned
`1654:1654`, the same file is still there after the next deploy, and `docker logs techieblog` no
longer prints *"Storing keys in a directory … that may not be persisted outside of the container."*

There is **no logs directory to own**: the container logs to stdout only (`LogFileEnabled=false`).
Read them with `docker logs techieblog`, and durably in Seq.

---

## 5. Nothing else

- Backups: no action. `pg_dumpall` picks up the `techieblog` database automatically, and the
  `/srv/data` archive picks up `/srv/data/techieblog/uploads` and `/srv/data/techieblog/dp-keys`.
- Database: created by `sudo /usr/local/bin/ensure-db techieblog` inside the pipeline. No `vector`
  extension (`NEEDS_PGVECTOR=no`). Schema is applied by DbUp inside the app on every boot — which
  means `appuser` needs **DDL rights**, not just `CONNECT`. Verify once:
  `docs/Prod-Deploy-Checklist.md` §5a.
- Caddy: the pipeline installs `/srv/caddy/sites/techieblog.caddy` **only if it does not already
  exist**, and reloads Caddy only in that case. An existing file is authoritative — edit it on the
  server and reload by hand.
