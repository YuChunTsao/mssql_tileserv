using NetTopologySuite.Geometries;

namespace MssqlTileServ.Cli.Services;

public class TileHelper
{
    // https://wiki.openstreetmap.org/wiki/Slippy_map_tilenames
    public static (double, double) TileIdToLonLat(int x, int y, int zoom)
    {
        double n = Math.Pow(2, zoom);
        double lon = x / n * 360.0 - 180.0;
        double latRadius = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n)));
        double lat = latRadius * 180.0 / Math.PI;

        return (lon, lat); // Top Left corner of the tile
    }

    // +-----------+-----------+
    // |           |           |
    // |  (0,0,1)  |  (1,0,1)  |
    // |           |           |
    // +-----------+-----------+
    // |           |           |
    // |  (0,1,1)  |  (1,1,1)  |
    // |           |           |
    // +-----------+-----------+
    public static Envelope TileIdToBounds(int x, int y, int zoom)
    {
        var (lon1, lat1) = TileIdToLonLat(x, y, zoom);
        var (lon2, lat2) = TileIdToLonLat(x + 1, y + 1, zoom);
        return new Envelope(lon1, lon2, lat2, lat1); // Top Left and Bottom Right corners
    }

    // +------------------------+
    // |       Buffer Area      |
    // |  +------------------+  |
    // |  |                  |  |
    // |  |      Extent      |  |
    // |  |                  |  |
    // |  +------------------+  |
    // |       Buffer Area      |
    // +------------------------+
    public static Envelope TileIdToBounds(int x, int y, int zoom, int extent, int buffer)
    {
        Envelope envelope = TileIdToBounds(x, y, zoom);

        double unitX = (envelope.MaxX - envelope.MinX) / extent;
        double unitY = (envelope.MaxY - envelope.MinY) / extent;
        double bufferX = buffer * unitX;
        double bufferY = buffer * unitY;

        Envelope bufferedEnvelope = new Envelope(
            envelope.MinX - bufferX,
            envelope.MaxX + bufferX,
            envelope.MinY - bufferY,
            envelope.MaxY + bufferY
        );

        return bufferedEnvelope;
    }
}
