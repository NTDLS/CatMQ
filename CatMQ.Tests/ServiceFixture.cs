using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;

namespace CatMQ.Tests
{
    /// <summary>
    /// Runs the real CatMQ service in its own process so that the HTTP API can be tested from the outside. It runs
    /// from a temporary copy, because the service keeps its configuration and data next to the executable.
    /// </summary>
    public class ServiceFixture : IDisposable
    {
        public const string ApiKey = "test-api-key";

        private readonly Process _process;
        private readonly string _folder = Path.Join(Path.GetTempPath(), "CatMQ.Tests.Service", Guid.NewGuid().ToString("N"));

        public int QueuePort { get; } = GetFreePort();
        public int WebPort { get; } = GetFreePort();
        public HttpClient Http { get; }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void CopyFolder(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Join(target, Path.GetFileName(file)));
            }
            foreach (var folder in Directory.GetDirectories(source))
            {
                var name = Path.GetFileName(folder);
                if (name is not "data" and not "config")
                {
                    CopyFolder(folder, Path.Join(target, name));
                }
            }
        }

        public ServiceFixture()
        {
            //The test output folder is CatMQ.Tests/bin/<configuration>/<framework>, and the service has the same layout.
            var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            var root = testOutput.Parent!.Parent!.Parent!.Parent!.FullName;
            var serviceOutput = Path.Join(root, "CatMQ.Service", "bin", testOutput.Parent.Name, testOutput.Name);

            CopyFolder(serviceOutput, _folder);

            Directory.CreateDirectory(Path.Join(_folder, "config"));
            File.WriteAllText(Path.Join(_folder, "config", "service.json"), JsonSerializer.Serialize(new
            {
                QueuePort,
                WebListenURL = $"http://127.0.0.1:{WebPort}",
                EnableWebApi = true,
                EnableWebUI = false,
                AcknowledgmentTimeoutSeconds = 30
            }));
            File.WriteAllText(Path.Join(_folder, "config", "accounts.json"), JsonSerializer.Serialize(new[]
            {
                new { Id = Guid.NewGuid(), Username = "test", ApiKeys = new[] { new { Id = Guid.NewGuid(), Key = ApiKey } } }
            }));

            _process = Process.Start(new ProcessStartInfo("dotnet", $"\"{Path.Join(_folder, "CatMQ.Service.dll")}\"")
            {
                WorkingDirectory = _folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new Exception("Failed to start the service.");

            _process.OutputDataReceived += (_, _) => { };
            _process.ErrorDataReceived += (_, _) => { };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{WebPort}/api/v1/"), Timeout = TimeSpan.FromSeconds(90) };
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

            if (Wait.Until(IsHealthy, 30) == false)
            {
                Dispose();
                throw new Exception("The service did not start.");
            }
        }

        private bool IsHealthy()
        {
            try
            {
                return Http.GetAsync("health").GetAwaiter().GetResult().IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            Http.Dispose();

            try
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
                //It already exited.
            }

            _process.Dispose();

            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException)
            {
                //Temporary files that are still locked are not worth failing a test run over.
            }

            GC.SuppressFinalize(this);
        }
    }
}
