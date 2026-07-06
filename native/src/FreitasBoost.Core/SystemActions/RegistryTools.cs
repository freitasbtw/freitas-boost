using Microsoft.Win32;
using FreitasBoost.Core.Models;

namespace FreitasBoost.Core.SystemActions;

internal static class RegistryTools
{
    public static RegistryValueState GetDWordState(string providerPath, string name)
    {
        var state = new RegistryValueState
        {
            Path = providerPath,
            Name = name,
            Type = "DWord"
        };

        try
        {
            using var key = OpenKey(providerPath, writable: false);
            if (key is null)
            {
                return state;
            }

            var value = key.GetValue(name, null);
            if (value is null)
            {
                return state;
            }

            state.Exists = true;
            state.Value = Convert.ToInt32(value);
        }
        catch
        {
            state.Exists = false;
            state.Value = null;
        }

        return state;
    }

    public static int? GetDWordValue(string providerPath, string name)
    {
        try
        {
            using var key = OpenKey(providerPath, writable: false);
            var value = key?.GetValue(name, null);
            return value is null ? null : Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    public static string? GetStringValue(string providerPath, string name)
    {
        try
        {
            using var key = OpenKey(providerPath, writable: false);
            return key?.GetValue(name, null)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public static void SetDWordValue(string providerPath, string name, int value)
    {
        using var key = OpenKey(providerPath, writable: true, create: true)
            ?? throw new InvalidOperationException($"Registro indisponivel: {providerPath}");
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    public static string? RestoreValue(RegistryValueState entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Path) || string.IsNullOrWhiteSpace(entry.Name))
        {
            return null;
        }

        if (entry.Exists)
        {
            SetDWordValue(entry.Path, entry.Name, entry.Value ?? 0);
            return $"{entry.Name} restaurado";
        }

        try
        {
            using var key = OpenKey(entry.Path, writable: true);
            key?.DeleteValue(entry.Name, throwOnMissingValue: false);
            return $"{entry.Name} removido";
        }
        catch
        {
            return null;
        }
    }

    private static RegistryKey? OpenKey(string providerPath, bool writable, bool create = false)
    {
        var normalized = providerPath.Replace('/', '\\').Trim();
        RegistryKey root;
        string subKey;

        if (normalized.StartsWith(@"HKCU:\", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase))
        {
            root = Registry.CurrentUser;
            subKey = normalized[(normalized.IndexOf('\\') + 1)..];
        }
        else if (normalized.StartsWith(@"HKLM:\", StringComparison.OrdinalIgnoreCase) ||
                 normalized.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
        {
            root = Registry.LocalMachine;
            subKey = normalized[(normalized.IndexOf('\\') + 1)..];
        }
        else
        {
            return null;
        }

        return create ? root.CreateSubKey(subKey, writable) : root.OpenSubKey(subKey, writable);
    }
}

