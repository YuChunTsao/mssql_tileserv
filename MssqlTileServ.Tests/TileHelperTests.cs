using MssqlTileServ.Cli.Services;
using NetTopologySuite.Geometries;

namespace MssqlTileServ.Tests;

public class TileHelperTests
{
    [Theory]
    [InlineData(0, 0, 0, -180.0, 85.05112878)]
    [InlineData(1, 1, 1, 0.0, 0.0)]
    [InlineData(2, 2, 2, 0.0, 0.0)]
    public void TileIdToLonLat_ReturnsExpectedLonLat(int x, int y, int zoom, double expectedLon, double expectedLat)
    {
        // Act
        var (lon, lat) = TileHelper.TileIdToLonLat(x, y, zoom);

        // Assert
        Assert.Equal(expectedLon, lon, 5);
        Assert.Equal(expectedLat, lat, 5);
    }

    [Theory]
    [InlineData(0, 0, 0, -180.0, 180.0, -85.05112878, 85.05112878)]
    [InlineData(1, 1, 1, 0.0, 180.0, -85.05112878, 0.0)]
    [InlineData(0, 0, 1, -180.0, 0.0, 0.0, 85.05112878)]
    public void TileIdToBounds_ReturnsExpectedEnvelope(
        int x, int y, int zoom,
        double expectedMinX, double expectedMaxX,
        double expectedMinY, double expectedMaxY)
    {
        // Act
        var envelope = TileHelper.TileIdToBounds(x, y, zoom);

        // Assert
        Assert.Equal(expectedMinX, envelope.MinX, 5);
        Assert.Equal(expectedMaxX, envelope.MaxX, 5);
        Assert.Equal(expectedMinY, envelope.MinY, 5);
        Assert.Equal(expectedMaxY, envelope.MaxY, 5);
    }

    [Fact]
    public void TileIdToBounds_WithBuffer_ReturnsBufferedEnvelope()
    {
        // Arrange
        int x = 1, y = 2, zoom = 3, extent = 4096, buffer = 256;

        // Act
        Envelope result = TileHelper.TileIdToBounds(x, y, zoom, extent, buffer);

        // Assert
        Assert.NotNull(result);
        // The buffered envelope should be larger than the original
        Envelope original = TileHelper.TileIdToBounds(x, y, zoom);
        Assert.True(result.MinX < original.MinX);
        Assert.True(result.MaxX > original.MaxX);
        Assert.True(result.MinY < original.MinY);
        Assert.True(result.MaxY > original.MaxY);
    }

    [Fact]
    public void TileIdToBounds_ZeroBuffer_ReturnsOriginalEnvelope()
    {
        int x = 0, y = 0, zoom = 0, extent = 4096, buffer = 0;

        Envelope result = TileHelper.TileIdToBounds(x, y, zoom, extent, buffer);
        Envelope original = TileHelper.TileIdToBounds(x, y, zoom);

        Assert.Equal(original.MinX, result.MinX);
        Assert.Equal(original.MaxX, result.MaxX);
        Assert.Equal(original.MinY, result.MinY);
        Assert.Equal(original.MaxY, result.MaxY);
    }
}
