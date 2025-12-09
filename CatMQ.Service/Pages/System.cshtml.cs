using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class SystemModel(ILogger<SystemModel> logger) : BasePageModel
    {
        private readonly ILogger<SystemModel> _logger = logger;
        public ProcessInfoViewModel ProcessInfo { get; private set; } = new();
        public string ApplicationVersion { get; private set; } = string.Empty;
        public List<ModuleInfo> Modules { get; set; } = new();

        public void OnGet()
        {
            try
            {
                ApplicationVersion = string.Join('.', (Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "0.0.0.0").Split('.').Take(3)); //Major.Minor.Patch

                ProcessInfo = BuildProcessInfo();

                var assembly = Assembly.GetExecutingAssembly();

                if (assembly == null || assembly.Location == null)
                {
                    return;
                }

                string? path = Path.GetDirectoryName(assembly.Location);
                if (path == null)
                {
                    return;
                }

                var files = Directory.EnumerateFiles(path, "*.dll", SearchOption.TopDirectoryOnly).ToList();
                files.AddRange(Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly).ToList());

                foreach (var file in files)
                {
                    try
                    {
                        var componentAssembly = AssemblyName.GetAssemblyName(file);
                        var versionInfo = FileVersionInfo.GetVersionInfo(file);
                        var companyName = versionInfo.CompanyName;

                        if (componentAssembly.Version != null && companyName?.ToLower()?.Contains("networkdls") == true)
                        {
                            Modules.Add(new(componentAssembly.Name ?? "", componentAssembly.Version.ToString()));
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }
        }

        private static ProcessInfoViewModel BuildProcessInfo()
        {
            var proc = Process.GetCurrentProcess();

            var nowUtc = DateTime.UtcNow;
            var startUtc = proc.StartTime.ToUniversalTime();
            var uptime = nowUtc - startUtc;

            var gcInfo = GC.GetGCMemoryInfo();

            // Approximate average CPU usage since start
            double cpuPercent = 0;
            if (uptime.TotalSeconds > 0 && Environment.ProcessorCount > 0)
            {
                cpuPercent = proc.TotalProcessorTime.TotalSeconds
                             / uptime.TotalSeconds
                             / Environment.ProcessorCount * 100.0;
            }

            return new ProcessInfoViewModel
            {
                MachineName = Environment.MachineName,
                OSDescription = RuntimeInformation.OSDescription,
                FrameworkDescription = RuntimeInformation.FrameworkDescription,
                ProcessorCount = Environment.ProcessorCount,

                ProcessId = proc.Id,
                ProcessName = proc.ProcessName,
                StartTimeUtc = startUtc,
                Uptime = uptime,

                TotalProcessorTime = proc.TotalProcessorTime,
                UserProcessorTime = proc.UserProcessorTime,
                PrivilegedTime = proc.PrivilegedProcessorTime,
                CpuUsagePercentAvg = cpuPercent,

                WorkingSetMb = proc.WorkingSet64 / 1024d / 1024d,
                PrivateMemoryMb = proc.PrivateMemorySize64 / 1024d / 1024d,

                GcHeapSizeMb = gcInfo.HeapSizeBytes / 1024d / 1024d,
                GcAvailableMemMb = gcInfo.TotalAvailableMemoryBytes / 1024d / 1024d,

                ThreadCount = proc.Threads.Count,
                HandleCount = proc.HandleCount,

                GcCollectionGen0 = GC.CollectionCount(0),
                GcCollectionGen1 = GC.CollectionCount(1),
                GcCollectionGen2 = GC.CollectionCount(2)
            };
        }

        public class ModuleInfo
        {
            public string Name { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;

            public ModuleInfo(string name, string version)
            {
                Name = name;
                Version = version;
            }
        }

        public class ProcessInfoViewModel
        {
            // Environment / runtime
            public string MachineName { get; set; } = string.Empty;
            public string OSDescription { get; set; } = string.Empty;
            public string FrameworkDescription { get; set; } = string.Empty;
            public int ProcessorCount { get; set; }

            // Process identity
            public int ProcessId { get; set; }
            public string ProcessName { get; set; } = string.Empty;

            // Uptime / CPU
            public DateTime StartTimeUtc { get; set; }
            public TimeSpan Uptime { get; set; }
            public TimeSpan TotalProcessorTime { get; set; }
            public TimeSpan UserProcessorTime { get; set; }
            public TimeSpan PrivilegedTime { get; set; }
            public double CpuUsagePercentAvg { get; set; }

            // Memory
            public double WorkingSetMb { get; set; }
            public double PrivateMemoryMb { get; set; }
            public double GcHeapSizeMb { get; set; }
            public double GcAvailableMemMb { get; set; }

            // “Process info” bits
            public int ThreadCount { get; set; }
            public int HandleCount { get; set; }

            // GC stats
            public int GcCollectionGen0 { get; set; }
            public int GcCollectionGen1 { get; set; }
            public int GcCollectionGen2 { get; set; }
        }

    }
}