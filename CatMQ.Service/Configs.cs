using NTDLS.Helpers;
using System.Reflection;
using System.Text.Json;

namespace CatMQ.Service
{
    public static class Configs
    {
        public enum ConfigFile
        {
            Accounts,
            Service
        }

        private static readonly Dictionary<ConfigFile, string> _configFiles = new()
        {
            { ConfigFile.Accounts, "accounts.json" },
            { ConfigFile.Service, "service.json" }
        };

        public static string GetFileName(ConfigFile configFile)
        {
            if (_configFiles.TryGetValue(configFile, out var fileName))
            {
                var executablePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Join(executablePath, "config", fileName);
            }
            throw new Exception($"Undefined file type: [{configFile}].");
        }

        public static bool Exists(ConfigFile configFile)
        {
            return File.Exists(GetFileName(configFile));
        }

        public static T Read<T>(ConfigFile configFile, T defaultValue)
        {
            var filePath = GetFileName(configFile);
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var obj = JsonSerializer.Deserialize<T>(json);
                return obj ?? defaultValue;
            }

            var directoryName = Path.GetDirectoryName(filePath);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            Write(configFile, defaultValue); //Create default file.
            return defaultValue;
        }

        public static T Read<T>(ConfigFile configFile)
        {
            var filePath = GetFileName(configFile);
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var obj = JsonSerializer.Deserialize<T>(json).EnsureNotNull();
                return obj;
            }
            throw new FileNotFoundException("Configuration file was not found", filePath);
        }

        public static void Write<T>(ConfigFile configFile, T obj)
        {
            var filePath = GetFileName(configFile);
            var json = JsonSerializer.Serialize<T>(obj, new JsonSerializerOptions { WriteIndented = true });

            var directoryName = Path.GetDirectoryName(filePath);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            File.WriteAllText(GetFileName(configFile), json);
        }
    }
}
