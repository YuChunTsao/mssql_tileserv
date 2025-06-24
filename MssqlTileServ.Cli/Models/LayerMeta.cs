namespace MssqlTileServ.Cli.Models
{
    public class LayerMeta
    {
        public string Name { get; set; } = string.Empty; // A name of table or view
        public string ObjectType { get; set; } = string.Empty; // A type of table or view, e.g., "U" for user table, "V" for view
        public string GeometryColumnName { get; set; } = string.Empty; // The name of the geometry column, e.g., "geom"
        public string GeometryTypeName { get; set; } = string.Empty; // The type of the geometry column, e.g., "geometry", "geography"
        public int[] SRID { get; set; } = Array.Empty<int>(); // The SRID of the geometry column, e.g., 4326
        public bool HasSpatialIndex { get; set; } = false; // Indicates if the table has a spatial index
    }
}
