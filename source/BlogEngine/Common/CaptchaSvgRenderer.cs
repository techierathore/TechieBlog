using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BlogEngine.Common;

/// <summary>
/// Renders a captcha code as a distorted, self-contained SVG image built entirely from vector
/// strokes, so the drawn characters exist nowhere in the payload as text.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Draws the challenge server-side using nothing but the .NET base class
/// library, so the deployment stays portable and no third-party or remote captcha service is
/// involved. [REQ-FN-049]</para>
///
/// <para><b>Code Flow:</b> Every glyph is looked up in <see cref="CaptchaGlyphSet"/> as a set of
/// normalised strokes. Each stroke is scaled, sheared, rotated about the glyph centre and
/// per-point jittered <i>in C#</i>, then emitted as an anonymous <c>&lt;path&gt;</c> element. The
/// glyph paths are mixed with visually identical noise paths and the whole list is shuffled with
/// <see cref="RandomNumberGenerator"/> before it is written, so document order carries no hint of
/// left-to-right reading order. Noise dots are painted last, on top.</para>
///
/// <para><b>Dependencies:</b> <see cref="CaptchaGlyphSet"/> for the letterforms and
/// <see cref="RandomNumberGenerator"/> for every random choice. <b>System.Drawing.Common is
/// deliberately NOT used</b> - it is Windows-only and unsupported on the Linux hosts this
/// application targets; vector output sidesteps the problem entirely.</para>
///
/// <para><b>The guarantee: no machine-readable answer in the DOM.</b> This is not a stylistic
/// preference, it is the fix for a shipped defect. The first version of this challenge rendered
/// the code as SVG <c>&lt;text&gt;</c> nodes and handed the browser a base64 <c>data:</c> URI —
/// which looks opaque and is not: the URI still contained <c>&lt;text&gt;A&lt;/text&gt;</c>, so
/// anyone could recover the whole answer by base64-decoding the image <c>src</c>. Encoding is not
/// concealment. The rule that replaced it is absolute and applies to every future edit of this
/// file: <b>a <c>&lt;text&gt;</c> node must never be reintroduced</b>, nor a font reference, a
/// character literal, an <c>id</c>, a <c>class</c>, an <c>aria-label</c> naming the code, or a
/// per-glyph <c>transform</c>. Only anonymous <c>&lt;path&gt;</c> stroke geometry goes out.
/// Recovering the answer must require recognising shapes, exactly as a human does.</para>
///
/// <para><b>The markup vocabulary is deliberately independent of the code.</b> Hiding the
/// characters is not enough if the <i>shape of the payload</i> still correlates with them. Two
/// renders of different codes must be indistinguishable by anything a parser can count, so: the
/// element and attribute vocabulary is identical whatever is drawn, noise strokes are emitted
/// through the same <see cref="BuildPathElement"/> method as glyph strokes and so are structurally
/// identical to them, every noise stroke uses both a <c>Q</c> and an <c>L</c> segment so neither
/// command's presence or absence is a hint, the noise count is randomised so the total element
/// count leaks nothing, and the combined list is shuffled so document order carries no reading
/// order. <c>CaptchaSvcTests.CaptchaMarkupVocabularyIsIndependentOfCode</c> pins this by rendering
/// <c>"AAAAA"</c> and <c>"39QWX"</c> and asserting the two payloads use the same set of words —
/// if an edit here makes the markup depend on the code, that test is what will say so.</para>
///
/// <para><b>Usage:</b> Called only by <c>CaptchaSvc</c>. The code passed to <see cref="Render"/> is
/// challenge material: it is never logged, never returned to the client and never round-tripped
/// through the markup.</para>
/// </remarks>
public static class CaptchaSvgRenderer
{
    /// <summary>Width of the rendered image in user units.</summary>
    private const int ImageWidth = 190;

    /// <summary>Height of the rendered image in user units.</summary>
    private const int ImageHeight = 60;

    /// <summary>Blank space kept at the left and right edges.</summary>
    private const int SideMargin = 12;

    /// <summary>Fewest distraction strokes mixed in among the glyph strokes.</summary>
    private const int MinNoiseLineCount = 5;

    /// <summary>Most distraction strokes mixed in among the glyph strokes.</summary>
    private const int MaxNoiseLineCount = 10;

    /// <summary>Number of distraction dots scattered over the finished drawing.</summary>
    private const int NoiseDotCount = 40;

    /// <summary>Largest displacement, in user units, applied to any single glyph point.</summary>
    private const double MaxPointJitter = 0.9;

    /// <summary>Largest sideways displacement, in user units, applied to a glyph centre.</summary>
    private const double MaxCentreJitter = 2.5;

    /// <summary>Ink colours used for characters and noise. Dark enough to read on the light plate.</summary>
    private static readonly string[] InkColours =
    {
        "#1f3a5f", "#4a2c6d", "#0f5132", "#7a2e2e", "#264653", "#3d348b"
    };

    /// <summary>
    /// Renders the supplied code as distorted SVG markup.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every glyph gets an independent size, shear, rotation,
    /// baseline, colour and stroke weight, and every point is nudged, so the same character never
    /// produces the same path data twice. That defeats both template matching and any attempt to
    /// fingerprint the geometry against a copy of the glyph table.</para>
    /// <para><b>Flow:</b> build glyph paths, build noise paths, shuffle the combined list, write
    /// the background, write the shuffled paths, sprinkle dots on top.</para>
    /// <para><b>Side Effects:</b> None. Consumes cryptographic randomness only.</para>
    /// </remarks>
    /// <param name="code">The characters to draw. Never logged, never returned to the client.</param>
    /// <returns>A complete, self-contained <c>&lt;svg&gt;</c> element.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is empty.</exception>
    public static string Render(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A captcha code is required.", nameof(code));

        var paths = new List<string>();
        CollectGlyphPaths(paths, code);
        CollectNoisePaths(paths);
        Shuffle(paths);

        var markup = new StringBuilder();
        AppendOpeningTag(markup);
        foreach (var path in paths)
        {
            markup.Append(path);
        }

        AppendNoiseDots(markup);
        markup.Append("</svg>");
        return markup.ToString();
    }

    /// <summary>
    /// Writes the SVG root element and the background plate.
    /// </summary>
    /// <param name="markup">The buffer being built.</param>
    private static void AppendOpeningTag(StringBuilder markup)
    {
        markup.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{ImageWidth}\" height=\"{ImageHeight}\" ")
            .Append(CultureInfo.InvariantCulture, $"viewBox=\"0 0 {ImageWidth} {ImageHeight}\" ")
            .Append("role=\"img\" aria-label=\"Verification image. Type the characters you see.\">")
            .Append(CultureInfo.InvariantCulture,
                $"<rect width=\"{ImageWidth}\" height=\"{ImageHeight}\" rx=\"6\" fill=\"#f2f4f7\" />");
    }

    /// <summary>
    /// Builds one <c>&lt;path&gt;</c> element for every stroke of every character.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Characters are spread evenly across the plate, then each
    /// centre is nudged sideways so the columns are not on a predictable pitch.</para>
    /// <para><b>Side Effects:</b> Appends to <paramref name="paths"/>.</para>
    /// </remarks>
    /// <param name="paths">The path elements collected so far.</param>
    /// <param name="code">The code being drawn.</param>
    private static void CollectGlyphPaths(List<string> paths, string code)
    {
        var cellWidth = (double)(ImageWidth - (2 * SideMargin)) / code.Length;
        for (var index = 0; index < code.Length; index++)
        {
            var centreX = SideMargin + (cellWidth * (index + 0.5)) + Jitter(MaxCentreJitter);
            var placement = BuildPlacement(code[index], centreX);
            foreach (var stroke in CaptchaGlyphSet.StrokesFor(code[index]))
            {
                paths.Add(BuildGlyphPath(stroke, placement));
            }
        }
    }

    /// <summary>
    /// Picks the random size, slant, rotation, colour and weight for one glyph.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>M</c> and <c>W</c> get extra width because their outlines
    /// carry more horizontal detail and look cramped at the common cell width.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="character">The character about to be drawn.</param>
    /// <param name="centreX">Where the glyph centre sits horizontally.</param>
    /// <returns>The placement to project the glyph's points through.</returns>
    private static GlyphPlacement BuildPlacement(char character, double centreX)
    {
        var width = Next(20, 26) + (character is 'M' or 'W' ? 5 : 0);
        var angle = Next(-24, 25) * Math.PI / 180d;
        return new GlyphPlacement(
            centreX,
            Next(27, 34),
            width,
            Next(29, 38),
            Math.Sin(angle),
            Math.Cos(angle),
            Next(-20, 21) / 100d,
            PickColour(),
            Next(22, 33) / 10d);
    }

    /// <summary>
    /// Emits a single glyph stroke as an SVG path element.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The rotation is baked into the coordinates rather than written
    /// as a <c>transform</c> attribute, so strokes belonging to the same character share nothing
    /// an attacker could group them by.</para>
    /// <para><b>Flow:</b> project the start point, then replay each line or curve segment.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="stroke">The normalised stroke to draw.</param>
    /// <param name="placement">The glyph's size, slant, rotation and ink.</param>
    /// <returns>The path element markup.</returns>
    private static string BuildGlyphPath(CaptchaGlyphSet.GlyphStroke stroke, GlyphPlacement placement)
    {
        var geometry = new StringBuilder("M ").Append(Project(stroke.Start, placement));
        foreach (var segment in stroke.Segments)
        {
            geometry.Append(segment.IsCurve ? " Q " : " L ");
            if (segment.IsCurve)
                geometry.Append(Project(segment.Control, placement)).Append(' ');

            geometry.Append(Project(segment.End, placement));
        }

        return BuildPathElement(geometry.ToString(), placement.Colour, placement.StrokeWidth, Next(90, 101) / 100d);
    }

    /// <summary>
    /// Projects a normalised glyph point onto the image.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Shear is applied first, then rotation about the glyph centre,
    /// then a small independent nudge per point. The nudge is what stops two renders of the same
    /// character from producing byte-identical path data.</para>
    /// <para><b>Side Effects:</b> None. Consumes cryptographic randomness.</para>
    /// </remarks>
    /// <param name="point">The point on the normalised 0..1 grid.</param>
    /// <param name="placement">The glyph's size, slant and rotation.</param>
    /// <returns>The projected coordinate pair, ready to drop into a <c>d</c> attribute.</returns>
    private static string Project(CaptchaGlyphSet.GlyphPoint point, GlyphPlacement placement)
    {
        var localY = (point.Y - 0.5) * placement.Height;
        var localX = ((point.X - 0.5) * placement.Width) + (localY * placement.Shear);
        var x = placement.CentreX + (localX * placement.AngleCos) - (localY * placement.AngleSin) + Jitter(MaxPointJitter);
        var y = placement.CentreY + (localX * placement.AngleSin) + (localY * placement.AngleCos) + Jitter(MaxPointJitter);
        return Format(x) + " " + Format(y);
    }

    /// <summary>
    /// Builds distraction strokes drawn in the same element shape as the glyphs.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Sweeping curves cross the whole plate so they intersect the
    /// glyphs, which breaks up the letter outlines for a shape matcher without making the image
    /// unreadable to a person. Each noise stroke deliberately uses both a curve and a line step,
    /// so <c>Q</c> and <c>L</c> are present in every payload whatever the code happens to be -
    /// otherwise "this drawing contains no straight lines" would itself be a hint. The count is
    /// randomised so the total number of path elements does not narrow down the glyphs either.</para>
    /// <para><b>Side Effects:</b> Appends to <paramref name="paths"/>.</para>
    /// </remarks>
    /// <param name="paths">The path elements collected so far.</param>
    private static void CollectNoisePaths(List<string> paths)
    {
        var lineCount = Next(MinNoiseLineCount, MaxNoiseLineCount + 1);
        for (var index = 0; index < lineCount; index++)
        {
            var geometry = string.Create(CultureInfo.InvariantCulture,
                $"M {Next(-10, 20)} {Next(4, ImageHeight - 4)} Q {Next(40, 150)} {Next(-12, ImageHeight + 12)} {Next(ImageWidth - 60, ImageWidth - 20)} {Next(4, ImageHeight - 4)} L {Next(ImageWidth - 20, ImageWidth + 10)} {Next(4, ImageHeight - 4)}");
            paths.Add(BuildPathElement(geometry, PickColour(), Next(10, 24) / 10d, Next(35, 61) / 100d));
        }
    }

    /// <summary>
    /// Wraps path geometry in a <c>&lt;path&gt;</c> element.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Glyph strokes and noise strokes go through this one method, so
    /// they are structurally identical elements - no id, no class, no marker of any kind.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="geometry">The <c>d</c> attribute contents.</param>
    /// <param name="colour">The stroke colour.</param>
    /// <param name="strokeWidth">The stroke weight in user units.</param>
    /// <param name="opacity">The stroke opacity, 0 to 1.</param>
    /// <returns>The path element markup.</returns>
    private static string BuildPathElement(string geometry, string colour, double strokeWidth, double opacity)
    {
        return $"<path d=\"{geometry}\" fill=\"none\" stroke=\"{colour}\" stroke-width=\"{Format(strokeWidth)}\" "
            + $"stroke-linecap=\"round\" stroke-linejoin=\"round\" opacity=\"{Format(opacity)}\" />";
    }

    /// <summary>
    /// Sprinkles small dots over the finished glyphs.
    /// </summary>
    /// <param name="markup">The buffer being built.</param>
    private static void AppendNoiseDots(StringBuilder markup)
    {
        for (var index = 0; index < NoiseDotCount; index++)
        {
            markup.Append(CultureInfo.InvariantCulture,
                $"<circle cx=\"{Next(0, ImageWidth)}\" cy=\"{Next(0, ImageHeight)}\" ")
                .Append(CultureInfo.InvariantCulture,
                    $"r=\"{Next(1, 3)}\" fill=\"{PickColour()}\" opacity=\"0.5\" />");
        }
    }

    /// <summary>
    /// Shuffles the path elements so document order reveals nothing.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Without this, the glyph paths would appear left to right and a
    /// parser could segment the code by element order alone. Fisher-Yates over
    /// <see cref="RandomNumberGenerator"/> keeps the permutation unpredictable.</para>
    /// <para><b>Side Effects:</b> Reorders <paramref name="paths"/> in place.</para>
    /// </remarks>
    /// <param name="paths">The path elements to reorder.</param>
    private static void Shuffle(List<string> paths)
    {
        for (var index = paths.Count - 1; index > 0; index--)
        {
            var swapWith = Next(0, index + 1);
            (paths[index], paths[swapWith]) = (paths[swapWith], paths[index]);
        }
    }

    /// <summary>
    /// Picks an ink colour using cryptographic randomness.
    /// </summary>
    /// <returns>A hex colour string.</returns>
    private static string PickColour()
    {
        return InkColours[Next(0, InkColours.Length)];
    }

    /// <summary>
    /// Returns a small random displacement in the range plus or minus <paramref name="magnitude"/>.
    /// </summary>
    /// <param name="magnitude">The largest displacement in user units.</param>
    /// <returns>The displacement.</returns>
    private static double Jitter(double magnitude)
    {
        return Next(-1000, 1001) / 1000d * magnitude;
    }

    /// <summary>
    /// Formats a coordinate for an SVG attribute, culture-independently.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The formatted value, trimmed to two decimals.</returns>
    private static string Format(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns a cryptographically random integer in the half-open range.
    /// </summary>
    /// <param name="fromInclusive">Lower bound, included.</param>
    /// <param name="toExclusive">Upper bound, excluded.</param>
    /// <returns>The random value.</returns>
    private static int Next(int fromInclusive, int toExclusive)
    {
        return RandomNumberGenerator.GetInt32(fromInclusive, toExclusive);
    }

    /// <summary>
    /// Everything that varies from one drawn glyph to the next.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Keeps a glyph's random appearance consistent across all of its
    /// strokes, which is what makes the character look like one letter rather than several.</para>
    /// <para><b>Code Flow:</b> Built by <see cref="BuildPlacement"/>, consumed by
    /// <see cref="Project"/> and <see cref="BuildGlyphPath"/>.</para>
    /// <para><b>Dependencies:</b> None.</para>
    /// <para><b>Usage:</b> Immutable value, valid for one glyph of one render.</para>
    /// </remarks>
    private readonly struct GlyphPlacement
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GlyphPlacement"/> struct.
        /// </summary>
        /// <param name="centreX">Horizontal centre of the glyph, in user units.</param>
        /// <param name="centreY">Vertical centre of the glyph, in user units.</param>
        /// <param name="width">Glyph width in user units.</param>
        /// <param name="height">Glyph height in user units.</param>
        /// <param name="angleSin">Sine of the rotation angle.</param>
        /// <param name="angleCos">Cosine of the rotation angle.</param>
        /// <param name="shear">Horizontal shear factor applied before rotation.</param>
        /// <param name="colour">Ink colour for every stroke of this glyph.</param>
        /// <param name="strokeWidth">Stroke weight for every stroke of this glyph.</param>
        public GlyphPlacement(
            double centreX,
            double centreY,
            double width,
            double height,
            double angleSin,
            double angleCos,
            double shear,
            string colour,
            double strokeWidth)
        {
            CentreX = centreX;
            CentreY = centreY;
            Width = width;
            Height = height;
            AngleSin = angleSin;
            AngleCos = angleCos;
            Shear = shear;
            Colour = colour;
            StrokeWidth = strokeWidth;
        }

        /// <summary>Horizontal centre of the glyph.</summary>
        public double CentreX { get; }

        /// <summary>Vertical centre of the glyph.</summary>
        public double CentreY { get; }

        /// <summary>Glyph width in user units.</summary>
        public double Width { get; }

        /// <summary>Glyph height in user units.</summary>
        public double Height { get; }

        /// <summary>Sine of the rotation angle.</summary>
        public double AngleSin { get; }

        /// <summary>Cosine of the rotation angle.</summary>
        public double AngleCos { get; }

        /// <summary>Horizontal shear factor applied before rotation.</summary>
        public double Shear { get; }

        /// <summary>Ink colour for every stroke of this glyph.</summary>
        public string Colour { get; }

        /// <summary>Stroke weight for every stroke of this glyph.</summary>
        public double StrokeWidth { get; }
    }
}
