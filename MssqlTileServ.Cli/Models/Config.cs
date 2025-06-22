namespace MssqlTileServ.Cli.Models;

public class Config
{
    public Database? Database { get; set; }
    public Service? Service { get; set; }
    public Tile? Tile { get; set; }
}

public class Database
{
    public string? Server { get; set; }
    public int Port { get; set; } = 1433;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? Name { get; set; }
    public string? Schema { get; set; }
    public int DbTimeout { get; set; } = 10;
    public int DbPoolSize { get; set; } = 4;
}

public class Service
{
    public int HttpPort { get; set; } = 5000;
    public int HttpsPort { get; set; } = 5001;
    public int CacheTTL { get; set; } = 0;
    public string[] CORSOrigins { get; set; } = new string[] { "*" };
}

public class Tile
{
    public int DefaultResoultion { get; set; } = 4096;
    public int DefaultBufferSize { get; set; } = 256;
}
