using System;
using System.Collections.Generic;
using Microsoft.Win32;
using SysLoja.PrinterTool.Models;

namespace SysLoja.PrinterTool.Services
{
    public sealed class RpcFixItem
    {
        public string SubKey { get; init; } = string.Empty;
        public string ValueName { get; init; } = string.Empty;
        public int DesiredValue { get; init; }
        public bool Success { get; set; }
        public object? OldValue { get; set; }
        public object? ReadBackValue { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Aplica as 5 correcoes de Registro relacionadas a RPC / Point-and-Print /
    /// Terminal Services / isolamento de driver. Sempre cria backup antes de
    /// escrever e revalida cada valor apos a escrita.
    ///
    /// Nota de correcao tecnica: PrintDriverIsolationExecutionPolicy fica em
    /// HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Printers, e nao em
    /// HKLM\SYSTEM\CurrentControlSet\Control\Print. Gravar no caminho errado
    /// nao teria efeito nenhum no comportamento real do spooler.
    /// </summary>
    public sealed class RpcFixService
    {
        private const string PrintControlPath = @"SYSTEM\CurrentControlSet\Control\Print";
        private const string PointAndPrintPath = @"SOFTWARE\Policies\Microsoft\Windows NT\Printers\PointAndPrint";
        private const string TerminalServicesPath = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";
        private const string PrintersPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows NT\Printers";

        private readonly RegistryService _registryService;
        private readonly BackupService _backupService;
        private readonly LogService _log = LogService.Instance;

        public RpcFixService(RegistryService registryService, BackupService backupService)
        {
            _registryService = registryService;
            _backupService = backupService;
        }

        public IReadOnlyList<(string SubKey, string ValueName, int DesiredValue)> GetTargets() => new List<(string, string, int)>
        {
            (PrintControlPath, "RpcAuthnLevelPrivacyEnabled", 0),
            (PrintControlPath, "CopyFilesPolicy", 1),
            (PointAndPrintPath, "RestrictDriverInstallationToAdministrators", 0),
            (TerminalServicesPath, "fEnablePrintRDR", 1),
            (PrintersPolicyPath, "PrintDriverIsolationExecutionPolicy", 1),
        };

        public (bool OverallSuccess, List<RpcFixItem> Items, RegistryBackupSet Backup) ApplyAllFixes()
        {
            if (!RegistryService.IsRunningAsAdministrator())
            {
                _log.Log(Models.LogLevel.Error, LogCategory.Registro, "Aplicar correcoes RPC",
                    "Operacao abortada: a ferramenta nao esta sendo executada como Administrador.",
                    result: "Bloqueado");
                throw new InvalidOperationException("E necessario executar como Administrador para aplicar as correcoes de RPC.");
            }

            var targets = GetTargets();
            var backupTargets = new List<(string, string, RegistryValueKind)>();
            foreach (var t in targets) backupTargets.Add((t.SubKey, t.ValueName, RegistryValueKind.DWord));

            var backup = _backupService.CreateBackup("Backup automatico antes de Aplicar Correcoes RPC", backupTargets);

            var items = new List<RpcFixItem>();
            bool overallSuccess = true;

            foreach (var t in targets)
            {
                var snapshot = backup.Snapshots.Find(s => s.SubKey == t.SubKey && s.ValueName == t.ValueName);
                var item = new RpcFixItem { SubKey = t.SubKey, ValueName = t.ValueName, DesiredValue = t.DesiredValue, OldValue = snapshot?.PreviousValue };

                var start = DateTime.Now;
                var (success, readBack, error) = _registryService.WriteAndVerifyDword(t.SubKey, t.ValueName, t.DesiredValue);
                var duration = DateTime.Now - start;

                item.Success = success;
                item.ReadBackValue = readBack;
                item.Error = error;

                if (success)
                {
                    _log.Log(Models.LogLevel.Success, LogCategory.Rpc, $"Aplicar {t.ValueName}",
                        $"Valor {t.ValueName} configurado e validado com sucesso.", result: "Corrigido",
                        registryPath: $@"HKLM\{t.SubKey}", oldValue: item.OldValue?.ToString() ?? "(nao definido)",
                        newValue: readBack?.ToString(), duration: duration);
                }
                else
                {
                    overallSuccess = false;
                    _log.Log(Models.LogLevel.Error, LogCategory.Rpc, $"Aplicar {t.ValueName}",
                        $"Falha ao configurar ou validar {t.ValueName}.", result: "Falha", technicalMessage: error,
                        registryPath: $@"HKLM\{t.SubKey}", oldValue: item.OldValue?.ToString(), newValue: readBack?.ToString(), duration: duration);
                }

                items.Add(item);
            }

            return (overallSuccess, items, backup);
        }
    }
}
