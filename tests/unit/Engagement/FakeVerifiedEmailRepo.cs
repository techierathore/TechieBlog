using System.Data;
using BlogModels;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// In-memory stand-in for <see cref="IVerifiedEmailRepo"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Models the verified-address registry, including the administrative
/// block flag that revokes the "skip confirmation" shortcut.</para>
/// <para><b>Code Flow:</b> <see cref="RecordVerifiedAsync"/> upserts case-insensitively, the
/// same way the <c>RecordVerifiedEmail</c> stored function does.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Seed with <see cref="RecordVerifiedAsync"/> to make an address
/// "already known" before the act step.</para>
/// </remarks>
public class FakeVerifiedEmailRepo : IVerifiedEmailRepo
{
    private readonly List<VerifiedEmail> verifiedEmails = new();
    private long nextId = 1;

    /// <summary>
    /// Gets the registry rows this fake currently holds.
    /// </summary>
    public IReadOnlyList<VerifiedEmail> VerifiedEmails => verifiedEmails;

    /// <inheritdoc />
    public Task<VerifiedEmail?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<VerifiedEmail?>(Find(email));
    }

    /// <inheritdoc />
    public Task<bool> IsVerifiedAsync(string email, CancellationToken cancellationToken = default)
    {
        var existing = Find(email);
        return Task.FromResult(existing != null && !existing.IsBlocked);
    }

    /// <inheritdoc />
    public Task<long> RecordVerifiedAsync(
        string email,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var existing = Find(email);
        if (existing != null)
        {
            existing.LastUsedOn = DateTime.UtcNow;
            existing.DisplayName = displayName ?? existing.DisplayName;
            return Task.FromResult(existing.VerifiedEmailId);
        }

        var created = new VerifiedEmail
        {
            VerifiedEmailId = nextId++,
            Email = email,
            DisplayName = displayName,
            VerifiedOn = DateTime.UtcNow,
            LastUsedOn = DateTime.UtcNow
        };
        verifiedEmails.Add(created);
        return Task.FromResult(created.VerifiedEmailId);
    }

    /// <inheritdoc />
    public Task SetBlockedAsync(string email, bool isBlocked, CancellationToken cancellationToken = default)
    {
        var existing = Find(email);
        if (existing != null)
            existing.IsBlocked = isBlocked;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IDbConnection GetOpenConnection() => throw new NotSupportedException("The fake repository has no database.");

    /// <inheritdoc />
    public long InsertToGetId(VerifiedEmail verifiedEmail)
    {
        verifiedEmail.VerifiedEmailId = nextId++;
        verifiedEmails.Add(verifiedEmail);
        return verifiedEmail.VerifiedEmailId;
    }

    /// <inheritdoc />
    public void Insert(VerifiedEmail verifiedEmail) => InsertToGetId(verifiedEmail);

    /// <inheritdoc />
    public void Update(VerifiedEmail verifiedEmailToUpdate)
    {
        var existing = verifiedEmails.FirstOrDefault(v => v.VerifiedEmailId == verifiedEmailToUpdate.VerifiedEmailId);
        if (existing == null)
            return;

        existing.DisplayName = verifiedEmailToUpdate.DisplayName;
        existing.LastUsedOn = verifiedEmailToUpdate.LastUsedOn;
        existing.IsBlocked = verifiedEmailToUpdate.IsBlocked;
    }

    /// <inheritdoc />
    public VerifiedEmail? GetSingle(long verifiedEmailId) =>
        verifiedEmails.FirstOrDefault(v => v.VerifiedEmailId == verifiedEmailId);

    /// <inheritdoc />
    public VerifiedEmail? GetIntSingle(int verifiedEmailId) => GetSingle(verifiedEmailId);

    /// <inheritdoc />
    public IEnumerable<VerifiedEmail> GetAll() => verifiedEmails.ToList();

    /// <inheritdoc />
    public IEnumerable<VerifiedEmail> GetPagedData(int pageSize, int offSet) =>
        verifiedEmails.Skip(offSet).Take(pageSize).ToList();

    /// <inheritdoc />
    public IEnumerable<VerifiedEmail> GetAllById(long verifiedEmailId)
    {
        var existing = GetSingle(verifiedEmailId);
        return existing == null ? Enumerable.Empty<VerifiedEmail>() : new[] { existing };
    }

    /// <summary>
    /// Finds a registry row, matching the address case-insensitively.
    /// </summary>
    /// <param name="email">The address to find.</param>
    /// <returns>The row, or null.</returns>
    private VerifiedEmail? Find(string email)
    {
        return verifiedEmails.FirstOrDefault(v =>
            string.Equals(v.Email, email, StringComparison.OrdinalIgnoreCase));
    }
}
