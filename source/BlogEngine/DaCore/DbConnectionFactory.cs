using Npgsql;
using System.Data;
using System.Data.Common;

namespace BlogEngine.DaCore;

/// <summary>
/// Creates and opens database connections, and is the only place in the engine that knows which
/// ADO.NET provider the application talks to.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Confines the provider choice to a single <c>switch</c>. Every repository in
/// the solution reaches the database through <see cref="GenericRepository{TEntity}"/>, which reaches
/// it through this factory, so swapping PostgreSQL for another provider is a change to one expression
/// rather than to 25 repositories. The factory also guarantees the returned connection is already
/// open, which removes a whole class of "connection must be open" bugs from the call sites.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Called by <see cref="GenericRepository{TEntity}.GetOpenConnectionAsync"/> (or the legacy
///     <see cref="GenericRepository{TEntity}.GetOpenConnection"/>).</item>
///   <item>Selects the provider connection type from <see cref="EDbConnectionTypes"/>.</item>
///   <item>Opens the connection and hands ownership to the caller.</item>
/// </list>
///
/// <para><b>Dependencies:</b></para>
/// <list type="bullet">
///   <item><c>Npgsql</c> — the PostgreSQL .NET driver, and the only provider currently wired up.</item>
/// </list>
///
/// <para><b>Usage:</b> Do not call this directly from a repository — go through the
/// <c>GetOpenConnectionAsync</c> member on the base class, which already supplies the connection
/// string and the database type. Prefer <see cref="GetDbConnectionAsync"/> over
/// <see cref="GetDbConnection"/> in all new code (REQ-NFR-026).</para>
///
/// <para><b>Connection string:</b> nothing here reads configuration. The string is passed in, and it
/// originates from the configuration key <c>AppDbConString</c> — note that this project does
/// <i>not</i> use the conventional <c>ConnectionStrings:Default</c>, so searching for that name finds
/// nothing. It is supplied through user secrets or the environment, never through the committed
/// <c>appsettings.json</c>, because it carries the database password.</para>
///
/// <para><b>Pooling:</b> Npgsql pools by connection string, so "opening" a connection is normally
/// just a rent from the pool and disposing it returns it. That is why the code opens and disposes a
/// connection per query rather than holding one open — holding one open is the pattern that
/// exhausts the pool under load.</para>
/// </remarks>
public class DbConnectionFactory
{
    /// <summary>
    /// Creates and opens a database connection for the specified database type.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b></para>
    /// <list type="number">
    ///   <item>Determines connection type based on dbType parameter</item>
    ///   <item>Creates new connection instance with connection string</item>
    ///   <item>Opens connection before returning</item>
    /// </list>
    ///
    /// <para><b>Flow:</b> switch on provider → construct → <c>Open</c> → return owned connection.</para>
    ///
    /// <para><b>Side Effects:</b> Opens a physical or pooled connection and <b>blocks the calling
    /// thread</b> for the whole handshake — TCP connect, TLS negotiation and authentication. Ownership
    /// transfers to the caller, which must dispose it.</para>
    ///
    /// <para><b>Supported Types:</b> PostgreSQL (primary), others reserved for future use.</para>
    ///
    /// <para><b>Deprecated by REQ-NFR-026.</b> This is the legacy synchronous entry point, kept only
    /// while the last repositories still expose synchronous members. New code calls
    /// <see cref="GetDbConnectionAsync"/>; the final stage of REQ-NFR-026 deletes the synchronous
    /// repository surface, and this overload goes with it. Note that unlike its async twin this method
    /// does not dispose the connection if <c>Open</c> throws — another reason not to add call
    /// sites.</para>
    /// </remarks>
    /// <param name="dbType">The type of database to connect to.</param>
    /// <param name="connectionString">Connection string for the database, sourced from <c>AppDbConString</c>.</param>
    /// <returns>An open <see cref="IDbConnection"/> instance the caller must dispose.</returns>
    /// <exception cref="InvalidOperationException">Thrown when unsupported database type is specified.</exception>
    public static IDbConnection GetDbConnection(EDbConnectionTypes dbType, string connectionString)
    {
        IDbConnection connection = dbType switch
        {
            EDbConnectionTypes.PostgreSql => new NpgsqlConnection(connectionString),
            _ => throw new InvalidOperationException($"Unsupported database type: {dbType}")
        };

        connection.Open();
        return connection;
    }

    /// <summary>
    /// Creates a database connection and opens it without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b></para>
    /// <list type="number">
    ///   <item>Determines the provider connection type from <paramref name="dbType"/>.</item>
    ///   <item>Creates the connection instance with the supplied connection string.</item>
    ///   <item>Awaits <see cref="DbConnection.OpenAsync(CancellationToken)"/> so the TCP handshake,
    ///         TLS negotiation and authentication round-trips release the thread instead of parking
    ///         it.</item>
    ///   <item>Disposes the half-built connection if opening fails, so a cancelled or rejected
    ///         attempt cannot leak a pooled handle.</item>
    /// </list>
    ///
    /// <para><b>Flow:</b> switch on provider → construct → <c>OpenAsync</c> → return owned connection.</para>
    ///
    /// <para><b>Side Effects:</b> Opens a physical or pooled connection. Ownership transfers to the
    /// caller, which must dispose it — normally with <c>await using</c>.</para>
    ///
    /// <para><b>Why <see cref="DbConnection"/> rather than <see cref="IDbConnection"/>:</b> the async
    /// members (<c>OpenAsync</c>, <c>DisposeAsync</c>) live on <see cref="DbConnection"/>, and
    /// Dapper's async extensions throw at runtime unless the connection is really a
    /// <see cref="DbConnection"/>. Returning the concrete base type makes that a compile-time
    /// guarantee (REQ-NFR-026).</para>
    /// </remarks>
    /// <param name="dbType">The type of database to connect to.</param>
    /// <param name="connectionString">Connection string for the database, sourced from <c>AppDbConString</c>.</param>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>An open <see cref="DbConnection"/> instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an unsupported database type is specified.</exception>
    public static async Task<DbConnection> GetDbConnectionAsync(
        EDbConnectionTypes dbType,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        DbConnection connection = dbType switch
        {
            EDbConnectionTypes.PostgreSql => new NpgsqlConnection(connectionString),
            _ => throw new InvalidOperationException($"Unsupported database type: {dbType}")
        };

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return connection;
    }
}

/// <summary>
/// The database providers <see cref="DbConnectionFactory"/> knows how to construct a connection for.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Names the provider choice so it can be passed around as a value rather than
/// inferred from the connection string. <see cref="PostgreSql"/> is the only member the factory
/// actually handles — every other member currently falls through to the
/// <see cref="InvalidOperationException"/> arm, by design: a member that is declared but unhandled
/// fails loudly at the connection attempt instead of silently producing the wrong driver.</para>
///
/// <para><b>Usage:</b> Adding a provider means adding both a member here <i>and</i> an arm to both
/// switch expressions in <see cref="DbConnectionFactory"/> — the enum alone does nothing. Note that
/// swapping the provider is not sufficient to port this application: the data access layer calls
/// PostgreSQL stored functions by name and relies on PostgreSQL parameter binding (see
/// <see cref="DbTimestamp"/>), so the SQL would have to be ported too.</para>
/// </remarks>
public enum EDbConnectionTypes
{
    /// <summary>PostgreSQL, via Npgsql. The only provider this application supports.</summary>
    PostgreSql,

    /// <summary>MariaDB. Declared but not implemented; requesting it throws.</summary>
    MariaDb,

    /// <summary>
    /// MySQL. The database this application was originally written against, before the PostgreSQL
    /// port; the MySQL scripts have since been removed. Declared but not implemented; requesting it
    /// throws.
    /// </summary>
    MySql,

    /// <summary>SQL Server. Declared but not implemented; requesting it throws.</summary>
    SqlServer
}
