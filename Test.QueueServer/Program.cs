using NTDLS.CatMQ.Server;

namespace Test.QueueServer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var server = new CMqServer();

            //Listen for queue clients on port 45784
            server.Start(45784);

            Console.WriteLine("Press [enter] to shutdown.");
            Console.ReadLine();

            server.Stop();
        }
    }
}
