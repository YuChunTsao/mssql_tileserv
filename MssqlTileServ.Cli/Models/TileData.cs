using NetTopologySuite.Geometries;

namespace MssqlTileServ.Cli.Models;

public class TileData
{
    public List<Geometry> Geometries { get; set; } = new List<Geometry>();
    public List<Dictionary<string, object?>> Attributes { get; set; } = new List<Dictionary<string, object?>>();
}
