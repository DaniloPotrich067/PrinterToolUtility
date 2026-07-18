using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using SysLoja.PrinterTool.Models;

namespace SysLoja.PrinterTool.Services
{
    /// <summary>
    /// Cria, persiste e restaura backups de valores de Registro alterados
    /// pela ferramenta. Cada conjunto de correcoes gera um RegistryBackupSet
    /// serializado em disco antes de qualquer escrita.
    /// </summary>
    public sealed class BackupService
    {
        private readonly RegistryService _registryService;
        private readonly LogService _log = LogService.Instance;
        private readonly string _backupFolder;

        public BackupService(RegistryService registryService)
        {
            _registryService = registryService;
            _backupFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SysLoja", "PrinterTool", "Backups");
            Directory.CreateDirectory(_backupFolder);
        }

        public string BackupFolder => _backupFolder;

        public RegistryBackupSet CreateBackup(string description, IEnumerable<(string SubKey, string ValueName, RegistryValueKind Kind)> targets)
        {
            var set = new RegistryBackupSet { Description = description };
            foreach (var (subKey, valueName, kind) in targets)
            {
                set.Snapshots.Add(_registryService.CaptureSnapshot(subKey, valueName, kind));
            }

            var path = Path.Combine(_backupFolder, $"backup_{set.CreatedAt:yyyyMMdd_HHmmss}_{set.Id}.json");
            var json = JsonSerializer.Serialize(set, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);

            _log.Log(Models.LogLevel.Info, LogCategory.Registro, "Criar backup de Registro",
                $"Backup criado com {set.Snapshots.Count} valores antes de aplicar correcoes.",
                result: "Backup criado", registryPath: path);

            return set;
        }

        public List<string> ListBackupFiles() => new(Directory.GetFiles(_backupFolder, "backup_*.json"));

        public RegistryBackupSet? LoadBackup(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<RegistryBackupSet>(json);
            }
            catch (Exception ex)
            {
                _log.Log(Models.LogLevel.Error, LogCategory.Registro, "Carregar backup",
                    "Falha ao carregar arquivo de backup.", result: "Falha", technicalMessage: ex.Message,
                    exceptionDetails: ex.ToString());
                return null;
            }
        }

        public (int Restored, int Failed) RestoreBackup(RegistryBackupSet set)
        {
            int restored = 0, failed = 0;
            foreach (var snapshot in set.Snapshots)
            {
                var ok = _registryService.RestoreSnapshot(snapshot, out var error);
                if (ok)
                {
                    restored++;
                    _log.Log(Models.LogLevel.Success, LogCategory.Registro, "Restaurar valor de Registro",
                        $"Valor {snapshot.ValueName} restaurado com sucesso.", result: "Restaurado",
                        registryPath: snapshot.SubKey, oldValue: "atual", newValue: snapshot.PreviousValue?.ToString());
                }
                else
                {
                    failed++;
                    _log.Log(Models.LogLevel.Error, LogCategory.Registro, "Restaurar valor de Registro",
                        $"Falha ao restaurar {snapshot.ValueName}.", result: "Falha", technicalMessage: error,
                        registryPath: snapshot.SubKey);
                }
            }
            return (restored, failed);
        }
    }
}
