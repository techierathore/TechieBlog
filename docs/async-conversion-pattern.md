# Async conversion pattern — REQ-NFR-026

**Status:** Stage 1 complete (the shared contract). Stages 2–4 pending.
**Reference implementation in the tree:** `source/BlogEngine/DbAccess/CategoryRepo.cs`
**Read before you touch a repository.** Everything here is normative for the conversion.

---

## 1. Why this exists

The performance audit behind REQ-NFR-001 measured throughput pinned at **~3.5 requests/second at every
concurrency level** — c10 gave a p50 of 2.35 s, c100 gave 249 timeouts out of 500 with a p50 of 22.9 s
and no recovery after the spike — while PostgreSQL sat healthy at 16 connections. The database was
never the bottleneck. Synchronous Dapper was: every query parks a thread-pool thread for the whole
round trip, so the ceiling is "threads available", not "work the database can do".

Measured on 2026-08-07, `source/BlogEngine/DbAccess/` holds **348 public methods across 25 repository
files, of which 54 are async** — not the 41-of-265 the checklist recorded. The checklist figure predates
the Newsletter, EmailVerificationToken, VerifiedEmail, PostView, Analytics and SiteSetting repositories.
**294 methods are still synchronous.**

## 2. Strategy — additive, not wholesale

**The async surface is added alongside the synchronous one. Nothing is deleted in stages 1–3.**

The alternative — replacing sync with async wholesale — was rejected, and the reason is worth stating
because it is the whole reason the fan-out can happen at all:

> With a wholesale swap, the solution does not compile again until the *last* of ~25 parallel agents has
> finished. No agent could build its own work, run the test suite, or smoke its own repository, because
> the other 24 repositories would be mid-conversion and broken. Every agent would be verifying nothing,
> and the first green build would arrive with 25 unverified conversions in it simultaneously.

The additive approach means the solution is green after every single repository, so each agent builds,
tests and smokes its own work in isolation, and a regression is attributable to the repository that
caused it.

**This was not theoretical.** The first cut of this contract added the async members to
`IGenericRepository<T>` as plain abstract members. That broke **54 builds' worth of errors across six
hand-written test doubles** under `tests/unit/` — `FakeBlogCommentRepo`, `FakeEmailVerificationTokenRepo`,
`FakePostRatingRepo`, `FakeSubscriberRepo`, `FakeVerifiedEmailRepo`, `FakeSiteSettingRepo` — none of
which derive from `GenericRepository<T>`. Six fakes broke from the *base interface alone*, before a
single repository was touched. The fix, and the shape you must preserve:

- **Every async member on `IGenericRepository<T>` carries a default interface implementation** that runs
  its synchronous twin and returns a completed task. Implementers that have not been converted — including
  every test double — keep compiling and keep behaving exactly as before, untouched.
- **`GenericRepository<T>` additionally provides `virtual` class-level implementations** of the same
  members, so a derived repository can write `override` and so concrete-typed callers see them.

All six fakes were fixed by **zero edits to the fakes**. That is the test of whether an additive design
is really additive: if your change requires editing implementers you did not intend to convert, it is not.

**A default implementation is correct but is not the fix.** An unoverridden member still blocks a
thread for the whole round trip. A repository that inherits the bridge is *unconverted*, no matter how
green the build is.

### Stages

| Stage | Scope | State |
|-------|-------|-------|
| 1 | `IGenericRepository<T>`, `GenericRepository<T>`, `DbConnectionFactory`, `DbTimestamp`, one reference repository (`CategoryRepo`) end to end | **Done** |
| 2 | The remaining 24 repositories + their interfaces (parallel fan-out) | Pending |
| 3 | Service layer and Blazor call sites migrate to the async members | Pending |
| 4 | Delete the synchronous members, the class-level bridges and the interface defaults | Pending |

Do not start stage 4 work while stage 2 is running.

## 3. Naming and signatures

- Async members take the `Async` suffix: `GetPostBySlug` → `GetPostBySlugAsync`. Enforced by `.editorconfig`.
- Every async member takes `CancellationToken cancellationToken = default` **as its last parameter**.
- Return types: `Task<T>` for values, `Task<T?>` where "not found" is a normal answer, `Task` for commands.
  Never `async void`. Never `ValueTask` here — nothing in this layer is hot enough to justify it.
- `IEnumerable<T>` stays `Task<IEnumerable<T>>`. Do **not** convert to `IAsyncEnumerable<T>`: results are
  buffered before the connection closes (§6, trap 3), so there is nothing to stream.
- Parameter names follow the standard — camelCase, no prefix. Several repositories still use `aSingleId` /
  `vConn`; **fix those while you are in the file**, they are banned (`REQ-NFR-021`).

## 4. The before/after shape

### Read returning many

```csharp
// BEFORE
public IEnumerable<Category> GetAll()
{
    using var vConn = GetOpenConnection();
    return vConn.Query<Category>(sql).ToList();
}

// AFTER — the sync twin stays, both now share one SQL constant
private const string SelectAllSql = "SELECT ... ORDER BY CategoryName";

public override async Task<IEnumerable<Category>> GetAllAsync(CancellationToken cancellationToken = default)
{
    return await QueryAsync<Category>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
}

public override IEnumerable<Category> GetAll()
{
    using var connection = GetOpenConnection();
    return connection.Query<Category>(SelectAllSql).ToList();
}
```

**Hoist the SQL into a `private const string` per statement.** Both twins then execute the same text, so
the async version cannot drift from the synchronous one it will replace, and stage 4 deletes only a method.

### Read returning one, or none

```csharp
public override async Task<Category?> GetSingleAsync(long categoryId, CancellationToken cancellationToken = default)
{
    return await QueryFirstOrDefaultAsync<Category>(
        SelectByIdSql, new { CategoryId = categoryId }, cancellationToken).ConfigureAwait(false);
}
```

### Command

```csharp
public override async Task UpdateAsync(Category category, CancellationToken cancellationToken = default)
{
    await ExecuteAsync(UpdateSql, new { category.CategoryId, category.CategoryName }, cancellationToken)
        .ConfigureAwait(false);
}
```

### Delegating member

A member that only widens or forwards must **not** be marked `async` — return the task directly:

```csharp
public override Task<Category?> GetIntSingleAsync(int categoryId, CancellationToken cancellationToken = default)
    => GetSingleAsync(categoryId, cancellationToken);
```

## 5. Use the protected helpers

`GenericRepository<T>` exposes five helpers. Route every query through them; they exist so you cannot
forget the three things that are easy to get wrong — opening the connection asynchronously, flowing the
token into the command, and `ConfigureAwait(false)`.

| Helper | Use for |
|--------|---------|
| `QueryAsync<T>(sql, parameters, ct)` | many rows (buffered) |
| `QueryFirstOrDefaultAsync<T>(sql, parameters, ct)` | one row or none |
| `QuerySingleAsync<T>(sql, parameters, ct)` | exactly one row — `RETURNING`, `SELECT SomeFunction(...)` |
| `ExecuteAsync(sql, parameters, ct)` | INSERT / UPDATE / DELETE with no result |
| `ExecuteScalarAsync<T>(sql, parameters, ct)` | a single value, `COUNT(...)` |

If you genuinely need a connection of your own — multi-statement work, a transaction, `QueryMultipleAsync` —
take it like this and **never** with the synchronous `GetOpenConnection()`:

```csharp
await using var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
var rows = await connection.QueryAsync<T>(command).ConfigureAwait(false);
```

`GetOpenConnectionAsync` returns `DbConnection`, not `IDbConnection`, deliberately: `OpenAsync` and
`DisposeAsync` only exist there, and Dapper's async extensions throw at runtime on a connection that is
not really a `DbConnection`. The stronger type makes that a compile-time guarantee.

## 6. Traps

These are ordered by how much time they will cost you if you meet them cold.

### Trap 1 — the `42883` timestamp trap (a runtime failure a green build cannot see)

**This has already happened in this codebase.** `PasswordResetTokenRepo.InsertToGetId` failed at runtime
with SQLSTATE `42883`, "function does not exist", for every password-reset request — silently, because
the forgot-password page returns the same generic message whether or not mail was sent.

The mechanism:

- `InsertPasswordResetToken` declares its parameters as `TIMESTAMP` (without time zone).
- `AuthSvc` passes `DateTime.UtcNow`, whose `Kind` is `Utc`.
- **Npgsql infers the wire type from `DateTimeKind`**: a `Utc` value is sent as `timestamptz`.
- PostgreSQL resolves function overloads **strictly**. `timestamptz` matched no declared overload, so the
  call resolved to no function at all.

**Setting `DbType` does not fix it.** Since Npgsql 6, `DbType.DateTime` itself maps to `timestamptz`, and
asking for `timestamp` while the value still carries `Kind = Utc` is rejected outright. **Normalising the
value's `Kind` is what changes the wire type.** Use the helper:

```csharp
parameters.Add("pCreatedAt", DbTimestamp.AsTimestamp(entity.CreatedAt));
```

`BlogEngine.DaCore.DbTimestamp.AsTimestamp` drops the `Kind` to `Unspecified` without moving the instant,
and converts a `Local` value to UTC first — stripping the `Kind` off a local time would silently record
the host's wall clock as though it were UTC.

**Why this matters to every one of you:** many repositories in this codebase call stored functions
(`SELECT * FROM GetPostBySlug(@slug)`), so this class of failure is **latent across the board**. It
compiles. It passes unit tests with fakes. It only shows up when the statement actually reaches
PostgreSQL. Two consequences:

1. Apply `DbTimestamp.AsTimestamp` to every `DateTime` you bind to a stored function or `TIMESTAMP` column.
2. **A green build is not evidence.** Every converted repository must have at least one of its paths
   exercised against the real database before you call it done (§8).

Plain parameterised SQL happens to survive without the helper because PostgreSQL casts the argument to
the target column type. That is why the failure only appears on stored-function paths — and why "the
other repository does not need it" is not a safe inference.

### Trap 2 — `GetOpenConnection()` inside an `async` method

```csharp
public async Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default)
{
    using var connection = GetOpenConnection();          // WRONG — blocks for the whole handshake
    return await connection.QueryAsync<T>(sql).ConfigureAwait(false);
}
```

This compiles, passes every test, and leaves a large part of the stall in place — the TCP, TLS and
authentication round trips still park a thread. Use `GetOpenConnectionAsync`, or the helpers.

### Trap 3 — returning a task from inside a `using`

```csharp
public Task<IEnumerable<T>> GetAllAsync()               // WRONG — no await
{
    using var connection = GetOpenConnection();
    return connection.QueryAsync<T>(sql);               // connection disposed before the task completes
}
```

The `using` disposes on return, not on task completion. Always `await` inside the scope. Related:
never pass `buffered: false` — an unbuffered Dapper result reads lazily and the connection is gone by
the time the caller enumerates it. The helpers buffer for you.

### Trap 4 — overload ambiguity in the nine repositories that already have async members

`AdminCountsRepo`, `AnalyticsRepo`, `BlogCommentRepo`, `EmailVerificationTokenRepo`, `NewsletterRepo`,
`PostRatingRepo`, `PostViewRepo`, `SiteSettingRepo` and `VerifiedEmailRepo` already contain async methods,
most without a `CancellationToken`. **Do not add a second overload beside them** — adding
`FooAsync(long id, CancellationToken ct = default)` next to an existing `FooAsync(long id)` makes every
existing call `FooAsync(id)` ambiguous, and the error appears at the call site, not in your file. **Modify
the existing signature in place** to add the token parameter.

While you are there: those existing async methods are missing `.ConfigureAwait(false)` in most cases, and
several open their connection synchronously (trap 2). They count as unconverted. Fix them.

### Trap 5 — `AllRepoInterfaces.cs` is a single file shared by twelve interfaces

`IBlogUserRepo`, `ISvcTokenRepo`, `IUserLoginRepository`, `ILoginLogRepo`, `IBlogImageRepo`,
`IBlogPostRepo`, `IBlogTagRepo`, `ICategoryRepo`, `IBlogCommentRepo`, `IUserEventRepo`, `IBlogSeriesRepo`,
`IPasswordResetTokenRepo`, `IPostRatingRepo` and `ISubscriberRepo` all live in
`source/BlogModel/Interfaces/AllRepoInterfaces.cs`. Parallel agents editing one file concurrently will
lose each other's writes.

**Rule: edit only the interface block you own, with a unique `old_string`, and never rewrite the file.**
The grouping in the fan-out plan keeps agents that share this file from running in the same wave where
possible; if you find your interface block already carries an async surface, someone else got there first —
do not revert it.

Interfaces in their own files (`IUserAwardsRepo`, `IUserSkillsRepo`, `IUserStatsRepo`, `ISiteSettingRepo`,
`IEmailVerificationTokenRepo`, `IVerifiedEmailRepo`) have no such contention.

### Trap 6 — per-repository test doubles

The base contract's defaults protect the fakes from *base* interface changes. They do **not** protect them
from members you add to a *specific* interface: adding `GetPendingAsync` to `IBlogCommentRepo` breaks
`FakeBlogCommentRepo`. That fake is yours to update — it is a direct consequence of your change. Either
implement the member on the fake, or give the specific interface's new members default implementations
too where that is sensible.

### Trap 7 — do not block on tasks to "reuse" code

Never implement the synchronous twin as `SomethingAsync(...).Result` or `.GetAwaiter().GetResult()`.
Inside a Blazor Server circuit that is a deadlock risk, and it would make the interim state worse than the
state it replaces. Leave the synchronous twin as it is; stage 4 deletes it.

### Trap 8 — `ExecuteScalarAsync<int>` returns `default` on an empty result

`0` from a counting query can mean "zero matched" or "no row came back". Where the difference matters,
use `QuerySingleAsync<T>`, which throws instead.

## 7. `Result<T>` is unaffected

`Result` / `Result<T>` (`BlogModels.Common`) is the service layer's expected-failure convention and the
conversion does not change it. A method that returned `Result<Category>` returns `Task<Result<Category>>`.

There is no `AsyncResult` and none is wanted: `Result` models the expected-failure axis, `Task` models the
completion axis, and they compose without either knowing about the other. The `try/catch` that turns an
unexpected exception into a failed `Result` keeps working verbatim, because an awaited call throws at the
`await` exactly as a blocking call throws at the call.

```csharp
public async Task<Result<Category>> CreateCategoryAsync(Category category, CancellationToken cancellationToken = default)
{
    if (category == null)
        return Result<Category>.Failure("Category cannot be null");     // no task needed for a guard

    try
    {
        var id = await CategoryRepo.InsertToGetIdAsync(category, cancellationToken).ConfigureAwait(false);
        category.CategoryId = id;
        return Result<Category>.Success(category);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to create category: {Name}", category.CategoryName);
        return Result<Category>.Failure($"Failed to create category: {ex.Message}");
    }
}
```

A pure delegating method returns the task rather than awaiting it, and wraps early guards with
`Task.FromResult` — see `CategorySvc.SaveCategoryAsync`.

## 8. `Insert` / `InsertToGetId` pairing

`InsertToGetIdAsync` is the primitive; `InsertAsync` is written in terms of it **when the repository's
insert genuinely returns a key and the entity needs it back**:

```csharp
public override async Task InsertAsync(PasswordResetToken entity, CancellationToken cancellationToken = default)
{
    entity.TokenId = await InsertToGetIdAsync(entity, cancellationToken).ConfigureAwait(false);
}
```

Where the caller does not need the key, keep them separate so the plain `INSERT` stays cheaper than the
`INSERT … RETURNING` — that is the shape `CategoryRepo` uses. What must **not** happen is the two drifting
apart: they insert the same columns, so they share the SQL constants or one calls the other. Never leave
`InsertAsync` bridged while `InsertToGetIdAsync` is converted; a half-converted pair is the easiest way to
ship a blocking write path that looks converted.

## 9. Definition of done for one repository

1. Every member of the repository's interface has an `…Async` twin carrying a `CancellationToken`.
2. Every async member on the class is a **real override/implementation** — none inherited from the bridge
   (`GetOpenConnectionAsync` excepted; the base version is already genuinely async).
3. Every async member uses `GetOpenConnectionAsync` or the protected helpers, flows the token into the
   `CommandDefinition`, and uses `.ConfigureAwait(false)`.
4. SQL is hoisted to `const` and shared by both twins.
5. Banned identifier prefixes (`aFoo`, `vConn`, `_field`) removed from the file.
6. XML docs on every public member with Business Logic / Flow / Side Effects, per the coding standard.
7. `~/.dotnet/dotnet build TechieBlog.slnx` green, `~/.dotnet/dotnet test TechieBlog.slnx` with **no new
   failures** — check the count, do not assume the baseline.
8. **At least one path exercised against the real database** (trap 1). A green build is explicitly not
   sufficient evidence.

Copy this guard into your repository's test file — it is the check that tells a conversion apart from the
appearance of one:

```csharp
[Fact]
public void ConvertedRepoOverridesEveryAsyncMember()
{
    var inherited = typeof(YourRepo)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => m.Name.EndsWith("Async", StringComparison.Ordinal))
        .Where(m => m.Name != nameof(IGenericRepository<YourEntity>.GetOpenConnectionAsync))
        .Where(m => m.DeclaringType != typeof(YourRepo))
        .Select(m => m.Name)
        .ToList();

    Assert.Empty(inherited);
}
```

## 10. Files that define the contract

| File | Role |
|------|------|
| `source/BlogModel/Interfaces/IGenericRepository.cs` | the contract, with default implementations |
| `source/BlogEngine/DaCore/GenericRepository.cs` | virtual async members, the bridge, the five helpers |
| `source/BlogEngine/DaCore/DbConnectionFactory.cs` | `GetDbConnectionAsync` → `DbConnection` |
| `source/BlogEngine/DaCore/DbTimestamp.cs` | the `42883` guard |
| `source/BlogEngine/DbAccess/CategoryRepo.cs` | the worked repository example |
| `source/BlogEngine/Services/CategorySvc.cs` | the worked service example (`Result<T>` + async) |
| `source/BlogUI/Pages/AdminPages/CategoriesList.razor` | the worked page example (`OnInitializedAsync`) |
| `tests/unit/DataAccess/` | contract tests, including the override guard to copy |
| `tests/verify/async-contract-smoke.spec.ts` | the render-truth + visual-truth smoke to copy |

---
Last updated: 2026-08-07 — stage 1 (shared contract + reference implementation).
