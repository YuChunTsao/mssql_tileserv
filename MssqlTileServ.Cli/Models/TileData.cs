namespace MssqlTileServ.Cli.Models;

public class TileData
{
    public List<byte[]> Geometries { get; set; } = new List<byte[]>();
    public List<Dictionary<string, object?>> Attributes { get; set; } = new List<Dictionary<string, object?>>();
}
