namespace CatMQ.Service
{
    public enum ConfigFile
    {
        Accounts,
        Service
    }

    internal static class ConfigFileLookup
    {
        private static readonly Dictionary<ConfigFile, string> _configFiles = new()
        {
            { ConfigFile.Accounts, "accounts.json" },
            { ConfigFile.Service, "service.json" }
        };

        public static string GetFileName(ConfigFile configFile)
        {
            if (_configFiles.TryGetValue(configFile, out var fileName))
            {
                return fileName;
            }
            throw new Exception($"Undefined file type: [{configFile}].");
        }

    }
}
