using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace KBot.App.Services
{
    // Persists the hunted position offset per client executable so it survives
    // reattach / client restart. Stored as an offset from the module base, which
    // stays valid across ASLR and only breaks when the binary changes.
    public sealed class PositionOffsetStore
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KBot", "position-offsets.json");

        public long? Get(string executableName)
        {
            if (string.IsNullOrWhiteSpace(executableName)) return null;
            try
            {
                if (!File.Exists(FilePath)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                var root = doc.RootElement;
                var key = executableName.ToLowerInvariant();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.ValueKind == JsonValueKind.Number &&
                        prop.Value.TryGetInt64(out var value))
                        return value;
                }
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or DirectoryNotFoundException)
            {
                return null;
            }
        }

        public void Set(string executableName, long offsetFromBase)
        {
            if (string.IsNullOrWhiteSpace(executableName)) return;
            var map = LoadMap();
            map[executableName.ToLowerInvariant()] = offsetFromBase;
            SaveMap(map);
        }

        public void Clear(string executableName)
        {
            if (string.IsNullOrWhiteSpace(executableName)) return;
            var map = LoadMap();
            if (map.Remove(executableName.ToLowerInvariant())) SaveMap(map);
        }

        private static Dictionary<string, long> LoadMap()
        {
            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return map;
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt64(out var v))
                        map[prop.Name] = v;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or DirectoryNotFoundException)
            {
            }
            return map;
        }

        private static void SaveMap(Dictionary<string, long> map)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
            }
        }
    }
}
