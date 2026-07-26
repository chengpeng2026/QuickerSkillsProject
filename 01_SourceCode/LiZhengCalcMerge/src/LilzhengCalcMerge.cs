// ============================================================
// LilzhengCalcMerge  v5.5.4
// RTF -> Word merge with cover, TOC, page numbers
// Uses PowerShell subprocess for COM (WPS/Word auto-detect)
// All PS Chinese strings use [char]0xXXXX to avoid encoding issues
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Quicker.Public;

public static string Exec(IStepContext context)
{
    // 1. Select source folder
    string folderPath = null;
    using (var dlg = new FolderBrowserDialog())
    {
        dlg.Description = "请选择包含理正基坑计算书（RTF文件）的文件夹";
        dlg.ShowNewFolderButton = false;
        if (dlg.ShowDialog() != DialogResult.OK) return "CANCELLED";
        folderPath = dlg.SelectedPath;
    }

    // 2. Scan RTF files
    var files = Directory.GetFiles(folderPath, "*.rtf", SearchOption.TopDirectoryOnly);
    if (files.Length == 0)
    {
        MessageBox.Show("该文件夹中没有找到 RTF 文件。", "未找到计算书", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return "NO_RTF";
    }

    // 3. Sort by profile number (PWC order: 1-1 < 1-1-1 < 2-2 < 10-10)
    var sorted = files.OrderBy(f => ParseSegments(Path.GetFileNameWithoutExtension(f)), SegCmp.I).ToArray();

    // 4. Select save location
    string saveFolder = folderPath;
    using (var saveDlg = new FolderBrowserDialog())
    {
        saveDlg.Description = "请选择合并后文件的保存位置";
        saveDlg.SelectedPath = folderPath;
        saveDlg.ShowNewFolderButton = true;
        if (saveDlg.ShowDialog() == DialogResult.OK)
            saveFolder = saveDlg.SelectedPath;
    }

    // 5. Input filename
    string defaultName = (context.GetVarValue("mergedFileName") as string ?? "").Trim();
    if (string.IsNullOrWhiteSpace(defaultName))
        defaultName = "合并计算书_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

    string name = ShowInputDialog("请输入合并后的文件名（不含扩展名）：", "命名合并文件", defaultName);
    if (string.IsNullOrWhiteSpace(name)) name = defaultName;
    foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
    string outPath = Path.Combine(saveFolder, name + ".docx");

    // 6. Merge via PowerShell subprocess
    string result = MergeViaPowerShell(sorted, outPath);

    if (result == "OK" && File.Exists(outPath))
    {
        string msg = "合并完成！\n\n共 " + sorted.Length + " 份计算书\n输出：" + outPath + "\n\n提示：打开文件后全选然后F9可刷新目录";
        MessageBox.Show(msg, "计算书合并", MessageBoxButtons.OK, MessageBoxIcon.Information);
        context.SetVarValue("text", outPath);
        return "OK";
    }
    else
    {
        MessageBox.Show("合并失败：\n" + result
            + "\n\n提示：请确认已安装 Microsoft Word 或 WPS Office。",
            "计算书合并", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return "FAIL";
    }
}

// ============ Input Dialog ============

static string ShowInputDialog(string prompt, string title, string defaultText)
{
    string result = defaultText;
    using (var form = new Form())
    {
        form.Text = title;
        form.Width = 460;
        form.Height = 180;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        var label = new Label() { Text = prompt, Left = 12, Top = 16, Width = 420, AutoSize = true };
        var textBox = new TextBox() { Text = defaultText, Left = 12, Top = 44, Width = 420 };
        var btnOk = new Button() { Text = "确定", Left = 260, Top = 82, Width = 80, DialogResult = DialogResult.OK };
        var btnCancel = new Button() { Text = "取消", Left = 350, Top = 82, Width = 80, DialogResult = DialogResult.Cancel };
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;
        form.Controls.AddRange(new Control[] { label, textBox, btnOk, btnCancel });
        form.Shown += (s, e) => { textBox.Focus(); textBox.SelectAll(); };
        if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text))
            result = textBox.Text.Trim();
    }
    return result;
}

// ============ PowerShell subprocess merge ============

static string MergeViaPowerShell(string[] files, string outPath)
{
    string tempDir = Path.GetTempPath();
    string listFile = Path.Combine(tempDir, "lz_lst_" + Guid.NewGuid().ToString("N") + ".txt");
    string psFile   = Path.Combine(tempDir, "lz_ps_" + Guid.NewGuid().ToString("N") + ".ps1");
    string flagFile = Path.Combine(tempDir, "lz_flg_" + Guid.NewGuid().ToString("N") + ".txt");

    try
    {
        File.WriteAllLines(listFile, files, new UTF8Encoding(false));
        string assetsDir = System.IO.Path.GetFullPath(@"e:\QuickerSkillsProject\01_SourceCode\LiZhengCalcMerge\src\assets");

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("try {");
        sb.AppendLine("  $files = Get-Content -LiteralPath '" + listFile + "' -Encoding UTF8");
        sb.AppendLine("  $out = '" + outPath.Replace("'", "''") + "'");
        sb.AppendLine("  $appType = [Type]::GetTypeFromProgID('Word.Application')");
        sb.AppendLine("  if ($appType -eq $null) { $appType = [Type]::GetTypeFromProgID('KWPS.Application') }");
        sb.AppendLine("  if ($appType -eq $null) { throw 'No Word/WPS detected' }");
        sb.AppendLine("  $app = [System.Activator]::CreateInstance($appType)");
        sb.AppendLine("  $app.Visible = $false");
        sb.AppendLine("  try { $app.DisplayAlerts = 0 } catch {}");
        sb.AppendLine("  $master = $app.Documents.Add()");
        sb.AppendLine("  $assetDir = '" + assetsDir.Replace("'", "''") + "'");
        sb.AppendLine("  $sel = $app.Selection");

        // Cover: top spacing
        sb.AppendLine("  $sel.Font.Size = 16; $sel.Font.Bold = 0");
        sb.AppendLine("  for ($i = 0; $i -lt 3; $i++) { $sel.TypeParagraph() }");
        // Project name = folder name
        sb.AppendLine("  $parentDir = (Get-Item (Split-Path -Parent $out)).Name");
        sb.AppendLine("  $sel.Font.Size = 22; $sel.Font.Bold = 1; $sel.ParagraphFormat.Alignment = 1");
        sb.AppendLine("  $sel.TypeText($parentDir); $sel.TypeParagraph()");
        // TypeText: "计算书" via Unicode escapes (char 0x8BA1=计, 0x7B97=算, 0x4E66=书)
        sb.AppendLine("  $s = [char]0x8BA1 + [char]0x7B97 + [char]0x4E66");
        sb.AppendLine("  $sel.TypeText($s); $sel.TypeParagraph(); $sel.TypeParagraph()");

        // Spacing before signature
        sb.AppendLine("  $sel.Font.Size = 16; $sel.Font.Bold = 0");
        sb.AppendLine("  for ($i = 0; $i -lt 6; $i++) { $sel.TypeParagraph() }");

        // Signature table: 3 rows x 2 cols, no borders
        sb.AppendLine("  $sel.Font.Size = 14; $sel.ParagraphFormat.Alignment = 1; $sel.TypeParagraph()");
        sb.AppendLine("  $tbl = $sel.Tables.Add($sel.Range, 3, 2)");
        sb.AppendLine("  $tbl.Borders.Enable = 0; $tbl.Rows.Alignment = 1");
        sb.AppendLine("  $tbl.Columns.Item(1).Width = 100; $tbl.Columns.Item(2).Width = 80");

        // Row 1: Calc  (0x8BA1=计, 0x7B97=算)
        sb.AppendLine("  $s = [char]0x8BA1 + '    ' + [char]0x7B97 + ':'");
        sb.AppendLine("  $tbl.Cell(1,1).Range.Text = $s; $tbl.Cell(1,1).Range.ParagraphFormat.Alignment = 2");
        sb.AppendLine("  $sigComp = Join-Path $assetDir 'sig_compute.png'");
        sb.AppendLine("  if (Test-Path $sigComp) { $tbl.Cell(1,2).Range.InlineShapes.AddPicture($sigComp, $false, $true) | Out-Null }");
        sb.AppendLine("  $tbl.Cell(1,2).Range.ParagraphFormat.Alignment = 0");

        // Row 2: Engineer responsible (0x5DE5=工, 0x7A0B=程, 0x8D1F=负, 0x8D23=责)
        sb.AppendLine("  $s = [char]0x5DE5 + [char]0x7A0B + [char]0x8D1F + [char]0x8D23 + ':'");
        sb.AppendLine("  $tbl.Cell(2,1).Range.Text = $s; $tbl.Cell(2,1).Range.ParagraphFormat.Alignment = 2");
        sb.AppendLine("  $sigEng = Join-Path $assetDir 'sig_engineer.png'");
        sb.AppendLine("  if (Test-Path $sigEng) { $tbl.Cell(2,2).Range.InlineShapes.AddPicture($sigEng, $false, $true) | Out-Null }");
        sb.AppendLine("  $tbl.Cell(2,2).Range.ParagraphFormat.Alignment = 0");

        // Row 3: Review (0x5BA1=审, 0x6838=核)
        sb.AppendLine("  $s = [char]0x5BA1 + '    ' + [char]0x6838 + ':'");
        sb.AppendLine("  $tbl.Cell(3,1).Range.Text = $s; $tbl.Cell(3,1).Range.ParagraphFormat.Alignment = 2");
        sb.AppendLine("  $sigRev = Join-Path $assetDir 'sig_review.png'");
        sb.AppendLine("  if (Test-Path $sigRev) { $tbl.Cell(3,2).Range.InlineShapes.AddPicture($sigRev, $false, $true) | Out-Null }");
        sb.AppendLine("  $tbl.Cell(3,2).Range.ParagraphFormat.Alignment = 0");

        // After table
        sb.AppendLine("  $sel.EndKey(6); $sel.TypeParagraph(); $sel.TypeParagraph()");

        // Company name (12 UTF-8 chars via hex escapes)
        sb.AppendLine("  $co = [char]0x6CB3 + [char]0x5357 + [char]0x6052 + [char]0x6CF0 + [char]0x5CA9 + [char]0x571F + [char]0x5DE5 + [char]0x7A0B + [char]0x6709 + [char]0x9650 + [char]0x516C + [char]0x53F8");
        sb.AppendLine("  $sel.ParagraphFormat.Alignment = 1; $sel.Font.Size = 16");
        sb.AppendLine("  $sel.TypeText($co); $sel.TypeParagraph()");
        sb.AppendLine("  $sel.Font.Size = 14; $sel.TypeText('            ' + (Get-Date -Format 'yyyy.MM'))");

        // Page break after cover
        sb.AppendLine("  $sel.InsertBreak(7)");

        // === TOC Page ===
        // "目  录" 0x76EE=目, 0x5F55=录, font 22pt bold centered
        sb.AppendLine("  $tocTitle = [char]0x76EE + '  ' + [char]0x5F55");
        sb.AppendLine("  $sel.Font.Size = 22; $sel.Font.Bold = 1; $sel.ParagraphFormat.Alignment = 1");
        sb.AppendLine("  $sel.TypeText($tocTitle); $sel.TypeParagraph(); $sel.TypeParagraph()");
        // TOC field: wdFieldTOC=97, \o \"1-1\" \h collects OL=1 paragraphs
        sb.AppendLine("  $tocRange = $sel.Range.Duplicate");
        sb.AppendLine("  $tocSw = [char]92 + 'o \"1-1\" ' + [char]92 + 'h'");
        sb.AppendLine("  $master.Fields.Add($tocRange, 97, $tocSw, $true) | Out-Null");
        sb.AppendLine("  $sel.TypeParagraph(); $sel.InsertBreak(7)");

        // === Merge RTFs: each filename as Heading 1 (OutlineLevel=1), no sub-levels ===
        sb.AppendLine("  $first = $true");
        sb.AppendLine("  foreach ($f in $files) {");
        sb.AppendLine("    if (-not (Test-Path -LiteralPath $f)) { continue }");
        sb.AppendLine("    if (-not $first) { $master.Activate(); $app.Selection.EndKey(6); $app.Selection.InsertBreak(7) }");
        sb.AppendLine("    $first = $false");
        // Filename without extension = Heading 1 title for TOC (replace \d+ with \d+ to avoid C# escape)
        sb.AppendLine("    $fname = [System.IO.Path]::GetFileNameWithoutExtension($f)");
        sb.AppendLine("    $master.Activate(); $app.Selection.EndKey(6)");
        // TypeParagraph first, then set style on the paragraph, then type text
        sb.AppendLine("    $app.Selection.TypeParagraph()");
        // set_Style(-2) = wdStyleHeading1, OutlineLevel=1 for TOC \o switch
        sb.AppendLine("    try { $app.Selection.set_Style(-2) } catch {}");
        sb.AppendLine("    $app.Selection.ParagraphFormat.OutlineLevel = 1");
        sb.AppendLine("    $app.Selection.Font.Size = 14; $app.Selection.Font.Bold = 1");
        sb.AppendLine("    $app.Selection.ParagraphFormat.Alignment = 0");
        sb.AppendLine("    $app.Selection.TypeText($fname)");
        sb.AppendLine("    $app.Selection.TypeParagraph()");
        // Restore to normal AFTER heading text + trailing newline
        sb.AppendLine("    try { $app.Selection.set_Style(-1) } catch {}");
        sb.AppendLine("    $app.Selection.ParagraphFormat.OutlineLevel = 10");
        sb.AppendLine("    $app.Selection.Font.Size = 10.5; $app.Selection.Font.Bold = 0");
        sb.AppendLine("    $app.Selection.ParagraphFormat.Alignment = 0");
        // Merge RTF content
        sb.AppendLine("    $src = $app.Documents.Open($f, $false, $true, $false)");
        sb.AppendLine("    $src.Content.Copy()");
        sb.AppendLine("    $master.Activate(); $app.Selection.EndKey(6); $app.Selection.Paste()");
        sb.AppendLine("    $src.Close($false)");
        sb.AppendLine("  }");

        // === A3 landscape (v5.5.4) ===
        sb.AppendLine("  $sec = $master.Sections.Item(1)");
        sb.AppendLine("  try { $sec.PageSetup.Orientation = 1 } catch {}");
        sb.AppendLine("  try { $sec.PageSetup.PageWidth = 1191 } catch {}");
        sb.AppendLine("  try { $sec.PageSetup.PageHeight = 842 } catch {}");
        sb.AppendLine("  try { $sec.PageSetup.TopMargin = 36; $sec.PageSetup.BottomMargin = 36 } catch {}");
        sb.AppendLine("  try { $sec.PageSetup.LeftMargin = 36; $sec.PageSetup.RightMargin = 36 } catch {}");

        // === Section break + columns: insert before first RTF ===
        sb.AppendLine("  $master.Activate(); $app.Selection.EndKey(6)");
        sb.AppendLine("  $app.Selection.InsertBreak(2) | Out-Null");

        // === Columns: set IMMEDIATELY after section break, before footer/SeekView ===
        sb.AppendLine("  try { $master.Sections.Item(1).PageSetup.TextColumns.SetCount(1) } catch {}");
        sb.AppendLine("  try { $master.Sections.Item(2).PageSetup.TextColumns.SetCount(2) } catch {}");

        // === Page numbering: Section 1 no footer, Section 2 PAGE field ===
        sb.AppendLine("  $sec1 = $master.Sections.Item(1)");
        sb.AppendLine("  try { $sec1.Footers.Item(1).Range.Text = '' } catch {}");
        sb.AppendLine("  $sec2 = $master.Sections.Item(2)");
        sb.AppendLine("  try { $sec2.Footers.Item(1).PageNumbers.StartingNumber = 1 } catch {}");
        sb.AppendLine("  $master.GoTo(1, 1, 3, '') | Out-Null");
        sb.AppendLine("  try { $app.ActiveWindow.ActivePane.View.SeekView = 4 } catch {}");
        sb.AppendLine("  try { $sel.HeaderFooter.Range.Text = '' } catch {}");
        sb.AppendLine("  $ft = [char]0x7B2C + ' '");
        sb.AppendLine("  $sel.TypeText($ft); $sel.Fields.Add($sel.Range, 33) | Out-Null");
        sb.AppendLine("  $ft = ' ' + [char]0x9875");
        sb.AppendLine("  $sel.TypeText($ft)");
        sb.AppendLine("  $sel.HeaderFooter.Range.ParagraphFormat.Alignment = 1");
        sb.AppendLine("  $sel.HeaderFooter.Range.Font.Size = 9");
        sb.AppendLine("  try { $app.ActiveWindow.ActivePane.View.SeekView = 0 } catch {}");

        // === Delete divider blocks ===
        sb.AppendLine("  $find = $app.Selection.Find");
        sb.AppendLine("  $find.Forward = $true");
        sb.AppendLine("  $find.Format = $false");
        sb.AppendLine("  $find.Text = [char]0x9A8C + [char]0x7B97 + [char]0x9879 + [char]0x76EE + ':'");
        sb.AppendLine("  while ($find.Execute()) {");
        sb.AppendLine("    try { $app.Selection.Paragraphs.Item(1).Previous().Range.Delete() } catch {}");
        sb.AppendLine("    try { $app.Selection.Paragraphs.Item(1).Range.Delete() } catch {}");
        sb.AppendLine("    try { $app.Selection.Paragraphs.Item(1).Next().Range.Delete() } catch {}");
        sb.AppendLine("  }");

        // === Font 10pt: anti-uplift to tensile section ===
        sb.AppendLine("  $find.Text = [char]0x6297 + [char]0x62D4 + [char]0x627F + [char]0x8F7D + [char]0x529B + [char]0x9A8C + [char]0x7B97 + [char]0x7ED3 + [char]0x679C");
        sb.AppendLine("  $endText = [char]0x53D7 + [char]0x62C9 + [char]0x627F + [char]0x8F7D + [char]0x529B + [char]0x9A8C + [char]0x7B97 + [char]0x7ED3 + [char]0x679C");
        sb.AppendLine("  while ($find.Execute()) {");
        sb.AppendLine("    $sr = $app.Selection.Range.Duplicate; $find.Text = $endText");
        sb.AppendLine("    if ($find.Execute()) { $er = $app.Selection.Range.Duplicate; $sr.End = $er.Start; $sr.Font.Size = 10 }");
        sb.AppendLine("    else { break }; $find.Text = [char]0x6297 + [char]0x62D4 + [char]0x627F + [char]0x8F7D + [char]0x529B + [char]0x9A8C + [char]0x7B97 + [char]0x7ED3 + [char]0x679C");
        sb.AppendLine("  }");

        // Refresh + Save
        sb.AppendLine("  try { $master.PrintPreview(); $master.ClosePrintPreview() } catch {}");
        sb.AppendLine("  $master.Fields.Update() | Out-Null");
        sb.AppendLine("  try { $master.PrintPreview(); $master.ClosePrintPreview() } catch {}");

        // === Save ===
        sb.AppendLine("  $tmp = Join-Path $env:TEMP ([System.IO.Path]::GetFileName($out))");
        sb.AppendLine("  $saved = $false");
        sb.AppendLine("  foreach ($fmt in @(16,12,0)) { try { $master.SaveAs($tmp, $fmt); $saved = $true; break } catch {} }");
        sb.AppendLine("  try { $master.Close($false) } catch {}");
        sb.AppendLine("  try { $app.Quit($false) } catch {}");
        sb.AppendLine("  try { [System.Runtime.InteropServices.Marshal]::ReleaseComObject($app) | Out-Null } catch {}");
        sb.AppendLine("  if (-not $saved -or -not (Test-Path -LiteralPath $tmp)) { throw 'Save failed' }");
        sb.AppendLine("  if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }");
        sb.AppendLine("  Move-Item -LiteralPath $tmp -Destination $out -Force");

        sb.AppendLine("  Set-Content -LiteralPath '" + flagFile + "' -Value 'OK' -Encoding UTF8");
        sb.AppendLine("} catch {");
        sb.AppendLine("  Set-Content -LiteralPath '" + flagFile + "' -Value ('ERR: ' + $_.Exception.Message) -Encoding UTF8");
        sb.AppendLine("} finally {");
        sb.AppendLine("  try { Get-Process -Name 'wps' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue } catch {}");
        sb.AppendLine("}");

        File.WriteAllText(psFile, sb.ToString(), new UTF8Encoding(true));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -STA -File \"" + psFile + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using (var proc = Process.Start(psi))
        {
            proc.WaitForExit(180000);
        }

        if (File.Exists(flagFile))
        {
            string flag = File.ReadAllText(flagFile, Encoding.UTF8).Trim();
            if (flag.StartsWith("OK")) return "OK";
            return flag;
        }
        return "PowerShell 未返回结果（可能超时或被拦截）";
    }
    catch (Exception ex)
    {
        return "Failed to start merge: " + ex.Message;
    }
    finally
    {
        try { if (File.Exists(listFile)) File.Delete(listFile); } catch { }
        try { if (File.Exists(psFile)) File.Delete(psFile); } catch { }
        try { if (File.Exists(flagFile)) File.Delete(flagFile); } catch { }
    }
}

// ============ Profile sorting: natural numeric sort on all segments ============

// Parse filename into numeric segments for natural sort: "1-1" < "1-1-1" < "2-2"
static int[] ParseSegments(string name)
{
    var matches = Regex.Matches(name, @"\d+");
    if (matches.Count > 0)
        return matches.Cast<Match>().Select(m => int.Parse(m.Value)).ToArray();
    return new[] { int.MaxValue };
}

// Compare two arrays lexicographically (pad short array with int.MinValue)
class SegCmp : IComparer<int[]>
{
    public static readonly SegCmp I = new SegCmp();
    public int Compare(int[] x, int[] y)
    {
        int len = Math.Max(x.Length, y.Length);
        for (int i = 0; i < len; i++)
        {
            int a = i < x.Length ? x[i] : int.MinValue;
            int b = i < y.Length ? y[i] : int.MinValue;
            int c = a.CompareTo(b);
            if (c != 0) return c;
        }
        return 0;
    }
}
