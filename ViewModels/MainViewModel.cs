using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SysLoja.PrinterTool.Models;
using SysLoja.PrinterTool.Services;

namespace SysLoja.PrinterTool.ViewModels
{
    public sealed class MainViewModel : ViewModelBase
    {
        private readonly DiagnosticService _diagnosticService = new();
        private readonly HealthAggregatorService _healthAggregator = new();
        private readonly SmartFixService _smartFixService = new();
        private readonly RegistryService _registryService = new();
        private readonly BackupService _backupService;
        private readonly RpcFixService _rpcFixService;
        public LogService Log { get; } = LogService.Instance;

        public MainViewModel()
        {
            _backupService = new BackupService(_registryService);
            _rpcFixService = new RpcFixService(_registryService, _backupService);

            RunDiagnosticCommand = new RelayCommand(async () => await RunDiagnosticAsync(), () => !IsBusy);
            ApplyRpcFixCommand = new RelayCommand(async () => await ApplyRpcFixAsync(), () => !IsBusy);
            RestartSpoolerCommand = new RelayCommand(async () => await RunSmartFixAsync(() => _smartFixService.RestartSpoolerSafely(), "Reiniciar Spooler"), () => !IsBusy);
            ClearQueueCommand = new RelayCommand(async () => await RunSmartFixAsync(() => _smartFixService.ClearStuckPrintQueue(), "Limpar fila"), () => !IsBusy);
            CheckOrphanDriversCommand = new RelayCommand(async () => await RunSmartFixAsync(_smartFixService.DetectOrphanDrivers, "Verificar drivers orfaos"), () => !IsBusy);
            CheckDependenciesCommand = new RelayCommand(async () => await RunSmartFixAsync(_smartFixService.ValidateSpoolerDependencies, "Validar dependencias"), () => !IsBusy);
            CheckDriverArchCommand = new RelayCommand(async () => await RunSmartFixAsync(_smartFixService.ValidateDriverArchitectureConsistency, "Validar arquitetura de drivers"), () => !IsBusy);
            RunFullAutoFixCommand = new RelayCommand(async () => await RunFullAutoFixAsync(), () => !IsBusy);
            ExportLogCommand = new RelayCommand(_ => ExportLog());
            OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());

            IsAdministrator = RegistryService.IsRunningAsAdministrator();
            StatusMessage = IsAdministrator
                ? "Pronto. Execute o diagnostico para comecar."
                : "Atencao: ferramenta nao esta em modo Administrador. Correcoes de Registro nao funcionarao.";
        }

        public ObservableCollection<HealthItem> HealthItems { get; } = new();
        public ObservableCollection<ServiceCheckResult> ServiceResults { get; } = new();
        public ObservableCollection<PrinterCheckResult> PrinterResults { get; } = new();
        public ObservableCollection<EventLogFinding> EventFindings { get; } = new();

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { if (SetField(ref _isBusy, value)) CommandManager_InvalidateAll(); }
        }

        private double _progress;
        public double Progress { get => _progress; set => SetField(ref _progress, value); }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        private string _serverTarget = string.Empty;
        public string ServerTarget { get => _serverTarget; set => SetField(ref _serverTarget, value); }

        private bool _isAdministrator;
        public bool IsAdministrator { get => _isAdministrator; set => SetField(ref _isAdministrator, value); }

        private NetworkCheckResult? _networkResult;
        public NetworkCheckResult? NetworkResult { get => _networkResult; set => SetField(ref _networkResult, value); }

        private FirewallCheckResult? _firewallResult;
        public FirewallCheckResult? FirewallResult { get => _firewallResult; set => SetField(ref _firewallResult, value); }

        private string _lastRpcSummary = string.Empty;
        public string LastRpcSummary { get => _lastRpcSummary; set => SetField(ref _lastRpcSummary, value); }

        public RelayCommand RunDiagnosticCommand { get; }
        public RelayCommand ApplyRpcFixCommand { get; }
        public RelayCommand RestartSpoolerCommand { get; }
        public RelayCommand ClearQueueCommand { get; }
        public RelayCommand CheckOrphanDriversCommand { get; }
        public RelayCommand CheckDependenciesCommand { get; }
        public RelayCommand CheckDriverArchCommand { get; }
        public RelayCommand RunFullAutoFixCommand { get; }
        public RelayCommand ExportLogCommand { get; }
        public RelayCommand OpenLogFolderCommand { get; }

        private void CommandManager_InvalidateAll()
        {
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        private async Task RunDiagnosticAsync()
        {
            IsBusy = true;
            Progress = 0;
            StatusMessage = "Executando diagnostico...";
            try
            {
                var report = await Task.Run(() =>
                {
                    var r = new DiagnosticReport();
                    r.Services.AddRange(_diagnosticService.CheckServices());
                    Progress = 20;
                    r.Printers.AddRange(_diagnosticService.CheckPrinters());
                    Progress = 45;
                    r.Network = _diagnosticService.CheckNetwork(ServerTarget);
                    Progress = 65;
                    r.Firewall = _diagnosticService.CheckFirewall();
                    Progress = 80;
                    r.EventFindings.AddRange(_diagnosticService.CheckEventLog());
                    Progress = 95;
                    r.HealthItems.AddRange(_healthAggregator.BuildHealthItems(r));
                    return r;
                });

                ServiceResults.Clear();
                foreach (var s in report.Services) ServiceResults.Add(s);

                PrinterResults.Clear();
                foreach (var p in report.Printers) PrinterResults.Add(p);

                EventFindings.Clear();
                foreach (var e in report.EventFindings) EventFindings.Add(e);

                HealthItems.Clear();
                foreach (var h in report.HealthItems) HealthItems.Add(h);

                NetworkResult = report.Network;
                FirewallResult = report.Firewall;

                Progress = 100;
                var problems = report.HealthItems.Count(h => h.Status == HealthStatus.Problem);
                var warnings = report.HealthItems.Count(h => h.Status == HealthStatus.Warning);
                StatusMessage = problems == 0 && warnings == 0
                    ? "Diagnostico concluido: nenhum problema detectado."
                    : $"Diagnostico concluido: {problems} problema(s) e {warnings} aviso(s) encontrados.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Falha ao executar diagnostico. Veja o log.";
                Log.Log(Models.LogLevel.Error, LogCategory.Sistema, "Executar diagnostico", "Falha inesperada no diagnostico.", result: "Falha", technicalMessage: ex.Message, exceptionDetails: ex.ToString());
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ApplyRpcFixAsync()
        {
            if (!IsAdministrator)
            {
                MessageBox.Show("E necessario executar como Administrador para aplicar correcoes de Registro (erro 0x0000011B).",
                    "SysLoja - Permissao necessaria", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsBusy = true;
            StatusMessage = "Aplicando correcoes de RPC / Point-and-Print...";
            try
            {
                var (overallSuccess, items, backup) = await Task.Run(() => _rpcFixService.ApplyAllFixes());
                var okCount = items.Count(i => i.Success);
                LastRpcSummary = $"{okCount}/{items.Count} valores corrigidos e validados. Backup salvo em: {_backupService.BackupFolder}";
                StatusMessage = overallSuccess
                    ? "Correcoes de RPC aplicadas e validadas com sucesso."
                    : "Correcoes de RPC aplicadas com falhas parciais. Veja o log para detalhes.";

                MessageBox.Show(LastRpcSummary, overallSuccess ? "SysLoja - Correcao concluida" : "SysLoja - Correcao parcial",
                    MessageBoxButton.OK, overallSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusMessage = "Falha ao aplicar correcoes de RPC.";
                MessageBox.Show(ex.Message, "SysLoja - Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RunSmartFixAsync(Func<SmartFixResult> action, string label)
        {
            IsBusy = true;
            StatusMessage = $"Executando: {label}...";
            try
            {
                var result = await Task.Run(action);
                StatusMessage = result.Applied
                    ? (result.Success ? $"{label}: corrigido com sucesso." : $"{label}: falhou. Veja o log.")
                    : $"{label}: nenhuma acao necessaria.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"{label}: erro inesperado.";
                Log.Log(Models.LogLevel.Error, LogCategory.Sistema, label, "Falha inesperada.", result: "Falha", technicalMessage: ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RunFullAutoFixAsync()
        {
            if (!IsAdministrator)
            {
                MessageBox.Show("E necessario executar como Administrador para a correcao automatica completa.",
                    "SysLoja - Permissao necessaria", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsBusy = true;
            StatusMessage = "Executando correcao automatica completa (diagnostico + RPC + spooler + fila)...";
            try
            {
                await RunDiagnosticAsync();
                await Task.Run(() => _smartFixService.ValidateSpoolerDependencies());
                await Task.Run(() => _smartFixService.ClearStuckPrintQueue());
                await Task.Run(() => _rpcFixService.ApplyAllFixes());
                await Task.Run(() => _smartFixService.RestartSpoolerSafely(forceApply: true));
                await RunDiagnosticAsync();

                StatusMessage = "Correcao automatica completa finalizada. Verifique o painel de saude e o log.";
                MessageBox.Show("Correcao automatica concluida. Revise o painel de saude para confirmar a resolucao dos erros 0x0000005B, 0x000004F8 e 0x0000011B.",
                    "SysLoja - Correcao automatica", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = "Falha na correcao automatica completa.";
                MessageBox.Show(ex.Message, "SysLoja - Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExportLog()
        {
            try
            {
                var html = Log.ExportAsHtml();
                var path = System.IO.Path.Combine(Log.LogFolder, $"Relatorio_{DateTime.Now:yyyyMMdd_HHmmss}.html");
                System.IO.File.WriteAllText(path, html);
                StatusMessage = $"Relatorio exportado: {path}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Falha ao exportar relatorio: {ex.Message}", "SysLoja - Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.LogFolder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Falha ao abrir pasta de log: {ex.Message}", "SysLoja - Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
