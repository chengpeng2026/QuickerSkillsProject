//css_ref System.Windows.Forms.dll
//css_ref System.Drawing.dll

using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Quicker.Public;

// ============================================================
// RtfNumberMultiply  v1.0.1  Build: 20260723
// RTF 中选中数字 ×1.3，结果自动复制到剪贴板
// 弹窗 2s 后自动关闭
// Roslyn v2 零样板模式：禁止 namespace/class
// ============================================================

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

const byte VK_CONTROL = 0x11;
const byte VK_C = 0x43;
const uint KEYEVENTF_KEYUP = 0x0002;

public static string Exec(IStepContext context)
{
    try
    {
        // —— 步骤1：清空剪贴板，避免读到旧数据 ——
        Clipboard.Clear();
        Thread.Sleep(60);

        // —— 步骤2：模拟 Ctrl+C 复制选中内容 ——
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);              // Ctrl 按下
        keybd_event(VK_C, 0, 0, UIntPtr.Zero);                     // C 按下
        Thread.Sleep(50);
        keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);       // C 松开
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Ctrl 松开
        Thread.Sleep(150);

        // —— 步骤3：读取剪贴板 ——
        string clipText = string.Empty;
        for (int i = 0; i < 10; i++)
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
            Thread.Sleep(50);
        }

        if (string.IsNullOrWhiteSpace(clipText))
        {
            MessageBox.Show("未检测到选中内容。\n请在 RTF 中选中一个数字后重试。",
                "RTF 数字乘1.3", MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
            return "CANCELLED:NO_SELECTION";
        }

        // —— 步骤4：从剪贴板内容中提取数字 ——
        // 支持中文/英文数字、正负、小数（去掉千分位逗号）
        string cleaned = clipText.Trim().Replace(",", "").Replace("，", "").Replace(" ", "").Replace(" ", "");

        // 尝试匹配第一个数字（支持正负号、小数点）
        Match match = Regex.Match(cleaned, @"[-+]?\d+\.?\d*");
        if (!match.Success)
        {
            MessageBox.Show(
                $"未识别到有效数字。\n\n选中的内容是：\n\"{clipText.Trim()}\"",
                "RTF 数字乘1.3", MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
            return "CANCELLED:NO_NUMBER";
        }

        if (!double.TryParse(match.Value, out double originalValue))
        {
            MessageBox.Show($"无法解析数字：{match.Value}",
                "RTF 数字乘1.3", MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
            return "CANCELLED:PARSE_ERROR";
        }

        // —— 步骤5：计算 ×1.3 ——
        double result = originalValue * 1.3;

        // 保留合理精度：输入几位小数，结果保留几位（最多6位）
        string originalStr = match.Value;
        int decimals = 0;
        int dotIndex = originalStr.IndexOf('.');
        if (dotIndex >= 0) decimals = originalStr.Length - dotIndex - 1;
        string resultStr = Math.Round(result, Math.Min(decimals + 1, 6)).ToString();

        // —— 步骤6：复制结果到剪贴板 ——
        Clipboard.SetText(resultStr);
        Thread.Sleep(50);

        // —— 步骤7：自动关闭弹窗 ——
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        Form popup = new Form
        {
            Text = "RTF 数字乘1.3",
            Size = new Size(160, 90),
            StartPosition = FormStartPosition.Manual,
            // 定位到屏幕右上角
            Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - 340, 40),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true
        };
        Label lblResult = new Label
        {
            Text = resultStr,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(76, 175, 80),
            AutoSize = true
        };
        Label lblHint = new Label
        {
            Text = "已复制到剪贴板 (2s 后自动关闭)",
            Font = new Font("Segoe UI", 7),
            ForeColor = Color.Gray,
            AutoSize = true
        };
        popup.Controls.Add(lblResult);
        popup.Controls.Add(lblHint);
        popup.Load += (s, e) =>
        {
            // 居中排列
            int totalH = lblResult.Height + lblHint.Height + 6;
            int startY = (popup.ClientSize.Height - totalH) / 2;
            lblResult.Location = new Point((popup.ClientSize.Width - lblResult.Width) / 2, startY);
            lblHint.Location = new Point((popup.ClientSize.Width - lblHint.Width) / 2, startY + lblResult.Height + 6);
        };
        timer.Tick += (s, e) => { timer.Stop(); popup.Close(); };
        timer.Start();
        popup.ShowDialog();

        return resultStr;
    }
    catch (Exception ex)
    {
        MessageBox.Show($"执行异常：{ex.Message}",
            "RTF 数字乘1.3", MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
        return "ERROR: " + ex.Message;
    }
}
