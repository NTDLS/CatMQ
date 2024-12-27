using NTDLS.PrudentMessageQueueLibrary;
using NTDLS.PrudentMessageQueueServer;
using Serilog;
using Topshelf.ServiceConfigurators;

namespace PrudentMessageQueueService
{
    public class QueuingService
    {
        private readonly PMqServer _mqServer = new();

        public QueuingService(ServiceConfigurator<QueuingService> s)
        {
            _mqServer = new PMqServer(new PMqServerConfiguration
            {
                PersistencePath = "C:\\DropZone\\MqServer"
            });
            _mqServer.OnException += MqServer_OnException;
        }

        public void Start()
        {
            var builder = WebApplication.CreateBuilder();
            var configuration = builder.Configuration;

            int portNumber = configuration.GetValue<int>("MqServer:Port");

            Log.Verbose("Starting message queue service.");
            _mqServer.Start(portNumber);
            Log.Verbose("Message queue service started.");


            builder.Services.AddSingleton<PMqServer>(_mqServer);

            // Add services to the container.
            builder.Services.AddRazorPages();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapRazorPages()
               .WithStaticAssets();

            Log.Verbose("Starting web service.");
            app.Run();
        }

        public void Stop()
        {
            Log.Verbose("Stopping message queue service.");
            _mqServer.Stop();
            Log.Verbose("Message queue service stopped.");
        }

        private void MqServer_OnException(PMqServer server, PMqQueueConfiguration? queue, Exception ex)
        {
            Log.Error(ex, "MqServer_OnException");
        }
    }
}
