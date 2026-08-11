# =============================================================================
# TechieBlog — production container image
#
# Build (BuildKit is REQUIRED for the secret mount):
#   DOCKER_BUILDKIT=1 docker build \
#     --secret id=nuget_pat,env=TRBLAZEUI_PACKAGES_TOKEN \
#     -t ghcr.io/techierathore/techieblog:latest .
#
# THE PRIVATE FEED, AND WHY THE TOKEN ARRIVES THIS WAY.
# BlogUI and the host reference TrBlazeUI.* from a PRIVATE GitHub Packages feed. GitHub Packages
# refuses anonymous reads, so an unauthenticated `dotnet restore` inside this image fails with
# NU1301 / 403 — the exact error currently breaking CI. The token is therefore mounted as a BuildKit
# secret: it exists only for the lifetime of the RUN that needs it, is never written to a layer, and
# never appears in `docker history`.
#
# It is NOT an ARG and NOT an ENV, and no credential is ever committed to NuGet.Config. Both of those
# WOULD land in the image: `docker history` prints every ARG and ENV, and a committed credential is
# published to every clone and every fork. That is not hypothetical here — a PAT was committed to
# this repository's NuGet.Config until 2026-08-09 and was revoked by GitHub secret scanning
# (REQ-NFR-025). The generated config below is written under /tmp and deleted inside the SAME RUN
# instruction, so the layer's diff contains neither the file nor the token.
#
# WHAT IS NOT BUILT: source/BlogApp targets net10.0-windows10.0.19041.0 (MAUI). It is not referenced
# by the host and is never restored or published here — this image builds the web head and its four
# dependencies only.
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1 — restore and publish
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Project files first, so a source-only change does not invalidate the restore layer.
# These five are the host's dependency closure; BlogApp is deliberately absent.
COPY source/TechieBlog/TechieBlog.csproj source/TechieBlog/
COPY source/BlogUI/BlogUI.csproj         source/BlogUI/
COPY source/BlogEngine/BlogEngine.csproj source/BlogEngine/
COPY source/BlogModel/BlogModels.csproj  source/BlogModel/
COPY source/BlogDb/BlogDb.csproj         source/BlogDb/

# The restore. The secret is optional at the Docker level (a build without it still runs) but the
# restore itself will fail on the TrBlazeUI packages when the token is missing or invalid — which is
# the correct outcome: a silent fallback to an unauthenticated feed would produce an image missing
# its UI library.
RUN --mount=type=secret,id=nuget_pat \
    set -eu; \
    NUGET_PAT="$(cat /run/secrets/nuget_pat 2>/dev/null || true)"; \
    { \
      echo '<?xml version="1.0" encoding="utf-8"?>'; \
      echo '<configuration>'; \
      echo '  <packageSources>'; \
      echo '    <clear />'; \
      echo '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'; \
      echo '    <add key="TrBlazeUI" value="https://nuget.pkg.github.com/techierathore/index.json" />'; \
      echo '  </packageSources>'; \
      if [ -n "$NUGET_PAT" ]; then \
        echo '  <packageSourceCredentials>'; \
        echo '    <TrBlazeUI>'; \
        echo '      <add key="Username" value="techierathore" />'; \
        echo "      <add key=\"ClearTextPassword\" value=\"$NUGET_PAT\" />"; \
        echo '    </TrBlazeUI>'; \
        echo '  </packageSourceCredentials>'; \
      fi; \
      echo '</configuration>'; \
    } > /tmp/nuget.docker.config; \
    dotnet restore source/TechieBlog/TechieBlog.csproj --configfile /tmp/nuget.docker.config; \
    rm -f /tmp/nuget.docker.config

# Sources second.
COPY source/ source/

# =============================================================================
# THE SECOND RESTORE IS LOAD-BEARING. DO NOT DELETE IT AS REDUNDANT.
# =============================================================================
# The restore above ran when ONLY the .csproj files existed — that is the whole point of the
# layer-caching split, and it is worth keeping. But a restore performed against a source tree with no
# .razor files and no wwwroot writes an obj/ state (project.assets.json + the generated
# *.csproj.nuget.g.props / .targets and the static-web-asset manifests) that does NOT include the
# Blazor FRAMEWORK static web assets. `dotnet publish --no-restore` then faithfully reuses that state:
# the build is green, the container starts, /healthz answers 200 and the home page renders — but
# /app/wwwroot has NO _framework directory, `@Assets["_framework/blazor.web.js"]` in App.razor emits a
# URL that 404s, the Blazor circuit never starts, and EVERY interaction on the site is dead. Measured:
# 586 published routes without this restore, 606 with it.
#
# Re-restoring here, now that the sources and wwwroot are present, repairs that state. Every package
# is already in the warm NuGet cache from the first restore, so this costs a few seconds and no
# downloads. The secret is mounted again because NuGet still evaluates the configured sources; without
# it this restore would be the unauthenticated one that reintroduces the NU1301 / 403.
#
# `--no-restore` stays on the publish below: it guarantees the publish cannot re-resolve packages
# without the secret, which would silently reintroduce the 403 at a later, more confusing point.
#
# If you are tempted to collapse the two restores into one, DON'T: dropping the first one throws away
# the layer cache, and dropping this one silently ships a non-interactive site that passes every
# automated health check. (Cluster L/M, 2026-08-11.)
RUN --mount=type=secret,id=nuget_pat \
    set -eu; \
    NUGET_PAT="$(cat /run/secrets/nuget_pat 2>/dev/null || true)"; \
    { \
      echo '<?xml version="1.0" encoding="utf-8"?>'; \
      echo '<configuration>'; \
      echo '  <packageSources>'; \
      echo '    <clear />'; \
      echo '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'; \
      echo '    <add key="TrBlazeUI" value="https://nuget.pkg.github.com/techierathore/index.json" />'; \
      echo '  </packageSources>'; \
      if [ -n "$NUGET_PAT" ]; then \
        echo '  <packageSourceCredentials>'; \
        echo '    <TrBlazeUI>'; \
        echo '      <add key="Username" value="techierathore" />'; \
        echo "      <add key=\"ClearTextPassword\" value=\"$NUGET_PAT\" />"; \
        echo '    </TrBlazeUI>'; \
        echo '  </packageSourceCredentials>'; \
      fi; \
      echo '</configuration>'; \
    } > /tmp/nuget.docker.config; \
    dotnet restore source/TechieBlog/TechieBlog.csproj --configfile /tmp/nuget.docker.config; \
    rm -f /tmp/nuget.docker.config

RUN dotnet publish source/TechieBlog/TechieBlog.csproj \
      --configuration Release \
      --no-restore \
      --output /app/publish

# DbUp applies the migrations at host startup and looks for the scripts beside the binaries when the
# development-tree relative path does not exist (see Program.cs). Copy them to that fallback
# location, or a fresh database is never created and the host logs "skipping migrations".
COPY source/BlogDb/PostgresScripts/ /app/publish/PostgresScripts/

# -----------------------------------------------------------------------------
# Stage 2 — runtime
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    # Docker captures stdout, and a file sink inside a container writes into an ephemeral layer that
    # the next redeploy discards. Console + Seq only (REQ-NFR-029).
    LogFileEnabled=false \
    # Bind /srv/data/techieblog/uploads here so uploaded images survive a redeploy (REQ-FN-025).
    UploadsPath=/app/uploads

COPY --from=build /app/publish .

# /app/uploads is created so the container starts cleanly when no volume is bound (images then live
# in the writable layer and are lost on redeploy — the compose file MUST bind the volume), and the
# whole of /app is handed to the runtime user.
#
# THE UID MATTERS TO THE SERVER. $APP_UID is 1654, the non-root `app` user the .NET runtime images
# already ship — do NOT create another user here, the group already exists and `groupadd` fails.
# The bind-mounted host directory is owned by root by default, and a non-root process cannot write
# to it, so uploads fail at the first save with an access-denied error and nothing else. The owner
# must run `chown -R 1654:1654 /srv/data/techieblog/uploads` on the server once.
RUN mkdir -p /app/uploads && chown -R $APP_UID:$APP_UID /app
USER $APP_UID

EXPOSE 8080

ENTRYPOINT ["dotnet", "TechieBlog.dll"]
