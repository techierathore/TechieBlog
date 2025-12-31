using Dapper;
using MySql.Data.MySqlClient;
using Npgsql;

namespace BlogDb;

/// <summary>
/// Migrates data from MySQL (TechiBlogAct) to PostgreSQL.
/// </summary>
public class DataMigrationUtility
{
    private readonly string _mysqlConn;
    private readonly string _pgConn;
    private readonly Action<string> _log;

    public DataMigrationUtility(string mysqlConn, string pgConn, Action<string> log = null)
    {
        _mysqlConn = mysqlConn;
        _pgConn = pgConn;
        _log = log ?? Console.WriteLine;
    }

    public async Task<MigrationResult> MigrateAllDataAsync()
    {
        var result = new MigrationResult();
        Log("Starting migration...\n");

        try
        {
            // Order matters for foreign keys
            await MigrateTable(result, "BlogUser", MigrateBlogUserAsync);
            await MigrateTable(result, "Tag", MigrateTagAsync);
            await MigrateTable(result, "Post", MigratePostAsync);
            await MigrateTable(result, "BlogComment", MigrateBlogCommentAsync);
            await MigrateTable(result, "BlogImage", MigrateBlogImageAsync);
            await MigrateTable(result, "UserEvents", MigrateUserEventsAsync);
            await MigrateTable(result, "Widgets", MigrateWidgetsAsync);

            result.Success = result.TableResults.Values.All(t => t.Success);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Log($"FATAL: {ex.Message}");
        }

        Log($"\nMigration {(result.Success ? "SUCCEEDED" : "FAILED")}");
        Log($"Total rows: {result.TableResults.Values.Sum(t => t.RowsMigrated)}");
        return result;
    }

    private async Task MigrateTable(MigrationResult result, string name, Func<Task<int>> migrate)
    {
        try
        {
            Log($"[{name}] Migrating...");
            var count = await migrate();
            result.TableResults[name] = new TableMigrationResult { Success = true, RowsMigrated = count };
            Log($"[{name}] {count} rows migrated");
        }
        catch (Exception ex)
        {
            result.TableResults[name] = new TableMigrationResult { Success = false, ErrorMessage = ex.Message };
            Log($"[{name}] ERROR: {ex.Message}");
        }
    }

    private async Task<int> MigrateBlogUserAsync()
    {
        // MySQL: UserID, FirstName, LastName, EmailID, PassHash, UserRole, CreatedTime, UpdatedTime, LastLogin
        // PostgreSQL: UserId, FirstName, LastName, EmailId, LoginPass, UserRole, CreatedOn, UpdatedOn, IsConfirmed, etc.
        const string select = @"
            SELECT UserID, FirstName, LastName, EmailID, PassHash, UserRole,
                   COALESCE(CreatedTime, NOW()) as CreatedTime,
                   COALESCE(UpdatedTime, NOW()) as UpdatedTime
            FROM BlogUser";

        const string insert = @"
            INSERT INTO BlogUser (UserId, FirstName, LastName, EmailId, LoginPass, UserRole, CreatedOn, UpdatedOn, IsConfirmed)
            VALUES (@UserID, @FirstName, @LastName, @EmailID, @PassHash, @UserRole, @CreatedTime, @UpdatedTime, false)
            ON CONFLICT (UserId) DO NOTHING";

        return await MigrateWithIdentity(select, insert, "bloguser", "userid");
    }

    private async Task<int> MigrateTagAsync()
    {
        // MySQL: TagID, TagName
        // PostgreSQL: TagId, TagName
        const string select = "SELECT TagID, TagName FROM Tag";
        const string insert = @"
            INSERT INTO Tag (TagId, TagName)
            VALUES (@TagID, @TagName)
            ON CONFLICT (TagId) DO NOTHING";

        return await MigrateWithIdentity(select, insert, "tag", "tagid");
    }

    private async Task<int> MigratePostAsync()
    {
        // MySQL: PostID, Title, Abstract, PostContent, CreatedOn, UpdatedOn, UserID, Tags, FeaturedImage, Published
        // PostgreSQL: Same but Published is boolean
        const string select = @"
            SELECT PostID, Title, Abstract, PostContent, CreatedOn, UpdatedOn, UserID, Tags, FeaturedImage, Published
            FROM Post";

        const string insert = @"
            INSERT INTO Post (PostId, Title, Abstract, PostContent, CreatedOn, UpdatedOn, UserId, Tags, FeaturedImage, Published)
            VALUES (@PostID, @Title, @Abstract, @PostContent, @CreatedOn, @UpdatedOn, @UserID, @Tags, @FeaturedImage, @Published::boolean)
            ON CONFLICT (PostId) DO NOTHING";

        return await MigrateWithIdentity(select, insert, "post", "postid");
    }

    private async Task<int> MigrateBlogCommentAsync()
    {
        // MySQL: CommentID, PostID, GivenOn, GivenBy, Email, Comment, Published, ParentCommentID
        const string select = @"
            SELECT CommentID, PostID, GivenOn, GivenBy, Email, Comment, Published,
                   NULLIF(ParentCommentID, 0) as ParentCommentID
            FROM BlogComment";

        const string insert = @"
            INSERT INTO BlogComment (CommentId, PostId, GivenOn, GivenBy, Email, Comment, Published, ParentCommentId)
            VALUES (@CommentID, @PostID, @GivenOn, @GivenBy, @Email, @Comment, @Published::boolean, @ParentCommentID)
            ON CONFLICT (CommentId) DO NOTHING";

        return await MigrateWithIdentity(select, insert, "blogcomment", "commentid");
    }

    private async Task<int> MigrateBlogImageAsync()
    {
        // MySQL: BlogImageID, ImageName, ImagePath, Size, CreatedTime, UserID
        const string select = "SELECT BlogImageID, ImageName, ImagePath, Size, CreatedTime, UserID FROM BlogImage";

        const string insert = @"
            INSERT INTO BlogImage (BlogImageId, ImageName, ImagePath, Size, CreatedTime, UserId)
            VALUES (@BlogImageID, @ImageName, @ImagePath, @Size, @CreatedTime, @UserID)
            ON CONFLICT (BlogImageId) DO NOTHING";

        return await MigrateWithIdentity(select, insert, "blogimage", "blogimageid");
    }

    private async Task<int> MigrateUserEventsAsync()
    {
        // MySQL: EventID, LogoIconPath, EventTitle, SessionTitle, EventUrl, EventDate, Type, UserID
        const string select = @"
            SELECT EventID, LogoIconPath, EventTitle, SessionTitle, EventUrl, EventDate, Type, UserID
            FROM UserEvents";

        const string insert = @"
            INSERT INTO UserEvents (EventId, LogoIconPath, EventTitle, SessionTitle, EventUrl, EventDate, Type, UserId)
            VALUES (@EventID, @LogoIconPath, @EventTitle, @SessionTitle, @EventUrl, @EventDate, @Type, @UserID)
            ON CONFLICT (EventId) DO NOTHING";

        return await MigrateWithIdentity(select, insert, "userevents", "eventid");
    }

    private async Task<int> MigrateWidgetsAsync()
    {
        // MySQL: WidgetID, WidgetName, WidgetContent, UpdatedTime, UserID
        const string select = "SELECT WidgetID, WidgetName, WidgetContent, UpdatedTime, UserID FROM Widgets";

        const string insert = @"
            INSERT INTO Widgets (WidgetId, WidgetName, WidgetContent, UpdatedTime, UserId)
            VALUES (@WidgetID, @WidgetName, @WidgetContent, @UpdatedTime, @UserID)
            ON CONFLICT (WidgetId) DO NOTHING";

        return await MigrateWithIdentity(select, insert, "widgets", "widgetid");
    }

    private async Task<int> MigrateWithIdentity(string selectSql, string insertSql, string tableName, string idColumn)
    {
        await using var mysql = new MySqlConnection(_mysqlConn);
        await using var pg = new NpgsqlConnection(_pgConn);
        await mysql.OpenAsync();
        await pg.OpenAsync();

        var rows = (await mysql.QueryAsync(selectSql)).ToList();
        if (rows.Count == 0) return 0;

        var count = 0;
        long maxId = 0;

        foreach (var row in rows)
        {
            try
            {
                await pg.ExecuteAsync(insertSql, (object)row);
                count++;

                // Track max ID for sequence reset
                var dict = (IDictionary<string, object>)row;
                var idKey = dict.Keys.FirstOrDefault(k => k.Equals(idColumn, StringComparison.OrdinalIgnoreCase))
                         ?? dict.Keys.FirstOrDefault(k => k.ToLower().Contains("id"));
                if (idKey != null && dict[idKey] != null)
                {
                    var id = Convert.ToInt64(dict[idKey]);
                    if (id > maxId) maxId = id;
                }
            }
            catch (Exception ex)
            {
                Log($"  Warning: {ex.Message}");
            }
        }

        // Reset sequence
        if (maxId > 0)
        {
            try
            {
                await pg.ExecuteAsync($"SELECT setval(pg_get_serial_sequence('{tableName}', '{idColumn}'), @max, true)",
                    new { max = maxId });
            }
            catch { /* Sequence might not exist */ }
        }

        return count;
    }

    public async Task<bool> ValidateConnectionsAsync()
    {
        Log("Validating connections...");
        try
        {
            await using var mysql = new MySqlConnection(_mysqlConn);
            await mysql.OpenAsync();
            var mysqlVer = await mysql.QueryFirstAsync<string>("SELECT VERSION()");
            Log($"  MySQL: {mysqlVer}");
        }
        catch (Exception ex)
        {
            Log($"  MySQL FAILED: {ex.Message}");
            return false;
        }

        try
        {
            await using var pg = new NpgsqlConnection(_pgConn);
            await pg.OpenAsync();
            var pgVer = await pg.QueryFirstAsync<string>("SELECT version()");
            Log($"  PostgreSQL: {pgVer}");
        }
        catch (Exception ex)
        {
            Log($"  PostgreSQL FAILED: {ex.Message}");
            return false;
        }

        Log("Connections OK\n");
        return true;
    }

    public async Task<bool> VerifyMigrationAsync()
    {
        Log("\nVerifying migration...\n");

        var tables = new[] { "BlogUser", "Tag", "Post", "BlogComment", "BlogImage", "UserEvents", "Widgets" };

        await using var mysql = new MySqlConnection(_mysqlConn);
        await using var pg = new NpgsqlConnection(_pgConn);
        await mysql.OpenAsync();
        await pg.OpenAsync();

        Log($"{"Table",-20} {"MySQL",10} {"PostgreSQL",12} {"Status",10}");
        Log(new string('-', 55));

        var allMatch = true;
        foreach (var table in tables)
        {
            var mysqlCount = await mysql.QueryFirstOrDefaultAsync<int>($"SELECT COUNT(*) FROM `{table}`");
            var pgCount = await pg.QueryFirstOrDefaultAsync<int>($"SELECT COUNT(*) FROM {table}");
            var status = mysqlCount == pgCount ? "OK" : "MISMATCH";
            if (status == "MISMATCH") allMatch = false;
            Log($"{table,-20} {mysqlCount,10} {pgCount,12} {status,10}");
        }

        Log(new string('-', 55));
        Log($"Verification: {(allMatch ? "PASSED" : "FAILED")}");
        return allMatch;
    }

    private void Log(string msg) => _log($"[{DateTime.Now:HH:mm:ss}] {msg}");

    public async Task<Dictionary<string, int>> GetMySqlRowCountsAsync()
    {
        var counts = new Dictionary<string, int>();
        var tables = new[] { "BlogUser", "Tag", "Post", "BlogComment", "BlogImage", "UserEvents", "Widgets" };

        await using var conn = new MySqlConnection(_mysqlConn);
        await conn.OpenAsync();

        foreach (var table in tables)
        {
            try
            {
                var count = await conn.QueryFirstOrDefaultAsync<int>($"SELECT COUNT(*) FROM `{table}`");
                counts[table] = count;
            }
            catch
            {
                counts[table] = -1;
            }
        }
        return counts;
    }

    public async Task<Dictionary<string, int>> GetPostgresRowCountsAsync()
    {
        var counts = new Dictionary<string, int>();
        var tables = new[] { "BlogUser", "Tag", "Post", "BlogComment", "BlogImage", "UserEvents", "Widgets" };

        await using var conn = new NpgsqlConnection(_pgConn);
        await conn.OpenAsync();

        foreach (var table in tables)
        {
            try
            {
                var count = await conn.QueryFirstOrDefaultAsync<int>($"SELECT COUNT(*) FROM {table}");
                counts[table] = count;
            }
            catch
            {
                counts[table] = -1;
            }
        }
        return counts;
    }
}

public class MigrationResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public Dictionary<string, TableMigrationResult> TableResults { get; } = new();
}

public class TableMigrationResult
{
    public bool Success { get; set; }
    public int RowsMigrated { get; set; }
    public string ErrorMessage { get; set; }
}
