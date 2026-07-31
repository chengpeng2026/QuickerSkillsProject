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
// RtfNumberMultiply  v2.2.0  Build: 20260731
// 浮动弹窗常驻 ﾗ1.3 累加器
// 10 个固定槽位，空格键触发追加，窗口不隐藏
// 结果顿号分隔 + KN 后缀实时更新剪贴板
// Roslyn v2 零样板模式：禁止 namespace/class
// ============================================================

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

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
            Size = new Size(260, 430),
            StartPosition = FormStartPosition.Manual,
            Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - 280, 40),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true,
            KeyPreview = true
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

        // 提示标签
        int hintY = slotStartY + 10 * (slotHeight + slotSpacing) + 10;
        var lblHint = new Label
        {
            Text = "WPS 选中数字 → 回到此处按 空格",
            Location = new Point(20, hintY),
            Size = new Size(220, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(76, 175, 80)
        };
        popup.Controls.Add(lblHint);

        // 空格键触发追加
        popup.KeyDown += (s, e) => {
            if (e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                AppendSlot();
            }
        };

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

        // 2. 模拟 Ctrl+C（窗口不隐藏，keybd_event 不依赖焦点）
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_C, 0, 0, UIntPtr.Zero);
        Thread.Sleep(80);
        keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(200);

        // 3. 读取剪贴板
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

        // 4. 提取数字
        if (string.IsNullOrWhiteSpace(clipText)) return;

        string cleaned = clipText.Trim().Replace(",", "").Replace("，", "").Replace(" ", "").Replace(" ", "");
        Match match = Regex.Match(cleaned, @"[-+]?\d+\.?\d*");
        if (!match.Success) return;

        if (!double.TryParse(match.Value, out double originalValue)) return;

        // 5. 计算 ×1.3，保留两位小数
        double result = originalValue * 1.3;
        string resultStr = Math.Round(result, 2).ToString("F2");

        // 6. 填入槽位
        slots[targetIdx] = resultStr;
        UpdateUI();
        UpdateClipboard();
    }
    catch (Exception ex)
    {
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
        Clipboard.SetText("");
    }
}