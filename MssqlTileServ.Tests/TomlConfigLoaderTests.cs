using MssqlTileServ.Cli.Models;
using MssqlTileServ.Cli.Utils;


public class TomlConfigLoaderTests
{
    [Fact]
    public void Load_ValidTomlFile_ReturnsDynamicModel()
    {
        // Arrange
        var filePath = "test.toml";
        var tomlContent = @"
            [Database]
            Server = 'localhost'
            Port = 1433
            User = 'sa'
            Password = 'password'
        ";
        File.WriteAllText(filePath, tomlContent);

        // Act
        Config config = TomlConfigLoader.Load(filePath);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("localhost", config.Database!.Server);
        Assert.Equal(1433, config.Database!.Port);
        Assert.Equal("sa", config.Database!.User);
        Assert.Equal("password", config.Database!.Password);

        // Clean up
        File.Delete(filePath);
    }
}
