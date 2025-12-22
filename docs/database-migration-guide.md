# TechieBlog Database Migration Guide

## MySQL to PostgreSQL Migration

This guide covers the complete process for migrating the TechieBlog database from MySQL to PostgreSQL.

---

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Migration Architecture](#migration-architecture)
4. [Step-by-Step Migration](#step-by-step-migration)
5. [Running the Migration](#running-the-migration)
6. [Verification](#verification)
7. [Rollback Plan](#rollback-plan)
8. [Troubleshooting](#troubleshooting)

---

## Overview

### What Gets Migrated

| Component | Source (MySQL) | Target (PostgreSQL) |
|-----------|----------------|---------------------|
| **Tables** | 22 tables | 22 tables (renamed to PascalCase) |
| **Stored Procedures** | MySQL SPs | PostgreSQL Functions (PL/pgSQL) |
| **Data** | All rows | All rows with type conversions |
| **Indexes** | MySQL indexes | PostgreSQL indexes |
| **Sequences** | AUTO_INCREMENT | SERIAL/BIGSERIAL |

### Key Differences

| Feature | MySQL | PostgreSQL |
|---------|-------|------------|
| Boolean | `TINYINT(1)` | `BOOLEAN` |
| Auto-increment | `AUTO_INCREMENT` | `SERIAL`/`BIGSERIAL` |
| Text types | `LONGTEXT` | `TEXT` |
| Stored procedures | `DELIMITER $$` syntax | `CREATE FUNCTION` PL/pgSQL |
| Case sensitivity | Case-insensitive | Case-sensitive (use lowercase) |
| ENUM | Native ENUM | CHECK constraint |

---

## Prerequisites

### 1. Software Requirements

- **PostgreSQL 15+** installed and running
- **.NET 10 SDK** installed
- **MySQL 8.x** accessible (source database)
- **Git** for version control

### 2. Connection Strings

You'll need two connection strings:

```
# MySQL (Source)
Server=localhost;Port=3306;Database=TechieBlog;Uid=root;Pwd=your_password;

# PostgreSQL (Target)
Host=localhost;Port=5432;Database=techieblog;Username=postgres;Password=your_password;
```

### 3. Create PostgreSQL Database

```sql
-- Connect to PostgreSQL as superuser
CREATE DATABASE techieblog;
CREATE USER techieblog_user WITH PASSWORD 'your_secure_password';
GRANT ALL PRIVILEGES ON DATABASE techieblog TO techieblog_user;
```

---

## Migration Architecture

### File Structure

```
source/BlogDb/
├── BlogDb.csproj              # Project file with dependencies
├── BlogDbSvc.cs               # DbUp schema migration service
├── DataMigrationUtility.cs    # Data migration utility (MySQL -> PostgreSQL)
├── MigrationRunner.cs         # Console entry point for migrations
├── MySqlScripts/              # Original MySQL scripts (reference)
│   ├── 00-DBCreationScript.sql
│   ├── 01-BlogImageSps.sql
│   ├── 02-BlogUserSps.sql
│   └── ...
└── PostgresScripts/           # PostgreSQL migration scripts
    ├── 001-CreateTables.sql   # All table definitions
    ├── 002-CreateStoredFunctions.sql  # All PL/pgSQL functions
    └── 003-SeedData.sql       # Initial seed data
```

### Migration Components

1. **BlogDbSvc.cs** - Uses DbUp to run PostgreSQL schema scripts
2. **DataMigrationUtility.cs** - Migrates data from MySQL to PostgreSQL
3. **MigrationRunner.cs** - CLI entry point to execute migrations

---

## Step-by-Step Migration

### Step 1: Backup MySQL Database

```bash
# Create full backup of MySQL database
mysqldump -u root -p TechieBlog > techieblog_backup_$(date +%Y%m%d).sql

# Verify backup
ls -la techieblog_backup_*.sql
```

### Step 2: Run PostgreSQL Schema Migration

The schema migration creates all tables and functions using DbUp.

```bash
cd source/BlogDb

# Run schema migration
dotnet run -- schema --connection "Host=localhost;Database=techieblog;Username=postgres;Password=your_password"
```

This executes:
- `001-CreateTables.sql` - Creates 22 tables with proper PostgreSQL types
- `002-CreateStoredFunctions.sql` - Creates all PL/pgSQL functions
- `003-SeedData.sql` - Seeds roles, default admin, and categories

### Step 3: Run Data Migration

The data migration reads from MySQL and writes to PostgreSQL.

```bash
cd source/BlogDb

# Run data migration
dotnet run -- data \
  --mysql "Server=localhost;Database=TechieBlog;Uid=root;Pwd=mysql_password" \
  --postgres "Host=localhost;Database=techieblog;Username=postgres;Password=pg_password"
```

### Step 4: Verify Migration

```bash
cd source/BlogDb

# Run verification
dotnet run -- verify \
  --mysql "Server=localhost;Database=TechieBlog;Uid=root;Pwd=mysql_password" \
  --postgres "Host=localhost;Database=techieblog;Username=postgres;Password=pg_password"
```

---

## Running the Migration

### Option 1: Using MigrationRunner CLI

```bash
cd source/BlogDb

# Full migration (schema + data + verify)
dotnet run -- full \
  --mysql "Server=localhost;Database=TechieBlog;Uid=root;Pwd=mysql_password" \
  --postgres "Host=localhost;Database=techieblog;Username=postgres;Password=pg_password"
```

### Option 2: Programmatic Usage

```csharp
using BlogDb;

// Step 1: Run schema migrations
var dbSvc = new BlogDbSvc();
var schemaSuccess = dbSvc.UpgradeDatabase(postgresConnectionString);

if (!schemaSuccess)
{
    Console.WriteLine("Schema migration failed!");
    return;
}

// Step 2: Run data migration
var migrator = new DataMigrationUtility(
    mysqlConnectionString,
    postgresConnectionString,
    message => Console.WriteLine(message)
);

// Validate connections first
if (!await migrator.ValidateConnectionsAsync())
{
    Console.WriteLine("Connection validation failed!");
    return;
}

// Run the migration
var result = await migrator.MigrateAllDataAsync();

if (result.Success)
{
    Console.WriteLine("Migration completed successfully!");

    // Verify the migration
    await migrator.VerifyMigrationAsync();
}
else
{
    Console.WriteLine($"Migration failed: {result.ErrorMessage}");
}
```

### Option 3: Individual Table Migration

```csharp
// For selective migration or retry of specific tables
var migrator = new DataMigrationUtility(mysqlConn, pgConn);

// Get row counts before migration
var mysqlCounts = await migrator.GetMySqlRowCountsAsync();
Console.WriteLine($"MySQL BlogUser count: {mysqlCounts["BlogUser"]}");

// Run full migration
var result = await migrator.MigrateAllDataAsync();

// Check individual table results
foreach (var (table, tableResult) in result.TableResults)
{
    if (tableResult.Success)
        Console.WriteLine($"{table}: {tableResult.RowsMigrated} rows migrated");
    else
        Console.WriteLine($"{table}: FAILED - {tableResult.ErrorMessage}");
}
```

---

## Verification

### Automated Verification

The `VerifyMigrationAsync()` method compares row counts between databases:

```
Table Row Count Comparison:
--------------------------------------------------
Table                     MySQL   PostgreSQL   Status
--------------------------------------------------
BlogUser                     5           5       OK
Post                        42          42       OK
Category                     5           5       OK
Tag                         15          15       OK
BlogComment                 23          23       OK
...
--------------------------------------------------
Verification: PASSED
```

### Manual Verification Queries

```sql
-- PostgreSQL: Check table counts
SELECT 'BlogUser' as table_name, COUNT(*) as count FROM BlogUser
UNION ALL
SELECT 'Post', COUNT(*) FROM Post
UNION ALL
SELECT 'Category', COUNT(*) FROM Category
UNION ALL
SELECT 'Tag', COUNT(*) FROM Tag;

-- PostgreSQL: Verify foreign keys
SELECT
    tc.table_name,
    kcu.column_name,
    ccu.table_name AS foreign_table_name
FROM information_schema.table_constraints AS tc
JOIN information_schema.key_column_usage AS kcu
    ON tc.constraint_name = kcu.constraint_name
JOIN information_schema.constraint_column_usage AS ccu
    ON ccu.constraint_name = tc.constraint_name
WHERE tc.constraint_type = 'FOREIGN KEY';

-- PostgreSQL: Check sequence values
SELECT
    sequencename,
    last_value
FROM pg_sequences
WHERE schemaname = 'public';
```

---

## Rollback Plan

### If Migration Fails

1. **Stop the migration process**
2. **Drop the PostgreSQL database and recreate**:
   ```sql
   DROP DATABASE IF EXISTS techieblog;
   CREATE DATABASE techieblog;
   ```
3. **Investigate the error** in the migration logs
4. **Fix the issue** and re-run migration

### If Application Has Issues Post-Migration

1. **Revert connection string** in `appsettings.json` to MySQL
2. **Investigate PostgreSQL data** for inconsistencies
3. **Re-run data migration** for affected tables

---

## Troubleshooting

### Common Issues

#### 1. Connection Refused

```
Error: Failed to connect to 127.0.0.1:5432
```

**Solution**: Ensure PostgreSQL is running:
```bash
# Windows
net start postgresql-x64-15

# Linux
sudo systemctl start postgresql
```

#### 2. Authentication Failed

```
Error: password authentication failed for user "postgres"
```

**Solution**: Verify credentials in `pg_hba.conf` or use correct password.

#### 3. Sequence Not Found

```
Note: Could not reset sequence for BlogUser.UserId
```

**Solution**: This is usually a warning, not an error. Sequences are created with table. Check sequence naming:
```sql
SELECT * FROM pg_sequences WHERE sequencename LIKE '%user%';
```

#### 4. Data Type Mismatch

```
Error: column "isconfirmed" is of type boolean but expression is of type integer
```

**Solution**: The migration utility handles this conversion. If you see this error, the data might be corrupted in MySQL. Check:
```sql
SELECT DISTINCT IsConfirmed FROM BlogUser;
```

#### 5. Foreign Key Violation

```
Error: insert or update on table "post" violates foreign key constraint
```

**Solution**: Tables are migrated in dependency order. If this occurs:
1. Verify the parent table was migrated first
2. Check for orphaned records in MySQL
3. Re-run migration with `ContinueOnError = true`

---

## Table Mapping Reference

| MySQL Table | PostgreSQL Table | Notes |
|-------------|------------------|-------|
| BlogUser | BlogUser | `TwiiterUrl` -> `TwitterUrl` (typo fix) |
| Post | Post | `SEOTitle` -> `SeoTitle` (case change) |
| BlogComment | BlogComment | `Published TINYINT` -> `Published BOOLEAN` |
| Category | Category | `CategoryID` -> `CategoryId` |
| Tag | Tag | `TagID` -> `TagId` |
| PostCategory | PostCategory | Composite PK preserved |
| BlogImage | BlogImage | Same structure |
| Subscriber | Subscriber | Same structure |
| LeadMagnet | LeadMagnet | Same structure |
| LeadMagnetDownload | LeadMagnetDownload | Same structure |
| UserEvents | UserEvents | Same structure |
| UserSettings | UserSettings | Same structure |
| Widgets | Widgets | Same structure |
| PostViews | PostViews | Same structure |
| UserActions | UserActions | Same structure |
| Newsletter | Newsletter | ENUM -> CHECK constraint |
| SubscriberNewsletter | SubscriberNewsletter | Same structure |
| EmailSequence | EmailSequence | Same structure |
| EmailSequenceStep | EmailSequenceStep | Same structure |
| SubscriberSequence | SubscriberSequence | Same structure |
| UserLogin | UserLogin | NEW table (JWT sessions) |
| LoginLog | LoginLog | NEW table (audit log) |
| UserRole | UserRole | NEW table (authorization) |

---

## Post-Migration Checklist

- [ ] Schema migration completed without errors
- [ ] Data migration completed without errors
- [ ] Row count verification passed
- [ ] Update `appsettings.json` with PostgreSQL connection string
- [ ] Test application login functionality
- [ ] Test CRUD operations for posts
- [ ] Test admin dashboard
- [ ] Verify scheduled post publisher works
- [ ] Backup PostgreSQL database
- [ ] Document any customizations made

---

## Support

For issues with the migration:

1. Check the troubleshooting section above
2. Review migration logs for specific error messages
3. Verify MySQL data integrity before migration
4. Test with a subset of data first if dealing with large datasets
