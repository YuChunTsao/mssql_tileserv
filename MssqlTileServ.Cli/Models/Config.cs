namespace MssqlTileServ.Cli.Models;

public class Config
{
    public DatabaseConfig Database { get; set; } = new DatabaseConfig();
    public ServiceConfig Service { get; set; } = new ServiceConfig();
    public TileConfig Tile { get; set; } = new TileConfig();
}

public class DatabaseConfig
{
    public string Server { get; set; } = "localhost";
    public int Port { get; set; } = 1433;
    public string User { get; set; } = "sa";
    public string Password { get; set; } = "YourPassword";
    public string Name { get; set; } = "master";
    public string Schema { get; set; } = "dbo";
    public int DbTimeout { get; set; } = 10;
    public int DbPoolMaxConns { get; set; } = 4;
}

public class ServiceConfig
{
    public int HttpPort { get; set; } = 5000;
    public int HttpsPort { get; set; } = 5001;
    public string[] CORSOrigins { get; set; } = new string[] { "*" };
    public int CacheTTL { get; set; } = 0;
    public double MemoryExpirationSeconds { get; set; } = 0;
}

public class TileConfig
{
    public int Extent { get; set; } = 4096;
    public int Buffer { get; set; } = 256;
}
