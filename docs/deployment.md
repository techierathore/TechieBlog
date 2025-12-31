# TechieBlog Deployment Guide

This guide covers deploying TechieBlog to various production environments.

---

## Table of Contents

1. [Pre-Deployment Checklist](#pre-deployment-checklist)
2. [Configuration for Production](#configuration-for-production)
3. [Deployment Options](#deployment-options)
   - [Docker](#docker-deployment)
   - [Azure App Service](#azure-app-service)
   - [Linux Server (Ubuntu/Debian)](#linux-server)
   - [Windows Server / IIS](#windows-server--iis)
4. [Database Setup](#database-setup)
5. [SSL/HTTPS Configuration](#sslhttps-configuration)
6. [Environment Variables](#environment-variables)
7. [Health Checks & Monitoring](#health-checks--monitoring)
8. [Backup & Recovery](#backup--recovery)
9. [Troubleshooting](#troubleshooting)

---

## Pre-Deployment Checklist

Before deploying to production:

- [ ] Change default admin password
- [ ] Update `appsettings.Production.json` with real values
- [ ] Configure SMTP for email functionality
- [ ] Set up PostgreSQL database
- [ ] Configure SSL certificate
- [ ] Set up backup strategy
- [ ] Review security settings
- [ ] Test locally in Release mode

---

## Configuration for Production

### Create Production Configuration

Create `source/TechieBlog/appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=your-db-server;Port=5432;Database=techieblog;Username=techieblog_user;Password=STRONG_PASSWORD_HERE"
  },
  "SiteSettings": {
    "SiteName": "My Blog",
    "SiteDescription": "Welcome to my blog",
    "SiteUrl": "https://myblog.com",
    "Theme": "fluent-modern",
    "PostsPerPage": 10,
    "AllowRegistration": true,
    "AllowComments": true,
    "RequireCommentApproval": true
  },
  "EmailSettings": {
    "SmtpHost": "smtp.your-provider.com",
    "SmtpPort": 587,
    "SmtpUsername": "your-smtp-user",
    "SmtpPassword": "your-smtp-password",
    "FromAddress": "noreply@myblog.com",
    "FromName": "My Blog"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "myblog.com;www.myblog.com"
}
```

### Security Recommendations

1. **Never commit production secrets** - Use environment variables or secret managers
2. **Use strong database passwords** - 20+ characters, mixed case, numbers, symbols
3. **Enable HTTPS only** - Redirect all HTTP to HTTPS
4. **Set AllowedHosts** - Prevent host header attacks
5. **Enable rate limiting** - Protect against brute force

---

## Deployment Options

### Docker Deployment

#### Dockerfile

Create `Dockerfile` in the root:

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY TechieBlog.slnx .
COPY source/ source/

# Restore and build
RUN dotnet restore
RUN dotnet publish source/TechieBlog/TechieBlog.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install PostgreSQL client for health checks (optional)
RUN apt-get update && apt-get install -y postgresql-client && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Create directory for uploaded images
RUN mkdir -p /app/BlogImages

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "TechieBlog.dll"]
```

#### Docker Compose

Create `docker-compose.yml`:

```yaml
version: '3.8'

services:
  techieblog:
    build: .
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__DefaultConnection=Host=db;Port=5432;Database=techieblog;Username=techieblog;Password=${DB_PASSWORD}
    depends_on:
      - db
    volumes:
      - blog-images:/app/BlogImages
    restart: unless-stopped

  db:
    image: postgres:15
    environment:
      - POSTGRES_DB=techieblog
      - POSTGRES_USER=techieblog
      - POSTGRES_PASSWORD=${DB_PASSWORD}
    volumes:
      - postgres-data:/var/lib/postgresql/data
    restart: unless-stopped

volumes:
  postgres-data:
  blog-images:
```

#### Deploy with Docker

```bash
# Set environment variables
export DB_PASSWORD=your-strong-password

# Build and start
docker-compose up -d

# View logs
docker-compose logs -f techieblog

# Stop
docker-compose down
```

---

### Azure App Service

#### Prerequisites

- Azure subscription
- Azure CLI installed
- PostgreSQL database (Azure Database for PostgreSQL or external)

#### Step 1: Create Resources

```bash
# Login to Azure
az login

# Create resource group
az group create --name techieblog-rg --location eastus

# Create App Service plan
az appservice plan create \
  --name techieblog-plan \
  --resource-group techieblog-rg \
  --sku B1 \
  --is-linux

# Create Web App
az webapp create \
  --name techieblog-app \
  --resource-group techieblog-rg \
  --plan techieblog-plan \
  --runtime "DOTNET|10.0"
```

#### Step 2: Configure App Settings

```bash
# Set connection string
az webapp config connection-string set \
  --name techieblog-app \
  --resource-group techieblog-rg \
  --settings DefaultConnection="Host=your-postgres.postgres.database.azure.com;Database=techieblog;Username=admin@your-postgres;Password=YOUR_PASSWORD;SSL Mode=Require" \
  --connection-string-type PostgreSQL

# Set app settings
az webapp config appsettings set \
  --name techieblog-app \
  --resource-group techieblog-rg \
  --settings \
    ASPNETCORE_ENVIRONMENT=Production \
    SiteSettings__SiteUrl=https://techieblog-app.azurewebsites.net
```

#### Step 3: Deploy

**Option A: GitHub Actions (Recommended)**

Create `.github/workflows/azure-deploy.yml`:

```yaml
name: Deploy to Azure

on:
  push:
    branches: [main]

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Build
        run: dotnet publish source/TechieBlog/TechieBlog.csproj -c Release -o ./publish

      - name: Deploy to Azure
        uses: azure/webapps-deploy@v2
        with:
          app-name: techieblog-app
          publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}
          package: ./publish
```

**Option B: Direct Publish**

```bash
# Publish locally
dotnet publish source/TechieBlog/TechieBlog.csproj -c Release -o ./publish

# Deploy using Azure CLI
az webapp deploy \
  --name techieblog-app \
  --resource-group techieblog-rg \
  --src-path ./publish.zip \
  --type zip
```

---

### Linux Server

#### Prerequisites

- Ubuntu 22.04+ or Debian 11+
- sudo access
- Domain name pointing to server

#### Step 1: Install Dependencies

```bash
# Update system
sudo apt update && sudo apt upgrade -y

# Install .NET 10 runtime
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --runtime aspnetcore --version 10.0

# Add to PATH
echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
echo 'export PATH=$PATH:$DOTNET_ROOT' >> ~/.bashrc
source ~/.bashrc

# Install PostgreSQL
sudo apt install -y postgresql postgresql-contrib

# Install Nginx
sudo apt install -y nginx
```

#### Step 2: Setup Database

```bash
# Switch to postgres user
sudo -u postgres psql

# Create database and user
CREATE DATABASE techieblog;
CREATE USER techieblog_user WITH ENCRYPTED PASSWORD 'your-strong-password';
GRANT ALL PRIVILEGES ON DATABASE techieblog TO techieblog_user;
\q
```

#### Step 3: Deploy Application

```bash
# Create app directory
sudo mkdir -p /var/www/techieblog
sudo chown $USER:$USER /var/www/techieblog

# Copy published files (from your build machine)
# scp -r ./publish/* user@server:/var/www/techieblog/

# Create appsettings.Production.json
sudo nano /var/www/techieblog/appsettings.Production.json
```

#### Step 4: Create Systemd Service

Create `/etc/systemd/system/techieblog.service`:

```ini
[Unit]
Description=TechieBlog
After=network.target postgresql.service

[Service]
WorkingDirectory=/var/www/techieblog
ExecStart=/home/youruser/.dotnet/dotnet /var/www/techieblog/TechieBlog.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=techieblog
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

Enable and start:

```bash
sudo systemctl enable techieblog
sudo systemctl start techieblog
sudo systemctl status techieblog
```

#### Step 5: Configure Nginx

Create `/etc/nginx/sites-available/techieblog`:

```nginx
server {
    listen 80;
    server_name myblog.com www.myblog.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # Increase max upload size for images
    client_max_body_size 10M;
}
```

Enable site:

```bash
sudo ln -s /etc/nginx/sites-available/techieblog /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

#### Step 6: Setup SSL with Let's Encrypt

```bash
# Install Certbot
sudo apt install -y certbot python3-certbot-nginx

# Get certificate
sudo certbot --nginx -d myblog.com -d www.myblog.com

# Auto-renewal is configured automatically
```

---

### Windows Server / IIS

#### Prerequisites

- Windows Server 2019+
- IIS installed
- .NET 10 Hosting Bundle

#### Step 1: Install Hosting Bundle

Download and install the [.NET 10 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) on the server.

#### Step 2: Create IIS Site

1. Open IIS Manager
2. Right-click "Sites" → "Add Website"
3. Configure:
   - Site name: `TechieBlog`
   - Physical path: `C:\inetpub\techieblog`
   - Binding: Choose your domain and port

#### Step 3: Configure Application Pool

1. Select the application pool
2. Set .NET CLR version to "No Managed Code"
3. Set "Start Mode" to "AlwaysRunning" (optional, for faster cold starts)

#### Step 4: Deploy Files

Publish and copy to `C:\inetpub\techieblog\`:

```powershell
dotnet publish source/TechieBlog/TechieBlog.csproj -c Release -o C:\inetpub\techieblog
```

#### Step 5: Set Permissions

```powershell
# Grant IIS_IUSRS permissions
icacls "C:\inetpub\techieblog" /grant "IIS_IUSRS:(OI)(CI)M"
icacls "C:\inetpub\techieblog\BlogImages" /grant "IIS_IUSRS:(OI)(CI)M"
```

---

## Database Setup

### PostgreSQL Production Setup

```sql
-- Connect as superuser
psql -U postgres

-- Create database
CREATE DATABASE techieblog
    WITH ENCODING = 'UTF8'
    LC_COLLATE = 'en_US.UTF-8'
    LC_CTYPE = 'en_US.UTF-8';

-- Create application user (limited privileges)
CREATE USER techieblog_app WITH ENCRYPTED PASSWORD 'strong-password-here';
GRANT CONNECT ON DATABASE techieblog TO techieblog_app;

-- Connect to the database
\c techieblog

-- Grant schema permissions
GRANT USAGE ON SCHEMA public TO techieblog_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO techieblog_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO techieblog_app;

-- Set default privileges for future tables
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO techieblog_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO techieblog_app;
```

### Run Migrations

Migrations run automatically on app startup, or manually:

```bash
cd source/BlogDb
dotnet run -- --environment Production
```

---

## SSL/HTTPS Configuration

### Force HTTPS

In `Program.cs` or via configuration:

```csharp
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
```

### HSTS Headers

Add to `appsettings.Production.json`:

```json
{
  "Hsts": {
    "MaxAge": 31536000,
    "IncludeSubDomains": true,
    "Preload": true
  }
}
```

---

## Environment Variables

For sensitive configuration, use environment variables instead of config files:

| Variable | Purpose |
|----------|---------|
| `ConnectionStrings__DefaultConnection` | Database connection |
| `SiteSettings__SiteUrl` | Public URL |
| `EmailSettings__SmtpPassword` | SMTP password |
| `ASPNETCORE_ENVIRONMENT` | Environment name |

### Setting in Docker

```yaml
environment:
  - ConnectionStrings__DefaultConnection=Host=db;...
```

### Setting in Linux

```bash
export ConnectionStrings__DefaultConnection="Host=localhost;..."
```

### Setting in Azure

```bash
az webapp config appsettings set --settings KEY=VALUE
```

---

## Health Checks & Monitoring

### Health Check Endpoint

TechieBlog exposes `/health` for monitoring:

```bash
curl https://myblog.com/health
# Returns: Healthy
```

### Monitoring with Uptime Tools

Configure your monitoring service to check:
- **URL:** `https://myblog.com/health`
- **Expected:** HTTP 200
- **Interval:** 60 seconds

### Logging

Logs are written via Serilog. Configure sinks in `appsettings.Production.json`:

```json
{
  "Serilog": {
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "/var/log/techieblog/log-.txt",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ]
  }
}
```

---

## Backup & Recovery

### Database Backup

```bash
# Daily backup script
pg_dump -U techieblog_app -h localhost techieblog > /backups/techieblog_$(date +%Y%m%d).sql

# Compress
gzip /backups/techieblog_$(date +%Y%m%d).sql
```

### Restore from Backup

```bash
gunzip techieblog_20250101.sql.gz
psql -U postgres -d techieblog < techieblog_20250101.sql
```

### Image Backup

```bash
# Backup uploaded images
tar -czf /backups/images_$(date +%Y%m%d).tar.gz /var/www/techieblog/BlogImages
```

### Automated Backup Script

Create `/etc/cron.daily/techieblog-backup`:

```bash
#!/bin/bash
BACKUP_DIR=/backups/techieblog
DATE=$(date +%Y%m%d)

# Database
pg_dump -U techieblog_app techieblog | gzip > $BACKUP_DIR/db_$DATE.sql.gz

# Images
tar -czf $BACKUP_DIR/images_$DATE.tar.gz /var/www/techieblog/BlogImages

# Keep only last 30 days
find $BACKUP_DIR -mtime +30 -delete
```

---

## Troubleshooting

### Application Won't Start

1. Check logs: `journalctl -u techieblog -f`
2. Verify database connection
3. Check file permissions
4. Ensure .NET runtime is installed

### 502 Bad Gateway (Nginx)

1. Check if app is running: `systemctl status techieblog`
2. Verify proxy_pass port matches app port
3. Check Nginx error logs: `tail -f /var/log/nginx/error.log`

### Database Connection Failed

1. Verify PostgreSQL is running
2. Check connection string format
3. Test connection: `psql -h host -U user -d database`
4. Check firewall rules

### SSL Certificate Issues

1. Verify certificate is valid: `certbot certificates`
2. Renew if needed: `certbot renew`
3. Check Nginx SSL configuration

### High Memory Usage

1. Check for memory leaks in logs
2. Configure garbage collection:
   ```json
   {
     "runtimeOptions": {
       "configProperties": {
         "System.GC.Server": true
       }
     }
   }
   ```

---

## Quick Reference

### Common Commands

| Task | Command |
|------|---------|
| Start app | `sudo systemctl start techieblog` |
| Stop app | `sudo systemctl stop techieblog` |
| Restart app | `sudo systemctl restart techieblog` |
| View logs | `journalctl -u techieblog -f` |
| Check status | `sudo systemctl status techieblog` |
| Test Nginx config | `sudo nginx -t` |
| Reload Nginx | `sudo systemctl reload nginx` |
| Renew SSL | `sudo certbot renew` |

### Port Reference

| Service | Default Port |
|---------|-------------|
| TechieBlog | 5000 (HTTP), 5001 (HTTPS) |
| PostgreSQL | 5432 |
| Nginx | 80 (HTTP), 443 (HTTPS) |

---

## Need Help?

- Check [GETTING_STARTED.md](../GETTING_STARTED.md) for initial setup
- Review [Architecture](architecture.md) for technical details
- Open an issue on GitHub for deployment problems
