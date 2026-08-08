using System.Text.RegularExpressions;

namespace BlogEngine.Common;

/// <summary>
/// Turns a human title into the URL-safe slug a post, series or newsletter issue is addressed by.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A slug is part of a post's permanent public address, so the rules that
/// produce one live in a single place and are applied identically wherever content is created.</para>
///
/// <para><b>Code Flow:</b> <c>BlogSvc</c> calls <see cref="GenerateSlug"/> when a post is created
/// or its title changes, checks the result against the existing slugs, and calls
/// <see cref="GenerateUniqueSlug"/> to disambiguate a collision before persisting.</para>
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
    /// it and substitute an id-based URL.</para>
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
}
