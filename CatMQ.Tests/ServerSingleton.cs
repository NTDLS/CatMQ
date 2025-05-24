using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;

namespace CatMQ.Tests
{
    public class ServerSingleton
    {
        private static int _referenceCount = 0;
        private static readonly Lock _lock = new();
        private static CMqServer? _server;

        public static CMqServer GetSingleInstance()
        {
            _referenceCount++;

            if (_server == null)
            {
                lock (_lock)
                {
                    _server ??= CreateNewInstance();
                }
            }

            return _server;
        }

        private static CMqServer CreateNewInstance()
        {
            try
            {

                Directory.Delete(Constants.ROOT_PATH, true);
            }
            catch
            {
            }
            Directory.CreateDirectory(Constants.ROOT_PATH);

            bool rootDirectoryFreshlyCreated = Directory.Exists(Constants.ROOT_PATH);

            var config = new CMqServerConfiguration()
            {
                PersistencePath = Constants.ROOT_PATH
            };

            var server = new CMqServer(config);
            server.OnLog += Server_OnLog;
            server.Start(Constants.LISTEN_PORT);

            return server;
        }

        private static void Server_OnLog(CMqServer server, CMqErrorLevel errorLevel, string message, Exception? ex = null)
        {
            Console.WriteLine($"{errorLevel}: {message}");
        }

        public static void Dereference()
        {
            _referenceCount--;

            if (_referenceCount == 0)
            {
                lock (_lock)
                {
                    _server?.Stop();
                    _server = null;
                }
            }
        }
    }
}
