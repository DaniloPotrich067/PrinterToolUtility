using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using SysLoja.PrinterTool.Models;

namespace SysLoja.PrinterTool.Services
{
    /// <summary>
    /// Fonte unica de verdade para logging tecnico. Singleton thread-safe.
    /// Gravacao em arquivo roda em fila propria; a UI nunca bloqueia por I/O.
    /// </summary>
    public sealed class LogService
    {
        private static readonly Lazy<LogService> _instance = new(() => new LogService());
        public static LogService Instance => _instance.Value;

        private readonly string _logFolder;
        private readonly string _logFile;
        private readonly BlockingCollection<LogEntry> _writeQueue = new();
        private readonly object _uiLock = new();

        public ObservableCollection<LogEntry> Entries { get; } = new();

        private LogService()
        {
            _logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SysLoja", "PrinterTool", "Logs");
            Directory.CreateDirectory(_logFolder);
            _logFile = Path.Combine(_logFolder, $"PrinterTool_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            CleanupOldLogs(30);

            var writerThread = new System.Threading.Thread(ProcessWriteQueue) { IsBackground = true };
            writerThread.Start();
        }

        public string LogFolder => _logFolder;
        public string LogFile => _logFile;

        private void CleanupOldLogs(int retentionDays)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-retentionDays);
                foreach (var file in Directory.GetFiles(_logFolder, "PrinterTool_*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
                }
            }
            catch { /* limpeza de logs antigos nunca deve impedir inicializacao */ }
        }

        public void Log(LogLevel level, LogCategory category, string operation, string friendlyMessage,
            string? result = null, string? errorCode = null, string? technicalMessage = null,
            string? exceptionDetails = null, string? registryPath = null, string? oldValue = null,
            string? newValue = null, TimeSpan? duration = null)
        {
            var entry = new LogEntry
            {
                Level = level,
                Category = category,
                Operation = operation,
                Result = result ?? level.ToString(),
                ErrorCode = errorCode,
                FriendlyMessage = friendlyMessage,
                TechnicalMessage = technicalMessage,
                ExceptionDetails = exceptionDetails,
                RegistryPath = registryPath,
                OldValue = oldValue,
                NewValue = newValue,
                Duration = duration
            };

            _writeQueue.Add(entry);

            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    lock (_uiLock)
                    {
                        Entries.Add(entry);
                        if (Entries.Count > 5000) Entries.RemoveAt(0);
                    }
                });
            }
        }

        private void ProcessWriteQueue()
        {
            foreach (var entry in _writeQueue.GetConsumingEnumerable())
            {
                try { File.AppendAllText(_logFile, entry.ToPlainLine() + Environment.NewLine, Encoding.UTF8); }
                catch { /* falha de disco nao pode derrubar a thread de log */ }
            }
        }

        public string ExportAsText(System.Collections.Generic.IEnumerable<LogEntry>? filtered = null)
        {
            var source = filtered ?? Entries;
            return string.Join(Environment.NewLine, source.Select(e => e.ToPlainLine()));
        }

        public string ExportAsHtml(System.Collections.Generic.IEnumerable<LogEntry>? filtered = null)
        {
            var source = filtered ?? Entries;
            var sb = new StringBuilder();
            sb.AppendLine("<html><head><meta charset='utf-8'><style>");
            sb.AppendLine("body{font-family:Consolas,monospace;background:#111827;color:#e5e7eb;padding:16px}");
            sb.AppendLine("table{width:100%;border-collapse:collapse;font-size:12px}");
            sb.AppendLine("th,td{border:1px solid #374151;padding:6px;text-align:left;vertical-align:top}");
            sb.AppendLine(".Success{color:#22c55e}.Warning{color:#f59e0b}.Error{color:#ef4444}.Info{color:#38bdf8}.Debug{color:#9ca3af}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h2>SysLoja - Relatorio de Log</h2>");
            sb.AppendLine("<table><tr><th>Data/Hora</th><th>Nivel</th><th>Categoria</th><th>Operacao</th><th>Resultado</th><th>Codigo</th><th>Mensagem</th><th>Chave/Valor</th></tr>");
            foreach (var e in source)
            {
                var reg = string.IsNullOrWhiteSpace(e.RegistryPath) ? "" : $"{e.RegistryPath}<br/>{e.OldValue} -&gt; {e.NewValue}";
                sb.AppendLine($"<tr class='{e.Level}'><td>{e.Timestamp:yyyy-MM-dd HH:mm:ss}</td><td>{e.Level}</td><td>{e.Category}</td><td>{System.Net.WebUtility.HtmlEncode(e.Operation)}</td><td>{System.Net.WebUtility.HtmlEncode(e.Result)}</td><td>{e.ErrorCode}</td><td>{System.Net.WebUtility.HtmlEncode(e.FriendlyMessage)}</td><td>{reg}</td></tr>");
            }
            sb.AppendLine("</table></body></html>");
            return sb.ToString();
        }
    }
}
