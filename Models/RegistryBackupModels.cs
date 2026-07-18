using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace SysLoja.PrinterTool.Models
{
    public sealed class RegistryValueSnapshot
    {
        public string Hive { get; init; } = "HKLM";
        public string SubKey { get; init; } = string.Empty;
        public string ValueName { get; init; } = string.Empty;
        public RegistryValueKind Kind { get; init; } = RegistryValueKind.DWord;
        public object? PreviousValue { get; init; }
        public bool KeyExistedBefore { get; init; }
        public bool ValueExistedBefore { get; init; }
    }

    public sealed class RegistryBackupSet
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public DateTime CreatedAt { get; init; } = DateTime.Now;
        public string Description { get; init; } = string.Empty;
        public List<RegistryValueSnapshot> Snapshots { get; init; } = new();
    }
}
