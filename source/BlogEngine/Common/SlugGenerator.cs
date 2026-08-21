using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BlogEngine.Common;

/// <summary>
/// Turns a human title into the URL-safe slug a post, series or newsletter issue is addressed by.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A slug is part of a post's permanent public address, so the rules that
/// produce one live in a single place and are applied identically wherever content is created.</para>
///
/// <para><b>Code Flow:</b> a service calls <see cref="EnsureSlug"/> to obtain a guaranteed non-empty
/// base slug — the author's own slug when they supplied one, the title-derived slug otherwise, and an
/// identifier-based fallback when neither yields anything — then passes that base to
/// <see cref="ResolveUniqueSlug"/> (or <see cref="ResolveUniqueSlugAsync"/>), which suffixes it until
/// the supplied existence probe reports it free. <see cref="GenerateSlug"/> and
/// <see cref="GenerateUniqueSlug"/> remain public as the primitives those two are built from.</para>
///
/// <para><b>Why the collision loop lives here (REQ-FN-054):</b> it was previously written out by hand
/// in sixteen places across <c>BlogSvc</c>, <c>CategorySvc</c>, <c>TagSvc</c> and <c>SeriesSvc</c>, and
/// every one of those copies carried the same defect — the first candidate was derived from the
/// author's supplied slug but every later candidate was re-derived from the title, so a chosen slug
/// was silently discarded from the second attempt on. Centralising the loop makes that class of drift
/// impossible: the base slug is fixed before the loop starts and is the only thing suffixed.</para>
///
/// <para><b>Dependencies:</b> <c>System.Text.RegularExpressions</c> only. Pure and static — no
/// database access, so the generator cannot itself guarantee uniqueness; that is the caller's job.</para>
///
/// <para><b>Character set is ASCII-only, deliberately and with a cost.</b> Everything outside
/// <c>a-z</c>, <c>0-9</c>, space and hyphen is discarded rather than transliterated, which keeps
/// slugs free of percent-encoding and safe in any URL. The consequence is that a title written
/// entirely in a non-Latin script produces an <b>empty</b> slug, and an accented Latin title loses
/// its accented letters ("Café" becomes "caf"). Callers must therefore treat an empty return as a
/// real outcome and fall back to an identifier-based URL rather than assuming a slug always
/// results.</para>
///
/// <para><b>Slugs are addresses, so changing one breaks links.</b> Re-slugging on every title edit
/// silently invalidates every inbound link and search-engine result for that post. If title edits
/// are ever made to re-slug, they need a redirect from the old slug — this class does not and
/// cannot provide that.</para>
///
/// <para><b>Usage:</b> Generate, then check for a collision against stored slugs, then persist.</para>
/// </remarks>
/// <example>
/// <code>
/// var slug = SlugGenerator.GenerateSlug("My Blog Post Title!");
/// // Result: "my-blog-post-title"
/// </code>
/// </example>
public static class SlugGenerator
{
    /// <summary>
    /// Generates a URL-friendly slug from the given title.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Lower-casing happens first so the character filter only has to
    /// describe one case, and the filter is an allow-list rather than a block-list — a character
    /// nobody anticipated is dropped, never passed through. Consecutive hyphens are collapsed and
    /// the ends trimmed so punctuation-heavy titles ("C# — Tips &amp; Tricks!") do not produce
    /// <c>c---tips---tricks-</c>.</para>
    /// <para><b>Flow:</b> lower-case → drop everything outside <c>[a-z0-9\s-]</c> → spaces to
    /// hyphens → collapse runs of hyphens → trim leading and trailing hyphens.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// <para><b>Edge case the caller must handle:</b> a title made entirely of characters the
    /// allow-list rejects — a non-Latin script, or pure punctuation — yields an <b>empty string</b>,
    /// not a fallback. An empty slug would make the post unaddressable, so the caller has to detect
    /// it and substitute an id-based URL. <b>Do not call this method directly from a service</b>;
    /// call <see cref="EnsureSlug"/>, which performs exactly that substitution and can therefore
    /// never hand a service an empty slug to persist (REQ-FN-054).</para>
    /// </remarks>
    /// <param name="title">The title to convert. May be null.</param>
    /// <returns>The slug, or an empty string when the title is null, whitespace, or contains no
    /// character the allow-list keeps.</returns>
    public static string GenerateSlug(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        // Convert to lowercase
        var slug = title.ToLowerInvariant();

        // Remove special characters (keep only letters, numbers, spaces, hyphens)
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");

        // Replace spaces with hyphens
        slug = Regex.Replace(slug, @"\s+", "-");

        // Remove multiple consecutive hyphens
        slug = Regex.Replace(slug, @"-+", "-");

        // Trim hyphens from ends
        slug = slug.Trim('-');

        return slug;
    }

    /// <summary>
    /// Generates a unique slug by appending a number if the base slug already exists.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The first post to claim a slug keeps it unsuffixed — the common
    /// case produces the clean URL — and each subsequent claimant takes the next ordinal, so the
    /// second is <c>-2</c> and the third <c>-3</c>. A count of zero or less means the base slug is
    /// free and is returned unchanged.</para>
    /// <para><b>Flow:</b> zero or fewer existing → return the base; otherwise append
    /// <c>existingCount + 1</c>.</para>
    /// <para><b>Side Effects:</b> None; pure — it performs no lookup and therefore <b>cannot itself
    /// guarantee uniqueness</b>. It computes a candidate from a count the caller supplies.</para>
    /// <para><b>The candidate can still collide, and the caller must cope.</b> Two known cases:
    /// (1) <i>deletions</i> — with <c>post</c>, <c>post-2</c> and <c>post-3</c> stored, deleting
    /// <c>post-2</c> leaves a count of 2 and this method proposes <c>post-3</c>, which is taken; and
    /// (2) <i>concurrency</i> — two authors saving the same title at once both read the same count
    /// and both propose the same suffix. Neither is fixable here, because a pure function cannot
    /// see the table. The durable answer is the unique index on the slug column: the caller must
    /// re-check (or catch the constraint violation) and retry with a higher count rather than
    /// treating this result as final.</para>
    /// </remarks>
    /// <param name="baseSlug">The slug produced by <see cref="GenerateSlug"/>.</param>
    /// <param name="existingCount">How many stored records already use this base slug.</param>
    /// <returns>The base slug when the count is zero or less; otherwise the base slug with an
    /// ordinal suffix. A <i>candidate</i>, not a guarantee — see the remarks.</returns>
    public static string GenerateUniqueSlug(string baseSlug, int existingCount)
    {
        if (existingCount <= 0)
            return baseSlug;

        return $"{baseSlug}-{existingCount + 1}";
    }

    /// <summary>
    /// The number of candidates <see cref="ResolveUniqueSlug"/> will try before giving up.
    /// </summary>
    /// <remarks>
    /// A safety valve against an unbounded loop, not a business rule. The base slug counts as the
    /// first attempt, so the highest suffix ever proposed is <c>-100</c>.
    /// </remarks>
    public const int MaxSlugAttempts = 100;

    /// <summary>
    /// Produces the base slug a record should be stored under, guaranteed never to be empty.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic (REQ-FN-054):</b> three sources are consulted in priority order and the
    /// first that yields something wins. (1) <b>The author's own slug.</b> If one was supplied it is
    /// taken verbatim apart from surrounding whitespace — an author who typed a slug chose their URL,
    /// and nothing here is entitled to replace it. (2) <b>The title.</b> When no slug was supplied the
    /// title is run through <see cref="GenerateSlug"/>. (3) <b>An identifier.</b> When the title is
    /// punctuation-only or written in a non-Latin script <see cref="GenerateSlug"/> returns an empty
    /// string, and persisting that would leave the record with no address at all; so a fallback of the
    /// form <c>post-42</c> is substituted, which is precisely the id-based URL
    /// <see cref="GenerateSlug"/>'s own documentation instructs callers to supply.</para>
    /// <para><b>Why the no-id fallback is a hash and not a random value:</b> on an insert the database
    /// has not issued an identifier yet, so there is no id to build on. Hashing the title instead of
    /// minting a GUID keeps the result <i>deterministic</i>, which matters for two reasons: the same
    /// non-Latin title always resolves to the same base slug, so <c>TagSvc.GetOrCreateTag</c> can still
    /// match an existing row rather than creating a duplicate on every keystroke; and a re-save is
    /// idempotent instead of allocating a fresh address each time. Collisions between two genuinely
    /// different titles are handled by <see cref="ResolveUniqueSlug"/> exactly as they are for Latin
    /// titles.</para>
    /// <para><b>Flow:</b> supplied slug → title-derived slug → <c>{prefix}-{id}</c> when an identifier
    /// is known → <c>{prefix}-{title hash}</c> otherwise.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="suppliedSlug">The slug the author entered, or <c>null</c>/blank when they left the
    /// field empty.</param>
    /// <param name="title">The title, name or tag text the slug is derived from when no slug was
    /// supplied. May be null.</param>
    /// <param name="fallbackPrefix">A short word naming the kind of record — <c>post</c>, <c>tag</c>,
    /// <c>category</c>, <c>series</c> — used only to build the identifier-based fallback.</param>
    /// <param name="entityId">The record's identifier when it already has one; pass <c>0</c> (the
    /// default) on an insert, where the database has not issued one yet.</param>
    /// <returns>A non-empty base slug. Uniqueness is <b>not</b> implied — pass the result to
    /// <see cref="ResolveUniqueSlug"/>.</returns>
    public static string EnsureSlug(string? suppliedSlug, string? title, string fallbackPrefix, long entityId = 0)
    {
        if (!string.IsNullOrWhiteSpace(suppliedSlug))
            return suppliedSlug.Trim();

        var fromTitle = GenerateSlug(title!);
        if (!string.IsNullOrWhiteSpace(fromTitle))
            return fromTitle;

        return BuildFallbackSlug(fallbackPrefix, title, entityId);
    }

    /// <summary>
    /// Builds the identifier-based address used when a title cannot produce a slug at all.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A record with an identifier gets the readable
    /// <c>{prefix}-{id}</c> form. A record that has not been inserted yet has no identifier, so a
    /// twelve-character hexadecimal digest of its title stands in — stable for a given title, which
    /// keeps repeated saves and <c>GetOrCreate</c> lookups pointing at the same address.</para>
    /// <para><b>Flow:</b> normalise the prefix → return <c>{prefix}-{id}</c> when the id is positive →
    /// otherwise hash the title and return <c>{prefix}-{digest}</c>.</para>
    /// <para><b>Side Effects:</b> None; pure. SHA-256 is used purely as a stable digest and carries no
    /// security meaning here.</para>
    /// </remarks>
    /// <param name="fallbackPrefix">A short word naming the kind of record. Slugged defensively, and
    /// replaced with <c>item</c> when it is blank or slugs to nothing.</param>
    /// <param name="seed">The text hashed when no identifier is available. May be null.</param>
    /// <param name="entityId">The record's identifier, or <c>0</c> when it has none yet.</param>
    /// <returns>A non-empty, URL-safe slug.</returns>
    public static string BuildFallbackSlug(string fallbackPrefix, string? seed, long entityId)
    {
        var prefix = GenerateSlug(fallbackPrefix);
        if (string.IsNullOrWhiteSpace(prefix))
            prefix = "item";

        if (entityId > 0)
            return $"{prefix}-{entityId}";

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed ?? string.Empty));
        return $"{prefix}-{Convert.ToHexString(digest, 0, 6).ToLowerInvariant()}";
    }

    /// <summary>
    /// Suffixes a base slug until the supplied probe reports it free.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic (REQ-FN-054):</b> every candidate is derived from
    /// <paramref name="baseSlug"/> and from nothing else, so an author's chosen slug survives attempt
    /// 2, attempt 3 and attempt 100 — the defect this method replaces re-derived later candidates from
    /// the title and quietly threw the author's slug away. The base itself is offered first so the
    /// common, uncontended case keeps the clean URL, and the suffixes then run <c>-2</c>, <c>-3</c>, …
    /// matching <see cref="GenerateUniqueSlug"/>'s ordinal convention.</para>
    /// <para><b>Flow:</b> probe the base → if taken, probe <c>base-2</c> … <c>base-100</c> → return the
    /// first free candidate, or the last one tried when the budget is exhausted.</para>
    /// <para><b>Side Effects:</b> None of its own, but it invokes <paramref name="slugExists"/> up to
    /// <see cref="MaxSlugAttempts"/> times, and that delegate normally reads the database.</para>
    /// <para><b>Exhaustion is not an error here.</b> After <see cref="MaxSlugAttempts"/> probes the last
    /// candidate is returned even though it is known to be taken; the unique index on the slug column
    /// is the real guard, and the caller reports its violation as a failed <c>Result</c>. Returning a
    /// value rather than throwing keeps the behaviour identical to the loops this replaced.</para>
    /// <para><b>Still racy, by nature.</b> The probe and the insert are separate statements, so two
    /// simultaneous saves can both be told the same candidate is free. See
    /// <see cref="GenerateUniqueSlug"/>'s remarks — the database constraint is the only real
    /// guarantee.</para>
    /// </remarks>
    /// <param name="baseSlug">The non-empty base slug, normally from <see cref="EnsureSlug"/>.</param>
    /// <param name="slugExists">Probe returning <c>true</c> when a candidate is already taken. The
    /// caller supplies the exclusion rule — an update excludes the row being edited.</param>
    /// <param name="maxAttempts">How many candidates to try, including the base itself.</param>
    /// <returns>The first candidate the probe reported free, or the last candidate tried.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="slugExists"/> is null.</exception>
    public static string ResolveUniqueSlug(string baseSlug, Func<string, bool> slugExists, int maxAttempts = MaxSlugAttempts)
    {
        ArgumentNullException.ThrowIfNull(slugExists);

        if (!slugExists(baseSlug))
            return baseSlug;

        var candidate = baseSlug;
        for (var attempt = 1; attempt < maxAttempts; attempt++)
        {
            candidate = GenerateUniqueSlug(baseSlug, attempt);
            if (!slugExists(candidate))
                return candidate;
        }

        return candidate;
    }

    /// <summary>
    /// Suffixes a base slug until the supplied asynchronous probe reports it free.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Behaviourally identical to <see cref="ResolveUniqueSlug"/> — same
    /// candidate sequence, same attempt budget, same "return the last candidate on exhaustion" rule —
    /// so a service's synchronous and asynchronous twins allocate the same slug for the same
    /// input.</para>
    /// <para><b>Flow:</b> await the probe for the base → if taken, await it for <c>base-2</c> …
    /// <c>base-100</c> → return the first free candidate, or the last one tried.</para>
    /// <para><b>Side Effects:</b> Invokes <paramref name="slugExistsAsync"/> up to
    /// <see cref="MaxSlugAttempts"/> times; each call normally reads the database. Cancellation is the
    /// caller's business — capture the token in the delegate and it will fault the returned task.</para>
    /// </remarks>
    /// <param name="baseSlug">The non-empty base slug, normally from <see cref="EnsureSlug"/>.</param>
    /// <param name="slugExistsAsync">Probe returning <c>true</c> when a candidate is already taken.</param>
    /// <param name="maxAttempts">How many candidates to try, including the base itself.</param>
    /// <returns>The first candidate the probe reported free, or the last candidate tried.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="slugExistsAsync"/> is null.</exception>
    public static async Task<string> ResolveUniqueSlugAsync(string baseSlug, Func<string, Task<bool>> slugExistsAsync, int maxAttempts = MaxSlugAttempts)
    {
        ArgumentNullException.ThrowIfNull(slugExistsAsync);

        if (!await slugExistsAsync(baseSlug).ConfigureAwait(false))
            return baseSlug;

        var candidate = baseSlug;
        for (var attempt = 1; attempt < maxAttempts; attempt++)
        {
            candidate = GenerateUniqueSlug(baseSlug, attempt);
            if (!await slugExistsAsync(candidate).ConfigureAwait(false))
                return candidate;
        }

        return candidate;
    }
}
