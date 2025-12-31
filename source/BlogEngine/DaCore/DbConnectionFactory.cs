using Npgsql;
using System.Data;

namespace BlogEngine.DaCore;

/// <summary>
/// Factory for creating database connections based on database type.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a centralized factory for creating database connections.
/// Currently supports PostgreSQL connections using Npgsql driver.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Called by GenericRepository.GetOpenConnection()</item>
///   <item>Creates appropriate connection based on EDbConnectionTypes</item>
///   <item>Opens the connection before returning</item>
/// </list>
///
/// <para><b>Dependencies:</b></para>
/// <list type="bullet">
///   <item>Npgsql - PostgreSQL .NET driver</item>
/// </list>
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
    /// <para><b>Supported Types:</b> PostgreSQL (primary), others reserved for future use.</para>
    /// </remarks>
    /// <param name="dbType">The type of database to connect to.</param>
    /// <param name="connectionString">Connection string for the database.</param>
    /// <returns>An open IDbConnection instance.</returns>
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
}

/// <summary>
/// Supported database connection types.
/// </summary>
/// <remarks>
/// PostgreSQL is the primary supported database for TechieBlog.
/// Other types are reserved for potential future expansion.
/// </remarks>
public enum EDbConnectionTypes
{
    /// <summary>PostgreSQL database (primary)</summary>
    PostgreSql,

    /// <summary>MariaDB database (reserved)</summary>
    MariaDb,

    /// <summary>MySQL database (deprecated)</summary>
    MySql,

    /// <summary>SQL Server database (reserved)</summary>
    SqlServer
}
