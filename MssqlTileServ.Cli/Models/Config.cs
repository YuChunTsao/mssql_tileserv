namespace MssqlTileServ.Cli.Models;

public class Config
{
    public DatabaseConfig? Database { get; set; }
    public ServiceConfig? Service { get; set; }
    public TileConfig? Tile { get; set; }
}

public class DatabaseConfig
{
    public string? Server { get; set; }
    public int Port { get; set; } = 1433;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? Name { get; set; }
    public string? Schema { get; set; } = "dbo";
    public int DbTimeout { get; set; } = 10;
    public int DbPoolMaxConns { get; set; } = 4;
}

public class ServiceConfig
{
    public int HttpPort { get; set; } = 5000;
    public int HttpsPort { get; set; } = 5001;
    public int CacheTTL { get; set; } = 0;
    public string[] CORSOrigins { get; set; } = new string[] { "*" };
}

public class TileConfig
{
    public int DefaultResolution { get; set; } = 4096;
    public int DefaultBufferSize { get; set; } = 256;
}
