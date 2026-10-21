# TechieBlog — project session memory (all harnesses)

<!-- This file is COMMITTED (unlike CLAUDE.md, which is gitignored and imports it).
     OpenCode and Codex auto-load AGENTS.md; Claude Code loads it through CLAUDE.md's
     @AGENTS.md import. Keep everything in here harness-neutral — Claude-only
     material (permissions, tool preference) lives in CLAUDE.md.
     Split 2026-08-27 from a pre-2026-08-20 full-content CLAUDE.md; every project
     section was carried over verbatim. The pre-split original is kept at
     .claude/CLAUDE.md.pre-split.bak. -->

## Required reading before any code change

ALWAYS read and follow:
- **docs/TechieBlog-Coding-Standards.md** — strict compliance for every line of code you write or modify.
- **docs/TechieBlog-Architecture.md** — respect module boundaries.
- **PROJECT-STATUS.md** — for current phase & next-step context.

## Host environment (carried forward from the previous CLAUDE.md, archived at docs/OldDocs/CLAUDE.md)

The repository lives on a Windows machine at `C:\1MyCode\TechieBlog`, reached from WSL as
`/mnt/c/1MyCode/TechieBlog`. When running commands:
- Use `/dev/null` for null-device redirection — never `> nul` / `2>nul`.
- Prefer the dedicated Read/Write/Edit/Glob/Grep tools over shell file commands (see the table below).
- Build with the ladder in `.tfcore/templates/v4custom/build-invocation-ladder.md` §B: this solution is
  non-MAUI, so **rung #2 — `~/.dotnet/dotnet build TechieBlog.slnx`** is the working invocation.
  Rung #4 (`cmd.exe /c "dotnet build …"`) reaches the Windows-side SDK when needed.

## Hard rules (non-negotiable — the harness enforces #1)

1. **Git is manual — agents NEVER run `git` or `gh`.** Not to commit, and not to read (`status`/`log`/`diff`/`grep`/`blame`). All harnesses enforce this mechanically: Claude Code via `.claude/settings.json` and `block-git.sh`; OpenCode via `opencode.jsonc` plus its plugin bridge; Codex via `.codex/rules/techieflow.rules` plus `.codex/hooks.json`. A blocked git call is the policy working, not an obstacle to route around. Evidence for status updates / "what changed" = the checklist Requirements Status table + the working-tree files (+ mtimes) + a fresh `dotnet build` (`.tfcore/tasks/_status-update-gate.md`). The OWNER commits, in a separate terminal.
2. **Run the app yourself — the test harness is fully set up.** Headless Playwright + Chromium live in WSL; the Windows/MAUI dotnet bridge is rung #4 of the build ladder; MAUI Android/iOS/Mac Catalyst are driven over the Appium bridge (`core-config.yaml → runtimeVerification.appium`). NEVER ask the owner to boot the app, run a build, or execute a command — "can't run on Linux/WSL", "it targets Windows", "it's MAUI", "Playwright needs a GUI", "the dependent service is down" are BANNED excuses (`.tfcore/tasks/_smoke-test-policy.md`). Asking the owner is the LAST resort, only after the build ladder + `verify-phase §3a` escalation genuinely fail — and even then you still run the test yourself once they reply.
3. **The framework tree is invisible to search — a search that finds nothing is NOT proof a file is missing.** `.tfcore/`, `.claude/`, `.codex/`, `.opencode/` and `.agents/skills/` are hidden dot-directories AND gitignored here (deliberate — deployed framework copies are never committed). File search skips hidden paths *and* honours `.gitignore`, so it takes **both** (`rg -uu`; `--hidden` alone is not enough), and `git grep`/`git ls-files` see nothing at all. Confirm a framework file by **reading its literal path** — every one has a single canonical location, and whatever needs it names that path. Never write "not present in this tree" into a verdict, a checklist Remarks cell, a BRD row, or a blocker without having tried the path and failed: that false negative propagates into the docs and the next agent treats it as fact. If a framework file really is absent, the repo needs `update-framework.sh <repo>` run once on this machine — report that instead of reimplementing what it does. (`.tfcore/tasks/_status-update-gate.md` §"The framework tree is INVISIBLE to search".)
4. **Native-head automation binds to the app's own window.** Drive a MAUI head only through a session attached to the app under test (Windows: launched PID → its top-level window handle; Android/iOS/Catalyst: the app's package/bundle id), interact element-by-element via `AutomationId`, and NEVER inject global keyboard/mouse input — it lands in whatever window happens to have focus, not the app (`verify-phase.md §3b`).

## The database — NEVER create your own container (added 2026-08-08 after a real incident)

The database is the **existing `WinPostgre` Docker container, host port `5550`** (`5550 -> 5432`). It is a
**shared, multi-project** PostgreSQL instance that also hosts `AppMngrDb` and `TkStoriesDb`. It is not
disposable: never `docker rm` it, never reset it, and **never stand up a second PostgreSQL container.**

```bash
docker start WinPostgre                                    # it is frequently stopped — just start it
docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog # psql lives INSIDE the container, not in WSL
```

`AppDbConString` = `Host=localhost;Port=5550;Database=TechieBlog;Username=PgVectorAdmin;Password=AdminPass@2025`

**Nothing listens on 5432** — that is the container's *internal* port. A dead 5432 means `WinPostgre` is
stopped, NOT that the database is missing. On 2026-08-08 a stale doc note ("TCP 5432 unreachable") led an
agent to start an unrelated container and every cluster in that build phase smoked against a throwaway
database. Schema + seed data are reproducible (DbUp applies `001…022` at startup), but the wasted work was not.

## Project basics

- Stack: .NET 10 LTS, Blazor **Server**, **TrBlazeUI**, PostgreSQL + Dapper + DbUp, Serilog. **No TechieRag** — no AI/RAG features.
- **TrBlazeUI IS used — corrected 2026-08-14.** This file previously said "No TrBlazeUI", which was wrong and had been misleading agents. `TrBlazeUI.Components` and `TrBlazeUI.Icons.Lucide` **2.0.3** (upgraded from 2.0.2 on 2026-08-25; every 2.0.3 fix was measured live before its workaround was removed — `tests/verify/trblazeui-203-upgrade.spec.ts`) are `PackageReference`d by `source/BlogUI`, `source/TechieBlog` and `source/BlogApp`, and render on the live public pages. **Microsoft Fluent UI Blazor was removed entirely** by REQ-UI-048 — zero references remain in any `.csproj`. The feed is private (`https://nuget.pkg.github.com/techierathore/index.json`), so restore needs a `read:packages` credential; CI reads the `TrBlazeUiPackagesToken` secret. Library gaps go to `docs/TechieBlog-TrBlazeUI-Feedback.md` (next free id **TR-076** — the file's own bottom counter line is authoritative, not this note).
- Solution: `TechieBlog.slnx` with five projects under `source/` — `TechieBlog` (host), `BlogUI` (RCL), `BlogEngine`, `BlogModel` (assembly `BlogModels`), `BlogDb`.
- Connection string config key is **`AppDbConString`** (not `ConnectionStrings:Default`). DbUp migrations run automatically at host startup.
- Field-prefix convention: **bare, no prefix — camelCase instance fields** (e.g. `private readonly ILogger<X> logger;`). PER-PROJECT day-1 decision based on ~89% of existing fields; recorded authoritatively in Coding Standards §"Fields, Parameters, Locals". `obj`/`a`/`v` prefixes and `_underscore` are BANNED here.
- Test naming: short PascalCase, NO underscores. Full scenario in XML `<summary>` doc.
- **Current build state: GREEN** (2026-08-14) — `dotnet build TechieBlog.slnx` returns `0 Error(s)` across all 7 projects; test suite **1 490** (1 487 pass / 3 skip / 0 fail). The old `NU1605` note here was stale; REQ-FN-043 is closed.
- **Coding-standards enforcement is now a BUILD gate, not a grep** (REQ-NFR-041, 2026-08-14). `tests/unit/Ops/SourceConventionTests.cs` scans `source/` and `tests/` for underscore-prefixed fields, underscored test-method names, Hungarian prefixes and `a`/`v` prefixes; `ExceptionDisclosureTests.cs` covers `ex.Message` disclosure. Each carries a self-test asserting its regex matches a synthetic violation — added because the hand-written greps in the Coding Standards were silently unmatchable **three separate times**, and each time the zero read as a pass. **If you run those greps manually, use `command grep`** (this shell aliases `grep` to `ugrep -G`, which changes the regex dialect) **and always pair a zero with a positive control.**
- **Build the WHOLE solution, never a subset.** `source/BlogApp` targets `net10.0-windows10.0.19041.0`, and a gate that skipped that TFM once let 4 compile errors ship while the phase closed "GREEN". Rung #2 (`~/.dotnet/dotnet build`) **cannot** build this solution — the Windows-restored `obj/` assets reference a VS fallback package folder WSL cannot see, so it fails with `MSB4018 / ResolvePackageAssets`. **Use rung #4** (`cmd.exe /c "dotnet build TechieBlog.slnx"`) for anything that must include BlogApp.
- **Both heads need secrets before they run.** The web host reads `JwtSigningKey` + `AppEncryptionKey` from user secrets (`techieblog-host-secrets`); BlogApp reads the same two from its connection-setup screen via the DPAPI `ConnectionStore`. They must match byte-for-byte — a mismatch fails silently. Setup commands: `docs/TechieBlog-UsageGuide.md` §Setup.

## Requirement ID prefixes used in this repo

- `REQ-UI-*` — UI work, built with **TrBlazeUI** components against `mockups/*.html`
- `REQ-FN-*` — backend, routed to /flow-master
- `REQ-RAG-*` — AI/RAG — **none in this project**
- `REQ-NFR-*` — non-functional

Always tag work with its REQ ID in the checklist's Requirements Status **Remarks** cell (e.g. `[REQ-UI-015] my-comments page added`). Agents never git-commit (Hard rule 1); the owner's own manual commits use the same `[REQ-*]` tags.

## Verification

After every implementation phase, the verifier runs and writes per-REQ verdicts into
`docs/TechieBlog-Checklist.md`'s Requirements Status table (the single source of truth — no dated
`docs/qa` result files; the existing `docs/qa/gates/*.yml` are historical BMad artifacts, read-only).
PROJECT-STATUS.md is updated after EVERY phase (mandatory gate). `Done (pre-existing)` rows were
migrated from the completed dev plan — do NOT rebuild them.

## Slash-command syntax (READ ME if a `/agent *command` invocation fails)

**Claude Code** registers TechieFlow-native agents under the path-derived namespace `TechieFlow:agents:<name>`. The short `/<agent>` form does NOT always resolve. When in doubt use the full form:

| Agent | Claude Code | OpenCode |
|-------|-------------|----------|
| analyst | `/TechieFlow:agents:analyst` (or `/analyst` if it resolves) | `/flow-analyst` |
| flow-master | `/TechieFlow:agents:flow-master` | `/flow-master` |
| architect | `/TechieFlow:agents:architect` | (add to opencode.jsonc) |
| verifier | `/TechieFlow:agents:verifier` | (add to opencode.jsonc) |

`/techierag` is a NuGet-deployed persona for TechieRag projects — **not applicable to TechieBlog** (no AI/RAG features). **`/trblazeui` IS applicable** — this project uses TrBlazeUI 2.0.3, so `*build-phase` routes `REQ-UI-*` clusters to it as a sub-agent. (Corrected 2026-08-14; this line previously excluded both.)

After the agent is loaded, every TechieFlow-native agent (analyst, architect, flow-master, verifier) accepts `*command args` style invocations.

How to talk to me:
1. Rate your confidence. Tag any claim you are not sure of: [Certain] with evidence, [Likely] a strong inference, [Guessing] filling a gap. If most of a reply is guessing, say so in the first line.
2. Uncomfortable answer first. If there is something I will not want to hear, it is the first sentence, not the third paragraph.
3. No warm-up. No "great question", no "there are several ways to look at this", no restating my question. Start with the most useful sentence.
4. Disagree with structure: "I disagree because [reason]. I would do [alternative]. The risk in your approach is [downside]." Then do what I decide.
