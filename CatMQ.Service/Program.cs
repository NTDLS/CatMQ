using Serilog;
#if WINDOWS
using Topshelf;
#endif

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

#if WINDOWS
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
#else
            var _shutdownEvent = new ManualResetEvent(false);

            var service = new QueuingService();
            service.Start();

            Console.WriteLine("Service started. Press Ctrl+C to exit.");

            // Capture Ctrl+C (SIGINT) or other shutdown signals
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                Console.WriteLine("Shutdown signal received.");
                _shutdownEvent.Set();
                eventArgs.Cancel = true; // Prevent the process from terminating immediately
            };


            _shutdownEvent.WaitOne();
            service.Stop();
#endif
        }
    }
}
