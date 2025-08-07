using MySql.Data.MySqlClient;
using System.Data;

namespace BlogEngine.DaCore;

public class DbConnectionFactory
{
    public static IDbConnection GetDbConnection(EDbConnectionTypes dbType, string connectionString)
    {
        IDbConnection connection = null;

        switch (dbType)
        {
            case EDbConnectionTypes.MariaDb:
                //connection = new MySqlConnection(connectionString);
                break;
            case EDbConnectionTypes.MySql:
                connection = new MySqlConnection(connectionString);
                break;
            case EDbConnectionTypes.SQLServer:
                //connection = new SqlConnection(connectionString);
                break;
            default:
                connection = null;
                break;
        }

        connection.Open();
        return connection;
    }
}

public enum EDbConnectionTypes
{
    MariaDb,
    MySql,
    SQLServer,
    Xml
}
