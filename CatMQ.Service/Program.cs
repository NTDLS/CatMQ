using Serilog;
using Topshelf;

namespace CatMQ.Service
{
    internal class Program
    {
        static void Main()
        {
            // Load appsettings.json
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Configure Serilog using the configuration
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            HostFactory.Run(x =>
            {
                x.StartAutomatically();

                x.EnableServiceRecovery(rc =>
                {
                    rc.RestartService(1);
                });

                x.Service<QueuingService>(s =>
                {
                    s.ConstructUsing(hostSettings => new QueuingService());
                    s.WhenStarted(tc => tc.Start());
                    s.WhenStopped(tc => tc.Stop());
                });
                x.RunAsLocalSystem();

                x.SetDescription("A high-performance and reliable persistent message queue designed for efficient inter-process communication, task queuing, load balancing, and data buffering over TCP/IP.");
                x.SetDisplayName("CatMQ Message Queuing");
                x.SetServiceName("CatMQService");
            });
        }
    }
}
