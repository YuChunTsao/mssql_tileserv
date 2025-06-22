namespace MssqlTileServ.Cli.Utils;

using MssqlTileServ.Cli.Models;
using Tomlyn;

public class TomlConfigLoader
{
    public static Config Load(string filePath)
    {
        string tomlContent = File.ReadAllText(filePath);
        Config config = Toml.ToModel<Config>(tomlContent);
        return config;
    }
}
