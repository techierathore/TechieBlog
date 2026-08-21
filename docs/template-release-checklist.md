# GitHub Template Repository Release Checklist

**Purpose:** Ensure TechieBlog is ready for release as a GitHub Template Repository.

**Last Updated:** December 31, 2025

---

## Pre-Release Checklist

### 1. Artifact Cleanup

- [ ] Delete `NUL` file from root (Windows artifact)
- [ ] Delete `CLAUDE.md` from root (already gitignored, but remove from repo if committed)
- [ ] Delete or empty `uiissues/` folder (dev-specific)
- [ ] Verify `.vs/` folder is not committed (should be gitignored)
- [ ] Verify `bin/` and `obj/` folders are not committed

**Commands to run:**
```bash
# Check for committed artifacts
git ls-files | grep -E "(NUL|CLAUDE\.md|uiissues)"

# Remove if found
git rm NUL
git rm CLAUDE.md
git rm -r uiissues/
```

### 2. Configuration Safety

- [ ] `appsettings.json` contains NO real passwords
- [ ] `appsettings.Development.json` uses `localhost` and placeholder passwords
- [ ] No `.env` files with real secrets committed
- [ ] Connection strings use placeholder: `Password=CHANGE_ME` or `Password=YOUR_PASSWORD`
- [ ] SMTP settings use placeholder values

**Verify with:**
```bash
# Search for potential secrets
git grep -i "password=" -- "*.json" "*.config"
git grep -i "apikey" -- "*.json" "*.config"
git grep -i "secret" -- "*.json" "*.config"
```

### 3. Documentation

- [ ] `README.md` updated with template-focused content
- [ ] `GETTING_STARTED.md` created with setup instructions
- [ ] `LICENSE.txt` exists and is appropriate (MIT recommended)
- [ ] `docs/architecture.md` exists or is referenced
- [ ] Remove or update any personal/project-specific references

### 4. .gitignore Verification

- [ ] `CLAUDE.md` is in .gitignore
- [ ] `.claude/` is in .gitignore
- [ ] `appsettings.Local.json` is in .gitignore
- [ ] `appsettings.Production.json` is in .gitignore
- [ ] `*.local.json` is in .gitignore
- [ ] `.vs/` is in .gitignore
- [ ] `bin/` and `obj/` are in .gitignore

### 5. Build Verification

- [ ] Fresh clone builds without errors
- [ ] `dotnet restore` succeeds
- [ ] `dotnet build` succeeds
- [ ] Application starts with placeholder config

**Test with:**
```bash
# Clone to temp directory
git clone . /tmp/techieblog-test
cd /tmp/techieblog-test
dotnet restore
dotnet build
```

### 6. Content Review

- [ ] No TODO comments with personal names/emails
- [ ] No hardcoded URLs pointing to private servers
- [ ] Sample/seed data is generic (not personal blog posts)
- [ ] Image assets are generic or properly licensed

### 7. Repository Settings (GitHub)

- [ ] Repository is public (or will be when released)
- [ ] "Template repository" checkbox is enabled in Settings
- [ ] Topics/tags added: `blazor`, `dotnet`, `blog`, `template`, `postgresql`
- [ ] Description updated
- [ ] Website URL set (if applicable)

---

## Post-Release Verification

After enabling as template, verify:

- [ ] "Use this template" button appears
- [ ] Create a test repository from template
- [ ] Clone test repository
- [ ] Verify build works
- [ ] Verify no secrets were included
- [ ] Delete test repository

---

## Files Summary

### Keep in Template

| File/Folder | Reason |
|-------------|--------|
| `README.md` | Essential documentation |
| `GETTING_STARTED.md` | Setup guide |
| `LICENSE.txt` | Legal requirement |
| `.gitignore` | Build hygiene |
| `TechieBlog.slnx` | Solution file |
| `source/` | All source code |
| `docs/` | Documentation |
| `mockups/` | Design reference (optional) |

### Remove Before Release

| File/Folder | Reason |
|-------------|--------|
| `NUL` | Windows artifact |
| `CLAUDE.md` | AI assistant config (personal) |
| `uiissues/` | Development notes (personal) |
| `.vs/` | IDE settings (should be gitignored) |
| `bin/`, `obj/` | Build output (should be gitignored) |

### Consider Removing

| File/Folder | Decision Needed |
|-------------|-----------------|
| `docs/stories/` | User stories are project-specific |
| `docs/qa/` | QA gates are project-specific |
| `.tfcore/` | Already gitignored |

---

## Quick Commands

```bash
# Full cleanup sequence
git rm -f NUL 2>/dev/null || true
git rm -f CLAUDE.md 2>/dev/null || true
git rm -rf uiissues/ 2>/dev/null || true

# Verify no secrets
git grep -i "password=" -- "*.json" | grep -v "CHANGE_ME\|YOUR_PASSWORD\|placeholder"

# Test fresh build
dotnet clean && dotnet restore && dotnet build

# Commit cleanup
git add -A
git commit -m "Prepare repository for template release"
```

---

## Final Sign-Off

| Check | Verified By | Date |
|-------|-------------|------|
| Artifacts cleaned | | |
| Secrets removed | | |
| Documentation complete | | |
| Build verified | | |
| Template enabled | | |
| Test clone successful | | |

---

**Template Release Status:** PENDING

*Complete all items above, then update status to READY*
