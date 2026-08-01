//css_ref System.Windows.Forms.dll
//css_ref System.Drawing.dll

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Quicker.Public;

// ============================================================
// RtfNumberMultiply  v2.1.5  Build: 20260802
// 浮动弹窗常驻 ﾗ1.3 累加器
// 10 个固定槽位，点追加自动复制选中数字并计算
// 结果顿号分隔 + KN 后缀实时更新剪贴板
// v2.1.3: 弹窗加宽，追加旁新增「清除」按钮（确认框+同步清空剪贴板）
// v2.1.4: 修复 SetText("") 在沙盒抛异常 → 多级兜底清空剪贴板
// v2.1.5: 修复追加无结果bug → 焦点检测确认WPS前台再Ctrl+C
// Roslyn v2 零样板模式：禁止 namespace/class
// ============================================================

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern IntPtr GetForegroundWindow();

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

const byte VK_CONTROL = 0x11;
const byte VK_C = 0x43;
const uint KEYEVENTF_KEYUP = 0x0002;

// 槽位数据：最多 10 个
static List<string> slots = new List<string>();
static List<Label> slotLabels = new List<Label>();
static List<Button> deleteButtons = new List<Button>();
static Form popup;

public static string Exec(IStepContext context)
{
    try
    {
        // 初始化 10 个空槽位
        slots.Clear();
        for (int i = 0; i < 10; i++) slots.Add(null);

        // 构建浮动弹窗
        popup = new Form
        {
            Text = "RTF 数字乘1.3",
            Size = new Size(350, 480),
            StartPosition = FormStartPosition.Manual,
            Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - 370, 40),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true
        };

        int slotStartY = 10;
        int slotHeight = 30;
        int slotSpacing = 4;

        for (int i = 0; i < 10; i++)
        {
            int idx = i;
            int y = slotStartY + idx * (slotHeight + slotSpacing);

            // 序号标签
            var lblNum = new Label
            {
                Text = (idx + 1) + ".",
                Location = new Point(8, y + 4),
                Size = new Size(22, 20),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 9)
            };
            popup.Controls.Add(lblNum);

            // 结果标签
            var lbl = new Label
            {
                Text = "",
                Location = new Point(32, y + 4),
                Size = new Size(140, 20),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(76, 175, 80)
            };
            slotLabels.Add(lbl);
            popup.Controls.Add(lbl);

            // 删除按钮
            var btnDel = new Button
            {
                Text = "✕",
                Location = new Point(178, y + 2),
                Size = new Size(26, 24),
                FlatStyle = FlatStyle.Flat,
                Visible = false,
                BackColor = Color.FromArgb(240, 240, 240),
                ForeColor = Color.FromArgb(200, 60, 60),
                Font = new Font("Segoe UI", 8, FontStyle.Bold)
            };
            btnDel.FlatAppearance.BorderSize = 0;
            int captureIdx = idx;
            btnDel.Click += (s, e) => { DeleteSlot(captureIdx); };
            deleteButtons.Add(btnDel);
            popup.Controls.Add(btnDel);
        }

        // 追加按钮
        var btnAppend = new Button
        {
            Text = "追加",
            Location = new Point(60, slotStartY + 10 * (slotHeight + slotSpacing) + 10),
            Size = new Size(130, 36),
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            BackColor = Color.FromArgb(76, 175, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnAppend.FlatAppearance.BorderSize = 0;
        btnAppend.Click += (s, e) => { AppendSlot(); };
        popup.Controls.Add(btnAppend);

        // 清除所有结果按钮（红/灰色，右侧并排）
        var btnClear = new Button
        {
            Text = "清除",
            Location = new Point(198, slotStartY + 10 * (slotHeight + slotSpacing) + 10),
            Size = new Size(90, 36),
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            BackColor = Color.FromArgb(230, 90, 90),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnClear.FlatAppearance.BorderSize = 0;
        btnClear.Click += (s, e) => { ClearAllSlots(); };
        popup.Controls.Add(btnClear);

        // 提示标签
        var lblHint = new Label
        {
            Text = "选中数字后点追加",
            Location = new Point(50, slotStartY + 10 * (slotHeight + slotSpacing) + 50),
            Size = new Size(160, 16),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 7),
            ForeColor = Color.Gray
        };
        popup.Controls.Add(lblHint);

        popup.ShowDialog();
        return "OK";
    }
    catch (Exception ex)
    {
        MessageBox.Show("执行异常：" + ex.Message,
            "RTF 数字乘1.3", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return "ERROR: " + ex.Message;
    }
}

static void AppendSlot()
{
    try
    {
        // 1. 找第一个空槽位
        int targetIdx = -1;
        for (int i = 0; i < slots.Count; i++)
        {
            if (string.IsNullOrEmpty(slots[i])) { targetIdx = i; break; }
        }
        if (targetIdx < 0)
        {
            MessageBox.Show("10 个槽位已满，请先删除一些结果。",
                "RTF 数字乘1.3", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 2. 隐藏弹窗，让焦点回到 WPS
        popup.Hide();
        Thread.Sleep(150); // 先等弹窗隐藏完成

        // 轮询等待前台窗口回到 WPS/Word（最多 2 秒），确保Ctrl+C命中正确窗口
        bool focused = false;
        for (int wait = 0; wait < 40; wait++)
        {
            IntPtr fg = GetForegroundWindow();
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(fg, sb, 256);
            string fgTitle = sb.ToString();
            if (fgTitle.Contains("WPS") || fgTitle.Contains("Word") || fgTitle.Contains("Microsoft"))
            {
                focused = true;
                break;
            }
            Thread.Sleep(50);
        }

        // 3. 模拟 Ctrl+C
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_C, 0, 0, UIntPtr.Zero);
        Thread.Sleep(100);
        keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(250);

        // 4. 读取剪贴板
        string clipText = string.Empty;
        for (int i = 0; i < 15; i++)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    clipText = Clipboard.GetText(TextDataFormat.Text);
                    if (!string.IsNullOrWhiteSpace(clipText)) break;
                }
            }
            catch { }
            Thread.Sleep(80);
        }

        // 5. 恢复弹窗
        popup.Show();
        popup.TopMost = true;
        popup.Activate();

        // 6. 提取数字
        if (string.IsNullOrWhiteSpace(clipText)) return;

        string cleaned = clipText.Trim().Replace(",", "").Replace("，", "").Replace(" ", "").Replace(" ", "");
        Match match = Regex.Match(cleaned, @"[-+]?\d+\.?\d*");
        if (!match.Success) return;

        if (!double.TryParse(match.Value, out double originalValue)) return;

        // 7. 计算 ×1.3，保留两位小数
        double result = originalValue * 1.3;
        string resultStr = Math.Round(result, 2).ToString("F2");

        // 8. 填入槽位
        slots[targetIdx] = resultStr;
        UpdateUI();
        UpdateClipboard();
    }
    catch (Exception ex)
    {
        try { popup.Show(); popup.TopMost = true; } catch { }
        MessageBox.Show("追加失败：" + ex.Message,
            "RTF 数字乘1.3", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

static void DeleteSlot(int idx)
{
    slots[idx] = null;
    UpdateUI();
    UpdateClipboard();
}

// 清除所有结果：确认框 → 清空槽位 → 清空剪贴板（多级兜底）
static void ClearAllSlots()
{
    try
    {
        // 防误触：确认框
        var confirm = MessageBox.Show("确定清空全部结果？", "RTF 数字乘1.3",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK) return;

        // 清空全部槽位
        for (int i = 0; i < slots.Count; i++) slots[i] = null;
        UpdateUI();

        // 同步清空剪贴板（防旧结果残留被下次追加误读，多级兜底）
        ClearClipboard();
    }
    catch (Exception ex)
    {
        MessageBox.Show("清空失败：" + ex.Message,
            "RTF 数字乘1.3", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

// 清空剪贴板：SetText("") 在 Roslyn v2 沙盒可能抛异常（ArgumentNullException），
// 失败则回退到 PowerShell 子进程真清空（[Clipboard]::Clear()，非沙盒可靠）
static void ClearClipboard()
{
    try
    {
        Clipboard.SetText("");
        return; // 沙盒内可用，直接成功
    }
    catch { }

    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -STA -Command \"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Clipboard]::Clear()\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using (var p = System.Diagnostics.Process.Start(psi))
        {
            if (p != null) p.WaitForExit(5000);
        }
    }
    catch { }
}

static void UpdateUI()
{
    for (int i = 0; i < 10; i++)
    {
        bool hasValue = !string.IsNullOrEmpty(slots[i]);
        slotLabels[i].Text = hasValue ? (slots[i] + "KN") : "";
        slotLabels[i].ForeColor = Color.FromArgb(76, 175, 80);
        deleteButtons[i].Visible = hasValue;
    }
}

static void UpdateClipboard()
{
    var parts = new List<string>();
    for (int i = 0; i < slots.Count; i++)
    {
        if (!string.IsNullOrEmpty(slots[i]))
            parts.Add(slots[i] + "KN");
    }
    if (parts.Count > 0)
    {
        string combined = string.Join("、", parts);
        Clipboard.SetText(combined);
    }
    else
    {
        // 全空时清空剪贴板（复用多级兜底，避免 SetText("") 在沙盒抛异常）
        ClearClipboard();
    }
}