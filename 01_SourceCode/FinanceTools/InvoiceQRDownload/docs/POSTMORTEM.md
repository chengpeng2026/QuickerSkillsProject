# 电子发票二维码识别下载 — 项目复盘

## 项目概况

| 项目 | 值 |
|------|-----|
| 动作名称 | 电子发票二维码识别下载 |
| 动作 ID | `4a0deeb9-ff9d-4444-b0a4-71f77e0ccfa1` |
| 开发时间 | 2026-07-14 ~ 2026-07-15 |
| 代码量 | 1064 行 C# + 80 行 JSON |
| 迭代次数 | 约 20 次构建 |
| 平台 | Quicker Roslyn v2 |

---

## 需求回顾

核心需求：识别电子发票二维码 → 提取链接 → 自动下载发票原件到用户指定目录。

最初设计为「双模式」（资源管理器选中文件 / 屏幕截图），后简化为「文件选择对话框 → 识别 → 选目录 → 下载」的线性四步流程。

---

## 架构演进

### 初始架构（v0.1）

```
选文件 → QR解码 → 选目录 → URL后缀判断 → 下载或浏览器
```

### 最终架构（v1.0）

```
选文件 → QR解码 → 选目录 → 策略A(直链下载)
                           → 策略B(HTML解析提取+URL推断)
                           → 策略C(WebView2自动点击下载)
                           → 策略D(枚举已知API路径)
                           → 策略E(浏览器打开)
```

### 5 层下载策略详解

| 层 | 策略 | 依赖 | 命中场景 |
|----|------|------|----------|
| A | HttpWebRequest 直链下载 + 文件魔数校验 | 无 | 302 重定向到 PDF 直链 |
| B | HTML 解析提取链接（UTF-8/GBK 双编码） + URL 模式推断 | 无 | 税局查验页内嵌真实 URL |
| C | WebView2 STA 线程后台加载 → JS 自动点击 → 拦截 DownloadStarting | WebView2 Runtime | 河南等需 JS 交互的技术中台 |
| D | 枚举 9 种已知 API 路径 + cs 参数拼接 | 无 | 全国统一电子发票服务平台 |
| E | .url 文件 / ShellExecute / 注册表 / 遍历 .exe | 无 | 最后回退 |

---

## 踩坑记录

### 坑1：Quicker 后台线程无 WPF Dispatcher

**现象**：首版使用 `Application.Current.Dispatcher.Invoke()`，点击无反应。

**根因**：Roslyn 后台线程中 `System.Windows.Application.Current` 为 null。

**修复**：
- UI 交互全部改用 `System.Windows.Forms`（MessageBox, OpenFileDialog, FolderBrowserDialog）
- Toast 通知仅在有 UI 线程时发射，否则静默跳过
- 入口改为同步 `public static string Exec()` 而非 `async Task`

### 坑2：ZBar/ZXing 反射通道全部失灵

**现象**：QR 解码始终返回 null。

**根因**：
- `Assembly.GetExecutingAssembly().Location` 在 Roslyn 临时编译目录，没有 zxing.dll
- 程序集按名匹配过于严格（`type.Name != "BarcodeReader"` 之类）
- Quicker 内置 zxing.dll 未被预加载

**修复**：
- 阶段1：暴力扫描所有已加载程序集的每个 Type，找 `Decode(Bitmap)` 方法签名
- 阶段2：通过 `Quicker.Public` 位置反推 + `C:\Program Files\Quicker` 硬编码 + 目录搜索三级路径定位 zxing.dll 然后 `Assembly.LoadFrom`
- 结果对象遍历 `Text`/`Data`/`Code` 三个属性名

### 坑3：`cmd /c start` 打开 URL 失败

**现象**：360 能打开，Edge 显示「错误网址」。

**根因**：
- `cmd /c start "" "url?cs=...&jrxt=..."` — cmd 把 `&` 当命令分隔符，截断到 `?cs=...` 就停了
- `.url` 文件用 UTF-8 编码导致系统 ANSI 解析异常（Edge 更严格）
- `Process.Start(url, UseShellExecute=true)` 在 Quicker 沙箱中被 COM 拦截

**修复**（4 层回退栈）：
1. `.url` 文件用 `Encoding.Default`（系统 ANSI）写入 → ShellExecute 打开文件
2. `rundll32 url.dll,FileProtocolHandler` — Windows 底层 URL 分发
3. 注册表 `HKCU\...http\UserChoice` 读默认浏览器路径 → 直接启动 .exe + 引号包裹 URL
4. 遍历 Chrome/Edge/Firefox/IE 安装路径 → Edge 加 `--ignore-certificate-errors-spki-list`

### 坑4：税局服务器 SSL 握手失败

**现象**：`DownloadToTemp` 始终抛 `WebException`。

**根因**：税局服务器（如 `dppt.henan.chinatax.gov.cn:8443`）使用自签名证书。

**修复**：
- `ServicePointManager.ServerCertificateValidationCallback` 返回 `true`
- 启用 `Tls12 | Tls11 | Tls`
- HttpWebRequest 加完整浏览器头（`Accept-Language: zh-CN`, `Accept-Encoding: gzip, deflate, br`）

### 坑5：HTML 解析失败（GBK 乱码）

**现象**：策略 B 从 HTML 提取链接始终返回 null。

**根因**：税局页面使用 GBK 编码，UTF-8 解析后中文全变 `�`，正则无法匹配中文关键词。

**修复**：
- 先 UTF-8 解码
- 检测前 2000 字符中 `�` 出现次数 > 10 → 判定为 GBK，重新解码
- 正则模式覆盖中英文关键词（`下载|打印版式|PDF|OFD|download|file`）

### 坑6：WebView2 集成复杂度

**现象**：尝试 3 次才编译通过。

**根因**：
- `Microsoft.Web.WebView2.Wpf` 命名空间在 Quicker 中不可用（需 WPF 宿主）
- JavaScript 字符串中的 `\` 和引号在 C# 逐字字符串中仍需转义
- 需要独立 STA 线程 + WinForms 消息循环

**最终方案**：
- 仅引用 `Microsoft.Web.WebView2.Core`
- 创建隐藏 WinForms Form 作为宿主
- `CoreWebView2Environment.CreateAsync` → `CreateCoreWebView2ControllerAsync(form.Handle)`
- 注入 JS：查找含下载关键词的按钮 → 自动点击 → 拦截 `DownloadStarting` 事件 → `DownloadToTemp` 保存
- 整个 C 策略包裹在 try/catch 中，失败静默回退

---

## 已知未解决问题

1. **WebView2 依赖**：策略 C 需要系统安装 WebView2 Runtime，未安装时跳过
2. **各税局 API 差异大**：河南、广东、四川等各省税务局发票平台不同，URL 模式推断的命中率不确定
3. **沙箱限制无法实测**：本机环境被代理拦截（解析到 `198.18.x.x`），无法实际请求税局服务器验证下载
4. **Edge vs 360 差异**：`.url` 文件 + 系统默认编码在 360 上正常但 Edge 对非 ASCII URL 处理不同

---

## 经验教训

### 应该做的

1. **先用命令行实测再写代码**：如果一开始就用 `curl` 请求税局 URL 看响应内容，就不会在猜 URL 模式上浪费大量迭代
2. **Quickster Roslyn 后台线程是第一约束**：任何 WPF/WinForms 混用都要先验证线程模型
3. **多编码容错**：在国内政务场景中 GBK/GB2312 仍然常见，UTF-8 only 会出问题
4. **URL 中 `&` 是沉默杀手**：所有命令行传参方案都会挂，必须用 ShellExecute 或 .url 文件

### 不应该做的

1. **不要猜服务器行为**：在无法实测的情况下编造了太多推测性下载 URL，应该尽早要求用户在浏览器中 F12 抓包确认
2. **不要让一个动作承担所有责任**：QR 解码 + 下载 + 浏览器自动化在一个动作里太重，应该拆分为「QR 解码」+「智能下载」两个独立动作
3. **代码膨胀问题**：从最初 200 行膨胀到 1064 行，多人维护会困难。应该尽早模块化

---

## 改进建议（如果要继续迭代）

| 优先级 | 改进 | 说明 |
|--------|------|------|
| P0 | 用户浏览器 F12 抓包获取真实下载 API | 让用户在浏览器打开查验页 → 按 F12 → Network 标签 → 点击下载按钮 → 看实际请求的 URL 和 Headers → 用该模式写死 |
| P1 | 拆分动作为「QR解码」+「智能下载」 | 降低单动作复杂度，QR 解码可复用 |
| P2 | 增加 `Referer` 头 | 许多税局服务器校验 Referer，当前请求不带 |
| P3 | 支持 OFD 转 PDF | 部分税务局返回 OFD 格式，用户可能更期望 PDF |
| P4 | 下载进度条 | 当前是隐式下载，大文件无反馈 |

---

## 文件清单

| 文件 | 路径 |
|------|------|
| JSON 配置 | `01_SourceCode/FinanceTools/InvoiceQRDownload/src/InvoiceQRDownload.json` |
| C# 逻辑 | `01_SourceCode/FinanceTools/InvoiceQRDownload/src/InvoiceQRDownload.cs` |
| 线上简介 | `01_SourceCode/FinanceTools/InvoiceQRDownload/src/InvoiceQRDownload_简介.md` |
| 项目复盘 | `01_SourceCode/FinanceTools/InvoiceQRDownload/docs/POSTMORTEM.md` |
