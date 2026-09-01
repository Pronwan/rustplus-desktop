using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RustPlusDesk.Services
{
    public static class CrashReporter
    {
        public static string CrashLogsDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk",
            "CrashLogs");

        private static readonly ConcurrentQueue<string> _recentLogs = new();
        private const int MaxRecentLogs = 120;
        private static Thread? _uiThread;
        private static Dispatcher? _uiDispatcher;
        private static CancellationTokenSource? _watchdogCts;
        private static Thread? _watchdogThread;
        private static bool _isInitialized;
        private static readonly object _reportLock = new();

        // UI Freeze detection state
        private static long _lastHeartbeatTicks = Stopwatch.GetTimestamp();
        private static bool _isFrozen;
        private static string? _activeFreezeLogPath;

        public static void Initialize(Dispatcher? uiDispatcher = null)
        {
            if (_isInitialized) return;
            _isInitialized = true;

            _uiThread = Thread.CurrentThread;
            _uiDispatcher = uiDispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            try
            {
                if (!Directory.Exists(CrashLogsDirectory))
                {
                    Directory.CreateDirectory(CrashLogsDirectory);
                }
                CleanOldCrashLogs();
            }
            catch { }

            // 1. AppDomain unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    LogCrash(ex, "AppDomain.UnhandledException", e.IsTerminating);
                }
                else
                {
                    LogCrash(new Exception($"Non-exception unhandled object: {e.ExceptionObject}"), "AppDomain.UnhandledException", e.IsTerminating);
                }
            };

            // 2. Dispatcher UI unhandled exceptions
            if (Application.Current != null)
            {
                Application.Current.DispatcherUnhandledException += (s, e) =>
                {
                    var logPath = LogCrash(e.Exception, "Dispatcher.UnhandledException", isFatal: false);
                    
                    // Show interactive prompt to user so they know where the log is
                    ShowCrashDialog(e.Exception, logPath, "UI Thread Exception");
                };
            }

            // 3. TaskScheduler unobserved task exceptions
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                e.SetObserved();

                // Routine background socket closures, cancelled tasks, or thread aborts are normal cleanup
                var baseEx = e.Exception?.GetBaseException();
                if (baseEx is OperationCanceledException or TaskCanceledException or ObjectDisposedException ||
                    (baseEx is System.Net.Sockets.SocketException se && (se.SocketErrorCode == System.Net.Sockets.SocketError.OperationAborted || se.SocketErrorCode == System.Net.Sockets.SocketError.Interrupted)))
                {
                    return;
                }

                if (e.Exception != null)
                {
                    LogCrash(e.Exception, "TaskScheduler.UnobservedTaskException", isFatal: false);
                }
            };

            // 4. Start UI Freeze Watchdog
            StartFreezeWatchdog();
        }

        public static void AddRecentLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            _recentLogs.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {line.Trim()}");
            while (_recentLogs.Count > MaxRecentLogs)
            {
                _recentLogs.TryDequeue(out _);
            }
        }

        public static string LogCrash(Exception ex, string source, bool isFatal = true)
        {
            lock (_reportLock)
            {
                try
                {
                    Directory.CreateDirectory(CrashLogsDirectory);
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string fileName = $"crash_report_{timestamp}.log";
                    string filePath = Path.Combine(CrashLogsDirectory, fileName);

                    var report = BuildDiagnosticReport(ex, source, isFatal ? "FATAL CRASH" : "UNHANDLED EXCEPTION");
                    File.WriteAllText(filePath, report, Encoding.UTF8);

                    // Also append to rolling history summary
                    string historyPath = Path.Combine(CrashLogsDirectory, "crash_history.log");
                    string historyLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {(isFatal ? "FATAL" : "ERROR")} - {ex.GetType().Name}: {ex.Message} -> {fileName}{Environment.NewLine}";
                    File.AppendAllText(historyPath, historyLine, Encoding.UTF8);

                    return filePath;
                }
                catch
                {
                    return Path.Combine(CrashLogsDirectory, "crash_report.log");
                }
            }
        }

        public static string LogFreeze(TimeSpan freezeDuration, string? extraDiagnostic = null)
        {
            lock (_reportLock)
            {
                try
                {
                    Directory.CreateDirectory(CrashLogsDirectory);
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string fileName = $"freeze_report_{timestamp}.log";
                    string filePath = Path.Combine(CrashLogsDirectory, fileName);

                    var report = BuildDiagnosticReport(
                        null, 
                        "UiFreezeWatchdog", 
                        $"UI HANG / FREEZE DETECTED ({freezeDuration.TotalSeconds:F1}s)",
                        extraDiagnostic);

                    File.WriteAllText(filePath, report, Encoding.UTF8);

                    string historyPath = Path.Combine(CrashLogsDirectory, "crash_history.log");
                    string historyLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [UiFreezeWatchdog] UI FREEZE ({freezeDuration.TotalSeconds:F1}s) -> {fileName}{Environment.NewLine}";
                    File.AppendAllText(historyPath, historyLine, Encoding.UTF8);

                    return filePath;
                }
                catch
                {
                    return Path.Combine(CrashLogsDirectory, "freeze_report.log");
                }
            }
        }

        public static void OpenCrashLogsFolder(string? selectFilePath = null)
        {
            try
            {
                Directory.CreateDirectory(CrashLogsDirectory);
                if (!string.IsNullOrEmpty(selectFilePath) && File.Exists(selectFilePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{selectFilePath}\"");
                }
                else
                {
                    Process.Start("explorer.exe", CrashLogsDirectory);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open logs folder:\n{ex.Message}\n\nFolder Path:\n{CrashLogsDirectory}", "Open Folder", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public static string? GetLatestCrashOrFreezeReport()
        {
            try
            {
                if (!Directory.Exists(CrashLogsDirectory)) return null;
                var dir = new DirectoryInfo(CrashLogsDirectory);
                var latest = dir.GetFiles("*_report_*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latest != null && latest.LastWriteTimeUtc > DateTime.UtcNow.AddHours(-24))
                {
                    return latest.FullName;
                }
            }
            catch { }
            return null;
        }

        private static void ShowCrashDialog(Exception ex, string logFilePath, string title)
        {
            try
            {
                string message = 
                    $"RustPlus Desktop encountered an unexpected error.\n\n" +
                    $"Error: {ex.GetType().Name}: {ex.Message}\n\n" +
                    $"A detailed diagnostic report has been saved to:\n{logFilePath}\n\n" +
                    $"Would you like to open the Crash Logs folder now to view or send this report?";

                var result = MessageBox.Show(
                    message,
                    $"RustPlus Desktop - {title}",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                {
                    OpenCrashLogsFolder(logFilePath);
                }
            }
            catch { }
        }

        private static void StartFreezeWatchdog()
        {
            if (_watchdogThread != null && _watchdogThread.IsAlive) return;

            _watchdogCts = new CancellationTokenSource();
            var ct = _watchdogCts.Token;

            _watchdogThread = new Thread(() =>
            {
                const int HeartbeatIntervalMs = 2000;
                const int FreezeThresholdSeconds = 8;

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        Thread.Sleep(HeartbeatIntervalMs);
                        if (ct.IsCancellationRequested) break;

                        var dispatcher = _uiDispatcher ?? Application.Current?.Dispatcher;
                        if (dispatcher == null || dispatcher.HasShutdownStarted) break;

                        long pingSentTicks = Stopwatch.GetTimestamp();

                        // Post lightweight heartbeat to UI thread dispatcher
                        try
                        {
                            dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                            {
                                Interlocked.Exchange(ref _lastHeartbeatTicks, Stopwatch.GetTimestamp());
                            }));
                        }
                        catch
                        {
                            break;
                        }

                        // Check elapsed time since last successful heartbeat
                        long lastAck = Interlocked.Read(ref _lastHeartbeatTicks);
                        double elapsedSeconds = (double)(Stopwatch.GetTimestamp() - lastAck) / Stopwatch.Frequency;

                        if (elapsedSeconds >= FreezeThresholdSeconds)
                        {
                            if (!_isFrozen)
                            {
                                _isFrozen = true;
                                var duration = TimeSpan.FromSeconds(elapsedSeconds);
                                string extra = $"UI Thread state: {_uiThread?.ThreadState}, IsAlive: {_uiThread?.IsAlive}";
                                _activeFreezeLogPath = LogFreeze(duration, extra);
                            }
                        }
                        else
                        {
                            if (_isFrozen)
                            {
                                _isFrozen = false;
                                if (!string.IsNullOrEmpty(_activeFreezeLogPath) && File.Exists(_activeFreezeLogPath))
                                {
                                    try
                                    {
                                        File.AppendAllText(_activeFreezeLogPath, $"{Environment.NewLine}=== RECOVERY ==={Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] UI Thread responded and recovered normal execution.{Environment.NewLine}", Encoding.UTF8);
                                    }
                                    catch { }
                                }
                                _activeFreezeLogPath = null;
                            }
                        }
                    }
                    catch (ThreadAbortException) { break; }
                    catch { }
                }
            })
            {
                IsBackground = true,
                Name = "UiFreezeWatchdog",
                Priority = ThreadPriority.BelowNormal
            };

            _watchdogThread.Start();
        }

        private static string BuildDiagnosticReport(Exception? ex, string source, string reportType, string? extraDetails = null)
        {
            var sb = new StringBuilder();
            var now = DateTime.Now;
            var proc = Process.GetCurrentProcess();

            sb.AppendLine("================================================================================");
            sb.AppendLine($"           RUSTPLUS DESKTOP DIAGNOSTIC & CRASH REPORT");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Report Type:       {reportType}");
            sb.AppendLine($"Event Source:      {source}");
            sb.AppendLine($"Local Timestamp:   {now:yyyy-MM-dd HH:mm:ss.fff} (UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff})");
            sb.AppendLine($"App Version:       {Assembly.GetExecutingAssembly().GetName().Version}");
            sb.AppendLine($"OS Version:        {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            sb.AppendLine($"Runtime:           .NET {Environment.Version}");
            sb.AppendLine($"Process Uptime:    {now - proc.StartTime}");
            sb.AppendLine($"Memory (Working):  {proc.WorkingSet64 / (1024 * 1024):N0} MB (Private: {proc.PrivateMemorySize64 / (1024 * 1024):N0} MB)");
            sb.AppendLine($"GC Total Memory:   {GC.GetTotalMemory(false) / (1024 * 1024):N0} MB");
            sb.AppendLine($"Active Threads:    {proc.Threads.Count}");
            sb.AppendLine($"Log Folder:        {CrashLogsDirectory}");
            sb.AppendLine("--------------------------------------------------------------------------------");

            if (!string.IsNullOrEmpty(extraDetails))
            {
                sb.AppendLine("ADDITIONAL DIAGNOSTICS:");
                sb.AppendLine(extraDetails);
                sb.AppendLine("--------------------------------------------------------------------------------");
            }

            if (ex != null)
            {
                sb.AppendLine("EXCEPTION DETAILS:");
                sb.AppendLine($"Exception Type:    {ex.GetType().FullName}");
                sb.AppendLine($"Message:           {ex.Message}");
                sb.AppendLine($"HResult:           0x{ex.HResult:X8}");
                if (ex.TargetSite != null)
                {
                    sb.AppendLine($"Target Site:       {ex.TargetSite.DeclaringType?.FullName}.{ex.TargetSite.Name}");
                }
                sb.AppendLine();
                sb.AppendLine("STACK TRACE:");
                sb.AppendLine(ex.StackTrace ?? "(No stack trace available)");

                // Inner exceptions
                var inner = ex.InnerException;
                int innerIdx = 1;
                while (inner != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"--- Inner Exception #{innerIdx} ---");
                    sb.AppendLine($"Type:       {inner.GetType().FullName}");
                    sb.AppendLine($"Message:    {inner.Message}");
                    sb.AppendLine($"Stack:      {inner.StackTrace}");
                    inner = inner.InnerException;
                    innerIdx++;
                }
                sb.AppendLine("--------------------------------------------------------------------------------");
            }

            // Recent in-memory logs
            sb.AppendLine("RECENT APPLICATION LOGS (Chronological):");
            if (_recentLogs.IsEmpty)
            {
                sb.AppendLine("(No recent logs recorded)");
            }
            else
            {
                foreach (var log in _recentLogs)
                {
                    sb.AppendLine(log);
                }
            }
            sb.AppendLine("================================================================================");
            sb.AppendLine("                 END OF DIAGNOSTIC REPORT");
            sb.AppendLine("================================================================================");

            return sb.ToString();
        }

        private static void CleanOldCrashLogs()
        {
            try
            {
                if (!Directory.Exists(CrashLogsDirectory)) return;
                var dir = new DirectoryInfo(CrashLogsDirectory);
                var files = dir.GetFiles("*_report_*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                // Keep newest 25 reports, delete older ones or files older than 30 days
                for (int i = 0; i < files.Count; i++)
                {
                    if (i >= 25 || files[i].LastWriteTimeUtc < DateTime.UtcNow.AddDays(-30))
                    {
                        try { files[i].Delete(); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
