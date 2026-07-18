using System;

namespace SysLoja.PrinterTool.Models
{
    public enum LogLevel { Debug, Info, Success, Warning, Error }

    public enum LogCategory { Registro, Rpc, Smb, Rede, Firewall, Impressoras, Drivers, Servicos, Spooler, Sistema }

    /// <summary>
    /// Entrada imutavel do log tecnico. Nunca deve ser criada com Level=Success
    /// sem que a operacao correspondente tenha sido validada de fato.
    /// </summary>
    public sealed class LogEntry
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public TimeSpan? Duration { get; init; }
        public LogLevel Level { get; init; }
        public LogCategory Category { get; init; }
        public string Operation { get; init; } = string.Empty;
        public string Result { get; init; } = string.Empty;
        public string? ErrorCode { get; init; }
        public string FriendlyMessage { get; init; } = string.Empty;
        public string? TechnicalMessage { get; init; }
        public string? ExceptionDetails { get; init; }
        public string? RegistryPath { get; init; }
        public string? OldValue { get; init; }
        public string? NewValue { get; init; }

        public string ToPlainLine()
        {
            var dur = Duration.HasValue ? $" | {Duration.Value.TotalMilliseconds:F0}ms" : "";
            var err = string.IsNullOrWhiteSpace(ErrorCode) ? "" : $" | Codigo: {ErrorCode}";
            var reg = string.IsNullOrWhiteSpace(RegistryPath) ? "" : $" | Chave: {RegistryPath} ({OldValue} -> {NewValue})";
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] [{Category}]{dur} {Operation} => {Result}{err}{reg} :: {FriendlyMessage}";
        }
    }
}
