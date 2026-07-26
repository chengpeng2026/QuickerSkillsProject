//css_ref System.Drawing.dll
//css_ref System.Windows.Forms.dll
//css_ref Microsoft.Web.WebView2.Core.dll

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Web.WebView2.Core;
using Quicker.Public;

// Roslyn v2 — 电子发票二维码识别下载
// 流程：选择图片 → 识别二维码 → 选择目录 → 下载
// QR 解码：运行时动态加载 zxing.dll（反射调用，避免编译时引用问题）

public static string Exec(IStepContext context)
{
    // 忽略 SSL 证书错误（税局服务器常见自签名证书）并强制 TLS 1.2
    ServicePointManager.ServerCertificateValidationCallback = RemoteCertificateValidationCB;
    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

    // ========== 步骤1：弹出文件选择对话框，选取发票图片 ==========
    string imgPath = null;
    using (var dialog = new System.Windows.Forms.OpenFileDialog())
    {
        dialog.Title = "请选择电子发票图片";
        dialog.Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*";
        dialog.RestoreDirectory = true;
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return "CANCELLED";
        }
        imgPath = dialog.FileName;
    }

    if (!File.Exists(imgPath))
    {
        System.Windows.Forms.MessageBox.Show(
            "所选文件不存在，请重新选择", "文件错误",
            System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Warning);
        return "ERROR";
    }

    // ========== 步骤2：解码图片中的二维码 ==========
    string qrContent = DecodeQRFromFile(imgPath);

    if (string.IsNullOrEmpty(qrContent))
    {
        System.Windows.Forms.MessageBox.Show(
            "未识别到有效二维码，请调整图片清晰度或重新选择图片", "识别失败",
            System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Warning);
        return "ERROR";
    }

    if (!Regex.IsMatch(qrContent, @"^https?://", RegexOptions.IgnoreCase))
    {
        System.Windows.Forms.MessageBox.Show(
            "二维码内容非有效下载链接：\n\n" + qrContent, "链接无效",
            System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Warning);
        return "ERROR";
    }

    context.SetVarValue("qrResult", qrContent);

    // ========== 步骤3：弹出目录选择对话框，指定保存位置 ==========
    string saveDir = null;
    using (var dirDialog = new System.Windows.Forms.FolderBrowserDialog())
    {
        dirDialog.Description = "请选择电子发票保存目录";
        dirDialog.ShowNewFolderButton = true;
        if (dirDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return "CANCELLED";
        }
        saveDir = dirDialog.SelectedPath;
    }

    context.SetVarValue("savePath", saveDir);

    // ========== 步骤4：智能下载（先直接下载 → 提取HTML中链接二次下载 → 浏览器） ==========
    string url = qrContent.Trim();

    // --- 策略A：直接下载（含302重定向追踪） ---
    string tmpPath = Path.Combine(saveDir, "invoice_" + DateTime.Now.ToString("yyyyMMddHHmmss") + "_A.tmp");
    tmpPath = GetUniqueFilePath(tmpPath);

    string dlErr = DownloadToTemp(url, tmpPath);
    if (dlErr == null && File.Exists(tmpPath) && new FileInfo(tmpPath).Length > 0)
    {
        string ext = CheckFileType(tmpPath);
        if (ext != null)
        {
            string finalPath = RenameTo(tmpPath, ext, saveDir);
            context.SetVarValue("downloadResult", finalPath);
            ShowToastSafe("下载完成 — " + Path.GetFileName(finalPath), "success");
            return "OK";
        }
    }

    // --- 策略B：从响应中提取真实文件URL后二次下载 ---
    string realUrl = ExtractDownloadUrlFromContent(tmpPath, url);
    TryDelete(tmpPath);

    if (string.IsNullOrEmpty(realUrl))
    {
        // 额外尝试：用常见全电发票URL模式直接拼接
        realUrl = TryGuessInvoiceDownloadUrl(url);
    }

    if (!string.IsNullOrEmpty(realUrl))
    {
        string tmpPathB = Path.Combine(saveDir, "invoice_" + DateTime.Now.ToString("yyyyMMddHHmmss") + "_B.tmp");
        tmpPathB = GetUniqueFilePath(tmpPathB);
        string dlErrB = DownloadToTemp(realUrl, tmpPathB);
        if (dlErrB == null && File.Exists(tmpPathB) && new FileInfo(tmpPathB).Length > 0)
        {
            string extB = CheckFileType(tmpPathB);
            if (extB != null)
            {
                string finalPath = RenameTo(tmpPathB, extB, saveDir);
                context.SetVarValue("downloadResult", finalPath);
                ShowToastSafe("下载完成 — " + Path.GetFileName(finalPath), "success");
                return "OK";
            }
        }
        TryDelete(tmpPathB);
    }

    // --- 策略C：WebView2 后台自动下载 ---
    try
    {
        string wvResult = WebView2AutoDownload(url, saveDir);
        if (wvResult != null && !wvResult.StartsWith("ERR:"))
        {
            context.SetVarValue("downloadResult", wvResult);
            ShowToastSafe("下载完成 — " + Path.GetFileName(wvResult), "success");
            return "OK";
        }
    }
    catch { /* WebView2 不可用时静默回退 */ }

    // --- 策略D：全电发票平台已知下载地址尝试 ---
    string smartResult = TryKnownDownloadPatterns(url, saveDir);
    if (smartResult != null)
    {
        context.SetVarValue("downloadResult", smartResult);
        ShowToastSafe("下载完成 — " + Path.GetFileName(smartResult), "success");
        return "OK";
    }

    // --- 策略E：打开浏览器（最后的回退） ---
    if (OpenUrlInBrowser(url))
    {
        ShowToastSafe("已打开发票查验页面，请在浏览器中手动下载", "info");
    }
    else
    {
        System.Windows.Forms.MessageBox.Show(
            "无法自动下载，也无法打开浏览器。\n请手动复制以下链接到浏览器打开：\n\n" + url,
            "请手动下载",
            System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Information);
    }
    return "OK";
}

// 尝试从已知平台URL模式推断下载地址
// 全国统一电子发票平台常见模式：/qrcode?... → /download?... 或 /api/file/download?...
private static string TryGuessInvoiceDownloadUrl(string url)
{
    try
    {
        var uri = new Uri(url);

        // 模式：把 /qrcode 替换为 /download
        string[] tryPaths = new[] {
            "/download", "/file/download", "/api/download", "/api/file/download",
            "/invoice/download", "/fp/download", "/getFile", "/api/getFile"
        };

        string query = uri.Query;

        // 如果 query 中包含 cs=... 参数，尝试拼接
        foreach (var path in tryPaths)
        {
            string candidate = uri.Scheme + "://" + uri.Authority + path + query;
            // 排除与自己相同的
            if (!candidate.Equals(url, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        // 有些平台用 /qrcode 但 get 参数加 &type=pdf
        if (url.IndexOf("&type=", StringComparison.OrdinalIgnoreCase) < 0
            && url.IndexOf("?type=", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return url + "&type=pdf";
        }
    }
    catch { }
    return null;
}

// 检查文件魔数，返回正确的扩展名，如果无法识别则返回 null
private static string CheckFileType(string filePath)
{
    try
    {
        byte[] h = new byte[4];
        using (var fs = File.OpenRead(filePath))
        {
            if (fs.Length < 4) return null;
            fs.Read(h, 0, 4);
        }
        // %PDF
        if (h[0] == 0x25 && h[1] == 0x50 && h[2] == 0x44 && h[3] == 0x46) return ".pdf";
        // PK (ZIP/OFD/DOCX)
        if (h[0] == 0x50 && h[1] == 0x4B) return ".ofd";
        // JPEG
        if (h[0] == 0xFF && h[1] == 0xD8) return ".jpg";
        // PNG
        if (h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return ".png";
    }
    catch { }
    return null;
}

// 重命名文件到指定扩展名（同名自动序号）
private static string RenameTo(string srcPath, string ext, string saveDir)
{
    string finalPath = Path.ChangeExtension(srcPath, ext);
    finalPath = GetUniqueFilePath(finalPath);
    // 确保目标不在临时目录
    if (!string.Equals(Path.GetDirectoryName(finalPath), saveDir, StringComparison.OrdinalIgnoreCase))
        finalPath = GetUniqueFilePath(Path.Combine(saveDir, Path.GetFileNameWithoutExtension(srcPath) + ext));
    try { File.Move(srcPath, finalPath); } catch { finalPath = srcPath; }
    return finalPath;
}

// ========== QR 解码：暴力扫描所有已加载程序集 → 动态加载 zxing.dll ==========
private static Assembly _zxingAsm = null;

private static string DecodeQRFromFile(string filePath)
{
    try
    {
        using (var bmp = new Bitmap(filePath))
        {
            return DecodeQRFromBitmap(bmp);
        }
    }
    catch { }
    return null;
}

private static string DecodeQRFromBitmap(Bitmap bmp)
{
    if (bmp == null) return null;

    // ---- 阶段1：在已加载程序集中无差别扫描所有含 Decode(Bitmap) 方法的类型 ----
    var typesWithDecode = new List<Type>();
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
    {
        if (asm.IsDynamic) continue;
        Type[] types;
        try { types = asm.GetTypes(); } catch { continue; }
        foreach (var t in types)
        {
            try
            {
                if (t.GetMethod("Decode", new[] { typeof(Bitmap) }) != null)
                    typesWithDecode.Add(t);
            }
            catch { }
        }
    }

    foreach (var type in typesWithDecode)
    {
        try
        {
            object instance;
            try { instance = Activator.CreateInstance(type); }
            catch { instance = null; }

            var method = type.GetMethod("Decode", new[] { typeof(Bitmap) });
            if (method == null) continue;

            var result = method.Invoke(instance, new object[] { bmp });
            if (result == null) continue;

            // 尝试 .Text / .Data / .ToString()
            foreach (var pn in new[] { "Text", "Data", "Code" })
            {
                var prop = result.GetType().GetProperty(pn);
                if (prop != null)
                {
                    var val = prop.GetValue(result) as string;
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
            var str = result as string;
            if (!string.IsNullOrEmpty(str)) return str;
        }
        catch { }
    }

    // ---- 阶段2：动态从磁盘加载 zxing.dll 再试一次 ----
    try { LoadZxingAssembly(); } catch { }
    if (_zxingAsm != null)
    {
        try
        {
            var readerType = _zxingAsm.GetType("ZXing.BarcodeReader");
            if (readerType != null)
            {
                var reader = Activator.CreateInstance(readerType);
                // 设属性（容错）
                try { readerType.GetProperty("AutoRotate")?.SetValue(reader, true); } catch { }
                try { readerType.GetProperty("TryInverted")?.SetValue(reader, true); } catch { }
                try
                {
                    var optType = _zxingAsm.GetType("ZXing.Common.DecodingOptions");
                    if (optType != null)
                    {
                        var opt = Activator.CreateInstance(optType);
                        try { optType.GetProperty("TryHarder")?.SetValue(opt, true); } catch { }
                        try
                        {
                            var fmtType = _zxingAsm.GetType("ZXing.BarcodeFormat");
                            if (fmtType != null)
                            {
                                var qrVal = Enum.Parse(fmtType, "QR_CODE");
                                var listType = typeof(List<>).MakeGenericType(fmtType);
                                var list = Activator.CreateInstance(listType);
                                listType.GetMethod("Add")?.Invoke(list, new[] { qrVal });
                                optType.GetProperty("PossibleFormats")?.SetValue(opt, list);
                            }
                        }
                        catch { }
                        readerType.GetProperty("Options")?.SetValue(reader, opt);
                    }
                }
                catch { }

                var decMethod = readerType.GetMethod("Decode", new[] { typeof(Bitmap) });
                if (decMethod != null)
                {
                    var decResult = decMethod.Invoke(reader, new object[] { bmp });
                    if (decResult != null)
                    {
                        var text = decResult.GetType().GetProperty("Text")?.GetValue(decResult) as string;
                        if (!string.IsNullOrEmpty(text)) return text;
                    }
                }
            }
        }
        catch { }
    }

    return null;
}

private static void LoadZxingAssembly()
{
    if (_zxingAsm != null) return;
    // 从 Quicker.Public 的位置推断
    try
    {
        var qp = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Quicker.Public");
        if (qp != null && !qp.IsDynamic)
        {
            string dir = Path.GetDirectoryName(qp.Location);
            string path = Path.Combine(dir, "zxing.dll");
            if (File.Exists(path)) { _zxingAsm = Assembly.LoadFrom(path); return; }
        }
    }
    catch { }
    // Quicker 安装目录
    try
    {
        string path = @"C:\Program Files\Quicker\zxing.dll";
        if (File.Exists(path)) { _zxingAsm = Assembly.LoadFrom(path); return; }
    }
    catch { }
    // 搜索目录下任意 *zxing*.dll
    try
    {
        if (Directory.Exists(@"C:\Program Files\Quicker"))
        {
            foreach (var f in Directory.GetFiles(@"C:\Program Files\Quicker", "*zxing*.dll", SearchOption.AllDirectories))
            {
                try { _zxingAsm = Assembly.LoadFrom(f); return; } catch { }
            }
        }
    }
    catch { }
}

// SSL 证书验证回调（税局等政务平台常有自签名证书）
private static bool RemoteCertificateValidationCB(
    object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
{
    return true; // 信任所有证书
}

// ========== 下载到临时文件（HttpWebRequest + 完整浏览器头 + 共享 Cookie） ==========
private static CookieContainer _sharedCookies = new CookieContainer();

private static string DownloadToTemp(string url, string tempPath)
{
    // 尝试多次：先用桌面 UA，失败再试移动端 UA
    string[] userAgents = new[] {
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1",
    };

    foreach (var ua in userAgents)
    {
        try
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.UserAgent = ua;
            req.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
            req.Headers["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8";
            req.Headers["Accept-Encoding"] = "gzip, deflate, br";
            req.Headers["Cache-Control"] = "no-cache";
            req.Headers["Pragma"] = "no-cache";
            req.AllowAutoRedirect = true;
            req.MaximumAutomaticRedirections = 10;
            req.Timeout = 15000;
            req.ReadWriteTimeout = 30000;
            req.CookieContainer = _sharedCookies;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var respStream = resp.GetResponseStream())
            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = respStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    fileStream.Write(buffer, 0, read);
                }
            }

            // 下载成功但内容可能是 HTML，返回 null 让调用者判断
            return null;
        }
        catch (WebException wex)
        {
            // 继续尝试下一个 UA
            if (wex.Response is HttpWebResponse resp)
                return "网络请求失败 (HTTP " + (int)resp.StatusCode + ")";
            continue;
        }
        catch { continue; }
    }

    return "所有请求方式均失败";
}

// ========== 安全删除临时文件 ==========
private static void TryDelete(string path)
{
    try { if (File.Exists(path)) File.Delete(path); } catch { }
}

// ========== 从 HTML/文本响应中提取真实文件下载链接 ==========
// 税局页面可能用 GBK 编码，先按 UTF-8 尝试，失败则 GBK
// 文件链接可能不以 .pdf 结尾（全国统一电子发票平台可能用动态 URL）
private static string ExtractDownloadUrlFromContent(string filePath, string baseUrl)
{
    try
    {
        if (!File.Exists(filePath)) return null;

        // 读全部字节，多编码尝试
        byte[] raw;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            int maxLen = 512 * 1024;
            int len = (int)Math.Min(fs.Length, maxLen);
            raw = new byte[len];
            fs.Read(raw, 0, len);
        }

        string text;
        try
        {
            text = System.Text.Encoding.UTF8.GetString(raw);
            // 验证 UTF-8 有效性：如果包含大量不可读字符，可能是 GBK
            int badChars = 0;
            for (int i = 0; i < Math.Min(text.Length, 2000); i++)
                if (text[i] == '�') badChars++;
            if (badChars > 10)
                text = System.Text.Encoding.GetEncoding("GBK").GetString(raw);
        }
        catch
        {
            text = System.Text.Encoding.GetEncoding("GBK").GetString(raw);
        }

        if (string.IsNullOrEmpty(text)) return null;

        // 模式1：href/src 引用 pdf/ofd/jpg/jpeg/png
        var match = Regex.Match(text,
            @"(?:href|src)\s*=\s*[""']([^""']*\.(?:pdf|ofd|jpg|jpeg|png))[""']",
            RegexOptions.IgnoreCase);
        if (match.Success)
            return ResolveUrl(match.Groups[1].Value.Trim(), baseUrl);

        // 模式2：data-url / data-link / data-src
        match = Regex.Match(text,
            @"data-(?:url|link|src|href)\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string found = match.Groups[1].Value.Trim();
            if (IsLikelyFileUrl(found))
                return ResolveUrl(found, baseUrl);
        }

        // 模式3：JavaScript 重定向
        match = Regex.Match(text,
            @"(?:location\.href|window\.open|window\.location(?:\.href)?|self\.location)\s*[=\(]\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string found = match.Groups[1].Value.Trim();
            if (IsLikelyFileUrl(found))
                return ResolveUrl(found, baseUrl);
        }

        // 模式4：JSON 内字段（fpath/filePath/url/downloadUrl/pdfUrl）
        match = Regex.Match(text,
            @"""(?:fpath|filePath|url|downloadUrl|fileUrl|pdfUrl|ofdUrl|download_url|pdf_url)""\s*:\s*""([^""]+)""",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string found = match.Groups[1].Value.Replace("\\/", "/").Replace("\\\\", "\\").Trim();
            if (IsLikelyFileUrl(found))
                return ResolveUrl(found, baseUrl);
        }

        // 模式5：完整 http/https URL 含 pdf/ofd
        match = Regex.Match(text,
            @"https?://[^\s""'<>\\]+\.(?:pdf|ofd)(?:\?[^\s""'<>\\]*)?",
            RegexOptions.IgnoreCase);
        if (match.Success) return match.Value.Trim();

        // 模式6：全电发票平台常见——URL 含 /download/ 或 /file/ 路径
        match = Regex.Match(text,
            @"(?:href|src|location)\s*=\s*[""']([^""']*(?:/download/|/file/|/api/.*download)[^""']*)[""']",
            RegexOptions.IgnoreCase);
        if (match.Success)
            return ResolveUrl(match.Groups[1].Value.Trim(), baseUrl);

        // 模式7：任意绝对 https?:// 链接，含 download/file/fp（发票拼音）关键词
        match = Regex.Match(text,
            @"https?://[^\s""'<>]+(?:download|file|invoice|fp)[^\s""'<>]*",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string found = match.Value.Trim();
            // 排除当前 qrcode 页面本身
            if (!found.Equals(baseUrl, StringComparison.OrdinalIgnoreCase))
                return found;
        }
    }
    catch { }
    return null;
}

// 判断是否可能是文件下载 URL
private static bool IsLikelyFileUrl(string url)
{
    if (string.IsNullOrEmpty(url)) return false;
    string lower = url.ToLower();
    return lower.IndexOf(".pdf", StringComparison.OrdinalIgnoreCase) >= 0
        || lower.IndexOf(".ofd", StringComparison.OrdinalIgnoreCase) >= 0
        || lower.IndexOf(".jpg", StringComparison.OrdinalIgnoreCase) >= 0
        || lower.IndexOf(".png", StringComparison.OrdinalIgnoreCase) >= 0
        || lower.IndexOf("download", StringComparison.OrdinalIgnoreCase) >= 0
        || lower.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0
        || lower.IndexOf("/api/", StringComparison.OrdinalIgnoreCase) >= 0
        || lower.IndexOf("attachment", StringComparison.OrdinalIgnoreCase) >= 0;
}

// 将相对 URL 基于 baseUrl 补全为绝对 URL
private static string ResolveUrl(string foundUrl, string baseUrl)
{
    if (string.IsNullOrEmpty(foundUrl)) return null;
    foundUrl = foundUrl.Replace("&amp;", "&").Trim();

    if (foundUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || foundUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        return foundUrl;

    // 相对路径 → 用 baseUrl 补全
    try
    {
        var baseUri = new Uri(baseUrl);
        var abs = new Uri(baseUri, foundUrl);
        return abs.ToString();
    }
    catch { }
    return null;
}

// ========== 策略C：WebView2 自动下载（STA线程 + CoreWebView2） ==========
private static string WebView2AutoDownload(string url, string saveDir)
{
    string result = null;
    var ready = new ManualResetEventSlim(false);

    var thread = new Thread(() =>
    {
        try
        {
            // 创建隐藏 WinForms 窗口作为 WebView2 宿主
            var form = new System.Windows.Forms.Form()
            {
                Width = 1024, Height = 768,
                ShowInTaskbar = false,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow,
                WindowState = System.Windows.Forms.FormWindowState.Minimized,
                Opacity = 0.01
            };

            form.Load += async (s, e2) =>
            {
                try
                {
                    string userData = Path.Combine(Path.GetTempPath(), "qk_inv_wv2");
                    try { Directory.CreateDirectory(userData); } catch { }

                    var env = await CoreWebView2Environment.CreateAsync(null, userData);
                    var controller = await env.CreateCoreWebView2ControllerAsync(form.Handle);
                    var webView = controller.CoreWebView2;

                    // 配置 SSL 信任（税局常有自签名证书）
                    webView.Settings.IsScriptEnabled = true;
                    webView.Settings.IsWebMessageEnabled = true;

                    // 拦截下载事件
                    webView.DownloadStarting += (sender2, args2) =>
                    {
                        try
                        {
                            string dlUrl = args2.DownloadOperation?.Uri;
                            if (!string.IsNullOrEmpty(dlUrl))
                            {
                                string tmpName = "inv_" + DateTime.Now.Ticks + ".tmp";
                                string tmpPath = GetUniqueFilePath(Path.Combine(saveDir, tmpName));
                                string dlErr = DownloadToTemp(dlUrl, tmpPath);
                                if (dlErr == null && File.Exists(tmpPath))
                                {
                                    string ext = CheckFileType(tmpPath);
                                    if (ext != null)
                                    {
                                        result = RenameTo(tmpPath, ext, saveDir);
                                        args2.Handled = true;
                                    }
                                }
                            }
                        }
                        catch { }
                        try { form.BeginInvoke(new Action(() => form.Close())); } catch { }
                    };

                    // 页面加载完成后注入 JS 自动点击下载按钮
                    webView.NavigationCompleted += async (sender2, args2) =>
                    {
                        if (!args2.IsSuccess) return;

                        string autoDownloadJS = @"
(function(){
    setTimeout(function(){
        // 查找所有链接和按钮
        var btns = document.querySelectorAll('button,a,input[type=button],input[type=submit],[role=button]');
        var keywords = '下载|打印版式|打印|版式文件|PDF|OFD|发票下载|下载发票|电子发票|ofd|pdf'.split('|');
        for(var i=0;i<btns.length;i++){
            var b=btns[i], txt=(b.textContent||b.value||b.title||b.innerText||'').toLowerCase();
            for(var k=0;k<keywords.length;k++){
                if(txt.indexOf(keywords[k].toLowerCase())>=0||(b.href||'').toLowerCase().indexOf(keywords[k].toLowerCase())>=0){
                    try{b.click();return;}catch(e){}
                }
            }
        }
        // 尝试搜索iframe内的下载
        var fms = document.querySelectorAll('iframe');
        for(var j=0;j<fms.length;j++){
            try{
                var d=fms[j].contentDocument||fms[j].contentWindow.document;
                if(d){
                    var als=d.querySelectorAll('a[href],button');
                    if(als.length>0){als[0].click();return;}
                }
            }catch(e){}
        }
        // 从URL提取参数，尝试直接拼接下载地址
        var m=location.search.match(/cs=([^&]+)/);
        if(m){
            var cs=m[1], base=location.protocol+'//'+location.host;
            var paths=[
                '/siginvoice-web/api/downLoadPdf?cs=',
                '/siginvoice-web/downLoadPdf?cs=',
                '/api/download/pdf?cs=',
                '/api/download?cs=',
                '/api/file/download?cs=',
                '/download?cs='
            ];
            for(var p=0;p<paths.length;p++){
                try{window.open(base+paths[p]+cs);}catch(e){}
            }
        }
    }, 2500);
    setTimeout(function(){
        // 如果4秒还没触发下载，自行关闭
        try{window.close();}catch(e){}
    }, 6000);
})();";

                        await webView.ExecuteScriptAsync(autoDownloadJS);
                    };

                    webView.Navigate(url);
                }
                catch (Exception ex)
                {
                    if (result == null) result = "ERR:" + ex.Message;
                    try { form.BeginInvoke(new Action(() => form.Close())); } catch { }
                }
            };

            // 超时关闭
            var timer = new System.Windows.Forms.Timer { Interval = 20000 };
            timer.Tick += (s2, e2) =>
            {
                timer.Stop();
                try { form.Close(); } catch { }
            };
            timer.Start();

            System.Windows.Forms.Application.Run(form);
            timer.Stop();
        }
        catch (Exception ex)
        {
            if (result == null) result = "ERR:" + ex.Message;
        }
        ready.Set();
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.IsBackground = true;
    thread.Start();

    if (!ready.Wait(25000))
        return "ERR:WebView2 自动下载超时";

    return result;
}
private static string TryKnownDownloadPatterns(string url, string saveDir)
{
    string[][] knownPatterns = new string[][] {
        // 河南税务局已知模式
        new[] { "/siginvoice-web/api/downLoadPdf" },
        new[] { "/siginvoice-web/downLoadPdf" },
        // 全国统一电子发票服务平台
        new[] { "/api/download/pdf" },
        new[] { "/api/download" },
        new[] { "/api/file/download" },
        new[] { "/api/v1/download" },
        new[] { "/download" },
        new[] { "/file/download" },
        new[] { "/api/getFile" },
    };

    try
    {
        var uri = new Uri(url);
        string baseUrl = uri.Scheme + "://" + uri.Authority;
        string query = uri.Query;

        // 从原始URL中提取 cs 参数值
        var csMatch = Regex.Match(query, @"cs=([^&]+)");
        string csParam = csMatch.Success ? "cs=" + csMatch.Groups[1].Value : null;

        // 组合所有可能的下载URL
        var tryUrls = new List<string>();
        foreach (var pattern in knownPatterns)
        {
            string path = pattern[0];
            tryUrls.Add(baseUrl + path + query);
            if (csParam != null && query != null)
            {
                tryUrls.Add(baseUrl + path + "?" + csParam);
            }
        }

        // 额外：原URL加 &type=pdf 或 &download=1
        tryUrls.Add(url + "&type=pdf");
        tryUrls.Add(url + "&download=1");
        tryUrls.Add(url + "&format=pdf");

        // 去重并逐个尝试下载
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        tried.Add(url); // 不重复尝试原始URL（策略A已试过）

        foreach (var tryUrl in tryUrls)
        {
            if (string.IsNullOrEmpty(tryUrl) || !tried.Add(tryUrl)) continue;

            string fileName = "invoice_" + DateTime.Now.ToString("yyyyMMddHHmmss") + "_C.tmp";
            string tmpPath = Path.Combine(saveDir, GetUniqueFilePath(Path.Combine(saveDir, fileName)));

            string dlErr = DownloadToTemp(tryUrl, tmpPath);
            if (dlErr == null && File.Exists(tmpPath) && new FileInfo(tmpPath).Length > 0)
            {
                string ext = CheckFileType(tmpPath);
                if (ext != null)
                {
                    string finalPath = RenameTo(tmpPath, ext, saveDir);
                    return finalPath;
                }
            }
            TryDelete(tmpPath);
        }
    }
    catch { }

    return null;
}

// ========== 打开浏览器（返回是否成功） ==========
// Quicker Roslyn 沙箱中 Process.Start 可能受限，多层回退
private static bool OpenUrlInBrowser(string url)
{
    // 方案1：写临时 .url 文件（最可靠，不经过命令行，& 安全）
    string urlFile = null;
    try
    {
        urlFile = Path.Combine(Path.GetTempPath(), "invoice_qr_" + DateTime.Now.Ticks + ".url");
        File.WriteAllText(urlFile, "[InternetShortcut]\r\nURL=" + url + "\r\n", System.Text.Encoding.Default);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = urlFile,
            UseShellExecute = true
        });
        CleanupUrlFile(urlFile);
        return true;
    }
    catch
    {
        try { if (urlFile != null && File.Exists(urlFile)) File.Delete(urlFile); } catch { }
    }

    // 方案2：Process.Start 直接调用 URL（让 Windows Shell 选择浏览器）
// 这是最标准的做法，360 能打开，Edge 不会错解析
try
{
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    return true;
}
catch { }

// 方案3：rundll32 url.dll,FileProtocolHandler
    try
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "rundll32.exe",
            Arguments = "url.dll,FileProtocolHandler " + url,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        return true;
    }
    catch { }

    // 方案3：注册表读默认浏览器 → 用引号包裹 URL 防截断
    try
    {
        string browser = GetDefaultBrowser();
        if (!string.IsNullOrEmpty(browser) && File.Exists(browser))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = browser,
                Arguments = "\"" + url + "\"",
                UseShellExecute = false
            });
            return true;
        }
    }
    catch { }

    // 方案5：遍历常见浏览器路径（Edge 需加安全绕过参数）
    foreach (var p in BrowserPaths())
    {
        try
        {
            if (File.Exists(p))
            {
                bool isEdge = p.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) >= 0
                           || p.IndexOf("msedge", StringComparison.OrdinalIgnoreCase) >= 0;
                string extraArgs = isEdge
                    ? "--ignore-certificate-errors-spki-list --no-first-run --no-service-autorun "
                    : "";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = p,
                    Arguments = extraArgs + "\"" + url + "\"",
                    UseShellExecute = false
                });
                return true;
            }
        }
        catch { }
    }

    return false;
}

// 异步清理临时 .url 文件
private static async void CleanupUrlFile(string path)
{
    try
    {
        await System.Threading.Tasks.Task.Delay(3000);
        if (File.Exists(path)) File.Delete(path);
    }
    catch { }
}

private static string GetDefaultBrowser()
{
    try
    {
        string progId = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice",
            "Progid", null) as string;
        if (!string.IsNullOrEmpty(progId))
        {
            string cmd = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CLASSES_ROOT\" + progId + @"\shell\open\command", "", null) as string;
            if (!string.IsNullOrEmpty(cmd))
                return CleanExePath(cmd);
        }
        string cmd2 = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CLASSES_ROOT\http\shell\open\command", "", null) as string;
        if (!string.IsNullOrEmpty(cmd2))
            return CleanExePath(cmd2);
    }
    catch { }
    return null;
}

private static string CleanExePath(string cmd)
{
    cmd = cmd.Replace("\"", "").Trim();
    int i = cmd.IndexOf(" --");
    if (i > 0) cmd = cmd.Substring(0, i);
    i = cmd.IndexOf(" %");
    if (i > 0) cmd = cmd.Substring(0, i);
    if (File.Exists(cmd)) return cmd;
    return null;
}

private static string[] BrowserPaths()
{
    string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    string pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return new[]
    {
        Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
        Path.Combine(pfx, @"Google\Chrome\Application\chrome.exe"),
        Path.Combine(la, @"Google\Chrome\Application\chrome.exe"),
        Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
        Path.Combine(pfx, @"Microsoft\Edge\Application\msedge.exe"),
        Path.Combine(pf, @"Mozilla Firefox\firefox.exe"),
        Path.Combine(pfx, @"Mozilla Firefox\firefox.exe"),
        Path.Combine(pf, @"Internet Explorer\iexplore.exe"),
    };
}

// 自动追加序号，避免覆盖已有文件
private static string GetUniqueFilePath(string filePath)
{
    if (!File.Exists(filePath)) return filePath;

    string dir = Path.GetDirectoryName(filePath);
    string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
    string ext = Path.GetExtension(filePath);

    int counter = 1;
    string newPath;
    do
    {
        newPath = Path.Combine(dir, nameWithoutExt + " (" + counter + ")" + ext);
        counter++;
    } while (File.Exists(newPath) && counter < 1000);

    return newPath;
}

// ========== Toast 通知（安全版本：有 UI 线程就用，没有就跳过） ==========
private static void ShowToastSafe(string message, string type)
{
    try
    {
        // 尝试获取 Quicker 主窗口（仅 UI 线程可用）
        var app = System.Windows.Application.Current;
        if (app == null) return; // 非 UI 线程，静默跳过

        app.Dispatcher.Invoke(() =>
        {
            try
            {
                var mw = app.MainWindow;
                if (mw == null) return;

                object notifier = mw.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(f => f.FieldType.FullName != null
                        && f.FieldType.FullName.Contains("ToastNotifications.Notifier"))
                    ?.GetValue(mw);

                if (notifier == null) return;

                string extClassName = type == "success"
                    ? "ToastNotifications.Messages.SuccessExtensions"
                    : "ToastNotifications.Messages.InformationExtensions";
                string methodName = type == "success" ? "ShowSuccess" : "ShowInformation";

                var extType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(t => t.FullName == extClassName);

                var method = extType?.GetMethod(methodName, new[] { notifier.GetType(), typeof(string) });
                method?.Invoke(null, new object[] { notifier, message });
            }
            catch { }
        });
    }
    catch { }
}
