namespace BlogModels;

/// <summary>
/// The upload rules one media category enforces — its size ceiling and its format allow-list —
/// together with every string a screen is allowed to advertise them with.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> BRD-45 makes the server the authority on what an upload may be, so the
/// numbers a screen prints and the numbers the service enforces have to be the same numbers. Before
/// REQ-FN-025's 2026-08-11 fix they were not: the size ceilings existed in four places — the engine
/// service, <c>ManageImages</c>, <c>ImagePicker</c>, and the dropzone component's own 10 MB default —
/// and the upload dialog printed two contradictory limits for one upload ("Max 2MB" beside
/// "Max size: 10 MB"). This type is the one place they now live.</para>
///
/// <para><b>Code Flow:</b> Purely declarative. A caller resolves a rule through
/// <see cref="ImageCategoryRules.For"/> or <see cref="ImageCategoryRules.TryGet"/> and then reads
/// whichever projection it needs: <see cref="MaxSizeBytes"/> for a machine check,
/// <see cref="ConstraintsText"/> or <see cref="MaxSizeDisplay"/> for a caption,
/// <see cref="AcceptAttribute"/> for a file input.</para>
///
/// <para><b>Dependencies:</b> None. This sits in <c>BlogModels</c>, the dependency leaf, precisely so
/// that the engine and the RCL can both reach it without either depending on the other.</para>
///
/// <para><b>Usage:</b> Never build one of these by hand and never re-type a limit next to a control.
/// A screen that wants to say "max 2 MB" asks the rule; if the rule changes, every surface changes
/// with it. <see cref="MaxSizeDisplay"/> deliberately matches the byte-to-text rendering used by the
/// TrBlazeUI <c>FileUpload</c> dropzone caption, so the two sentences agree character for
/// character.</para>
/// </remarks>
/// <param name="Category">Normalised, lower-case category key.</param>
/// <param name="MaxSizeBytes">Largest upload accepted for the category, in bytes.</param>
/// <param name="AllowedFormats">Lower-case file extensions, without the dot, accepted for the
/// category, in the order they should be shown.</param>
public sealed record ImageCategoryRule(
    string Category, long MaxSizeBytes, IReadOnlyList<string> AllowedFormats)
{
    /// <summary>
    /// The size ceiling rendered the way every surface must state it.
    /// </summary>
    /// <remarks>
    /// Rounded to whole units on purpose: the limits are round numbers, and a caption that reads
    /// "2 MB" beside a dropzone that reads "2 MB" is what this requirement is about. The uploaded
    /// file's own size is a different quantity and is still shown to one decimal place, so
    /// "File size (2.4 MB) exceeds maximum allowed size (2 MB)" stays readable.
    /// </remarks>
    public string MaxSizeDisplay => FormatLimit(MaxSizeBytes);

    /// <summary>
    /// The allow-list rendered as a comma-separated list, e.g. <c>jpg, jpeg, png, webp</c>.
    /// </summary>
    public string FormatsDisplay => string.Join(", ", AllowedFormats);

    /// <summary>
    /// The one sentence an upload surface prints to advertise this category's limits.
    /// </summary>
    public string ConstraintsText => $"Max {MaxSizeDisplay}, formats: {FormatsDisplay}";

    /// <summary>
    /// The value for a file input's <c>accept</c> attribute, derived from
    /// <see cref="AllowedFormats"/>.
    /// </summary>
    /// <remarks>
    /// Derived rather than tabulated, so an extension added to the allow-list cannot be left out of
    /// the browser's own filter. Duplicates collapse — <c>jpg</c> and <c>jpeg</c> are one MIME type.
    /// </remarks>
    public string AcceptAttribute => string.Join(
        ",",
        AllowedFormats
            .Select(ImageCategoryRules.MimeTypeFor)
            .Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Whether an extension is accepted for this category.
    /// </summary>
    /// <param name="extension">Lower-case extension without the dot; <c>null</c> or blank is never
    /// allowed.</param>
    /// <returns><c>true</c> when the extension appears in <see cref="AllowedFormats"/>.</returns>
    public bool AllowsFormat(string? extension)
    {
        return !string.IsNullOrWhiteSpace(extension)
            && AllowedFormats.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the message shown when a file is larger than this category allows.
    /// </summary>
    /// <remarks>
    /// The limit is quoted as <see cref="MaxSizeDisplay"/>, so the rejection names the same number
    /// the dialog advertised rather than a second one the user has never seen.
    /// </remarks>
    /// <param name="fileSizeBytes">The rejected file's size in bytes.</param>
    /// <returns>A user-facing sentence carrying no exception text and no server path.</returns>
    public string BuildOversizeMessage(long fileSizeBytes)
    {
        return $"File size ({FormatFileSize(fileSizeBytes)}) exceeds maximum allowed size " +
               $"({MaxSizeDisplay}) for category '{Category}'.";
    }

    /// <summary>
    /// Builds the message shown when a file's format is not accepted by this category.
    /// </summary>
    /// <param name="extension">The rejected extension, without the dot.</param>
    /// <returns>A user-facing sentence listing the accepted formats.</returns>
    public string BuildFormatMessage(string extension)
    {
        return $"File format '{extension}' is not allowed for category '{Category}'. " +
               $"Allowed formats: {FormatsDisplay}.";
    }

    /// <summary>
    /// Renders a configured ceiling as whole units.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Mirrors the TrBlazeUI <c>FileUpload</c> dropzone's own size
    /// formatting, which is what lets the category caption and the dropzone caption be compared for
    /// equality rather than merely for intent.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="bytes">The limit in bytes.</param>
    /// <returns>A short size string such as <c>200 KB</c> or <c>2 MB</c>.</returns>
    private static string FormatLimit(long bytes)
    {
        const long OneKilobyte = 1024;
        const long OneMegabyte = OneKilobyte * 1024;

        return bytes switch
        {
            >= OneMegabyte => $"{bytes / (double)OneMegabyte:F0} MB",
            >= OneKilobyte => $"{bytes / (double)OneKilobyte:F0} KB",
            _ => $"{bytes} B"
        };
    }

    /// <summary>
    /// Renders an actual file size, which unlike a ceiling is rarely a round number.
    /// </summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>A short size string such as <c>2.4 MB</c>.</returns>
    private static string FormatFileSize(long bytes)
    {
        const long OneKilobyte = 1024;
        const long OneMegabyte = OneKilobyte * 1024;

        return bytes switch
        {
            >= OneMegabyte => $"{bytes / (double)OneMegabyte:F1} MB",
            >= OneKilobyte => $"{bytes / (double)OneKilobyte:F1} KB",
            _ => $"{bytes} bytes"
        };
    }
}

/// <summary>
/// The seven fixed upload categories (BRD-46) and the per-category limits BRD-45 validates against.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The single authority for what an upload may be. <c>BlogImageService</c>
/// enforces these values server-side, and every screen that advertises a limit reads it from here
/// through <c>IBlogImageService.GetCategoryRule</c> — so an advertised number cannot drift from an
/// enforced one, which is the defect REQ-FN-025 was demoted for on 2026-08-11.</para>
///
/// <para><b>Code Flow:</b> Static tables built once. <see cref="TryGet"/> is the strict lookup used
/// by validation, which must reject an unknown category rather than quietly substituting one;
/// <see cref="For"/> is the lenient lookup used by display code, which falls back to
/// <see cref="DefaultCategory"/> so a caption can always be rendered.</para>
///
/// <para><b>Dependencies:</b> None — <c>BlogModels</c> is the dependency leaf.</para>
///
/// <para><b>Usage:</b> Change a limit here and nowhere else. The unit tests in
/// <c>tests/unit/Media/ImageCategoryRulesTests.cs</c> pin every value, so a change is a deliberate
/// act with a test to update.</para>
/// </remarks>
public static class ImageCategoryRules
{
    /// <summary>
    /// The category used when a caller supplies none, or one that is not recognised, and only where
    /// a fallback is safe — display code, never validation.
    /// </summary>
    public const string DefaultCategory = "general";

    /// <summary>
    /// The seven categories in the order screens list them (BRD-46).
    /// </summary>
    private static readonly string[] OrderedCategories =
        ["profiles", "logos", "awards", "icons", "blog", "cv", "general"];

    /// <summary>
    /// The authoritative per-category limits (BRD-45).
    /// </summary>
    private static readonly Dictionary<string, ImageCategoryRule> RuleMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["profiles"] = new ImageCategoryRule(
                "profiles", 2 * 1024 * 1024, ["jpg", "jpeg", "png", "webp"]),
            ["logos"] = new ImageCategoryRule(
                "logos", 500 * 1024, ["jpg", "jpeg", "png", "svg", "webp"]),
            ["awards"] = new ImageCategoryRule(
                "awards", 500 * 1024, ["jpg", "jpeg", "png", "svg", "webp"]),
            ["icons"] = new ImageCategoryRule(
                "icons", 200 * 1024, ["png", "svg", "webp"]),
            ["blog"] = new ImageCategoryRule(
                "blog", 5 * 1024 * 1024, ["jpg", "jpeg", "png", "gif", "webp"]),
            ["cv"] = new ImageCategoryRule(
                "cv", 10 * 1024 * 1024, ["pdf"]),
            ["general"] = new ImageCategoryRule(
                "general", 5 * 1024 * 1024, ["jpg", "jpeg", "png", "gif", "webp"])
        };

    /// <summary>
    /// MIME type for each extension any category allows.
    /// </summary>
    private static readonly Dictionary<string, string> MimeTypeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["jpg"] = "image/jpeg",
            ["jpeg"] = "image/jpeg",
            ["png"] = "image/png",
            ["gif"] = "image/gif",
            ["webp"] = "image/webp",
            ["svg"] = "image/svg+xml",
            ["pdf"] = "application/pdf"
        };

    /// <summary>
    /// The seven category keys, in display order.
    /// </summary>
    public static IReadOnlyList<string> Categories => OrderedCategories;

    /// <summary>
    /// The rules for every category, in display order.
    /// </summary>
    public static IReadOnlyList<ImageCategoryRule> All =>
        [.. OrderedCategories.Select(category => RuleMap[category])];

    /// <summary>
    /// Normalises a category key for lookup and storage.
    /// </summary>
    /// <param name="category">The caller's category, in any casing, possibly padded.</param>
    /// <returns>The trimmed, lower-case key; the empty string when nothing was supplied.</returns>
    public static string Normalise(string? category)
    {
        return category?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    /// <summary>
    /// Looks up a category's rules, refusing to substitute one the caller did not ask for.
    /// </summary>
    /// <remarks>
    /// This is the lookup validation uses: an unrecognised category is an input error and must be
    /// reported as one, not silently treated as <see cref="DefaultCategory"/>.
    /// </remarks>
    /// <param name="category">The category to resolve; matched case-insensitively.</param>
    /// <param name="rule">The resolved rules when the category is known.</param>
    /// <returns><c>true</c> when the category is one of the seven.</returns>
    public static bool TryGet(string? category, out ImageCategoryRule rule)
    {
        return RuleMap.TryGetValue(Normalise(category), out rule!);
    }

    /// <summary>
    /// Resolves a category's rules, falling back to <see cref="DefaultCategory"/>.
    /// </summary>
    /// <remarks>
    /// For display code only — a caption must always have something to print. Validation uses
    /// <see cref="TryGet"/> so an unknown category is rejected rather than accepted under the
    /// general limits.
    /// </remarks>
    /// <param name="category">The category to resolve; matched case-insensitively.</param>
    /// <returns>The category's rules, or the general category's rules.</returns>
    public static ImageCategoryRule For(string? category)
    {
        return TryGet(category, out var rule) ? rule : RuleMap[DefaultCategory];
    }

    /// <summary>
    /// Maps a file extension to the MIME type recorded against an upload and offered to the
    /// browser's <c>accept</c> filter.
    /// </summary>
    /// <param name="extension">Lower-case extension without the dot.</param>
    /// <returns>The MIME type, or the generic binary type for anything unrecognised.</returns>
    public static string MimeTypeFor(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }

        return MimeTypeMap.TryGetValue(extension, out var mimeType)
            ? mimeType
            : "application/octet-stream";
    }
}
