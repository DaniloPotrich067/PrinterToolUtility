using System.Windows;

namespace SysLoja.PrinterTool
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    Services.LogService.Instance.Log(Models.LogLevel.Error, Models.LogCategory.Sistema,
                        "Excecao nao tratada na interface", args.Exception.Message,
                        technicalMessage: args.Exception.ToString());
                }
                catch { }
                MessageBox.Show("Ocorreu um erro inesperado. Consulte o log para detalhes.\n\n" + args.Exception.Message,
                    "SysLoja - Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
