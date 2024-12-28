using NTDLS.CatMQ.Shared;
using NTDLS.Helpers;
using System.Reflection;
using System.Text.Json;

namespace CatMQ.Service
{
    public class ServiceConfiguration
    {
        private string? _dataPath;

        public bool EnableWebUI { get; set; } = true;
        public string DataPath
        {
            get
            {
                if (string.IsNullOrEmpty(_dataPath))
                {
                    _dataPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).EnsureNotNull();
                }
                return _dataPath;
            }
            set
            {
                _dataPath = value;
            }
        }
        public int QueuePort { get; set; } = 45784;
        public string? WebUIURL { get; set; } = "https://localhost:45783";

        /// <summary>
        /// When true, query replies are queued in a thread pool. Otherwise, queries block other activities.
        /// </summary>
        public bool AsynchronousQueryWaiting { get; set; } = true;

        /// <summary>
        /// The default amount of time to wait for a query to reply before throwing a timeout exception.
        /// </summary>
        public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(30);
        /// <summary>
        /// The initial size in bytes of the receive buffer.
        /// If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        public int InitialReceiveBufferSize { get; set; } = CMqDefaults.INITIAL_BUFFER_SIZE;

        /// <summary>
        ///The maximum size in bytes of the receive buffer.
        ///If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        public int MaxReceiveBufferSize { get; set; } = CMqDefaults.MAX_BUFFER_SIZE;

        /// <summary>
        ///The growth rate of the auto-resizing for the receive buffer.
        /// </summary>
        public double ReceiveBufferGrowthRate { get; set; } = CMqDefaults.BUFFER_GROWTH_RATE;

        public T Read<T>(string fileName, T defaultValue)
        {
            var filePath = Path.Combine(DataPath, fileName);
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var obj = JsonSerializer.Deserialize<T>(json);
                return obj ?? defaultValue;
            }
            return defaultValue;
        }

        public T? Read<T>(string fileName)
        {
            var filePath = Path.Combine(DataPath, fileName);
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var obj = JsonSerializer.Deserialize<T>(json);
                return obj;
            }
            return default;
        }

        public void Write<T>(string fileName, T obj)
        {
            var json = JsonSerializer.Serialize<T>(obj);

            var filePath = Path.Combine(DataPath, fileName);
            File.WriteAllText(filePath, json);
        }
    }
}
