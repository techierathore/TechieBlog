using BlogModels.Models;
using System.Collections.Concurrent;
using System.Data;

namespace BlogEngine.DbAccess;

/// <summary>
/// In-memory implementation of password reset token repository.
/// For production, this would use database storage.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Stores password reset tokens for account recovery.</para>
/// <para><b>Note:</b> Uses in-memory storage for MVP. Tokens are lost on app restart.</para>
/// </remarks>
public class PasswordResetTokenRepo : IPasswordResetTokenRepo
{
    /// <summary>
    /// Not used for in-memory implementation.
    /// </summary>
    public IDbConnection GetOpenConnection()
    {
        throw new NotImplementedException("In-memory implementation does not use database connections.");
    }

    private static readonly ConcurrentDictionary<string, PasswordResetToken> _tokens = new();
    private static long _nextId = 1;

    public PasswordResetToken GetByToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        _tokens.TryGetValue(token, out var resetToken);
        return resetToken;
    }

    public void Insert(PasswordResetToken entity)
    {
        entity.TokenId = Interlocked.Increment(ref _nextId);
        _tokens[entity.Token] = entity;
    }

    public long InsertToGetId(PasswordResetToken entity)
    {
        Insert(entity);
        return entity.TokenId;
    }

    public void MarkUsed(long tokenId)
    {
        var token = _tokens.Values.FirstOrDefault(t => t.TokenId == tokenId);
        if (token != null)
        {
            token.IsUsed = true;
        }
    }

    public void DeleteExpiredTokens()
    {
        var expiredTokens = _tokens.Where(t => t.Value.ExpiresAt < DateTime.UtcNow).ToList();
        foreach (var token in expiredTokens)
        {
            _tokens.TryRemove(token.Key, out _);
        }
    }

    public PasswordResetToken GetSingle(long aSingleId)
    {
        return _tokens.Values.FirstOrDefault(t => t.TokenId == aSingleId);
    }

    public IEnumerable<PasswordResetToken> GetAll()
    {
        return _tokens.Values.ToList();
    }

    public IEnumerable<PasswordResetToken> GetAllById(long aSingleId)
    {
        return _tokens.Values.Where(t => t.UserId == aSingleId).ToList();
    }

    public PasswordResetToken GetIntSingle(int aSingleId)
    {
        return GetSingle(aSingleId);
    }

    public void Update(PasswordResetToken entity)
    {
        if (_tokens.ContainsKey(entity.Token))
        {
            _tokens[entity.Token] = entity;
        }
    }

    public IEnumerable<PasswordResetToken> GetPagedData(int PageSize, int OffSet)
    {
        return _tokens.Values.Skip(OffSet).Take(PageSize).ToList();
    }
}
