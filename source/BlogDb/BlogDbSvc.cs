using DbUp;

namespace BlogDb;
/// <summary>
/// 
/// </summary>
public class BlogDbSvc
{
    public bool UpgradeDatabase(string connectionString)
    {
        var upgrader =
            DeployChanges.To
                .MySqlDatabase(connectionString)
                .WithScriptsFromFileSystem("MySqlScripts")
                .LogToConsole()
                .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(result.Error);
            Console.ResetColor();
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Success!");
        Console.ResetColor();
        return true;
    }
}


