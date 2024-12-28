using NTDLS.CatMQServer;
using NTDLS.CatMQShared;
using Serilog;
using System.Reflection;
using Topshelf.ServiceConfigurators;

namespace CatMQService
{
    public class QueuingService
    {
        private CMqServer? _mqServer;

        public QueuingService(ServiceConfigurator<QueuingService> s)
        {
        }

        public void Start()
        {
            var builder = WebApplication.CreateBuilder();
            var configuration = builder.Configuration;

            builder.Services.AddAuthentication("CookieAuth")
                .AddCookie("CookieAuth", options =>
                {
                    options.LoginPath = "/Login";
                });

            var persistencePath = configuration.GetValue<string>("MqServer:DataPath");

            if (string.IsNullOrEmpty(persistencePath))
            {
                var executablePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                persistencePath = Path.Join(executablePath, "Data");
                Directory.CreateDirectory(persistencePath);
            }

            _mqServer = new CMqServer(new CMqServerConfiguration
            {
                PersistencePath = persistencePath
            });
            _mqServer.OnException += MqServer_OnException;


            int portNumber = configuration.GetValue<int>("MqServer:Port");
            Log.Verbose($"Starting message queue service on port: {portNumber}.");
            _mqServer.Start(portNumber);
            Log.Verbose("Message queue service started.");

            builder.Services.AddSingleton<CMqServer>(_mqServer);

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

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapRazorPages()
               .WithStaticAssets();

            Log.Verbose("Starting web service.");
            app.RunAsync();
        }

        public void Stop()
        {
            if (_mqServer != null)
            {
                Log.Verbose("Stopping message queue service.");
                _mqServer.Stop();
                Log.Verbose("Message queue service stopped.");
            }
        }

        private void MqServer_OnException(CMqServer server, CMqQueueConfiguration? queue, Exception ex)
        {
            Log.Error(ex, "MqServer_OnException");
        }
    }
}
