using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LinkupFeed
{
    internal static class AtsCsv
    {
        public static List<Dictionary<string, string>> ReadRows(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("CSV path is required.", nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException($"CSV not found: {path}", path);

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 0) return new List<Dictionary<string, string>>();

            var headers = ParseLine(lines[0]).Select(h => h.Trim()).ToList();
            var rows = new List<Dictionary<string, string>>();

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var values = ParseLine(lines[i]);
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < headers.Count; c++)
                {
                    row[headers[c]] = c < values.Count ? values[c] : "";
                }
                rows.Add(row);
            }

            return rows;
        }

        public static string Get(Dictionary<string, string> row, string key)
        {
            return row.TryGetValue(key, out var value) ? value ?? "" : "";
        }

        private static List<string> ParseLine(string line)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (ch == ',' && !inQuotes)
                {
                    values.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }

            values.Add(current.ToString());
            return values;
        }
    }
}
