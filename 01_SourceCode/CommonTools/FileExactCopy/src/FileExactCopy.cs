//css_ref System.Windows.Forms.dll
//css_ref System.Drawing.dll

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Quicker.Public;

// ============================================================
// FileExactCopy  v1.0.0  Build: 20260727
// 文件精准复制：预设源文件夹 + 精确文件名 → 多选 → 复制到目标
// Roslyn v2 零样板模式：禁止 namespace/class
// ============================================================

public static string Exec(IStepContext context)
{
    try
    {
        string sourceFolder = (context.GetVarValue("sourceFolder") as string ?? "").Trim();
        string fileName = (context.GetVarValue("fileName") as string ?? "").Trim();
        string menuKey = context.GetVarValue("menuKey") as string ?? "";

        // —— 右键菜单：配置源文件夹和文件名 ——
        if (menuKey == "config")
            return ConfigDialog(context, sourceFolder, fileName);

        // —— 校验源文件夹 ——
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show("源文件夹路径无效或不存在。\n请右键 → [设置] 配置源文件夹。",
                    "文件精准复制", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
            return "CANCELLED:INVALID_SOURCE";
        }

        // —— 解析文件名（分号分隔） ——
        var names = fileName.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        if (names.Count == 0)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show("未设置文件名。\n请右键 → [设置] 配置文件名（多个用分号 ; 分隔）。",
                    "文件精准复制", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
            return "CANCELLED:NO_FILENAME";
        }

        // —— 在源文件夹中精确匹配文件 ——
        var matched = new List<string>();
        foreach (var name in names)
        {
            string full = Path.Combine(sourceFolder, name);
            if (File.Exists(full))
                matched.Add(full);
        }

        if (matched.Count == 0)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    $"在源文件夹中未找到任何匹配文件。\n\n源文件夹：{sourceFolder}\n查找文件名：{string.Join("、", names)}",
                    "文件精准复制", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
            return "CANCELLED:NO_MATCH";
        }

        // —— 选择要复制的文件 ——
        List<string> selected;
        if (matched.Count == 1)
        {
            selected = matched; // 仅一个文件，直接选中
        }
        else
        {
            List<string> picked = null;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                picked = ShowMultiSelect(matched);
            });
            if (picked == null || picked.Count == 0)
                return "CANCELLED:USER_SKIP";
            selected = picked;
        }

        // —— 选择目标文件夹 ——
        string targetFolder = null;
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            using (var dlg = new FolderBrowserDialog
            {
                Description = "选择目标文件夹 — 文件将复制到此处",
                ShowNewFolderButton = true
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    targetFolder = dlg.SelectedPath;
            }
        });

        if (string.IsNullOrWhiteSpace(targetFolder))
            return "CANCELLED:NO_TARGET";

        // —— 自动创建目标文件夹 ——
        if (!Directory.Exists(targetFolder))
        {
            try { Directory.CreateDirectory(targetFolder); }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"无法创建目标文件夹：{ex.Message}",
                        "文件精准复制", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
                return "ERROR:CREATE_TARGET_FAIL";
            }
        }

        // —— 逐个复制文件 ——
        int copied = 0, skipped = 0;
        foreach (var src in selected)
        {
            string name = Path.GetFileName(src);
            string dest = Path.Combine(targetFolder, name);

            // 同名冲突：弹窗询问
            if (File.Exists(dest))
            {
                bool overwrite = false;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    overwrite = MessageBox.Show(
                        $"目标已存在同名文件：\n{name}\n\n是否覆盖？\n[是] 覆盖   [否] 跳过",
                        "文件精准复制 — 同名冲突",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                });
                if (!overwrite) { skipped++; continue; }
            }

            // 执行复制
            try
            {
                File.Copy(src, dest, true);
                copied++;
            }
            catch (Exception ex)
            {
                bool goOn = false;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    goOn = MessageBox.Show(
                        $"复制失败：{name}\n{ex.Message}\n\n是否继续复制剩余文件？",
                        "文件精准复制 — 错误",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes;
                });
                if (!goOn) break;
                skipped++;
            }
        }

        // —— Toast 通知结果 ——
        string summary = copied > 0
            ? $"成功复制 {copied} 个文件" + (skipped > 0 ? $"（跳过 {skipped} 个）" : "")
            : $"未复制任何文件（跳过 {skipped} 个）";
        System.Windows.Application.Current.Dispatcher.Invoke(() => ShowToast(summary));

        return $"OK: copied={copied}, skipped={skipped}";
    }
    catch (Exception ex)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            MessageBox.Show($"执行异常：{ex.Message}", "文件精准复制",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        });
        return "ERROR: " + ex.Message;
    }
}

// ============================================================
// 配置对话框：设置源文件夹和文件名
// ============================================================
static string ConfigDialog(IStepContext context, string srcFolder, string fName)
{
    string newSrc = srcFolder;
    string newName = fName;
    bool saved = false;

    System.Windows.Application.Current.Dispatcher.Invoke(() =>
    {
        var form = new Form
        {
            Text = "文件精准复制 — 配置",
            Size = new Size(620, 260),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true
        };

        var lbl1 = new Label { Text = "源文件夹：", Location = new Point(20, 28), AutoSize = true };
        var txtSrc = new TextBox { Text = srcFolder, Location = new Point(110, 25), Width = 370 };
        var btnBrowse = new Button { Text = "浏览...", Location = new Point(490, 23), Size = new Size(80, 26) };
        btnBrowse.Click += (s, e) =>
        {
            using (var dlg = new FolderBrowserDialog { Description = "选择源文件夹" })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    txtSrc.Text = dlg.SelectedPath;
            }
        };

        var lbl2 = new Label { Text = "文件名：", Location = new Point(20, 70), AutoSize = true };
        var txtName = new TextBox { Text = fName, Location = new Point(110, 67), Width = 460 };
        var hint = new Label
        {
            Text = "多个文件名用分号 ; 分隔，如：报告.docx;图纸.pdf",
            Location = new Point(110, 94),
            AutoSize = true,
            ForeColor = Color.Gray
        };

        var btnOk = new Button { Text = "保存", Location = new Point(400, 170), Size = new Size(80, 30) };
        var btnCancel = new Button { Text = "取消", Location = new Point(490, 170), Size = new Size(80, 30) };

        btnOk.Click += (s, e) =>
        {
            newSrc = txtSrc.Text.Trim();
            newName = txtName.Text.Trim();
            saved = true;
            form.DialogResult = DialogResult.OK;
            form.Close();
        };
        btnCancel.Click += (s, e) =>
        {
            form.DialogResult = DialogResult.Cancel;
            form.Close();
        };

        form.Controls.AddRange(new Control[] { lbl1, txtSrc, btnBrowse, lbl2, txtName, hint, btnOk, btnCancel });
        form.ShowDialog();
    });

    if (saved)
    {
        context.SetVarValue("sourceFolder", newSrc);
        context.SetVarValue("fileName", newName);
        ShowToast("配置已保存");
    }

    return saved ? "CONFIG_SAVED" : "CONFIG_CANCELLED";
}

// ============================================================
// 多选窗口（优先 Quicker SelectOperationWindow，降级 WinForms）
// ============================================================
static List<string> ShowMultiSelect(List<string> filePaths)
{
    // 尝试 Quicker 原生多选窗口
    try
    {
        var asms = AppDomain.CurrentDomain.GetAssemblies();
        Type itemType = null, winType = null, locType = null;
        foreach (var asm in asms)
        {
            try
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.FullName == "Quicker.View.SimpleOperationItem") itemType = t;
                    if (t.FullName == "Quicker.View.SelectOperationWindow") winType = t;
                    if (t.FullName == "Quicker.Domain.ShowWindowLocation") locType = t;
                }
            }
            catch { }
            if (itemType != null && winType != null && locType != null) break;
        }

        if (itemType != null && winType != null && locType != null)
        {
            var listType = typeof(List<>).MakeGenericType(itemType);
            var items = Activator.CreateInstance(listType);
            var addMethod = items.GetType().GetMethod("Add");

            foreach (var fp in filePaths)
            {
                var item = Activator.CreateInstance(itemType);
                string fn = Path.GetFileName(fp);
                itemType.GetProperty("Name")?.SetValue(item, $"[fa:Regular_File:#2196F3] {fn}");
                itemType.GetProperty("Tooltip")?.SetValue(item, fp);
                addMethod.Invoke(items, new[] { item });
            }

            object loc = Enum.Parse(locType, "WithMouse1");
            var win = Activator.CreateInstance(winType, new[] { items, loc, true, true, true, 14.0 });
            var showMethod = winType.GetMethod("ShowDialog");

            if ((bool)showMethod.Invoke(win, null))
            {
                var result = new List<string>();

                // 多选：尝试 SelectedItems
                var selItemsProp = winType.GetProperty("SelectedItems");
                if (selItemsProp != null)
                {
                    var selItems = selItemsProp.GetValue(win) as System.Collections.IList;
                    if (selItems != null && selItems.Count > 0)
                    {
                        foreach (var si in selItems)
                        {
                            string tip = itemType.GetProperty("Tooltip")?.GetValue(si) as string;
                            if (!string.IsNullOrEmpty(tip)) result.Add(tip);
                        }
                    }
                }

                // 单选降级：SelectedItem
                if (result.Count == 0)
                {
                    var selItem = winType.GetProperty("SelectedItem")?.GetValue(win);
                    if (selItem != null)
                    {
                        string tip = itemType.GetProperty("Tooltip")?.GetValue(selItem) as string;
                        if (!string.IsNullOrEmpty(tip)) result.Add(tip);
                    }
                }

                if (result.Count > 0) return result;
            }
        }
    }
    catch { }

    // 降级：WinForms CheckedListBox
    return FallbackMultiSelect(filePaths);
}

// ============================================================
// 降级多选：WinForms CheckedListBox
// ============================================================
static List<string> FallbackMultiSelect(List<string> filePaths)
{
    var selected = new List<string>();

    var form = new Form
    {
        Text = "选择要复制的文件",
        Size = new Size(500, 380),
        StartPosition = FormStartPosition.CenterScreen,
        FormBorderStyle = FormBorderStyle.FixedDialog,
        MaximizeBox = false,
        MinimizeBox = false,
        TopMost = true
    };

    var clb = new CheckedListBox
    {
        Location = new Point(15, 15),
        Size = new Size(455, 250),
        CheckOnClick = true
    };
    foreach (var fp in filePaths)
        clb.Items.Add(Path.GetFileName(fp), true); // 默认全选

    var btnAll = new Button { Text = "全选", Location = new Point(15, 275), Size = new Size(70, 28) };
    var btnNone = new Button { Text = "全不选", Location = new Point(95, 275), Size = new Size(70, 28) };
    var btnOk = new Button { Text = "确定", Location = new Point(300, 275), Size = new Size(80, 28) };
    var btnCancel = new Button { Text = "取消", Location = new Point(390, 275), Size = new Size(80, 28) };

    btnAll.Click += (s, e) =>
    {
        for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, true);
    };
    btnNone.Click += (s, e) =>
    {
        for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, false);
    };
    btnOk.Click += (s, e) =>
    {
        for (int i = 0; i < clb.Items.Count; i++)
            if (clb.GetItemChecked(i))
                selected.Add(filePaths[i]);
        form.DialogResult = DialogResult.OK;
        form.Close();
    };
    btnCancel.Click += (s, e) =>
    {
        form.DialogResult = DialogResult.Cancel;
        form.Close();
    };

    form.Controls.AddRange(new Control[] { clb, btnAll, btnNone, btnOk, btnCancel });
    form.ShowDialog();

    return selected;
}

// ============================================================
// Toast 悬浮通知
// ============================================================
static void ShowToast(string message)
{
    try
    {
        var mw = System.Windows.Application.Current.MainWindow;
        if (mw == null) return;

        var notifierField = mw.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(f => f.FieldType.FullName?.Contains("ToastNotifications.Notifier") == true);

        if (notifierField == null) return;
        var notifier = notifierField.GetValue(mw);
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
        // 静默降级 — Toast 不是核心功能
    }
}