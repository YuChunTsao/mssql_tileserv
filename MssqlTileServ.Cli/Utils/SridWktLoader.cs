using System.Globalization;
using CsvHelper;

namespace MssqlTileServ.Cli.Utils
{
    public class SridWktEntry
    {
        public required string Authority { get; set; }
        public required int Code { get; set; }
        public required string Wkt { get; set; }
    }

    public static class SridWktLoader
    {
        private static List<SridWktEntry>? _cachedEntries = null;

        public static List<SridWktEntry> LoadFromCsv(string filePath)
        {
            if (_cachedEntries != null)
                return _cachedEntries;

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            var records = csv.GetRecords<SridWktEntry>().ToList();

            _cachedEntries = records;
            return _cachedEntries;
        }

        public static string? GetWkt(int srid)
        {
            if (_cachedEntries == null)
                throw new InvalidOperationException("SRID WKT data not loaded. Call LoadFromCsv(path) first.");
            var entry = _cachedEntries.FirstOrDefault(e => e.Code == srid);
            return entry?.Wkt;
        }
    }
}
