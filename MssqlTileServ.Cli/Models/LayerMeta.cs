namespace MssqlTileServ.Cli.Models
{
    public enum LayerHealthLevel
    {
        Healthy,
        Warning,
        Unhealthy
    }

    public class LayerMeta
    {
        public required string Name { get; set; } // A name of table or view
        public required string ObjectType { get; set; } // A type of table or view, e.g., "U" for user table, "V" for view
        public required string GeometryColumnName { get; set; } // The name of the geometry column, e.g., "geom"
        public required string GeometryTypeName { get; set; } // The type of the geometry column, e.g., "geometry", "geography"
        // SQL Server allow use insert geometries in different SRID, but we use only one SRID for each layer
        // If the count of different SRID is more than 1, we won't use this layer to serve tiles.
        // If the table has multiple geometry columns, we will use the first one.
        public int SRID { get; set; } // The SRID of the geometry column
        public bool HasSpatialIndex { get; set; } // Indicates if the table has a spatial index

        // Health level
        public LayerHealthLevel HealthLevel { get; set; }
        // Health message
        public List<string> HealthMessages { get; set; } = new List<string>();
    }
}
