using System;
using System.Security.Principal;
using Microsoft.Win32;
using SysLoja.PrinterTool.Models;

namespace SysLoja.PrinterTool.Services
{
    /// <summary>
    /// Encapsula todo acesso ao Registro relacionado a impressao. Toda
    /// escrita e seguida de releitura de validacao: se o valor gravado nao
    /// corresponder ao esperado, a operacao e reportada como falha real.
    /// </summary>
    public sealed class RegistryService
    {
        public static bool IsRunningAsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public RegistryValueSnapshot CaptureSnapshot(string subKey, string valueName, RegistryValueKind kind)
        {
            bool keyExisted;
            bool valueExisted = false;
            object? previous = null;

            using var key = Registry.LocalMachine.OpenSubKey(subKey, writable: false);
            keyExisted = key != null;
            if (key != null)
            {
                previous = key.GetValue(valueName);
                valueExisted = previous != null;
            }

            return new RegistryValueSnapshot
            {
                Hive = "HKLM",
                SubKey = subKey,
                ValueName = valueName,
                Kind = kind,
                PreviousValue = previous,
                KeyExistedBefore = keyExisted,
                ValueExistedBefore = valueExisted
            };
        }

        public (bool Success, object? ReadBack, string? Error) WriteAndVerifyDword(string subKey, string valueName, int value)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(subKey, writable: true)
                    ?? throw new InvalidOperationException($"Nao foi possivel abrir/criar a chave: {subKey}");

                key.SetValue(valueName, value, RegistryValueKind.DWord);

                var readBack = key.GetValue(valueName);
                bool ok = readBack is int i && i == value;
                return (ok, readBack, ok ? null : $"Valor gravado nao corresponde ao esperado. Esperado={value}, Lido={readBack}");
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        public object? ReadValue(string subKey, string valueName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(subKey, writable: false);
                return key?.GetValue(valueName);
            }
            catch { return null; }
        }

        public bool RestoreSnapshot(RegistryValueSnapshot snapshot, out string? error)
        {
            error = null;
            try
            {
                if (!snapshot.KeyExistedBefore)
                {
                    using var parent = Registry.LocalMachine.OpenSubKey(GetParentPath(snapshot.SubKey), writable: true);
                    if (parent != null)
                    {
                        try { parent.DeleteSubKeyTree(GetLeafName(snapshot.SubKey), throwOnMissingSubKey: false); }
                        catch (Exception ex) { error = ex.Message; return false; }
                    }
                    return true;
                }

                using var key = Registry.LocalMachine.OpenSubKey(snapshot.SubKey, writable: true);
                if (key == null)
                {
                    error = $"Chave {snapshot.SubKey} nao encontrada para restauracao.";
                    return false;
                }

                if (!snapshot.ValueExistedBefore)
                {
                    key.DeleteValue(snapshot.ValueName, throwOnMissingValue: false);
                    return true;
                }

                key.SetValue(snapshot.ValueName, snapshot.PreviousValue ?? 0, snapshot.Kind);
                var readBack = key.GetValue(snapshot.ValueName);
                bool ok = Equals(readBack, snapshot.PreviousValue);
                if (!ok) error = $"Falha ao validar restauracao de {snapshot.ValueName}. Esperado={snapshot.PreviousValue}, Lido={readBack}";
                return ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string GetParentPath(string subKey)
        {
            var idx = subKey.LastIndexOf('\\');
            return idx > 0 ? subKey[..idx] : subKey;
        }

        private static string GetLeafName(string subKey)
        {
            var idx = subKey.LastIndexOf('\\');
            return idx > 0 ? subKey[(idx + 1)..] : subKey;
        }
    }
}
