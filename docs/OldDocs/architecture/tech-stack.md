# 3. Tech Stack Alignment

### 3.1 Existing Technology Stack

| Category | Current Technology | Version | Usage in Enhancement | Notes |
|----------|-------------------|---------|---------------------|-------|
| **Framework** | .NET | 9.0 | Upgrade to 10.0 LTS | Long-term support target |
| **UI Framework** | Blazor Server | 9.0 | Keep, upgrade to 10.0 | Core rendering model unchanged |
| **UI Library** | Blazorise Bootstrap | 1.7.0 | **REPLACE** with Fluent UI | Major migration effort |
| **Database** | MySQL | 9.1.0 | **REPLACE** with PostgreSQL | Schema migration required |
| **ORM** | Dapper | 2.1.35 | Keep, minor updates | Micro-ORM pattern retained |
| **DB Migrations** | DbUp MySQL | 6.0.4 | **REPLACE** with DbUp PostgreSQL | Migration tool unchanged |
| **Authentication** | JWT | 8.2.1 | Keep, upgrade | Token infrastructure preserved |
| **Logging** | Serilog | 8.0.3 | Keep, configure | Structured logging retained |
| **Local Storage** | Blazored.LocalStorage | 4.5.0 | Keep | Theme preference storage |
| **Humanizer** | Humanizer.Core | 2.14.1 | Keep | Date/time formatting |

### 3.2 New Technology Additions

| Technology | Version | Purpose | Rationale | Integration Method |
|------------|---------|---------|-----------|-------------------|
| **Microsoft.FluentUI.AspNetCore.Components** | 4.x | UI component library | Microsoft-supported, modern Fluent design, better accessibility | Replace all Blazorise components |
| **Npgsql** | 8.x | PostgreSQL driver | Required for PostgreSQL connectivity | Replace MySql.Data |
| **dbup-postgresql** | 5.x | PostgreSQL migrations | Required for DbUp PostgreSQL support | Replace dbup-mysql |

---
