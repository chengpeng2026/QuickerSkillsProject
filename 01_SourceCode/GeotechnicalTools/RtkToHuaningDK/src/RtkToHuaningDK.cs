// ============================================================
// RtkToHuaningDK  v1.0.0  Build: 20260715
// RTK测点数据导入华宁DK文件
// 读取RTK设备DAT测点数据，自动清洗/转换/坐标顺序适配后
// 追加写入华宁岩土勘察DK文件，写入前自动备份原文件
// Roslyn v2 零样板模式：禁止 namespace/class 包裹入口函数
// ============================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Quicker.Public;

// ============================================================
// 数据结构定义（辅助类，允许在 Roslyn v2 中定义）
// ============================================================

/// <summary>有效测点记录</summary>
public class PointRecord
{
    public string PointId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double H { get; set; }
}

/// <summary>无效行记录</summary>
public class InvalidRow
{
    public int LineNumber { get; set; }
    public string Reason { get; set; }
    public string RawContent { get; set; }
}

/// <summary>DAT解析结果</summary>
public class DatParseResult
{
    public List<PointRecord> ValidPoints { get; set; } = new List<PointRecord>();
    public List<InvalidRow> InvalidRows { get; set; } = new List<InvalidRow>();
    public int TotalLines { get; set; }
    public int SkippedLines { get; set; }
}

// ============================================================
// 进度窗口（System.Windows.Forms，兼容后台STA线程）
// ============================================================

public class ProgressForm : Form
{
    private Label lblStatus;
    private ProgressBar progressBar;
    private Label lblDetail;

    public ProgressForm(string title)
    {
        this.Text = title;
        this.Size = new System.Drawing.Size(480, 150);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ControlBox = false;
        this.TopMost = true;

        lblStatus = new Label
        {
            Text = "正在初始化...",
            Location = new System.Drawing.Point(20, 15),
            AutoSize = true,
            Font = new System.Drawing.Font("Microsoft YaHei", 10F, System.Drawing.FontStyle.Regular)
        };

        progressBar = new ProgressBar
        {
            Location = new System.Drawing.Point(20, 45),
            Size = new System.Drawing.Size(425, 25),
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100
        };

        lblDetail = new Label
        {
            Text = "",
            Location = new System.Drawing.Point(20, 80),
            AutoSize = true,
            ForeColor = System.Drawing.Color.Gray
        };

        this.Controls.Add(lblStatus);
        this.Controls.Add(progressBar);
        this.Controls.Add(lblDetail);
    }

    public void UpdateProgress(int percent, string status, string detail)
    {
        if (this.InvokeRequired)
        {
            this.Invoke(new Action(() => UpdateProgress(percent, status, detail)));
            return;
        }
        lblStatus.Text = status ?? lblStatus.Text;
        progressBar.Value = Math.Max(0, Math.Min(100, percent));
        lblDetail.Text = detail ?? "";
        Application.DoEvents();
    }
}

// ============================================================
// 入口函数
// ============================================================

public static string Exec(IStepContext context)
{
    // ============================================================
    // 步骤1：获取源DAT文件路径（双模式自适应）
    // ============================================================
    string datFilePath = TryGetSelectedDatFile(context);

    if (string.IsNullOrEmpty(datFilePath))
    {
        // 非资源管理器选中场景，弹窗让用户手动选择
        using (var dlg = new OpenFileDialog())
        {
            dlg.Title = "请选择RTK测点DAT源文件";
            dlg.Filter = "DAT测点文件 (*.dat)|*.dat|所有文件 (*.*)|*.*";
            dlg.CheckFileExists = true;
            if (dlg.ShowDialog() != DialogResult.OK)
                return ""; // 用户取消，静默终止
            datFilePath = dlg.FileName;
        }
    }

    // ============================================================
    // 步骤2：弹窗引导用户选择目标华宁DK文件
    // ============================================================
    string dkFilePath;
    using (var dlg = new OpenFileDialog())
    {
        dlg.Title = "请选择目标华宁DK文件";
        dlg.Filter = "华宁DK文件 (*.dk)|*.dk|所有文件 (*.*)|*.*";
        dlg.CheckFileExists = true;
        if (dlg.ShowDialog() != DialogResult.OK)
            return ""; // 用户取消，静默终止
        dkFilePath = dlg.FileName;
    }

    // 校验目标DK文件可读性与写入权限
    try
    {
        if (!File.Exists(dkFilePath))
        {
            MessageBox.Show($"目标DK文件不存在：\n{dkFilePath}", "文件错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return "ERROR: DK文件不存在";
        }
        // 快速测试写入权限
        using (var fs = File.OpenWrite(dkFilePath))
        {
            // 仅测试，不写入
        }
    }
    catch (UnauthorizedAccessException)
    {
        MessageBox.Show($"目标DK文件无写入权限：\n{dkFilePath}\n\n请检查文件是否被其他程序占用或为只读属性。", "权限错误",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
        return "ERROR: DK文件无写入权限";
    }
    catch (Exception ex)
    {
        MessageBox.Show($"目标DK文件校验失败：\n{ex.Message}", "文件错误",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
        return "ERROR: DK文件校验失败 - " + ex.Message;
    }

    // ============================================================
    // 步骤3：自动备份原DK文件
    // ============================================================
    string dkDir = Path.GetDirectoryName(dkFilePath);
    string dkNameWithoutExt = Path.GetFileNameWithoutExtension(dkFilePath);
    string dkExt = Path.GetExtension(dkFilePath);
    string backupFileName = $"{dkNameWithoutExt}_备份_{DateTime.Now:yyyyMMddHHmmss}{dkExt}";
    string backupFilePath = Path.Combine(dkDir, backupFileName);

    try
    {
        File.Copy(dkFilePath, backupFilePath, overwrite: false);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"备份DK文件失败：\n{ex.Message}\n\n为保证数据安全，后续写入操作已终止。", "备份失败",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
        return "ERROR: 备份DK文件失败 - " + ex.Message;
    }

    // ============================================================
    // 步骤4：解析源DAT文件
    // ============================================================
    // 显示进度窗口
    ProgressForm progressForm = null;
    try
    {
        progressForm = new ProgressForm("RTK测点数据导入");
        progressForm.Show();
        Application.DoEvents();
    }
    catch { /* 进度窗口非关键功能，失败则继续 */ }

    DatParseResult parseResult;
    try
    {
        parseResult = ParseDatFile(datFilePath, progressForm);
    }
    catch (Exception ex)
    {
        progressForm?.Close();
        MessageBox.Show($"DAT文件解析失败：\n{ex.Message}", "解析错误",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
        return "ERROR: DAT解析失败 - " + ex.Message;
    }

    // 前置校验：至少1行有效测点数据
    if (parseResult.ValidPoints.Count == 0)
    {
        progressForm?.Close();
        MessageBox.Show("未解析到有效测点数据，请检查文件格式与字段顺序。\n\n" +
                        $"DAT源文件：{datFilePath}\n" +
                        $"已读取行数：{parseResult.TotalLines}\n" +
                        $"跳过行数：{parseResult.SkippedLines}\n" +
                        $"无效行数：{parseResult.InvalidRows.Count}",
                        "无有效数据", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return "ERROR: DAT文件无有效测点数据";
    }

    // ============================================================
    // 步骤5：单次测点数量超过1000条时弹窗确认
    // ============================================================
    if (parseResult.ValidPoints.Count > 1000)
    {
        // 进度窗口可能遮挡确认对话框，先隐藏
        if (progressForm != null)
        {
            try { progressForm.Hide(); } catch { }
        }

        // 注意：Quick.Public 命名空间和 System.Windows.Forms 的 DialogResult 是分开的，
        // 这里使用完全限定的 System.Windows.Forms.DialogResult
        var confirmResult = System.Windows.Forms.MessageBox.Show(
            $"检测到本次将导入 {parseResult.ValidPoints.Count} 条测点数据（超过1000条），\n" +
            $"目标DK文件：{Path.GetFileName(dkFilePath)}\n\n" +
            "是否继续导入？",
            "大量数据确认",
            System.Windows.Forms.MessageBoxButtons.YesNo,
            System.Windows.Forms.MessageBoxIcon.Question);

        if (confirmResult != System.Windows.Forms.DialogResult.Yes)
        {
            progressForm?.Close();
            return ""; // 用户取消
        }

        if (progressForm != null)
        {
            try { progressForm.Show(); } catch { }
        }
    }

    // ============================================================
    // 步骤6：字段映射转换 — DAT(点号,X,Y,H) → DK(点号,Y,X,H)
    // ============================================================
    progressForm?.UpdateProgress(85, "正在生成DK格式数据行...",
        $"已转换 {parseResult.ValidPoints.Count} 条测点数据");

    var dkLines = new List<string>();
    foreach (var pt in parseResult.ValidPoints)
    {
        // 坐标保留3位小数，高程保留2位小数
        string dkLine = $"{pt.PointId},{pt.Y:F3},{pt.X:F3},{pt.H:F2}";
        dkLines.Add(dkLine);
    }

    // ============================================================
    // 步骤7：追加写入DK文件（仅末尾追加，不修改已有数据）
    // ============================================================
    progressForm?.UpdateProgress(90, "正在写入DK文件...",
        $"目标：{Path.GetFileName(dkFilePath)}");

    try
    {
        // 确保原DK文件末尾有换行符
        string existingContent = File.ReadAllText(dkFilePath, Encoding.UTF8);
        StringBuilder sb = new StringBuilder();

        // 保留原有内容
        sb.Append(existingContent);

        // 如果原文件末尾没有换行，先补一个
        if (existingContent.Length > 0 && !existingContent.EndsWith("\r\n") && !existingContent.EndsWith("\n"))
        {
            sb.Append("\r\n");
        }
        else if (existingContent.Length == 0)
        {
            // 空文件不需要额外换行
        }
        else
        {
            // 已有换行，直接追加
        }

        // 逐行追加测点数据
        foreach (var line in dkLines)
        {
            sb.AppendLine(line);
        }

        // 原子性写入（先写临时文件，成功后再替换——但这里需求是追加，直接覆写整个文件内容）
        // 实际上为保护原始数据，我们已在步骤3做了备份
        File.WriteAllText(dkFilePath, sb.ToString(), Encoding.UTF8);
    }
    catch (Exception ex)
    {
        // 写入失败：立即还原备份文件
        progressForm?.Close();
        try
        {
            File.Copy(backupFilePath, dkFilePath, overwrite: true);
            MessageBox.Show($"写入DK文件失败，已自动还原备份文件。\n\n失败原因：{ex.Message}\n\n" +
                            $"备份文件：{backupFileName}",
                            "写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            MessageBox.Show($"写入DK文件失败，且自动还原备份也失败了！\n\n" +
                            $"写入失败原因：{ex.Message}\n\n" +
                            $"请手动将备份文件还原：\n{backupFilePath}\n→{dkFilePath}",
                            "严重错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return "ERROR: DK文件写入失败 - " + ex.Message;
    }

    // ============================================================
    // 步骤8：关闭进度窗口，弹窗展示最终结果
    // ============================================================
    progressForm?.Close();
    progressForm = null;

    // 构建结果消息
    var resultMsg = new StringBuilder();
    resultMsg.AppendLine($"✅ 导入成功！");
    resultMsg.AppendLine();
    resultMsg.AppendLine($"📊 统计信息：");
    resultMsg.AppendLine($"   成功导入测点：{parseResult.ValidPoints.Count} 条");
    resultMsg.AppendLine($"   无效数据行：{parseResult.InvalidRows.Count} 行");
    resultMsg.AppendLine($"   跳过行（空行/注释/表头）：{parseResult.SkippedLines} 行");
    resultMsg.AppendLine();
    resultMsg.AppendLine($"📁 文件信息：");
    resultMsg.AppendLine($"   目标DK文件：{Path.GetFileName(dkFilePath)}");
    resultMsg.AppendLine($"   备份文件：{backupFileName}");

    // 附加无效行明细（最多显示前20条）
    if (parseResult.InvalidRows.Count > 0)
    {
        resultMsg.AppendLine();
        resultMsg.AppendLine($"⚠️ 无效行明细（共 {parseResult.InvalidRows.Count} 条）：");

        int showCount = Math.Min(parseResult.InvalidRows.Count, 20);
        for (int i = 0; i < showCount; i++)
        {
            var row = parseResult.InvalidRows[i];
            string rawPreview = (row.RawContent ?? "").Length > 40
                ? row.RawContent.Substring(0, 40) + "..."
                : (row.RawContent ?? "");
            resultMsg.AppendLine($"   行{row.LineNumber}：{row.Reason} → \"{rawPreview}\"");
        }

        if (parseResult.InvalidRows.Count > 20)
        {
            resultMsg.AppendLine($"   ...（共{parseResult.InvalidRows.Count}条，仅显示前20条）");
        }
    }

    // 弹窗展示结果，并提供"打开目标目录"按钮
    var dialogResult = System.Windows.Forms.MessageBox.Show(
        resultMsg.ToString(),
        "RTK数据导入 — 完成",
        System.Windows.Forms.MessageBoxButtons.OK,
        System.Windows.Forms.MessageBoxIcon.Information);

    // 导入完成后打开目标文件所在目录
    try
    {
        Process.Start("explorer.exe", $"/select,\"{dkFilePath}\"");
    }
    catch { /* 浏览器打开失败非致命错误 */ }

    // 写入成功日志到返回值
    string rtnMsg = $"成功导入{parseResult.ValidPoints.Count}条测点，备份：{backupFileName}";
    try { context.SetVarValue("rtn", rtnMsg); } catch { }

    return "OK";
}

// ============================================================
// 辅助方法
// ============================================================

/// <summary>
/// 尝试从Quicker上下文中获取选中的DAT文件路径（资源管理器右键场景）
/// 多层回退：text变量 → selectedFile → selectedFiles → quicker_in_param解析
/// </summary>
private static string TryGetSelectedDatFile(IStepContext context)
{
    try
    {
        // 方式1: text变量（资源管理器选中文件的常见传递方式）
        string text = context.GetVarValue("text") as string;
        if (!string.IsNullOrWhiteSpace(text))
        {
            text = text.Trim();
            if (text.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) && File.Exists(text))
                return text;
        }

        // 方式2: selectedFile变量
        string selectedFile = context.GetVarValue("selectedFile") as string;
        if (!string.IsNullOrWhiteSpace(selectedFile))
        {
            selectedFile = selectedFile.Trim();
            if (selectedFile.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) && File.Exists(selectedFile))
                return selectedFile;
        }

        // 方式3: selectedFiles（可能是数组/列表/换行分隔字符串）
        var selectedFiles = context.GetVarValue("selectedFiles");
        if (selectedFiles != null)
        {
            // 尝试作为列表处理
            if (selectedFiles is System.Collections.IList list && list.Count > 0)
            {
                foreach (var item in list)
                {
                    string path = item?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(path) &&
                        path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(path))
                        return path;
                }
            }
            // 尝试作为换行分隔字符串
            else if (selectedFiles is string filesStr && !string.IsNullOrWhiteSpace(filesStr))
            {
                foreach (var line in filesStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string path = line.Trim();
                    if (path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                        return path;
                }
            }
        }

        // 方式4: 解析quicker_in_param中的文件路径
        string inParam = context.GetVarValue("quicker_in_param") as string;
        if (!string.IsNullOrWhiteSpace(inParam))
        {
            foreach (var part in inParam.Split('&'))
            {
                var pair = part.Split('=');
                if (pair.Length == 2)
                {
                    string val = System.Net.WebUtility.UrlDecode(pair[1]);
                    if (val.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) && File.Exists(val))
                        return val;
                }
            }
        }
    }
    catch { /* 获取失败则回退到手动选择 */ }

    return null;
}

/// <summary>
/// 全角字符转半角字符
/// 处理：全角数字(０-９)、全角字母(Ａ-Ｚ/ａ-ｚ)、全角标点(！～)、全角空格(　)
/// </summary>
private static string FullWidthToHalfWidth(string input)
{
    if (string.IsNullOrEmpty(input)) return input;

    var sb = new StringBuilder(input.Length);
    foreach (char c in input)
    {
        if (c == '　') // 全角空格
        {
            sb.Append(' ');
        }
        else if (c >= '！' && c <= '～') // 全角标点+数字+字母
        {
            sb.Append((char)(c - 0xFEE0));
        }
        else if (c >= '０' && c <= '９') // 全角数字（与FF01-FF5E有重叠，但以防万一）
        {
            sb.Append((char)(c - 0xFEE0));
        }
        else if (c >= 'Ａ' && c <= 'Ｚ') // 全角大写字母
        {
            sb.Append((char)(c - 0xFEE0));
        }
        else if (c >= 'ａ' && c <= 'ｚ') // 全角小写字母
        {
            sb.Append((char)(c - 0xFEE0));
        }
        else
        {
            sb.Append(c);
        }
    }
    return sb.ToString();
}

/// <summary>
/// 判断一行是否为应跳过的非数据行（空行、注释行、表头说明行）
/// </summary>
private static bool ShouldSkipLine(string line)
{
    if (string.IsNullOrWhiteSpace(line)) return true;

    string trimmed = line.Trim();

    // 注释行
    if (trimmed.StartsWith("#") || trimmed.StartsWith("//") || trimmed.StartsWith(";"))
        return true;

    // 纯表头说明行：不含任何看起来像数字的片段（数字或带小数点的数字）
    // 若行中完全没有任何数字，且包含中文字符或英文字母，判定为表头说明行
    bool hasDigit = false;
    bool hasChineseOrAlpha = false;
    foreach (char c in trimmed)
    {
        if (char.IsDigit(c) || c == '-' || c == '+') hasDigit = true;
        if (char.IsLetter(c)) hasChineseOrAlpha = true;
    }

    // 有字母/中文但没有数字 → 可能是表头
    if (hasChineseOrAlpha && !hasDigit) return true;

    // 仅由字母、中文、空格组成的行 → 表头
    if (!hasDigit)
    {
        int nonAlphaCount = 0;
        foreach (char c in trimmed)
        {
            if (!char.IsLetter(c) && c != ' ' && c != '\t' && c != ',')
                nonAlphaCount++;
        }
        if (nonAlphaCount == 0) return true;
    }

    return false;
}

/// <summary>
/// 自动探测DAT文件的分隔符
/// 在候选分隔符(逗号、空格、制表符)中，选择能产生合法4字段行数最多的
/// </summary>
private static char DetectDelimiter(List<string> dataLines)
{
    char[] candidates = { ',', ' ', '\t' };
    int bestScore = -1;
    char bestDelimiter = ',';

    foreach (char delim in candidates)
    {
        int score = 0;
        foreach (var line in dataLines)
        {
            var parts = line.Split(new[] { delim }, StringSplitOptions.RemoveEmptyEntries);
            // 至少要有4个字段才可能是数据行
            if (parts.Length >= 4)
            {
                // 检查后3个字段是否为有效数值（X, Y, H）
                int numericCount = 0;
                for (int i = parts.Length - 3; i < parts.Length; i++)
                {
                    if (double.TryParse(parts[i].Trim(),
                            NumberStyles.Float | NumberStyles.AllowLeadingSign,
                            CultureInfo.InvariantCulture, out _))
                    {
                        numericCount++;
                    }
                }
                if (numericCount >= 2) score++; // 至少2个数值字段视为匹配
            }
        }
        if (score > bestScore)
        {
            bestScore = score;
            bestDelimiter = delim;
        }
    }

    return bestDelimiter;
}

/// <summary>
/// 解析DAT文件，返回有效测点列表和无效行列表
/// </summary>
private static DatParseResult ParseDatFile(string datFilePath, ProgressForm progressForm)
{
    var result = new DatParseResult();

    // 读取全部行
    string[] rawLines;
    try
    {
        rawLines = File.ReadAllLines(datFilePath, Encoding.UTF8);
    }
    catch
    {
        // UTF-8 失败则尝试系统默认编码
        rawLines = File.ReadAllLines(datFilePath, Encoding.Default);
    }

    result.TotalLines = rawLines.Length;

    // 第一步：全角转半角 + 跳过空行/注释/表头
    var dataLines = new List<string>(); // 只包含候选数据行
    var dataLineIndexMap = new Dictionary<int, int>(); // dataLines索引 → 原始行号

    for (int i = 0; i < rawLines.Length; i++)
    {
        string line = rawLines[i];
        string converted = FullWidthToHalfWidth(line ?? "");

        if (ShouldSkipLine(converted))
        {
            result.SkippedLines++;
            continue;
        }

        dataLines.Add(converted);
        dataLineIndexMap[dataLines.Count - 1] = i + 1; // 行号从1开始
    }

    if (dataLines.Count == 0)
        return result; // 无有效数据行

    progressForm?.UpdateProgress(20, "正在识别分隔符...",
        $"候选数据行：{dataLines.Count} 行");

    // 第二步：自动识别分隔符
    char delimiter = DetectDelimiter(dataLines);

    progressForm?.UpdateProgress(30, $"分隔符已识别：\"{DelimiterDisplay(delimiter)}\"",
        $"正在解析 {dataLines.Count} 行数据...");

    // 第三步：逐行解析提取字段
    for (int i = 0; i < dataLines.Count; i++)
    {
        string line = dataLines[i];
        int lineNumber = dataLineIndexMap[i];

        // 更新进度（30% ~ 70%）
        if (i % 50 == 0 || i == dataLines.Count - 1)
        {
            int percent = 30 + (int)(40.0 * i / dataLines.Count);
            progressForm?.UpdateProgress(percent, $"正在解析DAT数据...",
                $"第 {i + 1}/{dataLines.Count} 行（行号 {lineNumber}）");
        }

        try
        {
            // 按分隔符切分并去除空白
            var parts = line.Split(new[] { delimiter }, StringSplitOptions.None)
                            .Select(p => p.Trim())
                            .Where(p => !string.IsNullOrEmpty(p))
                            .ToArray();

            if (parts.Length < 4)
            {
                result.InvalidRows.Add(new InvalidRow
                {
                    LineNumber = lineNumber,
                    Reason = $"字段不足（需≥4，实际{parts.Length}）",
                    RawContent = line
                });
                continue;
            }

            // 字段顺序：点号、X坐标、Y坐标、高程
            string pointId = parts[0];

            // 解析X坐标（索引1）
            if (!double.TryParse(parts[1],
                    NumberStyles.Float | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out double x))
            {
                result.InvalidRows.Add(new InvalidRow
                {
                    LineNumber = lineNumber,
                    Reason = $"X坐标无法解析为数值：\"{parts[1]}\"",
                    RawContent = line
                });
                continue;
            }

            // 解析Y坐标（索引2）
            if (!double.TryParse(parts[2],
                    NumberStyles.Float | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out double y))
            {
                result.InvalidRows.Add(new InvalidRow
                {
                    LineNumber = lineNumber,
                    Reason = $"Y坐标无法解析为数值：\"{parts[2]}\"",
                    RawContent = line
                });
                continue;
            }

            // 解析高程（索引3）
            if (!double.TryParse(parts[3],
                    NumberStyles.Float | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out double h))
            {
                result.InvalidRows.Add(new InvalidRow
                {
                    LineNumber = lineNumber,
                    Reason = $"高程无法解析为数值：\"{parts[3]}\"",
                    RawContent = line
                });
                continue;
            }

            // 全部校验通过，加入有效列表
            result.ValidPoints.Add(new PointRecord
            {
                PointId = pointId,
                X = x,
                Y = y,
                H = h
            });
        }
        catch (Exception ex)
        {
            result.InvalidRows.Add(new InvalidRow
            {
                LineNumber = lineNumber,
                Reason = $"解析异常：{ex.Message}",
                RawContent = line
            });
        }
    }

    return result;
}

/// <summary>
/// 分隔符字符的可读显示
/// </summary>
private static string DelimiterDisplay(char delim)
{
    switch (delim)
    {
        case ',': return "逗号(,)";
        case ' ': return "空格";
        case '\t': return "制表符(Tab)";
        default: return $"'{delim}'";
    }
}
