namespace BlogEngine.Common;

/// <summary>
/// The single definition of what counts as an acceptable password (REQ-FN-006, BRD-5/BRD-10).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One place that states the strength rules, so every path that sets a
/// password enforces the same bar and a rule change lands everywhere at once.</para>
///
/// <para><b>The rules:</b> at least 8 characters, and at least one uppercase letter, one lowercase
/// letter and one digit. All four are checked on every call and <b>every</b> failure is reported,
/// not just the first — a visitor should be told everything that is wrong with their choice in one
/// round trip rather than discovering the rules one rejection at a time.</para>
///
/// <para><b>Code Flow:</b> every password-setting path in <c>AuthSvc</c> funnels through here
/// before the value ever reaches the hasher:</para>
/// <list type="number">
///   <item>Account creation — <c>CreateStaffAccount</c> (self-service signup having been retired).</item>
///   <item>Forgotten-password reset — <c>ResetPasswordAsync</c>, from an emailed token.</item>
///   <item>Voluntary change from the profile screen — <c>ChangePasswordAsync</c>.</item>
///   <item><b>The forced first-login change</b> — the <c>/change-password</c> flow that a seeded or
///     administrator-created account is held on by its <c>MustChangePassword</c> flag (REQ-NFR-023)
///     now runs through this validator too. Before that, the one screen whose entire purpose was to
///     replace a known-weak password was the one screen not checking strength, so the forced change
///     could be satisfied with a weaker password than registration would have accepted.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None — pure BCL string inspection, no configuration and no I/O, which
/// is what makes it exhaustively unit-testable (<c>tests/unit/Security/PasswordValidatorTests.cs</c>).</para>
///
/// <para><b>Known limitations, stated rather than implied.</b> These rules are a floor, not a
/// strength model. There is no maximum length, no check against a breached-password corpus and no
/// dictionary or repetition test, so <c>Password1</c> satisfies every rule while being among the
/// first guesses any real attacker makes. What actually protects an account against guessing here
/// is <see cref="LoginThrottle"/> (five failures, then a 15-minute lockout) plus PBKDF2 hashing of
/// the stored credential — this class only stops the most trivially weak choices at the door.</para>
///
/// <para><b>Usage:</b> Call before hashing, never after. Do not add a bypass for
/// administrator-created accounts: those are exactly the accounts that end up with a shared
/// password.</para>
/// </remarks>
public static class PasswordValidator
{
    /// <summary>
    /// Checks a candidate password against every strength rule.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A null or empty password short-circuits with a single "required"
    /// message, because listing four complexity failures for an empty box is noise. Otherwise all
    /// four rules are evaluated and every violation is collected, so the caller can show the
    /// complete list in one go.</para>
    /// <para><b>Flow:</b> guard empty → length → uppercase → lowercase → digit → package the
    /// result.</para>
    /// <para><b>Side Effects:</b> None; pure. The password is never logged and never leaves this
    /// method — the returned messages describe rules, never the value that failed them.</para>
    /// </remarks>
    /// <param name="password">The candidate password. May be null.</param>
    /// <returns>A result carrying the verdict and every rule the candidate failed; the error list
    /// is empty when the password is acceptable.</returns>
    public static PasswordValidationResult Validate(string password)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Password is required");
            return new PasswordValidationResult(false, errors);
        }

        if (password.Length < 8)
            errors.Add("Password must be at least 8 characters");

        if (!password.Any(char.IsUpper))
            errors.Add("Password must contain an uppercase letter");

        if (!password.Any(char.IsLower))
            errors.Add("Password must contain a lowercase letter");

        if (!password.Any(char.IsDigit))
            errors.Add("Password must contain a number");

        return new PasswordValidationResult(errors.Count == 0, errors);
    }
}

/// <summary>
/// The verdict of one <see cref="PasswordValidator.Validate"/> call.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Carries the outcome and the reasons together, so a caller can branch on
/// the verdict and display the reasons without re-running the rules or re-deriving the wording.</para>
///
/// <para><b>Code Flow:</b> built by <see cref="PasswordValidator.Validate"/>; consumed by the
/// <c>AuthSvc</c> password paths, which map <see cref="ErrorMessage"/> onto the failure text of the
/// <c>Result</c> they return to the UI.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> The messages are written to be shown to a visitor verbatim — they describe
/// which rule was missed and never echo the submitted password.</para>
/// </remarks>
/// <param name="IsValid">True when the candidate satisfied every rule.</param>
/// <param name="Errors">One message per rule the candidate failed; empty when
/// <paramref name="IsValid"/> is true.</param>
public record PasswordValidationResult(bool IsValid, List<string> Errors)
{
    /// <summary>
    /// Gets every failure joined into one sentence, for a UI with a single error slot.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Callers that can only show one string get all of the reasons
    /// rather than an arbitrary first one. Returns an empty string on a valid password.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    public string ErrorMessage => string.Join(". ", Errors);
}
