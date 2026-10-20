using NTDLS.CatMQ.Shared;
using NTDLS.Helpers;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace CatMQ.Service
{
    public class ServiceConfiguration
    {
        private string? _dataPath;

        /// <summary>
        /// Whether ot not the web APIs are enabled.
        /// </summary>
        public bool EnableWebApi { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether instrumentation is enabled.
        /// </summary>
        public bool EnableInstrumentation { get; set; } = false;

        /// <summary>
        /// Whether or not the web UI is enabled.
        /// </summary>
        public bool EnableWebUI { get; set; } = true;

        [LocalizedRequired("Data Path")]
        public string DataPath
        {
            get
            {
                if (string.IsNullOrEmpty(_dataPath))
                {
                    lock (this)
                    {
                        if (string.IsNullOrEmpty(_dataPath))
                        {
                            var dataPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).EnsureNotNull();
                            dataPath = Path.Join(dataPath, "data");
                            Directory.CreateDirectory(dataPath);
                            _dataPath = dataPath;
                        }
                    }
                }
                return _dataPath;
            }
            set
            {
                _dataPath = value;
            }
        }

        /// <summary>
        /// The port which the queue service will listen on.
        /// </summary>
        [LocalizedRequired("Queue Port")]
        [LocalizedRange("Queue Port", 1, 65535)]
        public int QueuePort { get; set; } = CMqDefaults.DEFAULT_LISTEN_PORT;

        [LocalizedRequired("Web UI Binding")]
        public string? WebListenURL { get; set; } = "http://*:45783";

        /// <summary>
        /// The default amount of time to wait for a query to reply before throwing a timeout exception.
        /// </summary>
        [LocalizedRequired("Query Timeout (s)")]
        [LocalizedRange("Query Timeout (s)", 1, 3600)]
        public int AcknowledgmentTimeoutSeconds { get; set; } = CMqDefaults.DEFAULT_ACK_TIMEOUT_SECONDS;

        /// <summary>
        /// The initial size in bytes of the receive buffer.
        /// If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        [LocalizedRequired("Initial Receive Buffer Size (bytes)")]
        [LocalizedRange("Initial Receive Buffer Size (bytes)", 1024, 1048576)]
        public int InitialReceiveBufferSize { get; set; } = CMqDefaults.DEFAULT_INITIAL_BUFFER_SIZE;

        /// <summary>
        ///The maximum size in bytes of the receive buffer.
        ///If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        [LocalizedRequired("Max Receive Buffer Size (bytes)")]
        [LocalizedRange("Max Receive Buffer Size (bytes)", 1, 16777216)]
        public int MaxReceiveBufferSize { get; set; } = CMqDefaults.DEFAULT_MAX_BUFFER_SIZE;

        /// <summary>
        ///The growth rate of the auto-resizing for the receive buffer.
        /// </summary>
        [LocalizedRequired("Receive Buffer Growth Rate (decimal %)")]
        [LocalizedRange("Receive Buffer Growth Rate (decimal %)", 0.1, 2)]
        public double ReceiveBufferGrowthRate { get; set; } = CMqDefaults.DEFAULT_BUFFER_GROWTH_RATE;
    }
}
