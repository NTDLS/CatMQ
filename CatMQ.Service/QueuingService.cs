using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;
using Serilog;
using System.Text.Json.Serialization;

namespace CatMQ.Service
{
    public class QueuingService
    {
        private CMqServer? _mqServer;

        public void Start()
        {
            var serviceConfiguration = Configs.GetServiceConfig();

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

            if (serviceConfiguration.WebListenURL != null && (serviceConfiguration.EnableWebApi || serviceConfiguration.EnableWebUI))
            {
                var options = new WebApplicationOptions
                {
                    ContentRootPath = AppContext.BaseDirectory
                };

                var builder = WebApplication.CreateBuilder(options);

                builder.Services.AddAuthentication("CookieAuth")
                    .AddCookie("CookieAuth", options =>
                    {
                        options.Cookie.Name = "CatMQAuth";
                        options.LoginPath = "/Login";
                    });

                builder.Services.AddSingleton(_mqServer);
                builder.Services.AddSingleton(serviceConfiguration);

                if (serviceConfiguration.EnableWebUI)
                {
                    builder.Services.AddRazorPages();
                }

                if (serviceConfiguration.EnableWebApi)
                {
                    builder.Services.AddControllers()
                        .AddJsonOptions(options =>
                    {
                        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                    });
                }

                builder.WebHost.UseUrls(serviceConfiguration.WebListenURL);


                var app = builder.Build();

                app.UseRouting();

                if (serviceConfiguration.EnableWebApi)
                {
                    app.UseMiddleware<ApiKeyMiddleware>();
                    app.MapControllerRoute(name: "default", pattern: "/{controller=api}");
                }

                // Configure the HTTP request pipeline.
                if (!app.Environment.IsDevelopment())
                {
                    app.UseExceptionHandler("/Error");
                    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                    app.UseHsts();
                }

                //app.UseHttpsRedirection();
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapStaticAssets();
                app.UseStaticFiles();

                if (serviceConfiguration.EnableWebUI)
                {
                    app.MapRazorPages().WithStaticAssets();
                }

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

        private void MqServer_OnLog(CMqServer server, CMqErrorLevel errorLevel, string message, Exception? ex = null)
        {
            switch (errorLevel)
            {
                case CMqErrorLevel.Verbose:
                    if (ex != null)
                        Log.Verbose(ex, message);
                    else
                        Log.Verbose(message);
                    break;
                case CMqErrorLevel.Debug:
                    if (ex != null)
                        Log.Debug(ex, message);
                    else
                        Log.Debug(message);
                    break;
                case CMqErrorLevel.Information:
                    if (ex != null)
                        Log.Information(ex, message);
                    else
                        Log.Information(message);
                    break;
                case CMqErrorLevel.Warning:
                    if (ex != null)
                        Log.Warning(ex, message);
                    else
                        Log.Warning(message);
                    break;
                case CMqErrorLevel.Error:
                    if (ex != null)
                        Log.Error(ex, message);
                    else
                        Log.Error(message);
                    break;
                case CMqErrorLevel.Fatal:
                    if (ex != null)
                        Log.Fatal(ex, message);
                    else
                        Log.Error(message);
                    break;
            }
        }
    }
}
