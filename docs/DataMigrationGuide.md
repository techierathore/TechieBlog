# TechieBlog Data Migration Guide

## MySQL to PostgreSQL Data Migration

This guide explains how to migrate existing data from MySQL to PostgreSQL using the `DataMigrationUtility` class.

---

## Prerequisites

Before running the migration:

1. **PostgreSQL Database Setup**
   - PostgreSQL server installed and running
   - Database created: `CREATE DATABASE TechieBlog;`
   - User credentials configured

2. **Schema Migration Complete**
   - Run the TechieBlog application once to execute DbUp migrations
   - This creates all tables and stored functions in PostgreSQL
   - Verify tables exist: `\dt` in psql

3. **MySQL Database Accessible**
   - MySQL server running with existing TechieBlog data
   - Valid connection credentials

4. **Connection Strings Ready**
   - MySQL: `server=localhost;port=3306;user id=root;password=yourpass;database=TechieBlog;`
   - PostgreSQL: `Host=localhost;Port=5432;Database=TechieBlog;Username=postgres;Password=yourpass`

---

## Quick Start

### Option 1: Using a Simple C# Script (.NET 10)

Create a file named `migrate.cs`:

```csharp
using BlogDb;

// Connection strings
var mysqlConn = "server=localhost;port=3306;user id=root;password=yourpass;database=TechieBlog;";
var pgConn = "Host=localhost;Port=5432;Database=TechieBlog;Username=postgres;Password=yourpass";

// Create migrator
var migrator = new DataMigrationUtility(mysqlConn, pgConn);

// Validate connections first
if (!await migrator.ValidateConnectionsAsync())
{
    Console.WriteLine("Connection validation failed. Please check your connection strings.");
    return;
}

// Run migration
var result = await migrator.MigrateAllDataAsync();

// Verify migration
await migrator.VerifyMigrationAsync();

Console.WriteLine($"\nMigration {(result.Success ? "completed successfully!" : "failed.")}");
```

Run with:
```bash
dotnet run migrate.cs
```

### Option 2: Adding to Program.cs (One-time migration)

Add this code to your `Program.cs` before `app.Run()`:

```csharp
// One-time data migration (remove after migration is complete)
#if DEBUG
var runMigration = builder.Configuration.GetValue<bool>("RunDataMigration");
if (runMigration)
{
    var mysqlConn = builder.Configuration["MySqlConnectionString"];
    var pgConn = builder.Configuration["AppDbConString"];

    var migrator = new BlogDb.DataMigrationUtility(mysqlConn, pgConn);

    if (await migrator.ValidateConnectionsAsync())
    {
        var result = await migrator.MigrateAllDataAsync();
        await migrator.VerifyMigrationAsync();

        if (!result.Success)
        {
            throw new Exception("Data migration failed!");
        }
    }
}
#endif
```

Add to `appsettings.Development.json`:
```json
{
  "RunDataMigration": true,
  "MySqlConnectionString": "server=localhost;port=3306;user id=root;password=yourpass;database=TechieBlog;"
}
```

### Option 3: Integration Test / Console App

```csharp
using BlogDb;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("TechieBlog Data Migration Tool");
        Console.WriteLine("==============================\n");

        // Get connection strings from args or prompt
        var mysqlConn = args.Length > 0 ? args[0] : PromptForConnection("MySQL");
        var pgConn = args.Length > 1 ? args[1] : PromptForConnection("PostgreSQL");

        var migrator = new DataMigrationUtility(mysqlConn, pgConn);

        // Step 1: Validate connections
        Console.WriteLine("\n[Step 1] Validating connections...");
        if (!await migrator.ValidateConnectionsAsync())
        {
            Console.WriteLine("ERROR: Connection validation failed!");
            return;
        }

        // Step 2: Show source data counts
        Console.WriteLine("\n[Step 2] Checking source data...");
        var sourceCounts = await migrator.GetMySqlRowCountsAsync();
        Console.WriteLine("MySQL table row counts:");
        foreach (var (table, count) in sourceCounts.Where(c => c.Value > 0))
        {
            Console.WriteLine($"  {table}: {count} rows");
        }

        // Step 3: Confirm migration
        Console.Write("\nProceed with migration? (yes/no): ");
        if (Console.ReadLine()?.ToLower() != "yes")
        {
            Console.WriteLine("Migration cancelled.");
            return;
        }

        // Step 4: Run migration
        Console.WriteLine("\n[Step 3] Running migration...");
        var result = await migrator.MigrateAllDataAsync();

        // Step 5: Verify
        Console.WriteLine("\n[Step 4] Verifying migration...");
        var verified = await migrator.VerifyMigrationAsync();

        // Summary
        Console.WriteLine("\n" + new string('=', 50));
        Console.WriteLine("MIGRATION COMPLETE");
        Console.WriteLine(new string('=', 50));
        Console.WriteLine($"Status: {(result.Success && verified ? "SUCCESS" : "NEEDS ATTENTION")}");
        Console.WriteLine($"Tables migrated: {result.TableResults.Count}");
        Console.WriteLine($"Total rows: {result.TableResults.Values.Sum(t => t.RowsMigrated)}");
    }

    static string PromptForConnection(string dbName)
    {
        Console.Write($"Enter {dbName} connection string: ");
        return Console.ReadLine() ?? "";
    }
}
```

---

## Migration Process Details

### Table Migration Order

Tables are migrated in dependency order to maintain referential integrity:

1. **Independent Tables** (no foreign keys)
   - UserRole, Category, Tag, LeadMagnet, Newsletter, EmailSequence

2. **First-Level Dependencies**
   - BlogUser, Subscriber, EmailSequenceStep

3. **User-Dependent Tables**
   - Post, BlogImage, UserLogin, LoginLog, UserSettings, UserEvents, UserActions, Widgets

4. **Post-Dependent Tables**
   - PostCategory, BlogComment, PostViews

5. **Subscriber-Dependent Tables**
   - LeadMagnetDownload, SubscriberNewsletter, SubscriberSequence

### Data Type Conversions

The utility handles these MySQL to PostgreSQL conversions:

| MySQL | PostgreSQL | Handled By |
|-------|------------|------------|
| `TINYINT(1)` | `BOOLEAN` | `CASE WHEN col = 1 THEN TRUE ELSE FALSE END` |
| `BIT(1)` | `BOOLEAN` | Same as above |
| `DATETIME` | `TIMESTAMP` | Automatic |
| `LONGTEXT` | `TEXT` | Automatic |
| `AUTO_INCREMENT` | `SERIAL/BIGSERIAL` | Sequence reset after migration |

### Column Name Mappings

Some column names differ between MySQL and PostgreSQL schemas:

| MySQL Column | PostgreSQL Column |
|--------------|-------------------|
| `TwiiterUrl` | `TwitterUrl` |
| `PostID` | `PostId` |
| `UserID` | `UserId` |
| `CommentID` | `CommentId` |
| etc. | PascalCase convention |

---

## Verification

### During Migration

The utility logs progress for each table:
```
[12:30:45] [BlogUser] Starting migration...
[12:30:46] [BlogUser] Migrated 15 rows successfully.
```

### After Migration

Run verification to compare row counts:

```csharp
await migrator.VerifyMigrationAsync();
```

Output:
```
Table Row Count Comparison:
--------------------------------------------------
Table                         MySQL  PostgreSQL     Status
--------------------------------------------------
BlogUser                         15          15         OK
Post                            142         142         OK
Category                          5           5         OK
...
--------------------------------------------------
Verification: PASSED
```

### Manual Verification Queries

PostgreSQL:
```sql
-- Check total rows per table
SELECT
    schemaname,
    relname as table_name,
    n_live_tup as row_count
FROM pg_stat_user_tables
ORDER BY n_live_tup DESC;

-- Verify specific data
SELECT * FROM BlogUser LIMIT 5;
SELECT COUNT(*) FROM Post WHERE Published = TRUE;
```

---

## Troubleshooting

### Common Issues

1. **Connection Refused**
   ```
   PostgreSQL connection FAILED: Connection refused
   ```
   - Verify PostgreSQL is running: `pg_isready`
   - Check port (default 5432)
   - Verify firewall settings

2. **Authentication Failed**
   ```
   MySQL connection FAILED: Access denied
   ```
   - Verify username/password
   - Check user has SELECT privileges on source database

3. **Foreign Key Violations**
   ```
   Warning: Failed to insert row in Post: violates foreign key constraint
   ```
   - Ensure parent tables migrate first (automatic with default order)
   - Check for orphaned records in source database

4. **Sequence Not Found**
   ```
   Note: Could not reset sequence for BlogUser.UserId
   ```
   - Not critical - sequence will auto-increment from next INSERT
   - Can manually fix: `SELECT setval('bloguser_userid_seq', (SELECT MAX(userid) FROM bloguser));`

5. **Duplicate Key**
   ```
   Warning: Failed to insert row: duplicate key value
   ```
   - The utility uses `ON CONFLICT DO NOTHING` to skip duplicates
   - Safe to re-run migration multiple times

### Partial Migration Recovery

If migration fails partway through:

1. Check which tables succeeded in the output log
2. The utility is idempotent - simply re-run it
3. Already-migrated rows are skipped (ON CONFLICT DO NOTHING)

### Data Validation Queries

After migration, validate key data:

```sql
-- Check user credentials preserved
SELECT EmailId, LENGTH(LoginPass) as PassLength FROM BlogUser;

-- Check posts have valid authors
SELECT p.PostId, p.Title, u.FirstName
FROM Post p
JOIN BlogUser u ON p.UserId = u.UserId;

-- Check comments have valid posts
SELECT COUNT(*) FROM BlogComment bc
WHERE NOT EXISTS (SELECT 1 FROM Post p WHERE p.PostId = bc.PostId);
```

---

## Post-Migration Steps

1. **Remove Migration Code**
   - Delete or comment out migration code from Program.cs
   - Set `RunDataMigration: false` if using config flag

2. **Remove MySQL Package** (Optional)
   - If MySQL is no longer needed, remove from BlogDb.csproj:
   ```xml
   <!-- Remove this line -->
   <PackageReference Include="MySql.Data" Version="9.1.0" />
   ```

3. **Update Connection Strings**
   - Ensure all environments point to PostgreSQL
   - Remove any MySQL connection strings

4. **Backup**
   - Backup the PostgreSQL database after successful migration
   - `pg_dump TechieBlog > techieblog_backup.sql`

5. **Test Application**
   - Login functionality
   - Post creation/editing
   - Comment moderation
   - Image uploads

---

## API Reference

### DataMigrationUtility Class

```csharp
// Constructor
public DataMigrationUtility(
    string mysqlConnectionString,
    string postgresConnectionString,
    Action<string> logger = null)

// Methods
Task<bool> ValidateConnectionsAsync()
Task<MigrationResult> MigrateAllDataAsync()
Task<Dictionary<string, int>> GetMySqlRowCountsAsync()
Task<Dictionary<string, int>> GetPostgresRowCountsAsync()
Task<bool> VerifyMigrationAsync()
```

### MigrationResult Class

```csharp
public class MigrationResult
{
    bool Success { get; set; }
    string ErrorMessage { get; set; }
    bool ContinueOnError { get; set; }  // Default: true
    Dictionary<string, TableMigrationResult> TableResults { get; }
}
```

### TableMigrationResult Class

```csharp
public class TableMigrationResult
{
    bool Success { get; set; }
    int RowsMigrated { get; set; }
    string ErrorMessage { get; set; }
}
```

---

## Support

For issues with the migration utility:
1. Check the troubleshooting section above
2. Review the migration log output
3. Verify connection strings and database access
4. Ensure PostgreSQL schema migrations completed successfully
