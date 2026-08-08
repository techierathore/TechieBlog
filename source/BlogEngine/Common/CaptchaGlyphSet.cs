using System.Globalization;

namespace BlogEngine.Common;

/// <summary>
/// Vector stroke definitions for every character a captcha challenge may contain.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the captcha be drawn as pure geometry. Because every glyph is a set
/// of line and curve strokes rather than an SVG <c>&lt;text&gt;</c> node, the rendered challenge
/// contains no character literal anywhere, so the expected answer cannot be recovered by parsing
/// the payload the browser receives. [REQ-FN-049]</para>
///
/// <para><b>Code Flow:</b> Each character owns a compact outline expressed in a tiny subset of the
/// SVG path grammar (<c>M</c>, <c>L</c>, <c>Q</c>) on a normalised 0..1 grid where x grows to the
/// right and y grows downward. <see cref="Compile"/> turns that text into
/// <see cref="GlyphStroke"/> objects once, at type initialisation; the renderer then scales,
/// shears, rotates and jitters the points before emitting them.</para>
///
/// <para><b>Dependencies:</b> None beyond the base class library. No font is consulted, so the
/// output is identical on every host and no <c>System.Drawing.Common</c> is required.</para>
///
/// <para><b>Why this table exists at all.</b> The natural way to draw a letter in SVG is a
/// <c>&lt;text&gt;</c> node, and that is what the first version of this captcha did — with the
/// result that the answer sat in plain sight inside the base64 <c>data:</c> URI the browser was
/// handed, recoverable by decoding the image <c>src</c>. Hand-digitised outlines are the price of
/// removing every character literal from the payload. <b>Replacing this table with a font or a
/// <c>&lt;text&gt;</c> node reintroduces that defect</b>, however much simpler the code would
/// look.</para>
///
/// <para><b>Usage:</b> Internal to <see cref="CaptchaSvgRenderer"/>. The outlines are deliberately
/// coarse - a captcha wants slightly irregular letterforms, not typographic quality. The path
/// grammar is limited to <c>M</c>/<c>L</c>/<c>Q</c> on purpose: a wider vocabulary would let the
/// commands present in a payload vary with which characters were drawn, which is the correlation
/// <c>CaptchaSvcTests.CaptchaMarkupVocabularyIsIndependentOfCode</c> exists to forbid.</para>
/// </remarks>
internal static class CaptchaGlyphSet
{
    /// <summary>
    /// Normalised outlines keyed by character, in the <c>M</c>/<c>L</c>/<c>Q</c> path subset.
    /// </summary>
    /// <remarks>
    /// Coordinates sit on a 0..1 box; values slightly outside that range are intentional and let
    /// round shapes bulge past the nominal cell. The key set must match <c>CaptchaSvc</c>'s
    /// <c>CodeAlphabet</c> - <c>CaptchaSvcTests</c> asserts that it does.
    /// </remarks>
    private static readonly Dictionary<char, string> Outlines = new()
    {
        ['A'] = "M 0.05 1 L 0.5 0 L 0.95 1 M 0.22 0.6 L 0.78 0.6",
        ['B'] = "M 0.15 0 L 0.15 1 M 0.15 0 L 0.6 0 Q 0.95 0.25 0.6 0.5 L 0.15 0.5 M 0.15 0.5 L 0.62 0.5 Q 1 0.75 0.6 1 L 0.15 1",
        ['C'] = "M 0.9 0.22 Q 0.5 -0.1 0.18 0.28 Q 0.02 0.5 0.18 0.72 Q 0.5 1.1 0.9 0.78",
        ['D'] = "M 0.15 0 L 0.15 1 M 0.15 0 L 0.5 0 Q 0.98 0.5 0.5 1 L 0.15 1",
        ['E'] = "M 0.88 0 L 0.15 0 L 0.15 1 L 0.88 1 M 0.15 0.5 L 0.7 0.5",
        ['F'] = "M 0.88 0 L 0.15 0 L 0.15 1 M 0.15 0.48 L 0.7 0.48",
        ['G'] = "M 0.9 0.22 Q 0.5 -0.1 0.18 0.28 Q 0.02 0.5 0.18 0.72 Q 0.6 1.08 0.9 0.68 L 0.9 0.55 L 0.55 0.55",
        ['H'] = "M 0.15 0 L 0.15 1 M 0.85 0 L 0.85 1 M 0.15 0.5 L 0.85 0.5",
        ['J'] = "M 0.75 0 L 0.75 0.72 Q 0.75 1.05 0.42 1 Q 0.18 0.94 0.16 0.7",
        ['K'] = "M 0.15 0 L 0.15 1 M 0.88 0 L 0.15 0.55 M 0.42 0.36 L 0.9 1",
        ['M'] = "M 0.06 1 L 0.06 0 L 0.5 0.6 L 0.94 0 L 0.94 1",
        ['N'] = "M 0.15 1 L 0.15 0 L 0.85 1 L 0.85 0",
        ['P'] = "M 0.15 1 L 0.15 0 L 0.6 0 Q 1 0.28 0.6 0.55 L 0.15 0.55",
        ['Q'] = "M 0.5 0 Q 0.92 0 0.92 0.5 Q 0.92 1 0.5 1 Q 0.08 1 0.08 0.5 Q 0.08 0 0.5 0 M 0.6 0.7 L 0.95 1.05",
        ['R'] = "M 0.15 1 L 0.15 0 L 0.6 0 Q 1 0.28 0.6 0.55 L 0.15 0.55 M 0.5 0.55 L 0.9 1",
        ['T'] = "M 0.08 0 L 0.92 0 M 0.5 0 L 0.5 1",
        ['U'] = "M 0.15 0 L 0.15 0.62 Q 0.15 1.02 0.5 1.02 Q 0.85 1.02 0.85 0.62 L 0.85 0",
        ['V'] = "M 0.08 0 L 0.5 1 L 0.92 0",
        ['W'] = "M 0.04 0 L 0.26 1 L 0.5 0.35 L 0.74 1 L 0.96 0",
        ['X'] = "M 0.12 0 L 0.88 1 M 0.88 0 L 0.12 1",
        ['Y'] = "M 0.12 0 L 0.5 0.52 L 0.88 0 M 0.5 0.52 L 0.5 1",
        ['3'] = "M 0.15 0.12 Q 0.5 -0.12 0.8 0.16 Q 0.95 0.4 0.5 0.5 Q 1 0.56 0.85 0.84 Q 0.5 1.12 0.15 0.88",
        ['4'] = "M 0.72 1 L 0.72 0 L 0.1 0.68 L 0.95 0.68",
        ['6'] = "M 0.8 0.06 Q 0.42 -0.06 0.24 0.38 Q 0.1 0.78 0.22 0.92 Q 0.52 1.1 0.78 0.88 Q 0.92 0.62 0.68 0.5 Q 0.34 0.42 0.22 0.64",
        ['7'] = "M 0.1 0 L 0.9 0 L 0.42 1",
        ['8'] = "M 0.5 0.5 Q 0.12 0.42 0.16 0.22 Q 0.26 -0.02 0.5 0 Q 0.76 -0.02 0.84 0.22 Q 0.9 0.42 0.5 0.5 Q 0.08 0.6 0.12 0.8 Q 0.24 1.02 0.5 1 Q 0.78 1.02 0.88 0.8 Q 0.94 0.6 0.5 0.5",
        ['9'] = "M 0.78 0.42 Q 0.62 0.6 0.34 0.5 Q 0.12 0.38 0.2 0.16 Q 0.4 -0.06 0.64 0.06 Q 0.84 0.2 0.8 0.56 Q 0.72 0.96 0.28 0.94"
    };

    /// <summary>Compiled strokes, built once so rendering does no parsing.</summary>
    private static readonly Dictionary<char, GlyphStroke[]> CompiledGlyphs = CompileAll();

    /// <summary>
    /// Returns the strokes that draw a single character.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Lookup is case-insensitive because the table is stored in
    /// upper case. An unknown character throws rather than drawing nothing, so a mismatch between
    /// the alphabet and this table fails loudly in tests instead of silently producing a captcha
    /// nobody can answer.</para>
    /// <para><b>Flow:</b> upper-case the character, read the pre-compiled entry.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="character">The character to draw.</param>
    /// <returns>The strokes, in the normalised 0..1 coordinate space.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when no glyph is defined.</exception>
    public static IReadOnlyList<GlyphStroke> StrokesFor(char character)
    {
        var key = char.ToUpperInvariant(character);
        if (!CompiledGlyphs.TryGetValue(key, out var strokes))
            throw new ArgumentOutOfRangeException(nameof(character), character, "No captcha glyph is defined for this character.");

        return strokes;
    }

    /// <summary>
    /// Compiles every outline in the table.
    /// </summary>
    /// <returns>The compiled glyph dictionary.</returns>
    private static Dictionary<char, GlyphStroke[]> CompileAll()
    {
        var compiled = new Dictionary<char, GlyphStroke[]>(Outlines.Count);
        foreach (var entry in Outlines)
        {
            compiled[entry.Key] = Compile(entry.Value);
        }

        return compiled;
    }

    /// <summary>
    /// Turns one normalised outline string into strokes.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only <c>M</c> (start a new stroke), <c>L</c> (line to) and
    /// <c>Q</c> (quadratic curve to) are supported - enough for readable letterforms and small
    /// enough to keep the parser trivially auditable.</para>
    /// <para><b>Flow:</b> tokenise on spaces, walk the tokens, flush a stroke on every
    /// <c>M</c> and once more at the end.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="outline">The outline in the supported path subset.</param>
    /// <returns>The strokes it describes.</returns>
    private static GlyphStroke[] Compile(string outline)
    {
        var tokens = outline.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var strokes = new List<GlyphStroke>();
        var segments = new List<GlyphSegment>();
        var start = new GlyphPoint(0d, 0d);
        var cursor = 0;

        while (cursor < tokens.Length)
        {
            var command = tokens[cursor++];
            if (command == "M")
            {
                FlushStroke(strokes, segments, start);
                start = ReadPoint(tokens, ref cursor);
                continue;
            }

            segments.Add(ReadSegment(command, tokens, ref cursor));
        }

        FlushStroke(strokes, segments, start);
        return strokes.ToArray();
    }

    /// <summary>
    /// Moves the pending segments into a finished stroke and clears the buffer.
    /// </summary>
    /// <param name="strokes">The strokes collected so far.</param>
    /// <param name="segments">The pending segment buffer, emptied by this call.</param>
    /// <param name="start">Where the pending stroke began.</param>
    private static void FlushStroke(List<GlyphStroke> strokes, List<GlyphSegment> segments, GlyphPoint start)
    {
        if (segments.Count == 0)
            return;

        strokes.Add(new GlyphStroke(start, segments.ToArray()));
        segments.Clear();
    }

    /// <summary>
    /// Reads one line or curve segment from the token stream.
    /// </summary>
    /// <param name="command">The command token, <c>L</c> or <c>Q</c>.</param>
    /// <param name="tokens">The whole token stream.</param>
    /// <param name="cursor">The read position, advanced by this call.</param>
    /// <returns>The parsed segment.</returns>
    private static GlyphSegment ReadSegment(string command, string[] tokens, ref int cursor)
    {
        if (command != "Q")
            return new GlyphSegment(false, new GlyphPoint(0d, 0d), ReadPoint(tokens, ref cursor));

        var control = ReadPoint(tokens, ref cursor);
        return new GlyphSegment(true, control, ReadPoint(tokens, ref cursor));
    }

    /// <summary>
    /// Reads an x/y pair from the token stream.
    /// </summary>
    /// <param name="tokens">The whole token stream.</param>
    /// <param name="cursor">The read position, advanced by two.</param>
    /// <returns>The parsed point.</returns>
    private static GlyphPoint ReadPoint(string[] tokens, ref int cursor)
    {
        var x = double.Parse(tokens[cursor++], CultureInfo.InvariantCulture);
        var y = double.Parse(tokens[cursor++], CultureInfo.InvariantCulture);
        return new GlyphPoint(x, y);
    }

    /// <summary>
    /// A point on the normalised 0..1 glyph grid.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Font-independent coordinate carried from the table to the renderer.</para>
    /// <para><b>Code Flow:</b> Produced by <see cref="ReadPoint"/>, consumed by the renderer's
    /// projection.</para>
    /// <para><b>Dependencies:</b> None.</para>
    /// <para><b>Usage:</b> Immutable value; x grows right, y grows downward.</para>
    /// </remarks>
    internal readonly struct GlyphPoint
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GlyphPoint"/> struct.
        /// </summary>
        /// <param name="x">Horizontal position, 0 at the left of the cell.</param>
        /// <param name="y">Vertical position, 0 at the top of the cell.</param>
        public GlyphPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Horizontal position on the normalised grid.</summary>
        public double X { get; }

        /// <summary>Vertical position on the normalised grid.</summary>
        public double Y { get; }
    }

    /// <summary>
    /// One line or quadratic curve step within a stroke.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Describes how the pen moves from the previous point to the next.</para>
    /// <para><b>Code Flow:</b> Produced by <see cref="ReadSegment"/>, replayed by the renderer.</para>
    /// <para><b>Dependencies:</b> <see cref="GlyphPoint"/>.</para>
    /// <para><b>Usage:</b> <see cref="Control"/> is meaningful only when <see cref="IsCurve"/>.</para>
    /// </remarks>
    internal readonly struct GlyphSegment
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GlyphSegment"/> struct.
        /// </summary>
        /// <param name="isCurve">True for a quadratic curve, false for a straight line.</param>
        /// <param name="control">The quadratic control point; ignored for a line.</param>
        /// <param name="end">Where the segment finishes.</param>
        public GlyphSegment(bool isCurve, GlyphPoint control, GlyphPoint end)
        {
            IsCurve = isCurve;
            Control = control;
            End = end;
        }

        /// <summary>True when this segment is a quadratic curve.</summary>
        public bool IsCurve { get; }

        /// <summary>The quadratic control point.</summary>
        public GlyphPoint Control { get; }

        /// <summary>The end point of the segment.</summary>
        public GlyphPoint End { get; }
    }

    /// <summary>
    /// A single continuous pen stroke of a glyph.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Groups the segments that are drawn without lifting the pen, which the
    /// renderer emits as one SVG <c>&lt;path&gt;</c>.</para>
    /// <para><b>Code Flow:</b> Built by <see cref="FlushStroke"/> during compilation.</para>
    /// <para><b>Dependencies:</b> <see cref="GlyphPoint"/>, <see cref="GlyphSegment"/>.</para>
    /// <para><b>Usage:</b> Immutable; the same instance is reused for every render.</para>
    /// </remarks>
    internal sealed class GlyphStroke
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GlyphStroke"/> class.
        /// </summary>
        /// <param name="start">Where the pen is put down.</param>
        /// <param name="segments">The steps that follow.</param>
        public GlyphStroke(GlyphPoint start, GlyphSegment[] segments)
        {
            Start = start;
            Segments = segments;
        }

        /// <summary>Where the stroke begins.</summary>
        public GlyphPoint Start { get; }

        /// <summary>The line and curve steps making up the stroke.</summary>
        public IReadOnlyList<GlyphSegment> Segments { get; }
    }
}
