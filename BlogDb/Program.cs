// See https://aka.ms/new-console-template for more information
using DbUp;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Please provide the MySQL connection string as a parameter.");
            return -1;
        }

        var connectionString = args[0];

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
            return -1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Success!");
        Console.ResetColor();
        return 0;
    }
}
