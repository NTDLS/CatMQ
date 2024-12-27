using Serilog;
using Topshelf;

namespace PrudentMessageQueueService
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                 .WriteTo.Console()
                 .MinimumLevel.Verbose()
                 .CreateLogger();

            HostFactory.Run(x =>
            {
                x.StartAutomatically(); // Start the service automatically

                x.EnableServiceRecovery(rc =>
                {
                    rc.RestartService(1); // restart the service after 1 minute
                });

                x.Service<QueuingService>(s =>
                {
                    s.ConstructUsing(hostSettings => new QueuingService(s));
                    s.WhenStarted(tc => tc.Start());
                    s.WhenStopped(tc => tc.Stop());
                });
                x.RunAsLocalSystem();

                x.SetDescription("VPS message queuing service.");
                x.SetDisplayName("VPS.MessageQueue");
                x.SetServiceName("VPS.MessageQueue");
            });
        }
    }
}
