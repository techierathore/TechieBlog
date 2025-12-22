# BlogDb - Database Migration Tool

Migrates data from MySQL (TechiBlogAct) to PostgreSQL.

## Tables Migrated

| Table | Description |
|-------|-------------|
| BlogUser | User accounts |
| Tag | Post tags |
| Post | Blog posts |
| BlogComment | Comments |
| BlogImage | Uploaded images |
| UserEvents | Speaking events |
| Widgets | Custom widgets |

## Usage

```bash
cd source/BlogDb

# Discover MySQL structure
dotnet run -- discover --mysql "Server=localhost;Port=49166;Database=TechiBlogAct;Uid=root;Pwd=xxx"

# Run schema migration (creates PostgreSQL tables)
dotnet run -- schema --postgres "Host=localhost;Database=TechieBlog;Username=postgres;Password=xxx"

# Run data migration
dotnet run -- data --mysql "..." --postgres "..."

# Verify migration
dotnet run -- verify --mysql "..." --postgres "..."

# Full migration (schema + data + verify)
dotnet run -- full --mysql "..." --postgres "..."
```

## Column Mappings

- `PassHash` -> `LoginPass`
- `CreatedTime` -> `CreatedOn`
- `UpdatedTime` -> `UpdatedOn`
- MySQL `TINYINT(1)` -> PostgreSQL `BOOLEAN`
