using System.Data;
using BlogModels;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// In-memory stand-in for <see cref="IEmailVerificationTokenRepo"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Reproduces the single-use, time-limited semantics of the
/// <c>ConsumeEmailVerificationToken</c> stored function so the verification tests can prove
/// "works exactly once, then expires" without PostgreSQL.</para>
/// <para><b>Code Flow:</b> <see cref="ConsumeAsync"/> refuses a token that is unknown, already
/// used or past its expiry, exactly as the SQL does.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Reach into <see cref="Tokens"/> to age a token artificially.</para>
/// </remarks>
public class FakeEmailVerificationTokenRepo : IEmailVerificationTokenRepo
{
    private readonly List<EmailVerificationToken> tokens = new();
    private long nextId = 1;

    /// <summary>
    /// Gets the tokens this fake currently holds.
    /// </summary>
    public List<EmailVerificationToken> Tokens => tokens;

    /// <summary>
    /// Gets or sets the value returned by <see cref="CountRecentByEmailAsync"/>.
    /// </summary>
    public int RecentByEmailCount { get; set; }

    /// <inheritdoc />
    public Task<EmailVerificationToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<EmailVerificationToken?>(tokens.FirstOrDefault(t => t.Token == token));
    }

    /// <inheritdoc />
    public Task<long> InsertTokenAsync(EmailVerificationToken token, CancellationToken cancellationToken = default)
    {
        token.TokenId = nextId++;
        tokens.Add(token);
        return Task.FromResult(token.TokenId);
    }

    /// <inheritdoc />
    public Task<EmailVerificationToken?> ConsumeAsync(string token, CancellationToken cancellationToken = default)
    {
        var existing = tokens.FirstOrDefault(t => t.Token == token);
        if (existing == null || existing.IsUsed || existing.ExpiresOn <= DateTime.UtcNow)
            return Task.FromResult<EmailVerificationToken?>(null);

        existing.IsUsed = true;
        existing.ConsumedOn = DateTime.UtcNow;
        return Task.FromResult<EmailVerificationToken?>(existing);
    }

    /// <inheritdoc />
    public Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        var removed = tokens.RemoveAll(t => t.ExpiresOn < DateTime.UtcNow);
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<int> CountRecentByEmailAsync(
        string email,
        DateTime since,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(RecentByEmailCount);
    }

    /// <inheritdoc />
    public IDbConnection GetOpenConnection() => throw new NotSupportedException("The fake repository has no database.");

    /// <inheritdoc />
    public long InsertToGetId(EmailVerificationToken token)
    {
        token.TokenId = nextId++;
        tokens.Add(token);
        return token.TokenId;
    }

    /// <inheritdoc />
    public void Insert(EmailVerificationToken token) => InsertToGetId(token);

    /// <inheritdoc />
    public void Update(EmailVerificationToken tokenToUpdate)
    {
        var existing = tokens.FirstOrDefault(t => t.TokenId == tokenToUpdate.TokenId);
        if (existing == null)
            return;

        existing.IsUsed = tokenToUpdate.IsUsed;
        existing.ConsumedOn = tokenToUpdate.ConsumedOn;
    }

    /// <inheritdoc />
    public EmailVerificationToken? GetSingle(long tokenId) => tokens.FirstOrDefault(t => t.TokenId == tokenId);

    /// <inheritdoc />
    public EmailVerificationToken? GetIntSingle(int tokenId) => GetSingle(tokenId);

    /// <inheritdoc />
    public IEnumerable<EmailVerificationToken> GetAll() => tokens.ToList();

    /// <inheritdoc />
    public IEnumerable<EmailVerificationToken> GetPagedData(int pageSize, int offSet) =>
        tokens.Skip(offSet).Take(pageSize).ToList();

    /// <inheritdoc />
    public IEnumerable<EmailVerificationToken> GetAllById(long targetId) =>
        tokens.Where(t => t.TargetId == targetId).ToList();
}
