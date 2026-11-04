using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DeepSeekPet;

/// <summary>
/// 双击大肥鱼 → 打开 DeepSeek Harness 桌面端。
/// 路径按「手动指定 → 正在运行的实例 → 常见安装位置 → 注册表卸载项」的顺序找。
/// </summary>
internal static class HarnessLauncher
{
    private const string ProcessName = "DeepSeek Harness";
    private const string ExeName = "DeepSeek Harness.exe";

    public static string? Resolve(string? configuredPath, out string source)
    {
        source = "";

        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            source = "settings";
            return configuredPath;
        }

        foreach (Process process in SafeGetProcesses())
        {
            try
            {
                string? path = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    source = "running";
                    return path;
                }
            }
            catch { }
        }

        foreach (string candidate in Candidates())
        {
            if (File.Exists(candidate))
            {
                source = "common-path";
                return candidate;
            }
        }

        string? fromRegistry = FromRegistry();
        if (fromRegistry is not null)
        {
            source = "registry";
            return fromRegistry;
        }

        return null;
    }

    public static (bool Ok, string Message) LaunchOrFocus(string? configuredPath)
    {
        if (TryFocusExisting()) return (true, "工作台本来就在，给你拉到前面了。");
        if (RequestShow(configuredPath)) return (true, "好，我让它把窗口打开。");
        return (false, "找不到 DeepSeek Harness……右键菜单里可以手动指定一次。");
    }

    /// <summary>找到窗口并激活；没有窗口就返回 false。</summary>
    public static bool TryFocusExisting()
    {
        IntPtr target = FindTopWindow(allowHidden: false);
        if (target == IntPtr.Zero) target = FindTopWindow(allowHidden: true); // 收进托盘的隐藏窗口
        if (target == IntPtr.Zero) return false;

        Activate(target);
        App.Log($"[harness] focus 0x{target.ToInt64():X} visible={Native.IsWindowVisible(target)} iconic={Native.IsIconic(target)}");
        return true;
    }

    /// <summary>
    /// 请求工作台把自己显示出来。优先用它自己的协议 dsh://open（应用内就是 focusPrimaryWindow），
    /// 没注册协议再直接启动 exe。
    /// </summary>
    public static bool RequestShow(string? configuredPath)
    {
        try
        {
            var info = new ProcessStartInfo("dsh://open") { UseShellExecute = true };
            Process.Start(info);
            App.Log("[harness] fired dsh://open");
            return true;
        }
        catch (Exception ex)
        {
            App.Log("[harness] dsh://open failed: " + ex.Message);
        }

        string? path = Resolve(configuredPath, out string source);
        if (path is null) return false;

        try
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory
            };
            Process.Start(info);
            App.Log($"[harness] launch {path} (via {source})");
            return true;
        }
        catch (Exception ex)
        {
            App.Log("[harness] launch failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 把窗口从最小化/后台弄到前台。
    /// Electron 的窗口对 SetForegroundWindow 有时不理，得多管齐下：
    /// SW_RESTORE → SC_RESTORE → 附加前台线程输入 → SetForegroundWindow → SwitchToThisWindow。
    /// </summary>
    private static void Activate(IntPtr hWnd)
    {
        if (Native.IsIconic(hWnd))
        {
            Native.ShowWindow(hWnd, 9);                                    // SW_RESTORE
            Native.PostMessage(hWnd, 0x0112 /* WM_SYSCOMMAND */, (IntPtr)0xF120 /* SC_RESTORE */, IntPtr.Zero);
        }

        Native.ShowWindow(hWnd, 5);                                        // SW_SHOW

        IntPtr foreground = Native.GetForegroundWindow();
        uint foregroundThread = Native.GetWindowThreadProcessId(foreground, out _);
        uint currentThread = Native.GetCurrentThreadId();
        bool attached = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attached = Native.AttachThreadInput(currentThread, foregroundThread, true);

            Native.BringWindowToTop(hWnd);
            Native.SetForegroundWindow(hWnd);
            Native.SwitchToThisWindow(hWnd, true);
        }
        finally
        {
            if (attached) Native.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    /// <summary>找 DeepSeek Harness 的窗口：按进程名或窗口标题判断，取面积最大的那个。</summary>
    private static IntPtr FindTopWindow(bool allowHidden)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;

        Native.EnumWindows((hWnd, _) =>
        {
            bool visible = Native.IsWindowVisible(hWnd);
            if (!visible && !allowHidden) return true;
            if (Native.GetWindow(hWnd, 4 /* GW_OWNER */) != IntPtr.Zero) return true; // 跳过工具/子窗

            if (!Native.GetWindowRect(hWnd, out Native.RECT rect)) return true;
            long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            bool iconic = Native.IsIconic(hWnd);

            // 隐藏窗口要更严格：必须是像样的主窗口尺寸，避免抓到 0x0 的辅助窗口
            if (!visible && !iconic && area < 400 * 300) return true;

            // 最小化的窗口只剩一条（甚至被挪到 -32000），不能按面积把它筛掉
            if (!iconic && area < 200 * 120) return true;

            Native.GetWindowThreadProcessId(hWnd, out uint pid);

            string procName = string.Empty;
            try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { }

            var title = new System.Text.StringBuilder(256);
            Native.GetWindowText(hWnd, title, 256);
            string titleText = title.ToString();

            bool looksLikeHarness =
                procName.Contains("Harness", StringComparison.OrdinalIgnoreCase) ||
                titleText.Contains("DeepSeek Harness", StringComparison.OrdinalIgnoreCase);

            if (!looksLikeHarness) return true;

            // 最小化的主窗口优先，其余取面积最大的
            long score = iconic ? long.MaxValue : area;
            if (score > bestArea)
            {
                bestArea = score;
                best = hWnd;
            }
            return true;
        }, IntPtr.Zero);

        return best;
    }

    private static IEnumerable<Process> SafeGetProcesses()
    {
        try { return Process.GetProcessesByName(ProcessName); }
        catch { return Array.Empty<Process>(); }
    }

    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(@"D:\DeepSeek Harness", ExeName);
        yield return Path.Combine(@"C:\Program Files\DeepSeek Harness", ExeName);
        yield return Path.Combine(@"C:\Program Files (x86)\DeepSeek Harness", ExeName);

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Programs", "DeepSeek Harness", ExeName);
        yield return Path.Combine(local, "Programs", "deepseek-harness", ExeName);
        yield return Path.Combine(local, "DeepSeek Harness", ExeName);

        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(roaming, "DeepSeek Harness", ExeName);
    }

    private static string? FromRegistry()
    {
        string[] subKeys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                RegistryKey? baseKey = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                    foreach (string subKey in subKeys)
                    {
                        using RegistryKey? uninstall = baseKey.OpenSubKey(subKey);
                        if (uninstall is null) continue;

                        foreach (string name in uninstall.GetSubKeyNames())
                        {
                            using RegistryKey? app = uninstall.OpenSubKey(name);
                            if (app is null) continue;

                            string displayName = app.GetValue("DisplayName") as string ?? string.Empty;
                            if (!displayName.Contains("DeepSeek Harness", StringComparison.OrdinalIgnoreCase)) continue;

                            string installLocation = app.GetValue("InstallLocation") as string ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(installLocation))
                            {
                                string candidate = Path.Combine(installLocation.Trim('"'), ExeName);
                                if (File.Exists(candidate)) return candidate;
                            }

                            string displayIcon = app.GetValue("DisplayIcon") as string ?? string.Empty;
                            string iconPath = displayIcon.Split(',')[0].Trim('"', ' ');
                            if (File.Exists(iconPath) && iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                return iconPath;
                        }
                    }
                }
                catch { }
                finally { baseKey?.Dispose(); }
            }
        }

        return null;
    }
}

internal static class Native
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    public static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int maxCount);
}
