// ============================================================
// ObsidianInsertDateTime  v1.1.0  Build: 20250712
// 在 Obsidian 编辑状态下插入格式化日期时间
// Roslyn v2 零样板模式：禁止 namespace/class
// ============================================================
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Quicker.Public;

// Win32 API 声明：获取前台窗口与模拟按键
[DllImport("user32.dll")]
private static extern IntPtr GetForegroundWindow();

[DllImport("user32.dll")]
private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool SetForegroundWindow(IntPtr hWnd);

[DllImport("user32.dll")]
private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

private const byte VK_CONTROL = 0x11;
private const byte VK_V = 0x56;
private const byte VK_RETURN = 0x0D;
private const uint KEYEVENTF_KEYUP = 0x0002;

public static string Exec(IStepContext context)
{
    try
    {
        // 【步骤1】获取当前前台窗口，校验是否为 Obsidian 进程
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            ShowError("无法获取前台窗口，请重试。");
            return "ERROR: 无法获取前台窗口";
        }

        GetWindowThreadProcessId(hwnd, out uint processId);
        string processName;
        using (Process process = Process.GetProcessById((int)processId))
        {
            processName = process.ProcessName.ToLower();
        }

        if (!processName.Contains("obsidian"))
        {
            ShowError("请在 Obsidian 中使用此动作");
            return "ERROR: 当前窗口不是 Obsidian";
        }

        // 【步骤2】读取系统当前日期时间，格式化为 yyyy-MM-dd 星期X HH:mm（24小时制，自动补零）
        // 使用 zh-CN 文化信息运行时生成中文星期名，避免源文件编码导致中文字符丢失
        DateTime now = DateTime.Now;
        var zhCulture = new System.Globalization.CultureInfo("zh-CN");
        string formattedTime = $"{now:yyyy-MM-dd} {now.ToString("dddd", zhCulture)} {now:HH:mm}";

        // 【步骤3】通过剪贴板粘贴方式发送文本，天然支持光标插入与选中替换
        // 3.1 保存当前剪贴板文本内容
        string originalClipboard = null;
        bool clipboardRestored = false;
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    originalClipboard = Clipboard.GetText();
                    clipboardRestored = true;
                }
            }
            catch { /* 剪贴板被占用则跳过保存 */ }
        });

        // 3.2 设置剪贴板为目标时间文本
        Application.Current.Dispatcher.Invoke(() =>
        {
            Clipboard.SetText(formattedTime);
        });
        Thread.Sleep(20); // 确保剪贴板写入完成

        // 3.3 确保 Obsidian 窗口获得焦点后模拟 Ctrl+V 粘贴
        SetForegroundWindow(hwnd);
        Thread.Sleep(30);

        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(50); // 等待粘贴完成

        // 【步骤3.5】模拟按下 Enter 键，光标跳到下一行
        keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(20);

        // 3.4 恢复原始剪贴板内容
        if (clipboardRestored && originalClipboard != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try { Clipboard.SetText(originalClipboard); } catch { }
            });
        }

        // 【步骤4】右下角气泡提示成功
        ShowToast("日期时间已插入");

        return formattedTime;
    }
    catch (Exception ex)
    {
        ShowError($"插入失败，请确保 Obsidian 处于编辑状态。\n错误详情：{ex.Message}");
        return $"ERROR: {ex.Message}";
    }
}

// 模态弹窗提示（用于错误和重要结果）
private static void ShowError(string message)
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        MessageBox.Show(message, "Obsidian 日期时间插入", MessageBoxButton.OK, MessageBoxImage.Warning);
    });
}

// 右下角气泡通知（动态反射定位 Quicker Toast Notifier，兼容各版本混淆名）
private static void ShowToast(string message)
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        try
        {
            var mw = Application.Current.MainWindow;
            if (mw == null) return;

            object notifier = mw.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f => f.FieldType.FullName?.Contains("ToastNotifications.Notifier") == true)
                ?.GetValue(mw);

            if (notifier == null) return;

            var extType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                .FirstOrDefault(t => t.FullName == "ToastNotifications.Messages.SuccessExtensions");

            if (extType == null) return;

            var method = extType.GetMethod("ShowSuccess", new[] { notifier.GetType(), typeof(string) });
            method?.Invoke(null, new object[] { notifier, message });
        }
        catch
        {
            // 降级：Toast 失败不影响主功能，静默处理
        }
    });
}
