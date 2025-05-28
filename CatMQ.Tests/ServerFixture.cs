using NTDLS.CatMQ.Server;

namespace CatMQ.Tests
{
    public class ServerFixture : IDisposable
    {
        public CMqServer Server { get; private set; }

        public ServerFixture()
        {
            Server = ServerSingleton.GetSingleInstance();
        }

        public void Dispose()
        {
            ServerSingleton.Dereference();
            GC.SuppressFinalize(this);
        }
    }
}
