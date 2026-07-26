//css_ref System.Windows.Forms.dll
//css_ref System.Drawing.dll

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Quicker.Public;

// ============================================================
// LiZhengRTFMerge  v1.2.2  Build: 20260726
// 理正深基 RTF 计算书 合并为 A3 横向 Word
// 封面(模板)+目录 单栏，正文 双栏，页码从正文首页=1
// Roslyn v2 零样板模式：禁止 namespace/class
//
// v1.2.2 新增：抗拔承载力→受拉承载力区间字号设为 10pt
// v1.2.1 修复：封面+目录页脚用 Range.Delete() 彻底清除
// v1.2.0 新增：封面模板自动向上搜索 RTF 目录及所有父目录，适配独立 RTF 文件夹
// v1.2.0 新增：封面模板自动向上搜索 RTF 目录及所有父目录，适配独立 RTF 文件夹
// v1.1.1 修复：WaitForExit 放 ReadToEnd 之后，不阻塞管道导致 Quicker 悬停 15s
// v1.1.0 修复：PageWidth/PageHeight 替代 PaperSize 枚举，Footers.Item() 替代 Footers()，
//   InsertBreak 替代 PageBreakBefore 避免 RTF sect 冲突，每个 RTF 插入后立即修正纸张栏数，
//   硬编码节索引 (cover=1, TOC=2, body=3+) 避免 InsertBreak 后计数错误
// ============================================================

public static string Exec(IStepContext context)
{
    try
    {
        // —— 步骤1：选择 RTF 所在目录 ——
        string rtfDir = null;
        using (var dialog = new FolderBrowserDialog())
        {
            dialog.Description = "请选择包含 RTF 计算书的目录";
            dialog.ShowNewFolderButton = false;
            if (dialog.ShowDialog() != DialogResult.OK)
                return "CANCELLED:NO_DIR";
            rtfDir = dialog.SelectedPath;
        }

        // —— 步骤2：扫描 RTF 文件（自然排序），预处理字号 ——
        var processedFiles = new List<string>();
        foreach (var f in Directory.GetFiles(rtfDir, "*.RTF")
            .OrderBy(f => NaturalSortKey(Path.GetFileName(f))))
        {
            processedFiles.Add(FixFontSizeInRtf(f, Path.GetFileName(f)));
        }
        var rtfFiles = processedFiles;

        if (rtfFiles.Count == 0)
        {
            MessageBox.Show("选择的目录中没有 RTF 文件。",
                "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return "ERROR:NO_RTF_FILES";
        }

        // —— 步骤3：选择保存位置 ——
        string savePath = null;
        using (var dialog = new SaveFileDialog())
        {
            dialog.Title = "保存合并后的 Word 文档";
            dialog.Filter = "Word 文档 (*.docx)|*.docx";
            dialog.DefaultExt = "docx";
            dialog.FileName = "合并计算书.docx";
            if (dialog.ShowDialog() != DialogResult.OK)
                return "CANCELLED:NO_SAVE_PATH";
            savePath = dialog.FileName;
        }

        // —— 步骤4：定位封面模板 ——
        string coverPath = null;
        string lookIn = rtfDir;
        while (lookIn != null)
        {
            string candidate = Path.Combine(lookIn, "封面.docx");
            if (File.Exists(candidate)) { coverPath = candidate; break; }
            lookIn = Path.GetDirectoryName(lookIn);
        }
        if (coverPath == null)
            coverPath = @"C:\Users\12089\Desktop\最终计算书\封面.docx";
        bool hasCover = File.Exists(coverPath);

        // —— 步骤5：生成 PowerShell 脚本 ——
        string psScript = BuildPowerShellScript(rtfFiles, savePath, coverPath, hasCover);

        string psFile = Path.Combine(Path.GetTempPath(),
            "LiZhengMerge_" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(psFile, psScript, new UTF8Encoding(true));

        // —— 步骤6：执行 PowerShell ——
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-ExecutionPolicy Bypass -NoProfile -File \"" + psFile + "\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(coverPath) ?? rtfDir
        };

        using (var process = Process.Start(psi))
        {
            string psOutput = process.StandardOutput.ReadToEnd();
            string psError = process.StandardError.ReadToEnd();
            process.WaitForExit(600000); // 10 分钟超时

            if (process.ExitCode != 0)
            {
                // 异步 Tick 的 stdout 不可靠 — 不做 ReadToEnd 防止死锁
                string errMsg = "PS_EXIT_" + process.ExitCode;
                try { File.Delete(psFile); } catch { }
                MessageBox.Show(
                    "Word 合并失败。\n\n错误码: " + errMsg,
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return errMsg;
            }
        }

        try { File.Delete(psFile); } catch { }

        // —— 步骤7：验证并报告 ——
        if (File.Exists(savePath))
        {
            var fi = new FileInfo(savePath);
            MessageBox.Show(
                "合并完成！\n\n文件: " + savePath +
                "\n大小: " + (fi.Length / 1024.0).ToString("F1") + " KB" +
                "\n包含 " + rtfFiles.Count + " 个 RTF 文件",
                "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return savePath;
        }
        else
        {
            MessageBox.Show("合并完成但未找到输出文件。",
                "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return "WARNING:NO_OUTPUT";
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show("执行异常: " + ex.Message,
            "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return "ERROR:" + ex.Message;
    }
}

// ================================================================
// 自然排序：数字前补零，使 "2-2.RTF" < "10-10.RTF"
// ================================================================
static string NaturalSortKey(string filename)
{
    return Regex.Replace(filename, @"\d+", m => m.Value.PadLeft(10, '0'));
}

// ================================================================
// RTF 字号预处理：抗拔承载力→受拉承载力 区间内 \fsXX → \fs20 (10pt)
// 操作在原始 RTF 的 GB2312 字节层面进行，避免 Roslyn→PS 中文编码问题
// ================================================================
static string FixFontSizeInRtf(string rtfPath, string originalName)
{
    string tmpFile = null;
    try
    {
        // 读 RTF 原始文本
        string rtf = File.ReadAllText(rtfPath, Encoding.GetEncoding(936));

        // 定位区间标题（在 RTF 中中文用 \'xx 编码）
        string need1 = @"\'bf\'b9\'b0\'ce\'b3\'d0\'d4\'d8\'c1\'a6\'d1\'e9\'cb\'e3\'bd\'e1\'b9\'fb"; // 抗拔承载力验算结果
        string need2 = @"\'ca\'dc\'c0\'ad\'b3\'d0\'d4\'d8\'c1\'a6\'d1\'e9\'cb\'e3\'bd\'e1\'b9\'fb"; // 受拉承载力验算结果

        int idx1 = rtf.IndexOf(need1, StringComparison.Ordinal);
        int idx2 = rtf.IndexOf(need2, StringComparison.Ordinal);

        // 若两个标题都存在且顺序正确（idx1 < idx2），预处理区间
        if (idx1 >= 0 && idx2 > idx1)
        {
            string prefix = rtf.Substring(0, idx1);
            string interval = rtf.Substring(idx1, idx2 - idx1);
            string suffix = rtf.Substring(idx2);

            // 替换 \fsXX 为 \fs18（小五号=9pt=18 half-pts）
            interval = Regex.Replace(interval, @"\\fs(\d+)",
                m => m.Groups[1].Value == "18" ? m.Value : @"\fs18");

            rtf = prefix + interval + suffix;
        }

        // 写入临时文件，文件名直接用原始名称（无 Guid）
        tmpFile = Path.Combine(Path.GetTempPath(), originalName);
        File.WriteAllText(tmpFile, rtf, Encoding.GetEncoding(936));

        return tmpFile;
    }
    catch
    {
        // 预处理失败 → 回退原始文件
        if (tmpFile != null) try { File.Delete(tmpFile); } catch { }
        return rtfPath;
    }
}


// ================================================================
// 构建 PowerShell 脚本
// 中文使用 [char]0xXXXX 避免编码问题
// ================================================================
static string BuildPowerShellScript(
    List<string> rtfFiles,
    string outputPath,
    string coverPath,
    bool hasCover)
{
    var sb = new StringBuilder();

    // --- 变量区 ---
    sb.AppendLine("$OutputPath = '" + EscapePS(outputPath) + "'");
    sb.AppendLine("$CoverPath = '" + EscapePS(coverPath) + "'");
    sb.AppendLine("$HasCover = $" + (hasCover ? "true" : "false"));
    sb.AppendLine("$RtfFiles = @(");
    foreach (var f in rtfFiles)
        sb.AppendLine("    '" + EscapePS(f) + "'");
    sb.AppendLine(")");
    sb.AppendLine();

    // --- 主逻辑 ---
    sb.Append(@"$ErrorActionPreference = 'Stop'

try {
    # ===== 创建 Word 对象 =====
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0

    $doc = $word.Documents.Add()
    $selection = $word.Selection

    # ===== 全局页面设置：A3 横向（PageWidth/PageHeight 比 PaperSize 枚举更可靠）=====
    [void]$doc.PageSetup
    $doc.PageSetup.PageWidth = $word.CentimetersToPoints(42.0)
    $doc.PageSetup.PageHeight = $word.CentimetersToPoints(29.7)
    $doc.PageSetup.Orientation = 1        # wdOrientLandscape
    $doc.PageSetup.TopMargin    = $word.CentimetersToPoints(2.0)
    $doc.PageSetup.BottomMargin = $word.CentimetersToPoints(2.0)
    $doc.PageSetup.LeftMargin   = $word.CentimetersToPoints(2.5)
    $doc.PageSetup.RightMargin  = $word.CentimetersToPoints(2.5)

    $heading1Style = $doc.Styles(-2)      # wdStyleHeading1
    $normalStyle   = $doc.Styles(-1)      # wdStyleNormal

    function Fix-A3Page($sec) {
        $sec.PageSetup.PageWidth = $word.CentimetersToPoints(42.0)
        $sec.PageSetup.PageHeight = $word.CentimetersToPoints(29.7)
        $sec.PageSetup.Orientation = 1
        $sec.PageSetup.TextColumns.SetCount(2)
    }

    # ===== 第 1 节：封面 =====");
    sb.AppendLine();

    if (hasCover)
    {
        sb.Append(@"    if ($HasCover -and (Test-Path $CoverPath)) {
        $selection.InsertFile($CoverPath)
        Fix-A3Page $doc.Sections($doc.Sections.Count)
        [void]$selection.InsertBreak(2)    # wdSectionBreakNextPage
    }
");
    }

    sb.Append(@"
    # ===== 目录节 =====
    # 注意：封面占1节，TOC=从当前最后一节开始，正文=之后
    $selection.Font.Size = 22
    $selection.Font.Bold = $true
    $selection.ParagraphFormat.Alignment = 1  # wdAlignParagraphCenter
    $selection.TypeText([char]0x76EE + '  ' + [char]0x5F55)   # 目  录
    $selection.TypeParagraph()
    $selection.TypeParagraph()

    # 插入 TOC 域（仅收录 Heading 1）
    $tocFieldRange = $selection.Range.Duplicate
    $tocFieldRange.Collapse(1)
    [void]$doc.TablesOfContents.Add($tocFieldRange, $true, 1, 1)

    # 分节符（下一页）→ 正文
    [void]$selection.InsertBreak(2)

    # ===== 正文节：逐个 RTF 插入 =====
    $isFirst = $true
    foreach ($rtf in $RtfFiles) {
        if (-not (Test-Path $rtf)) { continue }

        $headingText = [System.IO.Path]::GetFileNameWithoutExtension($rtf)

        # 分节符（下一页），从第二个 RTF 开始
        if (-not $isFirst) { [void]$selection.InsertBreak(2) }
        $isFirst = $false

        # 一级标题
        $selection.Style = $heading1Style
        $selection.TypeText($headingText)
        $selection.TypeParagraph()

        # 恢复正常样式，插入 RTF 内容
        $selection.Style = $normalStyle
        $selection.InsertFile($rtf)

        # RTF InsertFile 覆盖纸张和栏数 → 立即修正当前节为 A3 双栏
        Fix-A3Page $doc.Sections($doc.Sections.Count)
    }

    # ===== 列数 + 纸张最终修正 =====
    $totalSections = $doc.Sections.Count
    if ($HasCover) {
        $bodyStartIdx = 3  # 封面=1, TOC=2, 正文=3+
    } else {
        $bodyStartIdx = 2  # TOC=1, 正文=2+
    }
    for ($i = 1; $i -le $totalSections; $i++) {
        $sec = $doc.Sections($i)
        Fix-A3Page $sec
        if ($i -lt $bodyStartIdx) {
            $sec.PageSetup.TextColumns.SetCount(1)
            $sec.PageSetup.TextColumns.LineBetween = $false
        }
    }

    # ===== 页脚：封面+目录无页码，正文页码从 1 开始 =====
    # 先断开所有节的页脚链接（指向首页）以防止交叉污染
    for ($i = 1; $i -le $totalSections; $i++) {
        $doc.Sections($i).Footers.Item(1).LinkToPrevious = $false
    }

    # 封面/TOC 清除页脚
    for ($i = 1; $i -lt $bodyStartIdx; $i++) {
        $f = $doc.Sections($i).Footers.Item(1)
        [void]$f.Range.Delete()
    }

    # 正文首页插入页码，从 1 开始
    if ($bodyStartIdx -le $totalSections) {
        $firstFooter = $doc.Sections($bodyStartIdx).Footers.Item(1)
        $firstFooter.LinkToPrevious = $false
        [void]$firstFooter.PageNumbers.Add(2)      # wdAlignPageNumberRight
        $firstFooter.PageNumbers.RestartNumberingAtSection = $true
        $firstFooter.PageNumbers.StartingNumber = 1

        for ($i = $bodyStartIdx + 1; $i -le $totalSections; $i++) {
            $doc.Sections($i).Footers.Item(1).LinkToPrevious = $true
        }
    }

    # ===== 更新目录 =====
    if ($doc.TablesOfContents.Count -gt 0) {
        [void]$doc.TablesOfContents(1).Update()
    }

    # 回到文档开头
    [void]$selection.HomeKey(6)   # wdStory

    # ===== 保存 =====
    if (Test-Path $OutputPath) {
        Remove-Item $OutputPath -Force -ErrorAction SilentlyContinue
    }
    $saveAsPath = [string]$OutputPath
    $saveAsFormat = [int]16
    [void]$doc.SaveAs([ref]$saveAsPath, [ref]$saveAsFormat)

    $doc.Close()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc) | Out-Null

    try { $word.Quit() } catch {}
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()

    Write-Output 'SUCCESS'
}
catch {
    $msg = $_.Exception.Message
    Write-Error $msg
    if ($doc) { try { $doc.Close(0) } catch {} }
    if ($word) {
        try { $word.Quit() } catch {}
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
    }
    exit 1
}
");

    return sb.ToString();
}

// ================================================================
// PowerShell 单引号字符串转义（仅需转义单引号本身）
// ================================================================
static string EscapePS(string s)
{
    return s.Replace("'", "''");
}
