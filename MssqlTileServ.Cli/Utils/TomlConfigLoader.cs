namespace MssqlTileServ.Cli.Utils;

using MssqlTileServ.Cli.Models;
using Tomlyn;

public class TomlConfigLoader
{
    public static Config Load(string filePath)
    {
        string tomlContent = File.ReadAllText(filePath);
        TomlModelOptions options = new TomlModelOptions
        {
            ConvertPropertyName = (name) =>
            {
                return name;
            }
        };
        Config config = Toml.ToModel<Config>(tomlContent, options: options);
        return config;
    }
}
