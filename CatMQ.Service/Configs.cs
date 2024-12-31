using CatMQ.Service.Models.Data;
using Microsoft.Extensions.Caching.Memory;
using NTDLS.Helpers;
using System.Reflection;
using System.Text.Json;

namespace CatMQ.Service
{
    public static class Configs
    {
        private static readonly MemoryCache _cache = new(new MemoryCacheOptions());

        public enum FileType
        {
            Accounts,
            Service
        }

        private static readonly Dictionary<FileType, string> _configFiles = new()
        {
            { FileType.Accounts, "accounts.json" },
            { FileType.Service, "service.json" }
        };

        public static string GetFileName(FileType configFile)
        {
            if (_configFiles.TryGetValue(configFile, out var fileName))
            {
                var executablePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Join(executablePath, "config", fileName);
            }
            throw new Exception($"Undefined file type: [{configFile}].");
        }

        public static bool Exists(FileType configFile)
            => File.Exists(GetFileName(configFile));

        public static List<Account> GetAccounts()
            => Read<List<Account>>(FileType.Accounts);

        public static ServiceConfiguration GetServiceConfig()
           => Read<ServiceConfiguration>(FileType.Service, new());

        public static void PutAccounts(List<Account> value)
            => Write(FileType.Accounts, value);

        public static void PutServiceConfig(ServiceConfiguration value)
           => Write(FileType.Service, value);

        private static T Read<T>(FileType fileType, T defaultValue)
        {
            if (_cache.TryGetValue<T>(fileType, out var cached) && cached != null)
            {
                return cached;
            }

            var filePath = GetFileName(fileType);
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var obj = JsonSerializer.Deserialize<T>(json);
                _cache.Set(fileType, obj);
                return obj ?? defaultValue;
            }

            var directoryName = Path.GetDirectoryName(filePath);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            Write(fileType, defaultValue); //Create default file.
            return defaultValue;
        }

        private static T Read<T>(FileType fileType)
        {
            if (_cache.TryGetValue<T>(fileType, out var cached) && cached != null)
            {
                return cached;
            }

            var filePath = GetFileName(fileType);
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var obj = JsonSerializer.Deserialize<T>(json).EnsureNotNull();
                _cache.Set(fileType, obj);
                return obj;
            }
            throw new FileNotFoundException("Configuration file was not found", filePath);
        }

        private static void Write<T>(FileType fileType, T obj)
        {
            _cache.Set(fileType, obj);

            var filePath = GetFileName(fileType);
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });

            var directoryName = Path.GetDirectoryName(filePath);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            File.WriteAllText(GetFileName(fileType), json);
        }
    }
}
