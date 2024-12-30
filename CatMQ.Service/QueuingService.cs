using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;
using Serilog;
using static CatMQ.Service.Configs;

namespace CatMQ.Service
{
    public class QueuingService
    {
        private CMqServer? _mqServer;

        public void Start()
        {
            var serviceConfiguration = Configs.Read(ConfigFile.Service, new ServiceConfiguration());

            _mqServer = new CMqServer(new CMqServerConfiguration
            {
                PersistencePath = serviceConfiguration.DataPath,
                AsynchronousAcknowledgment = serviceConfiguration.AsynchronousAcknowledgment,
                InitialReceiveBufferSize = serviceConfiguration.InitialReceiveBufferSize,
                MaxReceiveBufferSize = serviceConfiguration.MaxReceiveBufferSize,
                AcknowledgmentTimeoutSeconds = serviceConfiguration.AcknowledgmentTimeoutSeconds,
                ReceiveBufferGrowthRate = serviceConfiguration.ReceiveBufferGrowthRate,
            });
            _mqServer.OnLog += MqServer_OnLog;

            Log.Verbose($"Starting message queue service on port: {serviceConfiguration.QueuePort}.");
            _mqServer.Start(serviceConfiguration.QueuePort);
            Log.Verbose("Message queue service started.");

            if (serviceConfiguration.EnableWebUI && serviceConfiguration.WebUIURL != null)
            {
                var builder = WebApplication.CreateBuilder();

                builder.Services.AddAuthentication("CookieAuth")
                    .AddCookie("CookieAuth", options =>
                    {
                        options.LoginPath = "/Login";
                    });

                builder.Services.AddSingleton(_mqServer);
                builder.Services.AddSingleton(serviceConfiguration);

                // Add services to the container.
                builder.Services.AddRazorPages();

                builder.WebHost.UseUrls(serviceConfiguration.WebUIURL);

                var app = builder.Build();

                // Configure the HTTP request pipeline.
                if (!app.Environment.IsDevelopment())
                {
                    app.UseExceptionHandler("/Error");
                    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                    app.UseHsts();
                }

                //app.UseHttpsRedirection();
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapStaticAssets();
                app.MapRazorPages()
                   .WithStaticAssets();

                Log.Verbose("Starting web service.");
                app.RunAsync();
            }
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

        private void MqServer_OnLog(CMqServer server, ErrorLevel errorLevel, string message, Exception? ex = null)
        {
            switch (errorLevel)
            {
                case ErrorLevel.Verbose:
                    if (ex != null)
                        Log.Verbose(ex, message);
                    else
                        Log.Verbose(message);
                    break;
                case ErrorLevel.Debug:
                    if (ex != null)
                        Log.Debug(ex, message);
                    else
                        Log.Debug(message);
                    break;
                case ErrorLevel.Information:
                    if (ex != null)
                        Log.Information(ex, message);
                    else
                        Log.Information(message);
                    break;
                case ErrorLevel.Warning:
                    if (ex != null)
                        Log.Warning(ex, message);
                    else
                        Log.Warning(message);
                    break;
                case ErrorLevel.Error:
                    if (ex != null)
                        Log.Error(ex, message);
                    else
                        Log.Error(message);
                    break;
                case ErrorLevel.Fatal:
                    if (ex != null)
                        Log.Fatal(ex, message);
                    else
                        Log.Error(message);
                    break;
            }
        }
    }
}
