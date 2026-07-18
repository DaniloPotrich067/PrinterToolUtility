using System.Collections.Generic;
using System.Linq;
using SysLoja.PrinterTool.Models;

namespace SysLoja.PrinterTool.Services
{
    /// <summary>
    /// Traduz um DiagnosticReport em indicadores de saude clicaveis para o
    /// dashboard. Nao executa verificacoes propria: consome apenas dados
    /// ja coletados pelo DiagnosticService.
    /// </summary>
    public sealed class HealthAggregatorService
    {
        public List<HealthItem> BuildHealthItems(DiagnosticReport report)
        {
            return new List<HealthItem>
            {
                BuildServiceHealth("Servicos", report),
                BuildNetworkHealth("Rede", report),
                BuildRpcHealth("RPC", report),
                BuildSmbHealth("SMB", report),
                BuildFirewallHealth("Firewall", report),
                BuildPrinterHealth("Impressoras", report),
                BuildDriverHealth("Drivers", report),
                BuildDnsHealth("DNS", report),
                BuildHostnameHealth("Hostname"),
                BuildShareHealth("Compartilhamentos", report),
            };
        }

        private static HealthItem BuildServiceHealth(string name, DiagnosticReport r)
        {
            var unhealthy = r.Services.Where(s => !s.IsHealthy).ToList();
            var status = unhealthy.Count == 0 ? HealthStatus.Healthy : (unhealthy.Count <= 1 ? HealthStatus.Warning : HealthStatus.Problem);
            return new HealthItem
            {
                Name = name, Status = status,
                Evidence = unhealthy.Count == 0 ? "Todos os servicos criticos estao em execucao." : string.Join("; ", unhealthy.Select(s => $"{s.FriendlyName}: {s.Status}")),
                ProbableCause = unhealthy.Count == 0 ? "" : "Servico parado, desabilitado ou com falha de dependencia.",
                SuggestedFix = unhealthy.Count == 0 ? "" : "Iniciar o servico ou usar 'Reiniciar Spooler com seguranca'."
            };
        }

        private static HealthItem BuildNetworkHealth(string name, DiagnosticReport r)
        {
            if (r.Network == null || string.IsNullOrWhiteSpace(r.Network.Target))
                return new HealthItem { Name = name, Status = HealthStatus.Unknown, Evidence = "Nenhum servidor informado para teste.", SuggestedFix = "Informe um servidor/IP e rode o diagnostico novamente." };

            var status = r.Network.PingOk && r.Network.DnsOk ? HealthStatus.Healthy : (r.Network.PingOk || r.Network.DnsOk ? HealthStatus.Warning : HealthStatus.Problem);
            return new HealthItem
            {
                Name = name, Status = status, Evidence = r.Network.Details,
                ProbableCause = status == HealthStatus.Healthy ? "" : "Servidor inacessivel, firewall bloqueando ICMP/DNS ou problema de rede.",
                SuggestedFix = status == HealthStatus.Healthy ? "" : "Validar cabo/Wi-Fi, VPN, DNS e firewall entre cliente e servidor."
            };
        }

        private static HealthItem BuildRpcHealth(string name, DiagnosticReport r)
        {
            var relevant = r.Services.Where(s => s.ServiceName is "RpcSs" or "RpcEptMapper" or "DcomLaunch").ToList();
            bool allOk = relevant.Count > 0 && relevant.All(s => s.IsHealthy);
            return new HealthItem
            {
                Name = name, Status = allOk ? HealthStatus.Healthy : HealthStatus.Problem,
                Evidence = allOk ? "RPC, Endpoint Mapper e DCOM Launcher em execucao." : "Um ou mais servicos RPC/DCOM nao estao em execucao.",
                ProbableCause = allOk ? "" : "Servico RPC parado ou politica de hardening bloqueando chamadas.",
                SuggestedFix = allOk ? "" : "Iniciar servicos RPC/DCOM e considerar 'Aplicar Correcoes RPC' para 0x0000011B."
            };
        }

        private static HealthItem BuildSmbHealth(string name, DiagnosticReport r)
        {
            if (r.Network == null || string.IsNullOrWhiteSpace(r.Network.Target))
                return new HealthItem { Name = name, Status = HealthStatus.Unknown, Evidence = "Sem servidor informado.", SuggestedFix = "Informe um servidor para validar SMB." };

            var status = r.Network.Port445Ok ? HealthStatus.Healthy : HealthStatus.Problem;
            return new HealthItem
            {
                Name = name, Status = status,
                Evidence = status == HealthStatus.Healthy ? "Porta 445 acessivel." : "Porta 445 inacessivel no servidor informado.",
                ProbableCause = status == HealthStatus.Healthy ? "" : "Firewall bloqueando SMB ou servico de compartilhamento parado no servidor.",
                SuggestedFix = status == HealthStatus.Healthy ? "" : "Verificar firewall do servidor e servico LanmanServer."
            };
        }

        private static HealthItem BuildFirewallHealth(string name, DiagnosticReport r)
        {
            if (r.Firewall == null) return new HealthItem { Name = name, Status = HealthStatus.Unknown, Evidence = "Firewall nao verificado." };
            return new HealthItem
            {
                Name = name, Status = r.Firewall.FileAndPrinterSharingEnabled ? HealthStatus.Healthy : HealthStatus.Warning,
                Evidence = r.Firewall.Details,
                ProbableCause = r.Firewall.FileAndPrinterSharingEnabled ? "" : "Regras de firewall de compartilhamento desabilitadas.",
                SuggestedFix = r.Firewall.FileAndPrinterSharingEnabled ? "" : "Habilitar o grupo de regras 'Compartilhamento de Arquivos e Impressoras'."
            };
        }

        private static HealthItem BuildPrinterHealth(string name, DiagnosticReport r)
        {
            var withIssues = r.Printers.Where(p => p.Issues.Count > 0).ToList();
            var status = withIssues.Count == 0 ? HealthStatus.Healthy : (withIssues.Count <= 1 ? HealthStatus.Warning : HealthStatus.Problem);
            return new HealthItem
            {
                Name = name, Status = r.Printers.Count == 0 ? HealthStatus.Unknown : status,
                Evidence = withIssues.Count == 0 ? $"{r.Printers.Count} impressora(s), nenhum problema aparente." : string.Join("; ", withIssues.Select(p => $"{p.Name}: {string.Join(", ", p.Issues)}")),
                ProbableCause = withIssues.Count == 0 ? "" : "Impressora offline, fila travada ou driver ausente.",
                SuggestedFix = withIssues.Count == 0 ? "" : "Usar 'Limpar fila de impressao travada' ou verificar conexao da impressora."
            };
        }

        private static HealthItem BuildDriverHealth(string name, DiagnosticReport r)
        {
            var noDriver = r.Printers.Where(p => string.IsNullOrWhiteSpace(p.DriverName)).ToList();
            var status = noDriver.Count == 0 ? HealthStatus.Healthy : HealthStatus.Warning;
            return new HealthItem
            {
                Name = name, Status = r.Printers.Count == 0 ? HealthStatus.Unknown : status,
                Evidence = noDriver.Count == 0 ? "Todos os drivers identificados." : $"{noDriver.Count} impressora(s) sem driver identificado.",
                ProbableCause = noDriver.Count == 0 ? "" : "Driver desinstalado, corrompido ou incompativel.",
                SuggestedFix = noDriver.Count == 0 ? "" : "Reinstalar ou atualizar o driver da impressora afetada."
            };
        }

        private static HealthItem BuildDnsHealth(string name, DiagnosticReport r)
        {
            if (r.Network == null || string.IsNullOrWhiteSpace(r.Network.Target))
                return new HealthItem { Name = name, Status = HealthStatus.Unknown, Evidence = "Sem servidor informado." };
            return new HealthItem
            {
                Name = name, Status = r.Network.DnsOk ? HealthStatus.Healthy : HealthStatus.Problem,
                Evidence = r.Network.DnsOk ? "Resolucao de nome funcionando." : "Falha ao resolver o nome do servidor.",
                ProbableCause = r.Network.DnsOk ? "" : "Servidor DNS incorreto ou registro inexistente.",
                SuggestedFix = r.Network.DnsOk ? "" : "Validar configuracao de DNS do cliente ou usar o IP direto."
            };
        }

        private static HealthItem BuildHostnameHealth(string name)
        {
            return new HealthItem { Name = name, Status = HealthStatus.Healthy, Evidence = $"Nome do computador: {System.Environment.MachineName}." };
        }

        private static HealthItem BuildShareHealth(string name, DiagnosticReport r)
        {
            if (r.Network == null || string.IsNullOrWhiteSpace(r.Network.Target))
                return new HealthItem { Name = name, Status = HealthStatus.Unknown, Evidence = "Sem servidor informado." };
            return new HealthItem
            {
                Name = name, Status = r.Network.SmbShareOk ? HealthStatus.Healthy : HealthStatus.Warning,
                Evidence = r.Network.SmbShareOk ? "Caminho UNC acessivel." : "Caminho UNC nao acessivel a partir deste cliente.",
                ProbableCause = r.Network.SmbShareOk ? "" : "Permissao de compartilhamento, firewall ou servidor fora do ar.",
                SuggestedFix = r.Network.SmbShareOk ? "" : "Validar permissoes de compartilhamento no servidor de impressao."
            };
        }
    }
}
