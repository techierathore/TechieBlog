# VPS Runbook v5.2 — Simple Edition (Bluehost)

*Revision history:*
- *v5.2 — CI account renamed `deploy` → `ciuser` (avoids collision with "deploy key", the deploy job, and each repo's `deploy/` folder); SSH key file renamed to `~/.ssh/vps_ciuser`.*
- *v5.1 — **Part 2.1 rewritten**: GitHub Actions now connects as a dedicated `ciuser` user (docker group, `webops` group for `/srv/apps`, `/srv/data`, `/srv/caddy/sites`, sudo limited to `ensure-db`, no access to `/srv/backups`) instead of `ravi`. Secrets table moved to 2.2; `VPS_USER` is now `ciuser`. Part 9 checkup gains five CI-account checks.*
- *v5 — New **Part 0** (preflight inventory: this image ships without `nano`, `cron`, `rsync`, `wget`, `unzip`) and **Part 9** (`/srv/checkup.sh`, a one-command acceptance test for the whole build). Part 3 backup script rewritten: missing paths no longer abort it silently, uploads are verified rather than assumed, and 3.8's restore test now checks the restored data instead of the error output. 6.3 rewritten for per-app Seq API keys. Step 8 now creates `/srv/data` and `/srv/caddy/sites`.*
- *v4 — Part 6 (Seq) rewritten for Seq 2026.1: mandatory first-run admin password, pinned image tag, canonical URI, YAML-safe file creation, and a full 502 troubleshooting path. New Part 8 on splitting work between chat Claude and Claude Code.*

**Your server:** Bluehost **NVMe 4** VPS — 4 vCPU / 8 GB RAM / 200 GB NVMe, Ubuntu 24.04. Running:
- **Postgres + pgvector installed directly on Ubuntu** (no Docker for the database — simpler and safer for your data)
- **Your Blazor apps in Docker** (so GitHub Actions can deploy them automatically)
- **Caddy in Docker** (gives every app free automatic HTTPS)

**Before you start, know these 3 things:**

1. **How to paste in a terminal:** Copy a command here, click the black terminal window, press `Cmd+V`, press `Enter`. That's it. Do one grey block at a time.
2. **How to edit a file with `nano`:** `nano` is a simple text editor that runs inside the terminal (normally pre-installed on Ubuntu, but Bluehost's minimized image lacks it — Step 4 installs it). When a step says `nano somefile`, the editor opens. Type or paste the content. To save and exit: press `Ctrl+O`, then `Enter`, then `Ctrl+X`. (Yes, Ctrl — not Cmd — even on Mac.)
3. **Passwords don't show while typing.** When Linux asks for a password, the screen shows nothing as you type. It IS receiving it. Type it and press Enter.

**Replace these placeholders everywhere you see them:**

| Placeholder | Replace with |
|---|---|
| `YOUR_VPS_IP` | The IP address shown in Bluehost's VPS Web Console |
| `app1.techierathore.com` | Your real app domain |
| `DB_PASSWORD_HERE` | A password you generate in Step 6 |

---

## Part 0 — Preflight: find out what this server is missing (10 min, do this first)

**Why this part exists:** Bluehost ships a *minimized* Ubuntu image. Tools that are present on virtually every other Ubuntu box — `nano`, `cron`, `rsync`, `wget`, `unzip` — are simply absent. Without this part you discover each one individually, mid-step, as a `command not found` that stops you cold. The first build of this server hit that three separate times. Ten minutes here removes the whole category.

**The rule for this server:** `command not found` means *not installed*, not *broken*. Install it and carry on. Don't investigate.

### 0.1 Take inventory

Log in as root (Step 1) and run this. It changes nothing — it only reports:

```bash
echo "=== OS / hardware ==="
. /etc/os-release && echo "$PRETTY_NAME"
echo "CPU cores : $(nproc)"
echo "RAM       : $(free -h | awk '/^Mem:/{print $2}')"
echo "Disk free : $(df -h / | awk 'NR==2{print $4" of "$2}')"
echo "Swap      : $(swapon --show | grep -q . && echo present || echo NONE)"
echo
echo "=== Tools this runbook needs ==="
for c in curl wget nano git tar gzip unzip jq rsync crontab ufw fail2ban-client; do
  printf '%-16s %s\n' "$c" "$(command -v $c >/dev/null 2>&1 && echo present || echo MISSING)"
done
echo
echo "=== Installed later by their own steps (MISSING here is correct) ==="
for c in docker psql rclone; do
  printf '%-16s %s\n' "$c" "$(command -v $c >/dev/null 2>&1 && echo present || echo "MISSING (expected)")"
done
```

On a fresh Bluehost NVMe 4, expect most of the first list to say `MISSING` and `Swap` to say `NONE`. That's normal for this image — it's exactly what 0.2 fixes.

### 0.2 Install everything the runbook needs, in one go

```bash
apt update && apt upgrade -y
apt install -y nano curl wget git tar gzip unzip jq rsync cron ca-certificates gnupg \
               ufw fail2ban unattended-upgrades
systemctl enable --now cron
```

**You'll know it worked when:** re-running the 0.1 inventory shows `present` for every tool in the first list. `Swap` still says `NONE` — that's Part 7.1's job, scheduled deliberately after Postgres so the sizing is right.

Note `cron` in that list. Ubuntu normally has it pre-installed, this image doesn't, and it isn't needed until Part 3.7 — which is precisely why installing it now is worth doing. The same applies to `nano`: Step 4's install line still lists these packages so the step stands alone, but after 0.2 apt will just report they're already present.

### 0.3 What's deliberately not here

Docker, Postgres, and rclone are **not** installed in this part. Each needs a third-party apt repository added first (Steps 5, 6, and 3.1 respectively), and folding those into a single line would hide which repo signed which package. They stay in their own steps.

---

## Part 1 — Setting up the server

### Step 1 — Log in to your new server

Bluehost doesn't email you credentials — everything comes from their **VPS Web Console** (Bluehost account → your VPS → Manage). Do this there first:

1. Note the **Server IP** — that's `YOUR_VPS_IP` everywhere in this guide.
2. Use the **Reset Root Password** option and set a password you choose. Save it in your password manager (you'll only need it for the next 10 minutes — after Step 3 you'll never use root again).

Now open Terminal on your Mac and run:

```bash
ssh root@YOUR_VPS_IP
```

- First time, it asks *"Are you sure you want to continue connecting?"* → type `yes`, press Enter.
- It asks for a password → the root password you just set in the web console.

**If SSH refuses to connect at all:** some Bluehost VPS images ship with password login for root disabled. No problem — the Web Console has a built-in browser terminal ("Console" / "Terminal" button). Open that, log in as root there, and run the Step 2 commands in it; from Step 3 onward you'll be using normal SSH as ravi anyway.

**You'll know it worked when:** the prompt changes to something like `root@vps:~#`. You are now typing commands *on the server*, not on your Mac.

### Step 2 — Create your everyday login (the "non-root user")

**What this is:** `root` is the all-powerful account — one wrong command as root can wreck the server. Standard practice: create a normal user named `ravi` for daily work. When `ravi` needs admin power, he puts `sudo` in front of a command (think of `sudo` as "run this one command as admin").

> **About the "This system has been minimized" message** you see on every login: harmless. Bluehost's image is a slimmed-down Ubuntu with some common tools removed (that's why e.g. `rsync` doesn't exist on it). Ignore the `unminimize` suggestion — we simply install the few tools we need as we go.

Run these two commands one at a time:

```bash
adduser ravi
```

- It asks for a **new password** (then asks again to confirm) — this is ravi's password on the server. Pick a strong one and **save it in your password manager**; you'll type it at every `sudo`.
- It then asks Full Name, Room Number, etc. — just press **Enter** for each, then `Y` at the end.

```bash
usermod -aG sudo ravi
```

(This gives ravi the right to use `sudo`. No output = success.)

Quick sanity check that the password took — still as root, run:

```bash
su - ravi
```

It asks for ravi's password; on success the prompt changes to `ravi@...:~$`. Type `exit` to drop back to root, then `exit` again to leave the server entirely — you're back on your own machine.

(If `su - ravi` rejects the password, reset it right there as root with `passwd ravi` — it asks for a new password twice — then try `su - ravi` again.)

### Step 3 — Key login for ravi (no more passwords)

**What this is:** instead of typing a password, your computer proves its identity with a **key pair** — a private file that stays on your machine and a public file we place on the server. First we make sure your machine *has* a key pair (a fresh machine doesn't — that's what the "ERROR: No identities found" message means).

**3a. Check for an existing key — on your Mac** (Terminal) **or Windows** (PowerShell), same command:

```bash
ls ~/.ssh/*.pub
```

- If it lists a file like `id_ed25519.pub` or `id_rsa.pub` → you have a key, go to 3b.
- If it says *"No such file or directory"* / *"Cannot find path"* → create one:

```bash
ssh-keygen -t ed25519
```

Press **Enter** at every question (default location; empty passphrase is fine for now). This creates two files: `id_ed25519` (private — never leaves your machine, never share it) and `id_ed25519.pub` (public — safe to hand out). Do this on **both** your Mac and your Windows machine — each machine gets its own key pair.

**3b. Put the public key on the server:**

On **Mac**:

```bash
ssh-copy-id ravi@YOUR_VPS_IP
```

On **Windows PowerShell** (Windows has no `ssh-copy-id` — this one-liner does the same job):

```powershell
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh ravi@YOUR_VPS_IP "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 700 ~/.ssh && chmod 600 ~/.ssh/authorized_keys"
```

Either way it asks for **ravi's password** (the Step 2 one) — this is the last time you'll ever type it for login.

**3c. Test it:**

```bash
ssh ravi@YOUR_VPS_IP
```

**You'll know it worked when:** you land at the `ravi@...:~$` prompt **without being asked for a password**. Repeat 3a–3c from the other machine (Windows/Mac) so both can log in.

**If 3b says "Permission denied" when you type ravi's password:** the password didn't get set correctly in Step 2. Log in as root (`ssh root@YOUR_VPS_IP` or the Bluehost web console terminal), run `passwd ravi`, set it fresh, then retry 3b.

**From this point on, every command in this guide is run while logged in as `ravi`.** The prompt looks like `ravi@hal-server:~$`.

### Step 4 — Lock the doors (security, 5 commands)

**What this does:** turns on the firewall (only web traffic + your SSH login allowed in), turns on automatic security updates, and installs fail2ban (auto-blocks bots that guess passwords).

```bash
sudo apt update && sudo apt upgrade -y
```

(First time you use `sudo`, it asks for **ravi's password**. This one takes a few minutes — lines of text will scroll by. If a purple/pink screen appears asking about restarting services, just press Enter to accept the defaults.)

Because Bluehost's image is minimized, common tools aren't pre-installed — install everything this guide needs in one go (security tools plus `nano` and `curl`):

```bash
sudo apt install -y ufw fail2ban unattended-upgrades nano curl cron
sudo systemctl enable --now cron
```

Then turn the firewall on:

```bash
sudo ufw allow OpenSSH && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp && sudo ufw enable
```

(It warns the firewall "may disrupt existing ssh connections" → type `y`. It won't disconnect you — we just allowed SSH.)

```bash
sudo systemctl enable --now fail2ban
```

**Optional extra lock (recommended, do it when comfortable):** disable password logins entirely so only your Mac's key works:

```bash
sudo nano /etc/ssh/sshd_config
```

Find the line `#PasswordAuthentication yes` (use arrow keys to scroll) and change it to `PasswordAuthentication no` (remove the `#` too). Save and exit (`Ctrl+O`, Enter, `Ctrl+X`). Then:

```bash
sudo rm -f /etc/ssh/sshd_config.d/50-cloud-init.conf
sudo systemctl restart ssh
```

**Safety check (this is the "two terminals" thing, made simple):** do NOT close your current terminal yet. Open a **second** Terminal window on your Mac, run `ssh ravi@YOUR_VPS_IP`. If it logs in — great, close either window, you're safe. If it doesn't, your first window is still open to undo the change. That's all "keep a parallel terminal open" ever meant.

### Step 5 — Install Docker

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker ravi
exit
```

The `exit` is deliberate — log back in (`ssh ravi@YOUR_VPS_IP`) so the permission takes effect. Then verify:

```bash
docker ps
```

**You'll know it worked when:** you see a header line (`CONTAINER ID   IMAGE ...`) with nothing under it. That's an empty list — correct, nothing is running yet.

### Step 6 — Install Postgres 18 + pgvector (directly on Ubuntu, no Docker)

**Why not Docker for this?** Your friend is right for your situation: a database holds your data, and a native install has fewer ways to accidentally lose it, is easier to back up, and is what most tutorials assume. Apps stay in Docker; the database lives on the server itself.

**Why version 18, and how Postgres versioning works:** Postgres has **no LTS concept** — every major release (16, 17, 18…) is equally production-grade and gets exactly **5 years of support**. Ubuntu 24.04's built-in repo happens to ship 16 (supported to Nov 2028); the official PostgreSQL repo ("PGDG") carries the current release 18 (supported to **Nov 2030**). Installing 18 on a fresh server costs two extra commands and buys two extra years before you ever face a major upgrade. **And a major upgrade later is never "delete everything"** — it's dump-and-restore (`pg_dumpall` from the old version, restore into the new; data, users, and extensions all carry over) or `pg_upgrade` in place.

Add the official PostgreSQL repo, then install:

```bash
sudo apt install -y postgresql-common
sudo /usr/share/postgresql-common/pgdg/apt.postgresql.org.sh -y
sudo apt install -y postgresql-18 postgresql-18-pgvector
```

Postgres 18 is now installed AND running AND set to auto-start on reboot. It only listens on the server itself — the internet cannot reach it (and our firewall blocks it anyway). Verify:

```bash
sudo -u postgres psql -c "SELECT version();"
```

**You'll know it worked when:** the output starts with `PostgreSQL 18.`

Generate a database password and save it in your password manager:

```bash
openssl rand -base64 24
```

(Copy the output — that's `DB_PASSWORD_HERE` for the rest of this guide.)

Create your database user and first database:

```bash
sudo -u postgres psql
```

Your prompt changes to `postgres=#` — you're now *inside* Postgres. Run these four lines **ONE at a time — type or paste a single line, press Enter, wait for its response, then do the next**. (Do not paste them as a block: the `\c` line is a psql command that must sit alone on its line — in a multi-line paste it swallows the next line as arguments and you get errors like *invalid integer value "vector" for connection option "port"*, leaving the extension uncreated.)

```sql
CREATE USER appuser WITH PASSWORD 'DB_PASSWORD_HERE';
```
→ responds `CREATE ROLE` (replace the password!)
```sql
ALTER USER appuser CREATEDB;
```
→ responds `ALTER ROLE` (lets you create databases later from Beekeeper Studio too, not only via the server)
```sql
CREATE DATABASE appdb OWNER appuser;
```
→ responds `CREATE DATABASE`
```sql
\c appdb
```
→ responds `You are now connected to database "appdb" as user "postgres"` — only continue once you see this
```sql
CREATE EXTENSION vector;
```
→ responds `CREATE EXTENSION`

Verify with `\dx` — the list must include `vector`. Then type `\q` and press Enter to leave Postgres.

**Two things worth knowing about what you'll see in `\l`:** every Postgres cluster contains three system databases — `postgres` (an empty "lobby" DB that admin tools connect to), `template1` (the template copied by every `CREATE DATABASE`), and `template0` (its pristine backup). They belong to Postgres; never delete or rename them. Beekeeper hides them by default, `psql` doesn't. **And on passwords:** there are no per-database passwords — the password belongs to the *user* (`appuser`), shared across all your databases. To change it later: `sudo -u postgres psql -c "ALTER USER appuser WITH PASSWORD 'new-password';"` — then update everywhere it's stored: the GitHub secret `DB_PASSWORD`, compose files already on the server (each app picks up the new value on its next deploy), your Beekeeper connection, and local-debug connection strings.

(Later, when you ship Xpenser: same thing — `CREATE DATABASE xpenser OWNER appuser;` then `\c xpenser` then `CREATE EXTENSION vector;`. One Postgres, one database per app. **But you won't do this by hand** — the next block lets the CI/CD pipeline do it automatically.)

**Enable automated per-app database creation.** The deployment pipeline (Part 2 + the brief document) creates each app's database itself. It needs one small, tightly-scoped helper on the server:

```bash
sudo nano /usr/local/bin/ensure-db
```

Paste:

```bash
#!/usr/bin/env bash
# Usage: ensure-db <dbname> [vector]
# Creates the database (owner appuser) if missing; adds pgvector if "vector" is passed. Safe to re-run.
set -euo pipefail
DB="$1"
sudo -u postgres psql -tc "SELECT 1 FROM pg_database WHERE datname='$DB'" | grep -q 1 || \
  sudo -u postgres createdb -O appuser "$DB"
if [ "${2:-}" = "vector" ]; then
  sudo -u postgres psql -d "$DB" -c "CREATE EXTENSION IF NOT EXISTS vector;"
fi
```

Save, exit, then:

```bash
sudo chmod +x /usr/local/bin/ensure-db
echo 'ravi ALL=(ALL) NOPASSWD: /usr/local/bin/ensure-db *' | sudo tee /etc/sudoers.d/ensure-db
```

(The sudoers line means: ravi — and therefore the pipeline logging in as ravi — may run *this one script* without a password, and nothing else. Test it: `sudo ensure-db testdb vector` should finish silently; clean up with `sudo -u postgres psql -c "DROP DATABASE testdb;"`.)

### Step 6.5 — Manage the database from Beekeeper Studio (Mac or Windows)

**How it works:** the database is deliberately not open to the internet. Beekeeper connects through an **SSH tunnel** — it logs into the server as `ravi` first, then reaches Postgres from the inside. Beekeeper has this built in; no server changes and no firewall holes needed.

In Beekeeper Studio → **New Connection** → connection type **Postgres**, then fill in:

| Field | Value |
|---|---|
| Host | `localhost` |
| Port | `5432` |
| User | `appuser` |
| Password | your `DB_PASSWORD_HERE` |
| Default Database | `appdb` |
| **☑ Enable SSH Tunnel** | tick it |
| SSH Host | `YOUR_VPS_IP` (SSH port `22`) |
| SSH Username | `ravi` |
| SSH Auth | **Key file** → pick your key (Mac: `~/.ssh/id_ed25519` or `id_rsa`) |

Click **Test** → then **Connect** → **Save Connection** (name it e.g. `VPS - Production`).

(Yes, Host really is `localhost` — from the tunnel's point of view, Postgres *is* local. On your Windows machine: do Step 3a–3b from that machine once so its key is authorized too, then use the same settings with its key file at `C:\Users\<you>\.ssh\id_ed25519`.)

**Careful:** this connection is your live production data. When you're browsing "just to check something", prefer read-only queries; there's no undo on a production `DELETE`.

**Managing databases from Beekeeper:** "Default Database" is only where the connection lands — you're connected to the whole server, and the database dropdown in Beekeeper's sidebar switches between databases without reconnecting. You can also run `CREATE DATABASE x OWNER appuser;` and `DROP DATABASE x;` straight from the query editor (drop requires nothing to be connected to that DB — stop its app container first and switch yourself to a different database). Only exception: databases needing pgvector are easier via the server helper, `sudo ensure-db <name> vector`, since the extension may need superuser rights to install.

### Step 6.6 — Connect your LOCAL code to the production database (debugging)

Same trick as Beekeeper — an SSH tunnel — but from a plain terminal, so your locally-running app can use it. On your Mac (or Windows PowerShell):

```bash
ssh -L 5433:localhost:5432 ravi@YOUR_VPS_IP
```

Read it as: "while this terminal stays open, my machine's port `5433` is a secret tube to the server's Postgres." (We use 5433 locally so it never clashes with a Postgres you might have running on your own machine at 5432.)

Then run your app locally with this connection string — e.g. in `appsettings.Development.json` or a launch profile:

```
Host=localhost;Port=5433;Database=blog;Username=appuser;Password=DB_PASSWORD_HERE
```

Debug in Visual Studio / Rider as normal — breakpoints, watches, everything — against the real production data. Close the terminal window and the tunnel is gone.

**Two rules when doing this:**
1. **You are pointing a debugger at live data.** One careless "let me just test this save method" writes to production. Prefer read-path debugging; for anything write-heavy, restore last Sunday's backup into your local Postgres instead (`gunzip -c pg-XXXX.sql.gz | psql -U postgres` locally) and debug against the copy.
2. Never commit that connection string — keep it in `appsettings.Development.json` (git-ignored) or user secrets.

### Step 7 — Let Docker apps talk to Postgres

**What this does:** your apps run inside Docker, which is like a separate mini-network on the same machine. Three small edits let containers reach the database while the internet still can't.

Edit 1 — tell Postgres to also listen on the Docker side:

```bash
sudo nano /etc/postgresql/18/main/postgresql.conf
```

Find the line `#listen_addresses = 'localhost'` (it's near the top, under CONNECTIONS). Change it to:

```
listen_addresses = 'localhost,172.17.0.1'
```

(Remove the `#`.) Save and exit.

Edit 2 — allow connections *from Docker containers only*:

```bash
sudo nano /etc/postgresql/18/main/pg_hba.conf
```

Scroll to the very bottom and add this line:

```
host    all    appuser    172.16.0.0/12    scram-sha-256
```

Save and exit, then restart Postgres:

```bash
sudo systemctl restart postgresql
```

Edit 3 — one firewall exception for Docker's internal network:

```bash
sudo ufw allow from 172.16.0.0/12 to any port 5432
```

Done. The internet still cannot reach your database — only containers on this machine can.

### Step 8 — Folders and the shared Docker network

```bash
sudo mkdir -p /srv/caddy /srv/caddy/sites /srv/apps /srv/data /srv/backups
sudo chown -R ravi:ravi /srv
docker network create web
```

(`/srv/data` holds uploaded files and `/srv/caddy/sites` holds per-app Caddy snippets. Both are created now, empty, because the weekly backup archives all four paths — a missing one used to abort the backup silently. `/srv` is just the conventional Linux home for "stuff this server serves". `web` is a named network so Caddy and your apps can find each other.)

### Step 9 — Caddy (automatic HTTPS) — no domains needed today

**What Caddy does:** sits in front of all your apps, gets free SSL certificates automatically, and routes each domain to the right app.

**How we set it up:** Caddy starts **empty** — no domains at all. Each app owns a tiny config snippet file, and the app's CI/CD pipeline drops that snippet into `/srv/caddy/sites/` and tells Caddy to reload. **You will never edit Caddy config by hand after today.** Apps you mark as INTERNAL in the deployment brief simply never get a snippet, so they never touch Caddy.

```bash
mkdir -p /srv/caddy/sites
touch /srv/caddy/sites/placeholder.caddy
nano /srv/caddy/Caddyfile
```

The whole Caddyfile is one line — paste:

```
import sites/*.caddy
```

Save and exit. (The empty `placeholder.caddy` just keeps that import valid before the first app arrives.) Now the file that defines the Caddy container:

```bash
nano /srv/caddy/docker-compose.yml
```

Paste exactly:

```yaml
services:
  caddy:
    image: caddy:2
    container_name: caddy
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - ./sites:/etc/caddy/sites
      - caddy_data:/data
      - caddy_config:/config
    networks:
      - web

volumes:
  caddy_data:
  caddy_config:

networks:
  web:
    external: true
```

Save, exit, start it:

```bash
cd /srv/caddy && docker compose up -d
```

**You'll know it worked when:** it downloads some layers and ends with `✔ Container caddy  Started`. It's now idling, waiting for pipelines to hand it apps.

(For reference, a snippet the pipeline will place looks like this — you never write these yourself:)

```
blog.techierathore.com {
    reverse_proxy blog:8080
}
```

### Step 10 — DNS: one A record per app (manual, 2 minutes each)

DNS is a once-per-app task, so it stays manual and simple — no extra services, no migrations. When an app is ready to go live, Claude Code's generated deployment summary tells you the exact record; you add it in whichever dashboard the domain is registered with (GoDaddy / Namecheap / Cloudflare / etc.):

| Field | Value |
|---|---|
| Type | `A` |
| Name | the subdomain part (`appmanager`, `appmgrapi`, `stories`…) — or `@` for the bare domain |
| Value | `YOUR_VPS_IP` |
| TTL | default/auto |

Two notes:
- **If the domain is on Cloudflare:** set the record to **DNS only** (grey cloud, not orange), otherwise Caddy can't obtain its SSL certificate.
- DNS takes minutes to a few hours to spread worldwide. Add the record **before** pushing the app's first deploy, so the pipeline's final health check can reach the new address.

That's the entire DNS story: ~18 records over this server's lifetime, 2 minutes each.

> **GoDaddy user?** A common trap: the *nameserver* page only accepts hostnames, so entering an IP there errors — you want the **Manage DNS → DNS Records** page instead. A complete click-by-click GoDaddy walkthrough (plus doing the matching Caddy entries by hand and verifying certificates) is in the companion document **manual-dns-caddy-setup.md**.

### Step 11 — Your app's Dockerfile and health check (on your dev machine — Mac or Windows, identical)

> Works the same on Windows: the Dockerfile is just a text file in the repo, and the image is **built by GitHub Actions on Linux after you push** — your own machine never builds it, so you don't even need Docker installed locally. Anywhere these docs say "on your Mac", PowerShell on Windows runs the same commands (ssh, ssh-keygen, scp are built into Windows 10/11).

> Steps 11–12 show the manual version of what the deployment-brief pipeline automates, so you understand each moving part. Do them by hand for your **first** app only; from the second app onward, the brief document's master prompt generates all of this.

Add a file named `Dockerfile` at the root of your app's repo (adjust `.NET` version and project path):

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ./src/App1/App1.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "App1.dll"]
```

And in `Program.cs`, add a health endpoint (CI/CD and monitoring both use it). Install NuGet package `AspNetCore.HealthChecks.NpgSql`, then:

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default")!);
// ... after app is built:
app.MapHealthChecks("/healthz");
```

### Step 12 — Run the app on the server

```bash
mkdir -p /srv/apps/app1 && nano /srv/apps/app1/docker-compose.yml
```

Paste (put your real DB password in):

```yaml
services:
  app1:
    image: ghcr.io/techierathore/app1:latest
    container_name: app1
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__Default: "Host=172.17.0.1;Port=5432;Database=appdb;Username=appuser;Password=DB_PASSWORD_HERE"
    extra_hosts:
      - "host.docker.internal:host-gateway"
    networks:
      - web

networks:
  web:
    external: true
```

(`172.17.0.1` is "the server itself, as seen from inside Docker" — that's how the app finds your native Postgres. `app1` as container name must match the name in the Caddyfile.)

First deploy is manual (CI/CD takes over after Part 2). On the server:

```bash
echo YOUR_GITHUB_PAT | docker login ghcr.io -u techierathore --password-stdin
cd /srv/apps/app1 && docker compose up -d
```

(`YOUR_GITHUB_PAT` = a GitHub Personal Access Token with `read:packages` scope — create at github.com → Settings → Developer settings → Tokens (classic). You only do this login once ever.)

**Final check:** open `https://app1.techierathore.com/healthz` in your browser → it should say **Healthy**. If yes: server setup is done. 🎉

---

## Part 2 — CI/CD foundation (one-time, ~15 minutes)

**The idea:** push code to `main` → GitHub builds the image → creates the DNS record → puts the compose file, Caddy snippet, and database in place on the server → starts the new version → checks `/healthz`. You never SSH in to deploy.

**Division of labor between the two documents:** this Part sets up the **shared foundation** — one deploy key and one set of secrets that every app reuses. Everything **per-app** (Dockerfile, workflow, Seq wiring, uploads, DNS, Caddy snippet, database) lives in the separate **`claude-code-deployment-brief.md`** — that's the file you feed into your spec-driven framework, and its pipelines depend on the secrets defined here.

### 2.1 Create the `ciuser` account (the CI robot's own account)

GitHub Actions needs to SSH into this server on every push. It gets its **own** user — never `ravi`. Your account has full `sudo`; a key sitting in a GitHub secret must not.

**A. Create the account.** No password (key-only), and a shell, since the pipeline runs real commands and `scp` needs one:

```bash
sudo adduser --disabled-password --gecos "" ciuser
```

`--disabled-password` means nobody can log in as `ciuser` with a password — only with the key you're about to install.

**B. Docker access.** The pipeline runs `docker compose` and `docker exec caddy caddy reload`:

```bash
sudo usermod -aG docker ciuser
```

**C. Shared write access to the app folders.** `ravi` owns `/srv`; `ciuser` needs to write in three places and nowhere else. A shared group does this without handing over ownership:

```bash
sudo groupadd -f webops
sudo usermod -aG webops ravi
sudo usermod -aG webops ciuser

sudo chgrp -R webops /srv/apps /srv/data /srv/caddy/sites
sudo chmod -R g+rwX  /srv/apps /srv/data /srv/caddy/sites
sudo find /srv/apps /srv/data /srv/caddy/sites -type d -exec chmod g+s {} \;
```

That last line sets the setgid bit, so files the pipeline creates later inherit the `webops` group automatically instead of becoming `ciuser`-only. Note what is deliberately **excluded**: `/srv/backups` and `/srv/caddy/Caddyfile` stay `ravi`-owned, so a compromised CI key cannot touch your backups or the main routing config.

**D. Exactly one sudo permission.** The pipeline's only privileged action is creating databases:

```bash
echo 'ciuser ALL=(root) NOPASSWD: /usr/local/bin/ensure-db' | sudo tee /etc/sudoers.d/ciuser-ensure-db
sudo chmod 440 /etc/sudoers.d/ciuser-ensure-db
sudo visudo -c
```

`visudo -c` must print `parsed OK`. A syntax error in a sudoers file can lock out sudo entirely — never edit these by hand without that check.

**E. Install the deploy key.** On your **Mac**:

```bash
ssh-keygen -t ed25519 -f ~/.ssh/vps_ciuser -C "gha-deploy" -N ""
cat ~/.ssh/vps_ciuser.pub
```

Copy that line. On the **server**, install it with restrictions that limit what the key can do even if it leaks:

```bash
sudo mkdir -p /home/ciuser/.ssh
echo 'no-agent-forwarding,no-port-forwarding,no-X11-forwarding PASTE_PUBLIC_KEY_LINE_HERE' \
  | sudo tee /home/ciuser/.ssh/authorized_keys
sudo chmod 700 /home/ciuser/.ssh
sudo chmod 600 /home/ciuser/.ssh/authorized_keys
sudo chown -R ciuser:ciuser /home/ciuser/.ssh
```

The prefixes block using this key as a network tunnel into your server — the pipeline needs none of that.

**F. Verify before trusting it.** From your **Mac**:

```bash
ssh -i ~/.ssh/vps_ciuser ciuser@YOUR_VPS_IP \
  'whoami; docker ps --format "{{.Names}}"; sudo -l; \
   touch /srv/apps/.t && rm /srv/apps/.t && echo "APPS WRITE OK"; \
   touch /srv/caddy/sites/.t && rm /srv/caddy/sites/.t && echo "CADDY WRITE OK"'
```

**You'll know it worked when:** you see `ciuser`, your running containers, `sudo -l` listing **only** `/usr/local/bin/ensure-db`, and both `WRITE OK` lines.

Now confirm it's actually constrained — these two **must fail**:

```bash
ssh -i ~/.ssh/vps_ciuser ciuser@YOUR_VPS_IP 'sudo cat /etc/shadow'        # expect: not allowed
ssh -i ~/.ssh/vps_ciuser ciuser@YOUR_VPS_IP 'touch /srv/backups/.t'       # expect: Permission denied
```

If either succeeds, stop and re-check steps C and D before adding the key to GitHub.

**Honest limit of this isolation:** membership in the `docker` group is effectively root-equivalent on any Linux box — anyone who can run `docker` can mount the host filesystem into a container. A separate user does **not** make a stolen CI key harmless. What it does buy you is real and worth having: revocation without touching your own access (delete the key, the user, or both, and `ravi` is unaffected), a clean audit trail of what the robot did versus what you did, no password-based path in, and no ability to reach `/srv/backups` — so your recovery path survives a compromise of the deploy path. Treat `VPS_SSH_KEY` as a production credential regardless.

**Rotating the key** (do this if a repo is ever made public by accident, or annually): regenerate on the Mac with the same command in E, replace the line in `/home/ciuser/.ssh/authorized_keys`, update the `VPS_SSH_KEY` org secret. No app changes, no redeploys needed.

### 2.2 The shared GitHub secrets

Add these **once, at organization level** so every repo inherits them: GitHub → your org → Settings → Secrets and variables → Actions → "New organization secret". (No org? Add the same set per-repo.)

| Secret name | Value | Where it comes from |
|---|---|---|
| `VPS_HOST` | YOUR_VPS_IP | Bluehost console |
| `VPS_USER` | `ciuser` | **2.1 — the CI account, not `ravi`** |
| `VPS_SSH_KEY` | entire output of `cat ~/.ssh/vps_ciuser` on your Mac, **including** the `-----BEGIN` and `-----END` lines | 2.1 E |
| `DB_PASSWORD` | your `DB_PASSWORD_HERE` | Step 6 |

`SEQ_API_KEY` is deliberately **not** here — it is per-app and repo-level. See Part 6.3 and the deployment brief.

Two mistakes that cost an hour each: pasting the `.pub` file instead of the private key into `VPS_SSH_KEY` (it must be the long multi-line block), and leaving `VPS_USER` as `ravi` from an earlier build — the pipeline then works fine and quietly runs as you.

That's the entire foundation. Per app from here: run the kickoff prompt in Claude Code → approve the variable table → add the DNS record and Seq key it prints → push.

### 2.3 Rolling back a bad deploy

Every deploy pushes the image with **two tags**: `:latest` and the commit SHA (e.g. `:a1b2c3d`). That means every version you ever deployed is already stored in GHCR — your rollback "backup" exists automatically.

To roll back: find the last good commit's SHA (GitHub → repo → Commits, copy the short SHA), then on the server:

```bash
cd /srv/apps/app1
nano docker-compose.yml     # change image line to: ghcr.io/techierathore/app1:a1b2c3d
docker compose pull && docker compose up -d
```

You're back on the old version in ~30 seconds. When you've fixed the code, change the image line back to `:latest` and let the pipeline take over again.

**The cleaner, no-SSH way** (preferred now that the pipeline manages server files): on your Mac, `git revert HEAD && git push` — the pipeline redeploys the previous code exactly like any other release, and history stays honest. Use the manual SHA edit above only when GitHub Actions itself is down or you need speed.

(Note: rollback swaps the **code**, not the database. If a deploy also ran a DB migration, rolling back code while the schema moved forward can break — for anything risky, take a manual backup first: `sudo /srv/backups/backup.sh`.)

---

## Part 3 — Weekly backups (automatic, to OneDrive + Google Drive)

**The plan in one picture:**

```
Every Sunday 3:00 AM (automatic):
  1. Postgres → one compressed dump file        (pg-2026-08-02.sql.gz)
  2. Configs + uploaded images → one archive     (files-2026-08-02.tgz)
  3. Keep the newest 6 weeks on the server, delete older
  4. Copy this week's two files → OneDrive  (your 1 TB — primary)
  5. Copy the same two files   → Google Drive  (second copy — optional)
  6. Ping healthchecks.io = "backup succeeded"  (so silence raises an alarm)
```

The tool that talks to both clouds is **rclone** — think of it as "copy-paste to any cloud drive, from the command line". You connect each cloud once; after that it's automatic forever.

### 3.1 Install rclone (server + Mac)

On the **server**:

```bash
sudo apt install -y rclone
```

On your **Mac** (needed only during setup, because the server has no browser to do the Microsoft/Google login in — your Mac does that part and hands the resulting token back):

```bash
brew install rclone
```

### 3.2 Connect OneDrive (primary — your 1 TB)

On the **server**, start the connection wizard:

```bash
sudo rclone config
```

It shows a menu. Answer each prompt exactly like this (left = what it asks, right = what you type):

| Prompt | Type |
|---|---|
| e/n/d/r/c/s/q | `n` (new remote), Enter |
| name> | `onedrive`, Enter |
| Storage> (long list of clouds) | `onedrive`, Enter |
| client_id> | just Enter (blank) |
| client_secret> | just Enter (blank) |
| region> | `1` (Microsoft Cloud Global), Enter |
| Edit advanced config? | `n` |
| **Use web browser to automatically authenticate?** | **`n`** ← the important one |

It now prints a line like:

```
rclone authorize "onedrive" "eyJjbGllbn..."
```

**Copy that entire line**, switch to your **Mac's** terminal, paste it, press Enter. Your browser opens → sign in to your Microsoft account → Accept. Back in the Mac terminal you'll see:

```
Paste the following into your remote machine --->
{"access_token":"...very long token..."}
<---End paste
```

Copy everything between the arrows (the whole `{...}` block), switch back to the **server** terminal, paste it at the `config_token>` prompt, Enter. Then:

| Prompt | Type |
|---|---|
| config_type> (OneDrive Personal or Business...) | `1` (onedrive), Enter |
| Drive found / Chose drive to use | `1` (or the number of your personal drive), Enter |
| Drive OK? …Found drive "root" of type "personal"… | `y` |
| Keep this remote? | `y` |

You're back at the menu — **stay in it** for the next step.

### 3.3 Connect Google Drive (second copy — optional but recommended; two clouds means even a hacked/closed account can't cost you your data)

Still inside `rclone config` (if you left, run `sudo rclone config` again):

| Prompt | Type |
|---|---|
| e/n/d/r/c/s/q | `n`, Enter |
| name> | `gdrive`, Enter |
| Storage> | `drive`, Enter |
| client_id> | just Enter |
| client_secret> | just Enter |
| scope> | `1` (full access), Enter |
| service_account_file> | just Enter |
| Edit advanced config? | `n` |
| **Use web browser to automatically authenticate?** | **`n`** |

Same dance as before: it prints `rclone authorize "drive" ...` → run that line on your **Mac** → browser → Google login → Allow → copy the token block → paste back on the **server**. Then: *Configure as Shared Drive?* `n` → *Keep this remote?* `y` → `q` to quit the wizard.

### 3.4 Test both connections (30 seconds — don't skip)

```bash
sudo rclone mkdir onedrive:vps-backups
sudo rclone mkdir gdrive:vps-backups
sudo rclone lsd onedrive: && sudo rclone lsd gdrive:
```

**You'll know it worked when:** each `lsd` lists a `vps-backups` folder — and you can see the new folder in OneDrive and Google Drive on your phone/browser right now.

### 3.5 Create the backup script

```bash
sudo tee /srv/backups/backup.sh > /dev/null << 'EOF'
#!/usr/bin/env bash
set -euo pipefail

STAMP=$(date +%F)            # today's date, e.g. 2026-08-15 — used in filenames
DIR=/srv/backups
KEEP=6                       # how many weekly backups to keep on the server itself
HC_URL=""                    # paste your healthchecks.io URL here after Part 4.2

# 1. Dump EVERY database (all apps) into one compressed file
sudo -u postgres pg_dumpall | gzip > "$DIR/pg-$STAMP.sql.gz"

# 2. Archive configs + uploaded files. Paths that don't exist yet are skipped,
#    not treated as a fatal error — see the note below on why this matters.
TARGETS=()
for p in /srv/caddy/Caddyfile /srv/caddy/sites /srv/apps /srv/data; do
  [ -e "$p" ] && TARGETS+=("$p")
done
tar czf "$DIR/files-$STAMP.tgz" "${TARGETS[@]}"

# 3. Rotation: keep only the newest 6 of each on the server
ls -1t "$DIR"/pg-*.sql.gz | tail -n +$((KEEP+1)) | xargs -r rm
ls -1t "$DIR"/files-*.tgz | tail -n +$((KEEP+1)) | xargs -r rm

# 4. Copy this week's two files to OneDrive (primary, 1 TB)
rclone copy "$DIR" onedrive:vps-backups --include "*-$STAMP*"

# 5. Second copy to Google Drive (delete this line if you skipped 3.3)
rclone copy "$DIR" gdrive:vps-backups --include "*-$STAMP*"

# 6. VERIFY the files actually landed. An exit code of 0 from rclone is not proof:
#    a filter that matches nothing also "succeeds". Ask each remote directly.
for remote in onedrive gdrive; do
  for f in "pg-$STAMP.sql.gz" "files-$STAMP.tgz"; do
    rclone lsf "$remote:vps-backups/$f" > /dev/null \
      || { echo "MISSING on $remote: $f" >&2; exit 1; }
  done
done

# 7. Report success to healthchecks.io (skipped until HC_URL is filled in)
if [ -n "$HC_URL" ]; then curl -fsS -m 10 "$HC_URL" > /dev/null; fi

echo "backup ok: $STAMP"
EOF
sudo chmod +x /srv/backups/backup.sh
```

Written with `tee` rather than `nano` on purpose — pasting 30 lines of shell into a terminal editor invites a mangled line you won't spot.

Three design points worth understanding, because each one is a bug the first build of this server actually hit:

**Missing paths are skipped, not fatal.** `set -e` means any failing command aborts the script. `/srv/data` doesn't exist until your first app with uploads is deployed, so an unguarded `tar` over it exits 2 and kills the run — *after* creating the local files but *before* the upload. The original version also sent tar's stderr to `/dev/null`, so this failed completely silently: local files present, cloud empty, exit code hidden. Nothing is silenced here.

**Uploads are verified, not assumed.** `rclone copy` exits 0 when its `--include` filter matches no files. Step 6 asks each remote whether the two files are actually present, so a silent no-op fails loudly and — via `set -e` — never reaches the healthchecks ping.

**`HC_URL` is guarded.** With a placeholder URL, the curl at the end failed on every run, making the script always exit non-zero. That trains you to ignore exit codes, which is how the silent upload failure above went unnoticed. Empty `HC_URL` now means "skip", so a non-zero exit always means something is genuinely wrong.

Also note `/srv/caddy/sites` in the archive list. Backing up only the one-line `Caddyfile` would leave you restoring a server with no routing for any app, since every per-app snippet lives in `sites/`.

### 3.6 Run it once manually, right now

```bash
sudo /srv/backups/backup.sh; echo "EXIT: $?"
```

**You'll know it worked when:** the last two lines are `backup ok: 2026-XX-XX` and `EXIT: 0`.

You will also see these two lines, and they are **warnings, not errors** — tar is stripping the leading `/` so the archive stores relative paths (which is why restore uses `tar xzf ... -C /`):

```
tar: Removing leading `/' from member names
tar: Removing leading `/' from hard link targets
```

Because step 6 verifies both remotes, `EXIT: 0` is now genuine proof the files reached OneDrive and Google Drive — no need to check your phone. Do it once anyway, to confirm the folders are where you expect them.

If it exits non-zero, the last line names the problem. `MISSING on onedrive: ...` means the transfer silently did nothing — check `sudo rclone config file` points at `/root/.config/rclone/rclone.conf` and `sudo rclone listremotes` shows both remotes.

### 3.7 Schedule it — every Sunday 3 AM, forever

Bluehost's minimized image has no cron. If Part 0.2 was skipped, install it now:

```bash
command -v crontab || { sudo apt install -y cron && sudo systemctl enable --now cron; }
systemctl is-active cron
```

`is-active` must print `active` before scheduling anything.

```bash
sudo crontab -e
```

First time it asks which editor — press `1` (nano). Add this line at the very bottom:

```
0 3 * * 0 /srv/backups/backup.sh >> /srv/backups/backup.log 2>&1
```

Reading that left to right: minute `0`, hour `3`, any day of month `*`, any month `*`, weekday `0` (=Sunday) → run the script, appending everything it prints to `backup.log`. Save and exit, then confirm it registered:

```bash
sudo crontab -l
```

**Prove cron actually fires — don't wait until Sunday.** Cron runs jobs in a minimal environment, and the usual failure is root's rclone config not being found. Check the clock, then add a second temporary line for ~3 minutes ahead:

```bash
date          # note the current hour and minute
sudo crontab -e
```

```
MM HH * * * /srv/backups/backup.sh >> /srv/backups/backup.log 2>&1
```

Wait for it, then:

```bash
tail -20 /srv/backups/backup.log
```

**You'll know it worked when:** the log ends with `backup ok: 2026-XX-XX`. Now `sudo crontab -e` again and delete the temporary line, leaving only the Sunday one.

If a backup ever misbehaves, the first place to look:

```bash
tail -50 /srv/backups/backup.log
```

### 3.8 Practice ONE restore (15 min, once — this is the step people skip and regret)

A backup you've never restored is a hope, not a backup. Prove yours works while nothing is on fire:

```bash
gunzip -c /srv/backups/pg-2026-XX-XX.sql.gz | sudo -u postgres psql
```

(Use the real filename from `ls /srv/backups`.)

**Errors are expected here, and they are not the test.** Restoring a `pg_dumpall` into a cluster that already contains those roles and databases always produces lines like `role "app_blog" already exists` and `database "blog" already exists`. That's normal — the dump recreates objects that are already present. Don't read those as failure, and equally don't read them as success: a genuinely corrupt dump produces error output that looks much the same at a glance.

**The real test is querying the restored data**, not watching the console scroll:

```bash
sudo -u postgres psql -c "\l"     # every app database present?
sudo -u postgres psql -c "\du"    # every app role present?
```

Before any apps exist, this verifies roles and databases survive the round trip — which is what a bare-metal rebuild depends on. **Once your first app is live, redo this test and add a row count**, which is the only check that proves actual data came back:

```bash
sudo -u postgres psql -d blog -c "select count(*) from posts;"
```

Compare it to the same query before the restore. Matching numbers mean your safety net actually catches.

**Disaster-scenario cheat sheet** (server completely dead, starting from a brand-new VPS): Part 0 → redo Part 1 Steps 1–8 → download the two newest files from OneDrive → upload to the new server with `scp pg-*.sql.gz files-*.tgz ravi@NEW_IP:/srv/backups/` (run from your Mac) → restore DB with the command above → `sudo tar xzf /srv/backups/files-*.tgz -C /` (puts configs + uploads back) → redo Caddy/apps Steps 9–12 → `sudo /srv/checkup.sh` (Part 9) to confirm nothing was missed. Total: an evening, not a catastrophe.


---

## Part 4 — Monitoring (get alerted on your phone)

**How important is this?** Without it, the way you find out a site is down is a user telling you — or worse, nobody telling you and the site being dead for a week. And without backup monitoring, backups fail *silently*: you discover it the day you need one. This part is 25 minutes of setup that prevents both. Non-negotiable.

**"But the free tiers are too limited"** — run the actual numbers for your full 15–18 app plan:

| Tool | Free tier | Your need at 18 apps | Verdict |
|---|---|---|---|
| UptimeRobot | **50 monitors**, 5-min checks | 18 HTTP monitors + 1 server ping = **19** | Fits with 31 to spare |
| healthchecks.io | **20 checks** | 1 (backup job), maybe 2 | Barely scratched |

What paid actually buys: 1-minute checks instead of 5-minute, SMS alerts, status pages. For indie apps, learning about downtime within 5 minutes via a phone push is fully sufficient. Revisit paid only if a product gets serious paying customers with an SLA expectation. **Rule that can't be compromised:** the watcher must not live on the thing it watches — which is why "self-host monitoring on the same VPS" is not a substitute (when the VPS dies, so does the thing meant to tell you).

### 4.1 UptimeRobot — is my site up? (10 minutes)

1. Sign up free at **uptimerobot.com** (free plan: 50 monitors, checks every 5 minutes).
2. Add Monitor → type **HTTP(s)** → URL: `https://app1.techierathore.com/healthz` → create. Because `/healthz` also checks the database connection, this ONE monitor catches: app crashed, Caddy down, SSL problem, or database down.
3. Add a second monitor → type **Ping** → YOUR_VPS_IP. This tells you "the whole server is down" vs "just one app is down".
4. Install the **UptimeRobot app** on your phone and log in → you now get push notifications the moment anything goes down. (Email alerts are on by default too.)

Repeat monitor #2-style HTTP checks for each new app you ship. That's your "monitor from my phone / any application" answer — nothing to build or host.

### 4.2 healthchecks.io — did my backup actually run? (5 minutes)

UptimeRobot can't see a cron job. healthchecks.io alerts you when a scheduled job *fails to check in*:

1. Sign up free at **healthchecks.io** → Add Check → name it `weekly-pg-backup`.
2. Schedule: switch to **Cron** mode, expression `0 3 * * 0`, timezone `Asia/Kolkata`, grace time 2 hours.
3. Copy the check's ping URL (looks like `https://hc-ping.com/abc123...`) and paste it into the last line of `/srv/backups/backup.sh` (replacing `YOUR-CHECK-UUID`):
   ```bash
   sudo nano /srv/backups/backup.sh
   ```
4. Run `sudo /srv/backups/backup.sh` once — the check on healthchecks.io turns green.

Now if any Sunday passes without a successful backup, you get an email. Failures are loud, not silent.

### 4.3 Disk-space cleanup (prevents the most common "server suddenly died")

Old Docker images pile up with every deploy. Auto-clean weekly — add one more line to the same crontab:

```bash
sudo crontab -e
```

```
30 3 * * 0 docker system prune -af --filter "until=168h" >> /srv/backups/prune.log 2>&1
```

Anytime you're curious how full the disk is: `df -h /` — the `Use%` for `/` should stay under 80%.

---

## Part 5 — Apps that store uploaded files (blog banners, images)

**The problem:** every deploy throws the old container away and starts a fresh one from the new image. Anything your app wrote *inside* the container — like an `uploads` folder next to the app files — is deleted with it. Exactly like your bin/obj experience on local, but happening automatically on every single deploy.

**The fix:** keep uploaded files in a folder on the **server itself**, and "mount" that folder into the container. The container sees it as a normal folder; deploys replace the container but never touch the folder. This is called a bind mount, and it's one extra block in the compose file.

### 5.1 The folder on the server — created by the pipeline, not by you

The deployment pipeline runs an idempotent `mkdir -p /srv/data/<appname>/uploads` on every deploy (it's step one of the brief document's deploy job), so the folder always exists before the app starts. Shown here only so you know what lives where:

```
/srv/data/blog/uploads      ← the blog's banner images live here, forever
```

(`/srv/data/<appname>/` is the home for every app's user files. The backup script already archives all of `/srv/data`, so new apps' uploads are covered by the weekly OneDrive/Google Drive backup automatically — nothing to add.)

### 5.2 Mount it in the app's compose file

Add a `volumes:` block to the service in `/srv/apps/blog/docker-compose.yml`:

```yaml
services:
  blog:
    image: ghcr.io/techierathore/blog:latest
    container_name: blog
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__Default: "Host=172.17.0.1;Port=5432;Database=blog;Username=appuser;Password=DB_PASSWORD_HERE"
      Uploads__Path: "/app/uploads"
    volumes:
      - /srv/data/blog/uploads:/app/uploads
    networks:
      - web

networks:
  web:
    external: true
```

Read the volumes line as: "server folder `/srv/data/blog/uploads` appears inside the container as `/app/uploads`."

### 5.3 Point your app code at the configured path

Never hardcode the folder — read it from config, so local dev and server both work:

```csharp
// Saving an uploaded banner:
var uploadRoot = builder.Configuration["Uploads:Path"] ?? "uploads";  // local fallback
Directory.CreateDirectory(uploadRoot);
var filePath = Path.Combine(uploadRoot, safeFileName);
// ... save the file stream to filePath

// Serving the images back at /uploads/... URLs:
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        builder.Configuration["Uploads:Path"] ?? Path.Combine(app.Environment.ContentRootPath, "uploads")),
    RequestPath = "/uploads"
});
```

Now a banner saved once lives at `/srv/data/blog/uploads/...` on the server, survives every deploy and rollback, shows at `https://blog.techierathore.com/uploads/whatever.png`, and rides along in the weekly backup. Nothing else to do.

(If an app ever gets a "permission denied" writing to the folder, run `sudo chmod -R 777 /srv/data/blog/uploads` — acceptable here because the folder holds only public images.)

---

## Part 6 — Central logging: Serilog → Seq

**The idea:** every app already logs through Serilog. Add one sink so all apps send their logs to a single Seq server running on the VPS. You open `https://seq.techierathore.com` from anywhere — Mac, phone, office — and search/filter logs from all your apps in one place. Seq is free for single-user, which is exactly you.

> **Read 6.1 fully before typing anything.** Seq 2026.1 requires the admin password to be supplied *before* first start, and it is baked in permanently at that moment. Get it wrong and the only fix is destroying the data volume. Two minutes of reading saves the whole detour documented in 6.6.

### 6.1 Run Seq on the server

**Decide the admin password now** and have it in your clipboard/password manager before you run anything below. Seq reads `SEQ_FIRSTRUN_ADMINPASSWORD` exactly once — during the "Create the Administrator account" migration on the very first boot. After that the variable is ignored completely: editing it later changes nothing, and there is no password-reset flow from the UI.

```bash
mkdir -p /srv/apps/seq && cd /srv/apps/seq
```

Write the compose file with `cat` rather than `nano` — this avoids the indentation/tab damage that hand-editing YAML in a terminal editor causes. Replace `PUT_A_STRONG_PASSWORD_HERE` with your real password **before** you paste this block:

```bash
cat > /srv/apps/seq/docker-compose.yml << 'EOF'
services:
  seq:
    image: datalust/seq:2026.1
    container_name: seq
    restart: unless-stopped
    environment:
      ACCEPT_EULA: "Y"
      SEQ_FIRSTRUN_ADMINPASSWORD: "PUT_A_STRONG_PASSWORD_HERE"
      SEQ_API_CANONICALURI: "https://seq.techierathore.com"
      SEQ_CACHE_SYSTEMRAMTARGET: "0.2"
    mem_limit: 2g
    volumes:
      - seq_data:/data
    networks:
      - web

volumes:
  seq_data:

networks:
  web:
    external: true
EOF
```

Why each of the non-obvious lines is there:

| Line | Why it matters |
|---|---|
| `image: datalust/seq:2026.1` | **Pinned, not `:latest`.** Seq changed its first-run behaviour between releases; a pinned tag means a future `docker compose pull` can't spring a breaking change on a working logging server. |
| `SEQ_FIRSTRUN_ADMINPASSWORD` | Mandatory from 2026.1 onward. Without it the container fails the account-creation migration, exits, and Docker restarts it forever — which reaches your browser as a plain **HTTP 502**. |
| `SEQ_API_CANONICALURI` | Seq builds login redirects and notification links from this. Behind Caddy, omitting it produces redirects pointing at the container's internal address. |
| `SEQ_CACHE_SYSTEMRAMTARGET` | Soft cache target, expressed as a fraction of **host** RAM. |
| `mem_limit: 2g` | The hard ceiling that actually protects your other 17 apps. The cache target alone does not cap the process. |

Now validate the YAML *before* starting anything. This is the cheap step that catches a mangled file:

```bash
docker compose config
```

**You'll know it worked when:** it prints the fully parsed compose file. If instead you see `go-yaml load error ... did not find expected key`, the YAML is broken — Compose did nothing at all, and any container from a previous attempt is still running unchanged. Re-run the `cat >` block above rather than trying to repair it by hand.

Only once `config` prints cleanly:

```bash
docker compose up -d
sleep 8 && docker logs seq 2>&1 | tail -15
```

**You'll know it worked when:** the log shows this sequence, with **no** `FTL` lines anywhere:

```
[INF] Seq 2026.1.x running on OS Ubuntu 24.04.x LTS
[INF] Seq using canonical URI https://seq.techierathore.com/
[INF] Initializing a new metastore
[INF] Enabling username/password authentication, and using the supplied default admin password
[INF] Storage subsystem available
[INF] Ingestion enabled
```

The line about the supplied default admin password is the confirmation that your password took. Note also `Seq listening on ["http://localhost/", ...]` — inside the container Seq serves UI and API on **port 80** (and ingestion-only on 5341), and it accepts requests on any hostname despite what "localhost" suggests. That's why 6.2 proxies to `seq:80`.

### 6.2 Put it behind Caddy with its own subdomain

Seq is the one service you configure in Caddy by hand — every other app gets its snippet from its CI/CD pipeline. Follow the same convention the pipelines use (Step 9): a snippet file in `/srv/caddy/sites/`, never an edit to the main `Caddyfile`.

```bash
cat > /srv/caddy/sites/seq.caddy << 'EOF'
seq.techierathore.com {
    reverse_proxy seq:80
}
EOF
docker exec caddy caddy reload --config /etc/caddy/Caddyfile
```

Add a DNS **A record** for `seq` → YOUR_VPS_IP (same as Step 10). Add it *before* the reload if you can, so Caddy can fetch the certificate on its first attempt.

Verify the proxy hop from inside the Caddy container before you open a browser — this isolates "Caddy can't reach Seq" from "DNS/TLS isn't ready yet":

```bash
docker exec caddy wget -S -O /dev/null http://seq/ 2>&1 | head -5
```

**You'll know it worked when:** you get `HTTP/1.1 200 OK` or a `302` redirect to the login page. If you get `bad address 'seq'`, the container isn't on the shared network — fix with `docker network connect web seq`.

### 6.3 First login, and one API key per app

Authentication is **already on** — 2026.1 enables username/password auth during first run, so there is no "turn on Require authentication" toggle to find and no window where your logs sit exposed.

1. Open `https://seq.techierathore.com`.
2. Log in as `admin` with the password from 6.1.

**Create a separate API key for every app. Never share one key across apps.** It's marginally more clicking and it buys three things that matter at 15–18 apps:

| | Why per-app keys |
|---|---|
| **Filtering that can't be forgotten** | A key can attach fixed properties server-side, so `App: blog` is stamped on every event even if that app's `Enrich.WithProperty` is missing or wrong. With one shared key, a misconfigured app becomes unattributable noise in a stream of eighteen. |
| **Revocation without collateral** | A key leaked in a log, a screenshot, or a public repo gets revoked and reissued for that one app. A shared key means re-deploying everything. |
| **Per-app volume control** | A chatty app can be capped to Warning at the key, with no code change and no redeploy. One noisy app can otherwise drown the others. |

For each app, in **Settings → API Keys → Add API Key**:

1. **Title:** the app name (`blog`, `xpenser`, `appmanager`) — this is what you'll see in the key list later.
2. **Applied properties:** add `App` = the app name. This is the enrichment that makes filtering reliable.
3. *(Optional)* **Minimum level:** leave at Information; raise to Warning later if that app gets noisy.
4. Save and **copy the key immediately** — Seq shows it once.

**Where the key goes — not in the repo.** An API key is a secret, same as your database password, and a private repo is not a secret store. Follow the path your DB password already takes in Part 2:

1. Add it as a GitHub Actions secret on that app's repository, named `SEQ_APIKEY_BLOG` (uppercase, app suffix).
2. The deploy pipeline injects it into the app's compose file at deploy time, exactly as it does `DB_PASSWORD`.
3. On your Mac, local dev has no Seq URL configured at all, so no key is needed — Serilog just writes to console.

Never commit it, never paste it into the app's `appsettings.json`, and never put it in the per-app brief doc.

If a key does leak: **Settings → API Keys →** select it **→ Revoke**, create a replacement with the same title and properties, update the GitHub secret, redeploy that one app. Ninety seconds, and nothing else is touched — which is the whole argument for per-app keys.


### 6.4 Wire your apps to Seq

NuGet packages (once per app): `Serilog.AspNetCore` and `Serilog.Sinks.Seq`.

In `Program.cs`:

```csharp
builder.Host.UseSerilog((ctx, cfg) => cfg
    .MinimumLevel.Information()
    .Enrich.WithProperty("App", "blog")          // so you can filter per-app in Seq
    .WriteTo.Console()
    .WriteTo.Seq(
        ctx.Configuration["Seq:Url"] ?? "http://localhost:5341",
        apiKey: ctx.Configuration["Seq:ApiKey"]));
```

And two lines in each app's compose file `environment:` section on the server:

```yaml
      Seq__Url: "http://seq:5341"
      Seq__ApiKey: "PASTE_THE_API_KEY_FROM_6.3"
```

(`http://seq:5341` works because the app and Seq share the `web` Docker network — logs never leave the server, and the URL needs no HTTPS or public exposure. Port 5341 is the ingestion-only port: even if an app's key leaked, it can't be used to read your logs.)

Redeploy (or `docker compose up -d` after editing), then open Seq in your browser — log lines from every app stream in live, searchable, filterable by the `App` property. On local dev, apps just log to console since no Seq URL is configured — nothing breaks.

### 6.5 The one command that stops `down -v` from destroying your logs

Until you complete 6.3, the Seq volume holds nothing and `docker compose down -v` is free. **The moment apps start logging, that flag destroys every log you have.**

| Command | Effect | When |
|---|---|---|
| `docker compose restart` | Restarts the container | Routine |
| `docker compose down` then `up -d` | Recreates the container, **keeps** the volume | After editing compose |
| `docker compose down -v` | Recreates the container and **deletes all logs** | First-run recovery only, before 6.3 |

The only legitimate reason to use `-v` on Seq is the password-recovery path in 6.6 — and only while the instance is still empty.

### 6.6 Troubleshooting: browser shows HTTP 502

A 502 means Caddy received your request (so DNS and TLS are fine) but got nothing back from the `seq` upstream. Work in this order and **do not change any config until you have read the actual error message** — the message names the fix, and guessing from symptoms wastes attempts.

**Step 1 — is the container even up?**

```bash
docker ps -a --filter name=seq
```

`Restarting (1) N seconds ago` means it's crash-looping on startup. `Up` means the problem is the proxy hop instead — jump to Step 4.

**Step 2 — read the actual error, not the stack trace.** Seq dumps ~60 lines of .NET/Autofac frames that are pure noise; the one useful line is above them. Strip the frames:

```bash
docker logs seq 2>&1 | grep -v "^   at " | grep -v "^--- End" | tail -40
```

Look for the `[FTL]` line and the `---> System.…Exception:` immediately under it. That sentence is the diagnosis.

**Step 3 — match the message:**

| Message contains | Cause | Fix |
|---|---|---|
| `No default admin password was supplied` | `SEQ_FIRSTRUN_ADMINPASSWORD` missing from the compose file | Add it (6.1), then `docker compose down -v && docker compose up -d` — the `-v` is required because the metastore recorded a partial first-run state |
| `go-yaml load error … did not find expected key` (from `compose`, not Seq) | Broken YAML indentation, usually a tab from `nano` | Re-run the `cat >` block in 6.1, then `docker compose config` |
| `UnauthorizedAccessException` on a `/data` path | Volume ownership | `docker compose down -v && docker compose up -d` |
| Anything naming a schema/storage version | Volume written by a newer Seq than the pinned image | Match the tag to the version that wrote it, or wipe with `-v` if the instance is empty |

**Step 4 — container is `Up` but still 502:**

```bash
docker exec caddy wget -S -O /dev/null http://seq/ 2>&1 | head -5   # proxy hop
docker network inspect web | grep -i seq                             # is Seq on the shared network?
grep -r "seq" /srv/caddy/sites/                                      # did the snippet land?
docker exec caddy caddy validate --config /etc/caddy/Caddyfile       # is the config valid?
```

**Can't log in even though Seq is running?** The password is whatever was in the compose file at first boot — including the literal placeholder if you pasted the block without editing it. Check for a trailing space from copy-paste first. Otherwise, while the instance is still empty, edit the password in the compose file and run `docker compose down -v && docker compose up -d`; watch for `using the supplied default admin password` in the log to confirm the new one took.

---

## Part 7 — Hosting 15–18 apps on this one VPS

**Verdict: yes, technically viable** — with eyes open about the one real constraint. CPU is a non-issue (idle Blazor apps use ~0% CPU; 4 vCPU handles low-traffic indie apps easily). 200 GB NVMe is plenty. The constraint is **RAM**:

| What | RAM (realistic) |
|---|---|
| Ubuntu + Docker + Caddy | ~0.6 GB |
| Postgres (native) | ~0.5–1 GB |
| Seq (capped in its compose file) | ~1.6 GB max |
| Each idle Blazor Server app / API | ~120–200 MB |
| **18 apps** | **~2.5–3.5 GB** |
| **Total worst case** | **~7 GB of your 8** |

Tight but workable, with three protections:

### 7.1 Add swap — do this once, today (5 commands)

Swap is emergency overflow RAM on disk. Without it, running out of RAM means Linux kills a random container; with it, things just get slower under pressure. Since your disk is NVMe, swap is genuinely usable:

```bash
sudo fallocate -l 4G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

Verify: `free -h` → the Swap row now shows `4.0Gi`.

### 7.2 Cap each app's memory

In every app's compose file, one extra line under the service:

```yaml
    mem_limit: 384m
```

(.NET 8+ is container-aware — it sees the limit and tunes its garbage collector accordingly.) This guarantees no single misbehaving app can starve the other 17. Give heavier apps like the blog engine `512m`.

### 7.3 Grow gradually and watch the numbers

Don't deploy 18 apps on day one. Add apps as they're actually ready, and after each addition glance at:

```bash
docker stats --no-stream    # RAM per container, live
free -h                     # overall RAM + swap
df -h /                     # disk
```

When steady-state RAM usage (excluding cache) crosses ~6.5 GB, that's your signal to either upgrade the plan one notch or move Postgres+Seq to a second cheap VPS. You'll see it coming weeks ahead — no surprises.

### 7.4 Domains and subdomains — how all 18 map

Caddy routes purely by **hostname**, and subdomains are free and unlimited on domains you already own. So yes — every app binds to its own domain or subdomain, all on this one server, all with automatic HTTPS. Your list mapped:

| App | Address | Caddyfile entry? |
|---|---|---|
| Techierathore.com blog | `techierathore.com` (+ `www`) | Yes |
| Stories site | `stories.techierathore.com` (or own domain) | Yes |
| Vyaasi Ji | `vyaasiji.com` (own domain) | Yes |
| Astrolyfe marketing site | `astrolyfe.com` | Yes |
| Astrolyfe API | `api.astrolyfe.com` | Yes (external apps call it) |
| XVault sale site | `xvault.techierathore.com` (or own domain) | Yes |
| AppManager (your admin UI) |  `appmanager.techierathore.com` | Yes — its own login screen is the gate |
| AppManager API |  `appmgrapi.techierathore.com` | Yes — **public**, because your desktop/mobile apps (XVault, VyaasiJi, Xpenser clients) call it from outside the VPS |

Every app therefore needs a domain or subdomain to be reachable from a browser or client app — Caddy routes by hostname, and that's the whole system. Per app: you add one A record manually (2 minutes, Step 10); the pipeline handles everything else (Caddy snippet, compose file, database, folders).

Since AppManager API is public, its own security carries the load: it should authenticate every call (login/JWT or API keys per client app) — which per your design it already does. Same for AppManager UI: its login screen is sufficient; no extra Caddy-level gate needed.

**One optimization worth having:** apps running *on the VPS* shouldn't call AppManager API over the internet and back. Containers on the shared `web` network can reach it directly by container name — so in the *server* compose files of your hosted apps, set the base URL to `http://appmgrapi:8080`, while desktop/MAUI/mobile clients use `https://appmgrapi.techierathore.com`. Same API, two doors: the internal one is faster and never leaves the machine.

### 7.5 The internal-only pattern (keep in your back pocket)

If you ever build a service that is called **only** by other apps on this VPS — a background worker's API, an internal embedding service — mark it `DOMAIN: INTERNAL` in the deployment brief. It then gets no DNS record and no Caddy snippet: it's completely invisible from the internet (zero attack surface, no certificates), and neighbors reach it at `http://<container-name>:8080`. None of your current eight apps qualifies — but the pattern costs nothing to know.

---

## Part 8 — Which problems go to chat Claude, and which go to Claude Code

This part exists because of how the Seq 502 in 6.6 actually got solved: four round-trips of *paste a command → copy the output back → get the next command*, when the answer was sitting in one line of `docker logs` the whole time. Worth being precise about where the time went, because it changes what you hand to which tool.

### What went wrong, honestly

Two failures, and only one of them is a tooling problem:

1. **Diagnosing from symptoms instead of evidence.** The first fix proposed (missing `SEQ_PASSWORD`) was a guess from the shape of the symptom, offered before anyone had read the error. It happened to be adjacent to the truth, which is worse than being wrong — it looked confirmed enough to act on. The rule this earns: **on a service that starts and dies, no config changes until the `[FTL]` line has been read out loud.** That rule is now baked into 6.6 Step 2.
2. **Human copy-paste as the transport layer.** Every observation had to be manually ferried from the server to the chat and every command manually ferried back. Roughly 15 minutes of the ~25 was transport, not thinking.

Claude Code fixes the second one completely and helps with the first only indirectly.

### Would Claude Code have done this faster? Yes — for a specific reason

Claude Code closes the loop: it runs the command, reads the output itself, edits the file, re-runs, and checks the result — without you in the middle. On this exact problem it would have run `docker ps`, seen `Restarting`, run `docker logs`, read `No default admin password was supplied`, patched the compose file, run `docker compose config`, brought the container up, and confirmed `using the supplied default admin password` in the log. One instruction from you, no pasting.

Note what did *not* get faster: the diagnosis itself was trivial once the log was read. Claude Code's advantage here is **iteration speed on a machine it can see**, not superior reasoning. Any problem where the hard part is the thinking rather than the round-trips gains much less from it.

### Setting it up for this server

Run Claude Code **on your Mac** and let it reach the VPS over your existing SSH key. Do not install it on the VPS itself — that puts an agent with `sudo` inside your production box permanently, and this server holds every app's database.

Create `/srv/CLAUDE.md` on the server (and a matching copy in whatever local folder you run Claude Code from) with the standing rules:

```markdown
# Production VPS — standing rules for agents

This is a live production server. Every app's Postgres database and all Seq logs live here.

## Never run without explicit per-instance approval
- `docker compose down -v`, `docker volume rm`, `docker system prune -a` — these destroy data volumes
- `DROP DATABASE`, `DROP TABLE`, `TRUNCATE`
- `rm -rf` anywhere under /srv, /var/lib/docker, or /home
- `ufw` changes, edits to /etc/ssh/sshd_config, `systemctl restart ssh` — these can lock everyone out
- Anything that touches /srv/backups or the rclone remotes

## Always
- Read logs and reproduce the failure before editing any config
- Run `docker compose config` before `docker compose up` on any edited compose file
- Pin image tags; never introduce `:latest` into a compose file
- Report what the actual error message said, quoted, before proposing a fix
```

The destructive list matters more than it looks. In 6.6, `docker compose down -v` was the correct fix *only because the instance was still empty*. The same command next month wipes every log you have — and an agent optimising for "get the container healthy" has no way to know which month it is.

### The triage table

| Bring it to **chat Claude** | Take it to **Claude Code** |
|---|---|
| "Should Seq live on this VPS or a second one?" — architecture and trade-off calls | "Seq is 502ing, work out why and fix it" — closed-loop debugging on a live service |
| Reading vendor docs/release notes to find what changed between versions | Applying the change across 15 compose files once you know what it is |
| Updating this runbook after an incident | Generating the per-app deployment brief, Caddy snippet, and compose file |
| Anything where the fix is destructive and you want to think before acting | Anything that is `run → read → edit → re-run` and you'd otherwise do by hand |
| Security posture decisions — what to expose, what to authenticate, what stays INTERNAL | Grepping the whole server for a misconfiguration you already know the shape of |
| "Is this approach sane?" before you've written anything | "Make the approach work" after you've decided |
| Drafting the CLAUDE.md guardrails themselves | Operating inside those guardrails |

The clean split: **chat for judgement, Claude Code for loops.** If your next message would be pasting terminal output back, that was a Claude Code task from the start. If your next message would be "but is that the right design?", it wasn't.

### The one habit worth keeping either way

Whichever tool you use, make the first action *read the error*, not *change the config*. Claude Code will happily guess too if you ask it to fix something without pointing it at the logs — the phrasing that gets the good behaviour is "read the container logs and tell me the actual failure before changing anything," and the CLAUDE.md rule above makes that the default.

---

## Part 9 — Checkup: prove the whole server is actually set up

Part 0 tells you what's missing before you start. This part tells you what's working after you finish — and every time you come back to this server weeks later wondering whether something drifted.

The failures this catches are the quiet ones. A backup that stopped uploading. A container restart-looping since Tuesday. A cron job that was never registered. Nothing about those announces itself; you find out when you need the thing.

### 9.1 Install the checkup script

```bash
sudo tee /srv/checkup.sh > /dev/null << 'EOF'
#!/usr/bin/env bash
# Reports server state. Changes nothing. Safe to run any time.

pass=0; fail=0
check() {
  if eval "$2" > /dev/null 2>&1; then
    printf '[ OK ] %s\n' "$1"; pass=$((pass+1))
  else
    printf '[FAIL] %s\n' "$1"; fail=$((fail+1))
  fi
}

echo "=== Host ==="
. /etc/os-release && echo "$PRETTY_NAME | $(nproc) cores | $(free -h | awk '/^Mem:/{print $2}') RAM"
echo "Disk : $(df -h / | awk 'NR==2{print $3" used of "$2" ("$5")"}')"
echo "RAM  : $(free -h | awk '/^Mem:/{print $3" used of "$2}')"
echo "Swap : $(free -h | awk '/^Swap:/{print $3" used of "$2}')"
echo

echo "=== Base system ==="
check "swap file active"            'swapon --show | grep -q .'
check "firewall (ufw) active"       'ufw status | grep -q "Status: active"'
check "fail2ban running"            'systemctl is-active --quiet fail2ban'
check "unattended-upgrades running" 'systemctl is-active --quiet unattended-upgrades'
check "cron running"                'systemctl is-active --quiet cron'
echo

echo "=== Database ==="
check "postgres running"            'systemctl is-active --quiet postgresql'
check "postgres accepts queries"    'sudo -u postgres psql -tAc "select 1"'
check "pgvector available"          'sudo -u postgres psql -tAc "select 1 from pg_available_extensions where name = '"'"'vector'"'"'" | grep -q 1'
echo

echo "=== Docker ==="
check "docker running"              'systemctl is-active --quiet docker'
check "shared network web exists"   'docker network inspect web'
check "no container restart-looping" '! docker ps --filter status=restarting --format "{{.Names}}" | grep -q .'
echo "Containers up: $(docker ps --format '{{.Names}}' | paste -sd, -)"
restarting=$(docker ps --filter status=restarting --format '{{.Names}}' | paste -sd, -)
[ -n "$restarting" ] && echo "RESTART-LOOPING: $restarting  -> docker logs <name>"
echo

echo "=== Web / TLS ==="
check "caddy container up"          'docker ps --format "{{.Names}}" | grep -qx caddy'
check "caddy config valid"          'docker exec caddy caddy validate --config /etc/caddy/Caddyfile'
echo "Sites configured: $(ls -1 /srv/caddy/sites/*.caddy 2>/dev/null | wc -l)"
echo

echo "=== Logging ==="
check "seq container up"            'docker ps --format "{{.Names}}" | grep -qx seq'
check "seq answers through caddy"   'docker exec caddy wget -q -O /dev/null http://seq/'
echo

echo "=== CI deploy account ==="
check "ciuser exists"          'id ciuser'
check "ciuser in docker group"      'id -nG ciuser | tr " " "\n" | grep -qx docker'
check "ciuser sudo limited to ensure-db" '[ "$(sudo -l -U ciuser 2>/dev/null | grep -c NOPASSWD)" = "1" ]'
check "ciuser ssh key installed"    '[ -s /home/ciuser/.ssh/authorized_keys ]'
check "ciuser CANNOT reach backups" '! sudo -u ciuser test -w /srv/backups'
echo

echo "=== Folders ==="
for d in /srv/caddy /srv/caddy/sites /srv/apps /srv/data /srv/backups; do
  check "exists: $d" "[ -d $d ]"
done
echo

echo "=== Backups ==="
check "backup script executable"    '[ -x /srv/backups/backup.sh ]'
check "weekly cron job registered"  'crontab -l | grep -q backup.sh'
check "a backup ran in last 8 days" '[ -n "$(find /srv/backups -name "pg-*.sql.gz" -mtime -8 2>/dev/null)" ]'
check "latest backup on onedrive"   'rclone lsf onedrive:vps-backups | grep -q "^pg-"'
check "latest backup on gdrive"     'rclone lsf gdrive:vps-backups | grep -q "^pg-"'
echo "Local backups:"
ls -1t /srv/backups/pg-*.sql.gz 2>/dev/null | head -3 | sed 's/^/  /' || echo "  none"
echo "Newest on OneDrive: $(rclone lsf onedrive:vps-backups 2>/dev/null | sort | tail -1)"
echo

echo "=== Monitoring ==="
check "healthchecks URL configured" 'grep -q "HC_URL=\"https" /srv/backups/backup.sh'
echo

echo "==================================="
echo "  $pass passed, $fail failed"
[ $fail -gt 0 ] && echo "  Re-read the runbook section for each [FAIL] above."
echo "==================================="
EOF
sudo chmod +x /srv/checkup.sh
```

### 9.2 Run it

```bash
sudo /srv/checkup.sh
```

**You'll know the build is complete when:** every line reads `[ OK ]` and the footer shows `0 failed`.

Expect some `[FAIL]` lines while you're still working through the runbook — they're a progress tracker, not an error. `healthchecks URL configured` fails until Part 4.2. Backup checks fail until Part 3. Seq checks fail until Part 6. That's the point: the script tells you where you actually are.

### 9.3 When to run it

- **After finishing the runbook** — the acceptance test for the whole build.
- **After deploying each new app** — catches a restart-looping container immediately, instead of via a 502 in your browser later.
- **Monthly, or any time something feels off** — the fastest way to see whether the boring infrastructure is still boring.
- **First command after any reboot** — confirms everything that should auto-start actually did.

### 9.4 Reading a failure

`[FAIL]` names the check, and each one maps to a section:

| Failed check | Go to |
|---|---|
| swap file active | Part 7.1 |
| firewall / fail2ban / unattended-upgrades | Step 4 |
| cron running | Part 0.2 (`apt install cron`) |
| postgres / pgvector | Steps 6–7 |
| shared network web exists | Step 8 |
| container restart-looping | Part 6.6 Step 2 — read the logs before changing anything |
| caddy config valid | Step 9 |
| seq answers through caddy | Part 6.6 |
| exists: /srv/data | Step 8 |
| weekly cron job registered | Part 3.7 |
| ciuser account / groups / sudo | Part 2.1 |
| backup on onedrive / gdrive | Part 3.6 |
| healthchecks URL configured | Part 4.2, then paste the URL into `HC_URL` in `backup.sh` |

---

## Do it in this order

| # | What | Where | Time |
|---|---|---|---|
| 0 | **Part 0: preflight inventory + install missing base tools** | Server | ~10 min |
| 1 | Steps 1–5: login, user, security, Docker | Server | ~30 min |
| 2 | Steps 6–8: Postgres + pgvector, Beekeeper, folders | Server + Mac | ~25 min |
| 3 | Part 7.1: add swap (5 commands) | Server | ~5 min |
| 4 | Step 9: Caddy (starts empty — no domains needed) | Server | ~10 min |
| 5 | Part 2: deploy key + shared secrets (one-time foundation) | Mac + GitHub | ~15 min |
| 6 | Per app: fill the brief doc, run Claude Code, add the A record it prints, push | Mac + registrar | ~15 min per app |
| 7 | Part 3: rclone (OneDrive + GDrive), backup script, cron, test restore | Server + Mac | ~45 min |
| 8 | Part 4: UptimeRobot + healthchecks.io | Browser + phone | ~15 min |
| 9 | Part 5: bind mounts for any app that stores uploads | Server + app code | ~20 min per app |
| 10 | Part 6: Seq container + Serilog sink in each app (**read 6.1 before typing**) | Server + app code | ~30 min |
| 11 | Part 7.4–7.6: repeat per app as each one becomes ready | — | ~30 min per app |
| 12 | **Part 9: install `/srv/checkup.sh` and run it — every line must read `[ OK ]`** | Server | ~5 min |
