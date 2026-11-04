using System.IO;
using Microsoft.Win32;

namespace DeepSeekPet;

/// <summary>
/// 开机自启：写当前用户注册表 Run 项（不需要管理员权限）。
/// 只存 exe 的绝对路径，所以从任意位置运行都能正确登记。
/// </summary>
internal static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DeepSeekPet";

    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DeepSeekPet.exe");

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key?.GetValue(ValueName) is not string value) return false;
            return string.Equals(value.Trim('"'), ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            App.Log("[autostart] read failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>返回是否成功；失败原因会写进 pet-error.log。</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (key is null)
            {
                App.Log("[autostart] cannot open Run key");
                return false;
            }

            if (enabled)
                key.SetValue(ValueName, "\"" + ExecutablePath + "\"", RegistryValueKind.String);
            else
                key.DeleteValue(ValueName, false);

            App.Log($"[autostart] set {enabled} -> {ExecutablePath}");
            return true;
        }
        catch (Exception ex)
        {
            App.Log("[autostart] write failed: " + ex.Message);
            return false;
        }
    }
}
