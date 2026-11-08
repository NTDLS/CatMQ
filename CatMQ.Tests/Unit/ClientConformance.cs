using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// Runs the Python and JavaScript clients' conformance runners against the real service, and checks that each passed
    /// every scenario in clients/conformance/scenarios.json. A client is skipped when its runtime is not installed.
    /// </summary>
    public class ClientConformance(ServiceFixture service)
        : IClassFixture<ServiceFixture>
    {
        private static string ClientsFolder()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && Directory.Exists(Path.Join(folder.FullName, "clients", "conformance")) == false)
            {
                folder = folder.Parent;
            }
            return folder == null
                ? throw new DirectoryNotFoundException("The clients folder was not found above the test output.")
                : Path.Join(folder.FullName, "clients");
        }

        private static string? FindOnPath(params string[] names)
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", "" } : new[] { "" };

            foreach (var name in names)
            {
                foreach (var path in paths)
                {
                    foreach (var extension in extensions)
                    {
                        var candidate = Path.Join(path, name + extension);
                        //The Windows Store's python.exe is a stub that opens the store, so it is not a real Python.
                        if (File.Exists(candidate) && candidate.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase) == false)
                        {
                            return candidate;
                        }
                    }
                }
            }
            return null;
        }

        [Theory(DisplayName = "Clients: each client passes every shared conformance scenario.", Timeout = 300_000)]
        [InlineData("python")]
        [InlineData("javascript")]
        public async Task TestConformance(string client)
        {
            var clients = ClientsFolder();

            var (runtime, arguments, workingFolder) = client switch
            {
                "python" => (FindOnPath("python3", "python"), "-I -B tests/conformance.py", Path.Join(clients, "python")),
                "javascript" => (FindOnPath("node"), "test/conformance.mjs", Path.Join(clients, "javascript")),
                _ => throw new ArgumentException(client)
            };

            if (runtime == null)
            {
                Assert.Skip($"The {client} runtime is not installed.");
            }

            var start = new ProcessStartInfo(runtime, arguments)
            {
                WorkingDirectory = workingFolder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.Environment["CATMQ_URL"] = service.WebBaseUrl;
            start.Environment["CATMQ_API_KEY"] = ServiceFixture.ApiKey;
            start.Environment["CATMQ_WEBHOOK_HOST"] = "127.0.0.1";

            using var process = Process.Start(start)!;
            var errors = new StringBuilder();
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errors) errors.AppendLine(e.Data); };
            process.BeginErrorReadLine();

            var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            var results = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(o => o.StartsWith('{'))
                .Select(o => JsonDocument.Parse(o).RootElement.Clone())
                .ToDictionary(o => o.GetProperty("scenario").GetString()!);

            using var scenarios = JsonDocument.Parse(File.ReadAllText(Path.Join(clients, "conformance", "scenarios.json")));
            var failures = new List<string>();
            foreach (var scenario in scenarios.RootElement.GetProperty("scenarios").EnumerateArray())
            {
                var id = scenario.GetProperty("id").GetString()!;
                if (results.TryGetValue(id, out var result) == false)
                {
                    failures.Add($"{id}: no result.");
                }
                else if (result.GetProperty("passed").GetBoolean() == false)
                {
                    failures.Add($"{id}: {result.GetProperty("error").GetString()}");
                }
            }

            Assert.True(failures.Count == 0, $"The {client} client failed:\n{string.Join("\n", failures)}\n\nstderr:\n{errors}");
            Assert.Equal(0, process.ExitCode);
        }
    }
}
