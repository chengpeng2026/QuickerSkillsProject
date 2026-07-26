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

// LiZhengRTFMerge v1.6.0 Build: 20260726
// 纯 File.Write 写 PS 文件 — 避免 C# 字符串中文问题

public static string Exec(IStepContext context)
{
    try
    {
        string rtfDir = null;
        using (var d = new FolderBrowserDialog())
        {
            d.Description = "请选择包含 RTF 计算书的目录";
            d.ShowNewFolderButton = false;
            if (d.ShowDialog() != DialogResult.OK) return "CANCELLED";
            rtfDir = d.SelectedPath;
        }

        var rtfFiles = Directory.GetFiles(rtfDir, "*.RTF")
            .OrderBy(f => NaturalSortKey(Path.GetFileName(f)))
            .ToList();

        if (rtfFiles.Count == 0)
        {
            MessageBox.Show("目录中没有 RTF 文件。", "提示");
            return "NO_RTF";
        }

        string savePath = null;
        using (var d = new SaveFileDialog())
        {
            d.Title = "保存合并后的 Word 文档";
            d.Filter = "Word 文档 (*.docx)|*.docx";
            d.DefaultExt = "docx";
            d.FileName = "合并计算书.docx";
            if (d.ShowDialog() != DialogResult.OK) return "CANCELLED";
            savePath = d.FileName;
        }

        string coverPath = null;
        string look = rtfDir;
        while (look != null)
        {
            string c = Path.Combine(look, "封面.docx");
            if (File.Exists(c)) { coverPath = c; break; }
            look = Path.GetDirectoryName(look);
        }
        if (coverPath == null)
            coverPath = @"C:\Users\12089\Desktop\最终计算书\封面.docx";
        bool hasCover = File.Exists(coverPath);

        // Write PS file
        string psFile = Path.GetTempFileName() + ".ps1";
        PSWrite(psFile, rtfFiles, savePath, coverPath, hasCover);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-ExecutionPolicy Bypass -NoProfile -File \"" + psFile + "\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var p = Process.Start(psi))
        {
            string so = p.StandardOutput.ReadToEnd();
            string se = p.StandardError.ReadToEnd();
            p.WaitForExit(600000);
            if (p.ExitCode != 0)
            {
                try { File.Delete(psFile); } catch { }
                MessageBox.Show("Word 合并失败。\n\n" + se + "\n" + so, "错误");
                return "PS_FAIL_" + p.ExitCode;
            }
        }

        try { File.Delete(psFile); } catch { }

        if (File.Exists(savePath))
        {
            var fi = new FileInfo(savePath);
            MessageBox.Show("合并完成！\n\n" + savePath + "\n大小: " + (fi.Length / 1024.0).ToString("F1") + " KB\n包含 " + rtfFiles.Count + " 个 RTF", "完成");
            return savePath;
        }
        else
        {
            MessageBox.Show("合并完成但未找到输出文件。", "警告");
            return "WARNING";
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show("异常: " + ex.Message, "错误");
        return "ERROR:" + ex.Message;
    }
}

static string NaturalSortKey(string f) { return Regex.Replace(f, @"\d+", m => m.Value.PadLeft(10, '0')); }

static string EP(string s) { return s.Replace("'", "''"); }

// Write PS file line-by-line — no C# here-string, no Chinese in C# source
static void PSWrite(string path, List<string> rtfs, string outPath, string cover, bool hasCover)
{
    using (var w = new StreamWriter(path, false, new UTF8Encoding(true)))
    {
        w.NewLine = "\r\n";
        w.WriteLine("$ErrorActionPreference = 'Stop'");
        w.WriteLine("");
        w.WriteLine("$OutputPath = '" + EP(outPath) + "'");
        w.WriteLine("$CoverPath = '" + EP(cover) + "'");
        w.WriteLine("$HasCover = $" + (hasCover ? "true" : "false"));
        w.WriteLine("$RtfFiles = @(");
        foreach (var f in rtfs) w.WriteLine("    '" + EP(f) + "'");
        w.WriteLine(")");
        w.WriteLine("");

        w.WriteLine("try {");
        w.WriteLine("    $word = New-Object -ComObject Word.Application");
        w.WriteLine("    $word.Visible = $false");
        w.WriteLine("    $word.DisplayAlerts = 0");
        w.WriteLine("    $doc = $word.Documents.Add()");
        w.WriteLine("    $selection = $word.Selection");
        w.WriteLine("");
        w.WriteLine("    $doc.PageSetup.PageWidth = $word.CentimetersToPoints(42.0)");
        w.WriteLine("    $doc.PageSetup.PageHeight = $word.CentimetersToPoints(29.7)");
        w.WriteLine("    $doc.PageSetup.Orientation = 1");
        w.WriteLine("    $doc.PageSetup.TopMargin    = $word.CentimetersToPoints(2.0)");
        w.WriteLine("    $doc.PageSetup.BottomMargin = $word.CentimetersToPoints(2.0)");
        w.WriteLine("    $doc.PageSetup.LeftMargin   = $word.CentimetersToPoints(2.5)");
        w.WriteLine("    $doc.PageSetup.RightMargin  = $word.CentimetersToPoints(2.5)");
        w.WriteLine("");
        w.WriteLine("    $heading1Style = $doc.Styles(-2)");
        w.WriteLine("    $normalStyle   = $doc.Styles(-1)");
        w.WriteLine("");
        w.WriteLine("    function Fix-A3Page($sec) {");
        w.WriteLine("        $sec.PageSetup.PageWidth = $word.CentimetersToPoints(42.0)");
        w.WriteLine("        $sec.PageSetup.PageHeight = $word.CentimetersToPoints(29.7)");
        w.WriteLine("        $sec.PageSetup.Orientation = 1");
        w.WriteLine("        $sec.PageSetup.TextColumns.SetCount(2)");
        w.WriteLine("    }");
        w.WriteLine("");

        if (hasCover)
        {
            w.WriteLine("    if ($HasCover -and (Test-Path $CoverPath)) {");
            w.WriteLine("        $selection.InsertFile($CoverPath)");
            w.WriteLine("        Fix-A3Page $doc.Sections($doc.Sections.Count)");
            w.WriteLine("        [void]$selection.InsertBreak(2)");
            w.WriteLine("    }");
        }

        w.WriteLine("");
        w.WriteLine("    $selection.Font.Size = 22");
        w.WriteLine("    $selection.Font.Bold = $true");
        w.WriteLine("    $selection.ParagraphFormat.Alignment = 1");
        string tocText = "\u76EE  \u5F55"; // 目  录
        w.WriteLine("    $selection.TypeText(\"" + tocText + "\")");
        w.WriteLine("    $selection.TypeParagraph()");
        w.WriteLine("    $selection.TypeParagraph()");
        w.WriteLine("    $tocRange = $selection.Range.Duplicate");
        w.WriteLine("    $tocRange.Collapse(1)");
        w.WriteLine("    [void]$doc.TablesOfContents.Add($tocRange, $true, 1, 1)");
        w.WriteLine("    [void]$selection.InsertBreak(2)");
        w.WriteLine("");

        w.WriteLine("    $isFirst = $true");
        w.WriteLine("    foreach ($rtf in $RtfFiles) {");
        w.WriteLine("        if (-not (Test-Path $rtf)) { continue }");
        w.WriteLine("        $headingText = [System.IO.Path]::GetFileNameWithoutExtension($rtf)");
        w.WriteLine("        if (-not $isFirst) { [void]$selection.InsertBreak(2) }");
        w.WriteLine("        $isFirst = $false");
        w.WriteLine("        $selection.Style = $heading1Style");
        w.WriteLine("        $selection.TypeText($headingText)");
        w.WriteLine("        $selection.TypeParagraph()");
        w.WriteLine("        $selection.Style = $normalStyle");
        w.WriteLine("        $selection.InsertFile($rtf)");
        w.WriteLine("        Fix-A3Page $doc.Sections($doc.Sections.Count)");
        w.WriteLine("");

        // font-size fix: Find 抗拔 → 受拉 → set 9pt
        string tb = "\u6297\u62D4\u627F\u8F7D\u529B\u9A8C\u7B97\u7ED3\u679C"; // 抗拔承载力验算结果
        string tl = "\u53D7\u62C9\u627F\u8F7D\u529B\u9A8C\u7B97\u7ED3\u679C"; // 受拉承载力验算结果
        w.WriteLine("        try {");
        w.WriteLine("            $cs = $doc.Sections($doc.Sections.Count)");
        w.WriteLine("            $sr = $cs.Range.Duplicate");
        w.WriteLine("            $f1 = $sr.Duplicate");
        w.WriteLine("            $f1.Find.ClearFormatting()");
        w.WriteLine("            $f1.Find.Text = \"" + tb + "\"");
        w.WriteLine("            if ($f1.Find.Execute()) {");
        w.WriteLine("                $at = $f1.End + 1");
        w.WriteLine("                $f2 = $sr.Duplicate");
        w.WriteLine("                $f2.Find.ClearFormatting()");
        w.WriteLine("                $f2.Find.Text = \"" + tl + "\"");
        w.WriteLine("                if ($f2.Find.Execute()) {");
        w.WriteLine("                    $be = $f2.Start");
        w.WriteLine("                    $md = $sr.Duplicate");
        w.WriteLine("                    $md.SetRange($at, $be)");
        w.WriteLine("                    $md.Font.Size = 9");
        w.WriteLine("                }");
        w.WriteLine("            }");
        w.WriteLine("        }");
        w.WriteLine("        catch { }");

        // font-size fix 2: 土层参数 → 坑内土不加固 → 10pt
        string ts = "土层参数"; // 土层参数
        string tc = "坑内土不加固"; // 坑内土不加固
        w.WriteLine("        try {");
        w.WriteLine("            $cs2 = $doc.Sections($doc.Sections.Count)");
        w.WriteLine("            $sr2 = $cs2.Range.Duplicate");
        w.WriteLine("            $f3 = $sr2.Duplicate");
        w.WriteLine("            $f3.Find.ClearFormatting()");
        w.WriteLine("            $f3.Find.Text = \"" + ts + "\"");
        w.WriteLine("            if ($f3.Find.Execute()) {");
        w.WriteLine("                $at2 = $f3.End + 1");
        w.WriteLine("                $f4 = $sr2.Duplicate");
        w.WriteLine("                $f4.Find.ClearFormatting()");
        w.WriteLine("                $f4.Find.Text = \"" + tc + "\"");
        w.WriteLine("                if ($f4.Find.Execute()) {");
        w.WriteLine("                    $be2 = $f4.Start");
        w.WriteLine("                    $md2 = $sr2.Duplicate");
        w.WriteLine("                    $md2.SetRange($at2, $be2)");
        w.WriteLine("                    $md2.Font.Size = 10");
        w.WriteLine("                }");
        w.WriteLine("            }");
        w.WriteLine("        }");
        w.WriteLine("        catch { }");
        w.WriteLine("    }");
        w.WriteLine("");

        w.WriteLine("    $totalSections = $doc.Sections.Count");
        w.WriteLine("    if ($HasCover) { $bodyStartIdx = 3 } else { $bodyStartIdx = 2 }");
        w.WriteLine("    for ($i = 1; $i -le $totalSections; $i++) {");
        w.WriteLine("        $sec = $doc.Sections($i)");
        w.WriteLine("        Fix-A3Page $sec");
        w.WriteLine("        if ($i -lt $bodyStartIdx) {");
        w.WriteLine("            $sec.PageSetup.TextColumns.SetCount(1)");
        w.WriteLine("            $sec.PageSetup.TextColumns.LineBetween = $false");
        w.WriteLine("        }");
        w.WriteLine("    }");
        w.WriteLine("");

        w.WriteLine("    for ($i = 1; $i -le $totalSections; $i++) {");
        w.WriteLine("        $doc.Sections($i).Footers.Item(1).LinkToPrevious = $false");
        w.WriteLine("    }");
        w.WriteLine("    for ($i = 1; $i -lt $bodyStartIdx; $i++) {");
        w.WriteLine("        $f = $doc.Sections($i).Footers.Item(1)");
        w.WriteLine("        [void]$f.Range.Delete()");
        w.WriteLine("    }");
        w.WriteLine("");

        w.WriteLine("    if ($bodyStartIdx -le $totalSections) {");
        w.WriteLine("        $firstFooter = $doc.Sections($bodyStartIdx).Footers.Item(1)");
        w.WriteLine("        $firstFooter.LinkToPrevious = $false");
        w.WriteLine("        [void]$firstFooter.PageNumbers.Add(2)");
        w.WriteLine("        $firstFooter.PageNumbers.RestartNumberingAtSection = $true");
        w.WriteLine("        $firstFooter.PageNumbers.StartingNumber = 1");
        w.WriteLine("        for ($i = $bodyStartIdx + 1; $i -le $totalSections; $i++) {");
        w.WriteLine("            $doc.Sections($i).Footers.Item(1).LinkToPrevious = $true");
        w.WriteLine("        }");
        w.WriteLine("    }");
        w.WriteLine("");

        w.WriteLine("    if ($doc.TablesOfContents.Count -gt 0) {");
        w.WriteLine("        [void]$doc.TablesOfContents(1).Update()");
        w.WriteLine("    }");
        w.WriteLine("    [void]$selection.HomeKey(6)");
        w.WriteLine("");

        w.WriteLine("    if (Test-Path $OutputPath) { Remove-Item $OutputPath -Force -ErrorAction SilentlyContinue }");
        w.WriteLine("    $saveAsPath = [string]$OutputPath");
        w.WriteLine("    $saveAsFormat = [int]16");
        w.WriteLine("    [void]$doc.SaveAs([ref]$saveAsPath, [ref]$saveAsFormat)");
        w.WriteLine("    $doc.Close()");
        w.WriteLine("    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc) | Out-Null");
        w.WriteLine("    try { $word.Quit() } catch {}");
        w.WriteLine("    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null");
        w.WriteLine("    [System.GC]::Collect()");
        w.WriteLine("    [System.GC]::WaitForPendingFinalizers()");
        w.WriteLine("    Write-Output 'SUCCESS'");
        w.WriteLine("}");
        w.WriteLine("catch {");
        w.WriteLine("    $msg = $_.Exception.Message");
        w.WriteLine("    Write-Error $msg");
        w.WriteLine("    if ($doc) { try { $doc.Close(0) } catch {} }");
        w.WriteLine("    if ($word) {");
        w.WriteLine("        try { $word.Quit() } catch {}");
        w.WriteLine("        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null");
        w.WriteLine("    }");
        w.WriteLine("    exit 1");
        w.WriteLine("}");
    }
}
