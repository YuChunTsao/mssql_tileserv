using MssqlTileServ.Cli.Models;

public static class DbUtils
{
    public static string GetConnectionString(Config config)
    {
        string connectionString =
            $"Server={config.Database.Server},{config.Database.Port};" +
            $"Database={config.Database.Name};" +
            $"User Id={config.Database.User};" +
            $"Password={config.Database.Password};" +
            $"TrustServerCertificate=True;" +
            $"Pooling=true;" +
            $"Max Pool Size={config.Database.DbPoolMaxConns}";

        return connectionString;
    }
}
