using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;

namespace SysLoja.PrinterTool.Services
{
    public sealed class SmartFixResult
    {
        public string Operation { get; init; } = string.Empty;
        public bool ConditionMet { get; init; }
        public bool Applied { get; init; }
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// Correcoes pontuais usadas em suporte de infraestrutura. Cada metodo
    /// primeiro verifica evidencia objetiva do problema (ConditionMet) e so
    /// aplica a correcao se a condicao for verdadeira.
    /// </summary>
    public sealed class SmartFixService
    {
        private readonly LogService _log = LogService.Instance;

        public SmartFixResult RestartSpoolerSafely(bool forceApply = false)
        {
            const string op = "Reiniciar Spooler com seguranca";
            try
            {
                using var sc = new ServiceController("Spooler");
                bool needsRestart = sc.Status != ServiceControllerStatus.Running || forceApply;

                if (!needsRestart)
                {
                    _log.Log(Models.LogLevel.Info, Models.LogCategory.Spooler, op, "Spooler ja esta em execucao normal. Nenhuma acao necessaria.", result: "Nao necessario");
                    return new SmartFixResult { Operation = op, ConditionMet = false, Applied = false, Success = true, Message = "Spooler saudavel." };
                }

                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
                sc.Refresh();
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));

                bool ok = sc.Status == ServiceControllerStatus.Running;
                _log.Log(ok ? Models.LogLevel.Success : Models.LogLevel.Error, Models.LogCategory.Spooler, op,
                    ok ? "Spooler reiniciado e validado em execucao." : "Spooler nao voltou ao estado Running apos reinicio.",
                    result: ok ? "Corrigido" : "Falha");
                return new SmartFixResult { Operation = op, ConditionMet = true, Applied = true, Success = ok, Message = sc.Status.ToString() };
            }
            catch (Exception ex)
            {
                _log.Log(Models.LogLevel.Error, Models.LogCategory.Spooler, op, "Falha ao reiniciar o Spooler.", result: "Falha", technicalMessage: ex.Message, exceptionDetails: ex.ToString());
                return new SmartFixResult { Operation = op, ConditionMet = true, Applied = true, Success = false, Message = ex.Message };
            }
        }

        public SmartFixResult ClearStuckPrintQueue()
        {
            const string op = "Limpar fila de impressao travada";
            try
            {
                var spoolPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "PRINTERS");
                int fileCount = Directory.Exists(spoolPath) ? Directory.GetFiles(spoolPath).Length : 0;

                if (fileCount == 0)
                {
                    _log.Log(Models.LogLevel.Info, Models.LogCategory.Spooler, op, "Nenhum arquivo pendente na fila de impressao.", result: "Nao necessario");
                    return new SmartFixResult { Operation = op, ConditionMet = false, Applied = false, Success = true, Message = "Fila ja estava vazia." };
                }

                using (var sc = new ServiceController("Spooler"))
                {
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    }
                }

                foreach (var file in Directory.GetFiles(spoolPath))
                {
                    try { File.Delete(file); } catch { /* pode estar em uso; seguimos */ }
                }

                using (var sc2 = new ServiceController("Spooler"))
                {
                    sc2.Start();
                    sc2.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                }

                var remaining = Directory.Exists(spoolPath) ? Directory.GetFiles(spoolPath).Length : 0;
                bool ok = remaining == 0;

                _log.Log(ok ? Models.LogLevel.Success : Models.LogLevel.Warning, Models.LogCategory.Spooler, op,
                    ok ? $"Fila limpa com sucesso ({fileCount} arquivo(s) removido(s))." : $"Fila parcialmente limpa. {remaining} arquivo(s) permanecem (podem estar em uso).",
                    result: ok ? "Corrigido" : "Parcial");

                return new SmartFixResult { Operation = op, ConditionMet = true, Applied = true, Success = ok, Message = $"{fileCount - remaining} de {fileCount} removidos." };
            }
            catch (Exception ex)
            {
                _log.Log(Models.LogLevel.Error, Models.LogCategory.Spooler, op, "Falha ao limpar fila de impressao.", result: "Falha", technicalMessage: ex.Message, exceptionDetails: ex.ToString());
                return new SmartFixResult { Operation = op, ConditionMet = true, Applied = true, Success = false, Message = ex.Message };
            }
        }

        public SmartFixResult DetectOrphanDrivers()
        {
            const string op = "Detectar drivers orfaos";
            try
            {
                var driverNames = new HashSet<string>();
                using (var driverSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PrinterDriver"))
                {
                    foreach (ManagementObject d in driverSearcher.Get())
                    {
                        var n = d["Name"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(n)) driverNames.Add(n);
                    }
                }

                var usedDrivers = new HashSet<string>();
                using (var printerSearcher = new ManagementObjectSearcher("SELECT DriverName FROM Win32_Printer"))
                {
                    foreach (ManagementObject p in printerSearcher.Get())
                    {
                        var n = p["DriverName"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(n)) usedDrivers.Add(n);
                    }
                }

                var orphan = driverNames.Where(d => !usedDrivers.Any(u => d.Contains(u.Split(',')[0]))).ToList();
                bool conditionMet = orphan.Count > 0;

                _log.Log(conditionMet ? Models.LogLevel.Warning : Models.LogLevel.Info, Models.LogCategory.Drivers, op,
                    conditionMet ? $"{orphan.Count} driver(s) instalados sem impressora associada." : "Nenhum driver orfao aparente detectado.",
                    result: conditionMet ? "Atencao" : "OK");

                return new SmartFixResult { Operation = op, ConditionMet = conditionMet, Applied = false, Success = true, Message = string.Join(", ", orphan) };
            }
            catch (Exception ex)
            {
                _log.Log(Models.LogLevel.Error, Models.LogCategory.Drivers, op, "Falha ao analisar drivers instalados.", result: "Falha", technicalMessage: ex.Message);
                return new SmartFixResult { Operation = op, ConditionMet = false, Applied = false, Success = false, Message = ex.Message };
            }
        }

        public SmartFixResult ValidateSpoolerDependencies()
        {
            const string op = "Validar dependencias do Spooler";
            try
            {
                using var searcher = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_Service.Name='Spooler'} WHERE AssocClass=Win32_DependentService Role=Antecedent");
                var missing = new List<string>();
                foreach (ManagementObject dep in searcher.Get())
                {
                    var state = dep["State"]?.ToString();
                    var name = dep["Name"]?.ToString();
                    if (!string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase)) missing.Add($"{name} ({state})");
                }

                bool conditionMet = missing.Count > 0;
                _log.Log(conditionMet ? Models.LogLevel.Warning : Models.LogLevel.Success, Models.LogCategory.Spooler, op,
                    conditionMet ? $"Dependencias com problema: {string.Join(", ", missing)}" : "Todas as dependencias do Spooler estao saudaveis.",
                    result: conditionMet ? "Atencao" : "OK");

                return new SmartFixResult { Operation = op, ConditionMet = conditionMet, Applied = false, Success = true, Message = string.Join(", ", missing) };
            }
            catch (Exception ex)
            {
                _log.Log(Models.LogLevel.Error, Models.LogCategory.Spooler, op, "Falha ao validar dependencias do Spooler.", result: "Falha", technicalMessage: ex.Message);
                return new SmartFixResult { Operation = op, ConditionMet = false, Applied = false, Success = false, Message = ex.Message };
            }
        }

        public SmartFixResult ValidateDriverArchitectureConsistency()
        {
            const string op = "Validar consistencia de arquitetura de drivers";
            try
            {
                var inconsistent = new List<string>();
                using var searcher = new ManagementObjectSearcher("SELECT Name, DriverName FROM Win32_Printer");
                foreach (ManagementObject p in searcher.Get())
                {
                    var driverName = p["DriverName"]?.ToString() ?? "";
                    if (driverName.Contains("x86") && Environment.Is64BitOperatingSystem)
                    {
                        inconsistent.Add($"{p["Name"]}: driver x86 em sistema x64 (pode exigir driver adicional para clientes x64).");
                    }
                }

                bool conditionMet = inconsistent.Count > 0;
                _log.Log(conditionMet ? Models.LogLevel.Warning : Models.LogLevel.Info, Models.LogCategory.Drivers, op,
                    conditionMet ? string.Join(" ", inconsistent) : "Nenhuma inconsistencia de arquitetura detectada.",
                    result: conditionMet ? "Atencao" : "OK");

                return new SmartFixResult { Operation = op, ConditionMet = conditionMet, Applied = false, Success = true, Message = string.Join(" ", inconsistent) };
            }
            catch (Exception ex)
            {
                _log.Log(Models.LogLevel.Error, Models.LogCategory.Drivers, op, "Falha ao validar arquitetura de drivers.", result: "Falha", technicalMessage: ex.Message);
                return new SmartFixResult { Operation = op, ConditionMet = false, Applied = false, Success = false, Message = ex.Message };
            }
        }
    }
}
