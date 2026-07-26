//css_ref System.Windows.Forms.dll

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Quicker.Public;

// ============================================================================
// 微信→Edge v10 — 公众号文章专用版
// 微信内置浏览器没有地址栏，唯一路径:
//   点击右上角 "..." → 弹出菜单 → 点击 "复制链接" → 读剪贴板
// ============================================================================

public static class Win32
{
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public const int SW_RESTORE = 9;
}

[DllImport("user32.dll")]
static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

const uint LEFT_DOWN  = 0x0002;
const uint LEFT_UP    = 0x0004;

// ============================================================================
public static string Exec(IStepContext context)
{
    // ── 找 WeChatAppEx 浏览器窗口 ──
    IntPtr hWnd = FindBrowserWindow();
    if (hWnd == IntPtr.Zero)
    {
        ShowMsg("未找到微信浏览器窗口。\n请先打开微信→公众号文章→等页面加载完成。");
        return "FAIL:NO_WINDOW";
    }

    // ── 获取窗口尺寸 ──
    Win32.RECT r;
    Win32.GetWindowRect(hWnd, out r);
    int winW = r.Right - r.Left;
    int winH = r.Bottom - r.Top;

    // ── 激活窗口 ──
    if (Win32.IsIconic(hWnd)) Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
    Win32.SetForegroundWindow(hWnd);
    Thread.Sleep(400);

    // ── 清剪贴板 ──
    try { Clipboard.Clear(); } catch { }
    Thread.Sleep(60);

    // ═══════════════════════════════════════════════════════════
    // 核心流程:
    //   步骤1: 点击右上角 "..." 按钮
    //   步骤2: 弹出菜单后, 逐行点击找 "复制链接"
    //   步骤3: 读剪贴板 URL
    // ═══════════════════════════════════════════════════════════

    // ── 步骤1: 点击右上角 "..." ──
    // 微信浏览器右上角 "..." 位置: X≈窗口右边缘-60px, Y≈标题栏下
    int menuBtnX = r.Left + winW - 60;
    int menuBtnY = r.Top + 45;

    Click(menuBtnX, menuBtnY);
    Thread.Sleep(600);  // 等菜单弹出

    // ── 步骤2: 逐行扫描菜单项 ──
    // 微信浏览器菜单从标题栏下约 80px 开始, 每项约 45px 高
    // 菜单项顺序通常: 分享给朋友→朋友圈→收藏→复制链接→在浏览器中打开→...
    // "复制链接" 通常是第 4 项 (index 3)
    // 菜单在窗口右侧, 内容区域约 X=窗口左起 60% 到右边缘-20px

    int menuStartY = r.Top + 85;   // 菜单第一项起始 Y
    int menuItemH  = 45;           // 每项高度
    int menuClickX = r.Left + (int)(winW * 0.75);  // 菜单内容区域中偏右

    string url = null;

    // 扫描菜单第 0-7 项
    for (int idx = 0; idx < 8; idx++)
    {
        int clickY = menuStartY + idx * menuItemH;
        if (clickY > r.Top + winH - 30) break;  // 超出窗口

        // 每次尝试前清剪贴板
        try { Clipboard.Clear(); } catch { }
        Thread.Sleep(40);

        // 点击这一项
        Click(menuClickX, clickY);
        Thread.Sleep(300);

        // 读剪贴板
        url = ReadUrl();
        if (url != null) goto FOUND;
    }

    // ── 如果没找到，可能菜单没弹出，重试 ──
    // 再次点击 "..." 然后重试
    Click(menuBtnX, menuBtnY);
    Thread.Sleep(600);

    for (int idx = 0; idx < 6; idx++)
    {
        int clickY = menuStartY + idx * menuItemH;
        try { Clipboard.Clear(); } catch { }
        Thread.Sleep(40);
        Click(menuClickX, clickY);
        Thread.Sleep(350);
        url = ReadUrl();
        if (url != null) goto FOUND;
    }

FOUND:
    if (url != null)
    {
        // ── 关闭菜单 (点空白区域) ──
        Click(r.Left + winW / 2, r.Top + 500);
        Thread.Sleep(200);

        // ── 启动 Edge ──
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { }
        return "OK:" + url;
    }

    // ── 完全失败 ──
    ShowMsg(
        "未能从菜单中找到"复制链接"。\n\n" +
        "请手动操作：\n" +
        "1. 在微信浏览器中点击右上角 [...]\n" +
        "2. 找到 [复制链接] 并用鼠标点击\n" +
        "3. 然后触发本动作，会自动检测剪贴板中的 URL 并打开 Edge");
    return "FAIL";
}

// ============================================================================
// 找 WeChatAppEx 浏览器窗口
// ============================================================================
static IntPtr FindBrowserWindow()
{
    string[] names = { "WeChatAppEx", "RadiumWMPF" };

    foreach (string name in names)
    {
        try
        {
            foreach (Process p in Process.GetProcessesByName(name))
            {
                if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                    return p.MainWindowHandle;
            }
        }
        catch { }
    }

    // 枚举顶层窗口
    IntPtr result = IntPtr.Zero;
    Win32.EnumWindows((hWnd, lParam) =>
    {
        if (!Win32.IsWindowVisible(hWnd)) return true;
        uint pidUint;
        Win32.GetWindowThreadProcessId(hWnd, out pidUint);
        try
        {
            var proc = Process.GetProcessById((int)pidUint);
            if (proc.ProcessName == "WeChatAppEx" || proc.ProcessName == "RadiumWMPF")
            {
                var sb = new StringBuilder(512);
                Win32.GetWindowText(hWnd, sb, sb.Capacity);
                if (!string.IsNullOrWhiteSpace(sb.ToString()))
                {
                    result = hWnd;
                    return false;
                }
            }
        }
        catch { }
        return true;
    }, IntPtr.Zero);

    return result;
}

// ============================================================================
// 工具
// ============================================================================
static void Click(int x, int y)
{
    Win32.SetCursorPos(x, y);
    Thread.Sleep(25);
    mouse_event(LEFT_DOWN, 0, 0, 0, UIntPtr.Zero);
    Thread.Sleep(12);
    mouse_event(LEFT_UP, 0, 0, 0, UIntPtr.Zero);
    Thread.Sleep(15);
}

static string ReadUrl()
{
    for (int i = 0; i < 5; i++)
    {
        try
        {
            if (!Clipboard.ContainsText()) { Thread.Sleep(80); continue; }
            string t = Clipboard.GetText(TextDataFormat.Text);
            if (string.IsNullOrWhiteSpace(t)) { Thread.Sleep(80); continue; }
            t = Regex.Replace(t.Trim(), @"[\p{C}-[\t\r\n]]", "");

            foreach (Match m in Regex.Matches(t, @"https?://[^\s""'<>，。；;、]+"))
            {
                string u = m.Value.TrimEnd('.', ',', ')', ']', '，', '。');
                if (u.Length > 12) return u;
            }
            return null;
        }
        catch { Thread.Sleep(100); }
    }
    return null;
}

static void ShowMsg(string m)
{
    MessageBox.Show(m, "微信→Edge",
        MessageBoxButtons.OK, MessageBoxIcon.Information,
        MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
}
