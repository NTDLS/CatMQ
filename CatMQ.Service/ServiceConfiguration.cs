using NTDLS.CatMQ.Shared;
using NTDLS.Helpers;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace CatMQ.Service
{
    public class ServiceConfiguration
    {
        private string? _dataPath;

        [Required(ErrorMessage = "Enable Web UI is required.")]
        public bool EnableWebUI { get; set; } = true;

        [Required(ErrorMessage = "Data Path is required.")]
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
        [Required(ErrorMessage = "Queue Port is required.")]
        [Range(1, 65535, ErrorMessage = "Queue Port must be between 1 and 65,535.")]
        public int QueuePort { get; set; } = CMqDefaults.LISTEN_PORT;

        [Required(ErrorMessage = "Web UI URL is required.")]
        public string? WebUIURL { get; set; } = "http://localhost:45783";

        /// <summary>
        /// When true, query replies are queued in a thread pool. Otherwise, queries block other activities.
        /// </summary>
        [Required(ErrorMessage = "Asynchronous Query Waiting is required.")]
        public bool AsynchronousAcknowledgment { get; set; } = true;

        /// <summary>
        /// The default amount of time to wait for a query to reply before throwing a timeout exception.
        /// </summary>
        [Required(ErrorMessage = "Query Timeout Seconds is required.")]
        [Range(1, 3600, ErrorMessage = "Query Timeout Seconds must be between 1 and 3,600.")]
        public int AcknowledgmentTimeoutSeconds { get; set; } = CMqDefaults.ACK_TIMEOUT_SECONDS;

        /// <summary>
        /// The initial size in bytes of the receive buffer.
        /// If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        [Required(ErrorMessage = "Initial Receive Buffer Size is required.")]
        [Range(1024, 1048576, ErrorMessage = "Initial Receive Buffer Size must be between 1 and 1,048,576.")]
        public int InitialReceiveBufferSize { get; set; } = CMqDefaults.INITIAL_BUFFER_SIZE;

        /// <summary>
        ///The maximum size in bytes of the receive buffer.
        ///If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        [Required(ErrorMessage = "Max Receive Buffer Size is required.")]
        [Range(1, 16777216, ErrorMessage = "Max Receive Buffer Size must be between 1 and 16,777,216.")]
        public int MaxReceiveBufferSize { get; set; } = CMqDefaults.MAX_BUFFER_SIZE;

        /// <summary>
        ///The growth rate of the auto-resizing for the receive buffer.
        /// </summary>
        [Required(ErrorMessage = "Receive Buffer Growth Rate is required.")]
        [Range(0.1, 2, ErrorMessage = "Receive Buffer Growth Rate must be between 0.1 and 2.0.")]
        public double ReceiveBufferGrowthRate { get; set; } = CMqDefaults.BUFFER_GROWTH_RATE;
    }
}
