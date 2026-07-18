using System;
using System.Collections.Generic;

namespace SysLoja.PrinterTool.Models
{
    public sealed class ServiceCheckResult
    {
        public string ServiceName { get; init; } = string.Empty;
        public string FriendlyName { get; init; } = string.Empty;
        public string Status { get; init; } = "Desconhecido";
        public string StartMode { get; init; } = "Desconhecido";
        public bool IsHealthy { get; init; }
        public string Impact { get; init; } = string.Empty;
    }

    public sealed class PrinterCheckResult
    {
        public string Name { get; init; } = string.Empty;
        public string PortName { get; init; } = string.Empty;
        public string DriverName { get; init; } = string.Empty;
        public bool IsOffline { get; init; }
        public bool IsPaused { get; init; }
        public bool IsShared { get; init; }
        public int JobsStuck { get; init; }
        public List<string> Issues { get; init; } = new();
    }

    public sealed class NetworkCheckResult
    {
        public bool PingOk { get; init; }
        public bool DnsOk { get; init; }
        public bool Port445Ok { get; init; }
        public bool Port139Ok { get; init; }
        public bool SmbShareOk { get; init; }
        public string Target { get; init; } = string.Empty;
        public string Details { get; init; } = string.Empty;
    }

    public sealed class FirewallCheckResult
    {
        public bool FileAndPrinterSharingEnabled { get; init; }
        public string Details { get; init; } = string.Empty;
    }

    public sealed class EventLogFinding
    {
        public DateTime TimeCreated { get; init; }
        public string Source { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string Classification { get; init; } = string.Empty;
    }

    public sealed class DiagnosticReport
    {
        public DateTime ExecutedAt { get; init; } = DateTime.Now;
        public List<ServiceCheckResult> Services { get; init; } = new();
        public List<PrinterCheckResult> Printers { get; init; } = new();
        public NetworkCheckResult? Network { get; set; }
        public FirewallCheckResult? Firewall { get; set; }
        public List<EventLogFinding> EventFindings { get; init; } = new();
        public List<HealthItem> HealthItems { get; init; } = new();
    }
}
