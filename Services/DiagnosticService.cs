using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.ServiceProcess;
using SysLoja.PrinterTool.Models;

namespace SysLoja.PrinterTool.Services
{
    /// <summary>
    /// Executa verificacoes (servicos, impressoras, drivers, rede, firewall,
    /// Event Viewer) e monta um DiagnosticReport. Nao aplica correcoes:
    /// apenas coleta evidencia objetiva usada depois pelo checklist.
    /// </summary>
    public sealed class DiagnosticService
    {
        private readonly LogService _log = LogService.Instance;

        private static readonly (string Name, string Friendly, string Impact)[] CriticalServices =
        {
            ("Spooler", "Print Spooler", "Fila, drivers e envio de impressao"),
            ("RpcSs", "RPC", "Comunicacao remota e conexao com impressoras"),
            ("RpcEptMapper", "RPC Endpoint Mapper", "Roteamento de chamadas RPC"),
            ("DcomLaunch", "DCOM Launcher", "Ativacao de componentes COM/DCOM usados pelo spooler"),
            ("LanmanWorkstation", "Workstation", "Acesso a compartilhamentos de rede"),
            ("LanmanServer", "Server", "Compartilhamento local de impressoras/arquivos"),
        };

        public List<ServiceCheckResult> CheckServices()
        {
            var results = new List<ServiceCheckResult>();
            foreach (var (name, friendly, impact) in CriticalServices)
            {
                try
                {
                    using var sc = new ServiceController(name);
                    string startMode = "Desconhecido";
                    try
                    {
                        using var searcher = new ManagementObjectSearcher($"SELECT StartMode FROM Win32_Service WHERE Name='{name}'");
                        foreach (ManagementObject mo in searcher.Get())
                        {
                            startMode = mo["StartMode"]?.ToString() ?? "Desconhecido";
                        }
                    }
                    catch { }

                    bool healthy = sc.Status == ServiceControllerStatus.Running;
                    results.Add(new ServiceCheckResult
                    {
                        ServiceName = name, FriendlyName = friendly, Status = sc.Status.ToString(),
                        StartMode = startMode, IsHealthy = healthy, Impact = impact
                    });

                    _log.Log(healthy ? Models.LogLevel.Success : Models.LogLevel.Warning, LogCategory.Servicos,
                        $"Verificar servico {friendly}",
                        healthy ? $"Servico {friendly} esta em execucao." : $"Servico {friendly} esta '{sc.Status}'.",
                        result: sc.Status.ToString());
                }
                catch (Exception ex)
                {
                    results.Add(new ServiceCheckResult { ServiceName = name, FriendlyName = friendly, Status = "Nao encontrado", StartMode = "N/D", IsHealthy = false, Impact = impact });
                    _log.Log(Models.LogLevel.Error, LogCategory.Servicos, $"Verificar servico {friendly}",
                        "Falha ao consultar o servico.", result: "Falha", technicalMessage: ex.Message);
                }
            }
            return results;
        }

        public List<PrinterCheckResult> CheckPrinters()
        {
            var results = new List<PrinterCheckResult>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
                foreach (ManagementObject printer in searcher.Get())
                {
                    var name = printer["Name"]?.ToString() ?? "Desconhecida";
                    var portName = printer["PortName"]?.ToString() ?? "";
                    var driverName = printer["DriverName"]?.ToString() ?? "";
                    var isOffline = Convert.ToBoolean(printer["WorkOffline"] ?? false);
                    var isShared = Convert.ToBoolean(printer["Shared"] ?? false);
                    int jobsStuck = 0;
                    var issues = new List<string>();

                    try
                    {
                        var escapedName = name.Replace(@"\", @"\\");
                        using var jobSearcher = new ManagementObjectSearcher($"SELECT * FROM Win32_PrintJob WHERE Name LIKE '{escapedName},%'");
                        jobsStuck = jobSearcher.Get().Count;
                    }
                    catch { }

                    if (isOffline) issues.Add("Impressora marcada como offline.");
                    if (jobsStuck > 3) issues.Add($"Fila com {jobsStuck} jobs, possivel travamento.");
                    if (string.IsNullOrWhiteSpace(driverName)) issues.Add("Driver nao identificado.");

                    results.Add(new PrinterCheckResult
                    {
                        Name = name, PortName = portName, DriverName = driverName, IsOffline = isOffline,
                        IsPaused = false, IsShared = isShared, JobsStuck = jobsStuck, Issues = issues
                    });
                }

                _log.Log(Models.LogLevel.Info, LogCategory.Impressoras, "Verificar impressoras",
                    $"{results.Count} impressora(s) verificada(s).", result: "Concluido");
            }
            catch (Exception ex)
            {
                _log.Log(Models.LogLevel.Error, LogCategory.Impressoras, "Verificar impressoras",
                    "Falha ao consultar impressoras instaladas.", result: "Falha", technicalMessage: ex.Message);
            }
            return results;
        }

        public NetworkCheckResult CheckNetwork(string? target)
        {
            bool pingOk = false, dnsOk = false, port445Ok = false, port139Ok = false, smbOk = false;
            var details = new List<string>();

            if (string.IsNullOrWhiteSpace(target))
            {
                details.Add("Nenhum servidor/IP informado. Testes de rede especificos nao executados.");
                _log.Log(Models.LogLevel.Warning, LogCategory.Rede, "Verificar rede", details[0], result: "Nao executado");
                return new NetworkCheckResult { Target = "", Details = string.Join(" ", details) };
            }

            try
            {
                using var ping = new Ping();
                var reply = ping.Send(target, 2000);
                pingOk = reply.Status == IPStatus.Success;
                details.Add(pingOk ? "Ping respondido com sucesso." : $"Ping falhou: {reply.Status}.");
            }
            catch (Exception ex) { details.Add($"Erro no ping: {ex.Message}"); }

            try
            {
                var addresses = System.Net.Dns.GetHostAddresses(target);
                dnsOk = addresses.Length > 0;
                details.Add(dnsOk ? $"Resolucao DNS OK ({addresses.Length} endereco(s))." : "Resolucao DNS sem retorno.");
            }
            catch (Exception ex) { details.Add($"Falha na resolucao DNS: {ex.Message}"); }

            port445Ok = TestPort(target, 445, out var e445);
            details.Add(port445Ok ? "Porta 445 (SMB) acessivel." : $"Porta 445 inacessivel: {e445}");

            port139Ok = TestPort(target, 139, out var e139);
            details.Add(port139Ok ? "Porta 139 (NetBIOS) acessivel." : $"Porta 139 inacessivel: {e139}");

            try
            {
                var uncPath = $@"\\{target}\";
                smbOk = System.IO.Directory.Exists(uncPath);
                details.Add(smbOk ? "Caminho UNC acessivel." : "Caminho UNC nao acessivel (pode exigir compartilhamento especifico).");
            }
            catch (Exception ex) { details.Add($"Erro ao validar UNC: {ex.Message}"); }

            var result = new NetworkCheckResult
            {
                Target = target, PingOk = pingOk, DnsOk = dnsOk, Port445Ok = port445Ok,
                Port139Ok = port139Ok, SmbShareOk = smbOk, Details = string.Join(" ", details)
            };

            _log.Log(pingOk && dnsOk ? Models.LogLevel.Success : Models.LogLevel.Warning, LogCategory.Rede,
                $"Verificar rede ({target})", result.Details, result: pingOk && dnsOk ? "OK" : "Atencao");

            return result;
        }

        private static bool TestPort(string host, int port, out string error)
        {
            error = "";
            try
            {
                using var client = new TcpClient();
                var task = client.ConnectAsync(host, port);
                if (task.Wait(TimeSpan.FromSeconds(2)) && client.Connected) return true;
                error = "Timeout ou conexao recusada.";
                return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public FirewallCheckResult CheckFirewall()
        {
            bool enabled = false;
            string details;
            try
            {
                var psi = new ProcessStartInfo("netsh", "advfirewall firewall show rule group=\"Compartilhamento de Arquivos e Impressoras\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                var output = proc!.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                enabled = output.Contains("Enabled:") && output.Contains("Yes");
                details = enabled ? "Regras de compartilhamento de arquivos e impressoras habilitadas." : "Regras de compartilhamento de arquivos e impressoras NAO habilitadas ou nao encontradas.";
            }
            catch (Exception ex)
            {
                details = $"Falha ao consultar firewall: {ex.Message}";
            }

            _log.Log(enabled ? Models.LogLevel.Success : Models.LogLevel.Warning, LogCategory.Firewall,
                "Verificar firewall de impressao", details, result: enabled ? "Habilitado" : "Atencao");

            return new FirewallCheckResult { FileAndPrinterSharingEnabled = enabled, Details = details };
        }

        public List<EventLogFinding> CheckEventLog(int maxEvents = 50)
        {
            var findings = new List<EventLogFinding>();
            try
            {
                var query = new EventLogQuery("Microsoft-Windows-PrintService/Admin", PathType.LogName) { ReverseDirection = true };
                using var reader = new EventLogReader(query);
                int count = 0;
                for (var evt = reader.ReadEvent(); evt != null && count < maxEvents; evt = reader.ReadEvent(), count++)
                {
                    using (evt)
                    {
                        var message = SafeFormatDescription(evt);
                        findings.Add(new EventLogFinding
                        {
                            TimeCreated = evt.TimeCreated ?? DateTime.MinValue,
                            Source = evt.ProviderName ?? "PrintService",
                            Category = "PrintService",
                            Message = message,
                            Classification = ClassifyMessage(message)
                        });
                    }
                }
                _log.Log(Models.LogLevel.Info, LogCategory.Sistema, "Ler Event Viewer (PrintService)",
                    $"{findings.Count} evento(s) recente(s) analisado(s).", result: "Concluido");
            }
            catch (Exception ex)
            {
                _log.Log(Models.LogLevel.Warning, LogCategory.Sistema, "Ler Event Viewer (PrintService)",
                    "Nao foi possivel ler o log de eventos do PrintService.", result: "Indisponivel", technicalMessage: ex.Message);
            }
            return findings;
        }

        private static string SafeFormatDescription(EventRecord evt)
        {
            try { return evt.FormatDescription() ?? "(sem descricao)"; }
            catch { return "(descricao indisponivel)"; }
        }

        private static string ClassifyMessage(string message)
        {
            var lower = message.ToLowerInvariant();
            if (lower.Contains("access") && lower.Contains("denied")) return "Acesso negado";
            if (lower.Contains("rpc")) return "RPC";
            if (lower.Contains("driver")) return "Driver";
            if (lower.Contains("offline")) return "Impressora offline";
            if (lower.Contains("spooler") || lower.Contains("spool")) return "Spooler";
            return "Geral";
        }
    }
}
