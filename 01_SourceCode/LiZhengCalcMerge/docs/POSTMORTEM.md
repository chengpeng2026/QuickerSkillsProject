# 理正基坑计算书合并 — 项目复盘

**日期**: 2026-07-23 ~ 2026-07-25  
**最终版本**: v5.5.5  (ActionId: `6fc230f2-6a72-43f4-b504-39e069bb9148`)  
**代码行数**: ~370 行 C#（含 ~130 行内嵌 PowerShell 脚本生成）  
**构建迭代**: 约 60+ 次  

---

## 一、项目目标

将文件夹中的理正基坑计算书（RTF 格式）合并为一个 Word 文档：
1. 按剖面编号自然排序（多段数值序列对比：1-1 < 1-1-1 < 2-2 < 10-10）
2. 封面（工程名称 + 签名表格 + 公司名 + 日期）
3. 封面无页码，正文页脚"第 X 页 / 共 Y 页"，底部居中
4. 支持 Word 和 WPS 双引擎
5. 自动目录页（TOC 域，`\o "1-1"`），每个 RTF 文件名作为一级标题
6. 页码起始值设为 0（封面为第 1 页隐藏，正文第 2 页起显示"第 1 页"）
7. 按 PWC 编号习惯排序（每段数值逐一比较，1-1 排在 1-1-1 前）

## 二、交互流程

```
选源文件夹 → 选保存位置 → 输入文件名 → 自动合并
```

## 三、遇到的问题与解决状态

### ✅ 已解决（17 项）

| # | 问题 | 解决方案 | 关键版本 |
|---|------|----------|----------|
| 1 | **Quicker Roslyn 无法创建 Office/WPS COM** | 放弃进程内 COM，改为生成 .ps1 脚本用 `powershell.exe -STA` 子进程执行 | v3.0.0 |
| 2 | **WPS 的 InsertFile 报错 0xFFF40005** | 改用 `Documents.Open → Content.Copy → Selection.Paste` | v1.2.0 |
| 3 | **WPS 的 Quit() 抛 RPC 错误 0x800706BE** | 所有 Cleanup 操作（Close/Quit/ReleaseComObject）包 try-catch 设为非致命 | v3.1.0 |
| 4 | **SaveAs 到中文路径静默失败** | 先 `SaveAs` 到 `%TEMP%`，再 `Move-Item` 到目标目录 | v3.1.0 |
| 5 | **WPS COM ProgID 在 Quicker 中不可见** | 无关紧要——PowerShell 子进程方案绕过了这个问题 | v3.0.0 |
| 6 | **封面生成** | PowerShell 脚本内用 Selection.TypeText 逐行生成 | v4.0.0 |
| 7 | **签名图片嵌入封面** | 3 行 × 2 列无边框表格（标签右对齐 + 签名图左对齐）| v4.6.0 |
| 8 | **封面无页码** | `DifferentFirstPageHeaderFooter = -1`，首页页脚 Footer(2) 清空 | v4.7.6 |
| 9 | **自定义文件名输入** | WinForms 弹窗（Form + TextBox + 确定/取消按钮）| v3.2.0 |
| 10 | **保存位置选择** | `FolderBrowserDialog.SelectedPath = 源文件夹` 作为默认值 | v3.3.0 |
| 11 | **剖面编号自然排序** | v1.0: Regex 提取 (int, int) 二元组；v5.5.1 重构为 `ParseSegments` + `SegCmp` 通用多段数值序列对比，支持 1-1 < 1-1-1 < 2-2 < 10-10 的自然排序 | v1.0 → v5.5.1 |
| 12 | **UTF-8 BOM 编码** | .cs 文件必须存 UTF-8 with BOM，否则 C# 中文乱码且 Roslyn 编译报"Unrecognized escape sequence" | v1.0 |
| 13 | **个别文件损坏容错** | 每个 RTF 的 Open/Copy/Paste 包独立 try-catch，失败跳过不中断 | v1.0 |
| 14 | **页码每页都显示相同数字** | v5.2.0 ZIP 整段替换方案因 ZipArchive 类型加载失败无效；v5.3.0 改为 SeekView + Selection.Fields.Add + PrintPreview + Fields.Update，在 Word 上已验证通过 | v5.2.0 → v5.3.0 |
| 15 | **自动目录（TOC）+ 一级标题** | v5.4.0 增加目录页，插入 wdFieldTOC=97 域，`\o "1-1" \h` 收集所有 OutlineLevel=1 段落；v5.5.0 将每个 RTF 文件名（如"1-1剖面计算书"）设为 Heading 1 样式（set_Style(-2) + OutlineLevel=1），TOC 自动索引 | v5.4.0 → v5.5.0 |
| 17 | **PWC 编号：短序列排在前面** | v5.5.1 的 `SegCmp` 短序列补 `int.MaxValue`，导致短序列排到长序列后面（1-1 排在 1-1-1 之后）。v5.5.2 将 Pad 值从 `int.MaxValue` 改为 `int.MinValue`，仅改 2 个字符。教训：PWC 编号习惯是缺失段视为"无"而非"无穷大"。 | v5.5.2 |

### ❌ 未解决（1 项）

| # | 问题 | 当前状态 | 影响 |
|---|------|----------|------|
| 1 | **云端发布失败** | `getquicker.net` 返回 nginx 400 Bad Request，边缘层拦截 | 动作只能在本地使用，无法分享到 Quicker 社区 |

### 中途放弃的方向（5 项）

| 尝试 | 为什么放弃 |
|------|-----------|
| 进程内 COM（Type.GetTypeFromProgID + Activator.CreateInstance） | Quicker Roslyn 沙箱限制，始终报 0x80040154/0x80080005 |
| CLSID GUID 直连 | 同 Quicker 进程限制 |
| 手动读注册表 + /regserver | WPS /regserver 只注册到 HKCU，Quicker 进程看不到 |
| 双节分节（SectionBreak 实现封面独立节 + 正文页码从1开始） | WPS 的 Section.Footers.LinkToPrevious 不可靠，页脚写入频繁失败 |
| 日常自动生成封面 | 用户提供了固定签名图片，改为固定模板 |

## 四、技术架构（最终方案）

```
C# Exec()                         ← Quicker Roslyn 进程
  ├── FolderBrowserDialog          ← 选源文件夹
  ├── FolderBrowserDialog          ← 选保存位置
  ├── Form + TextBox               ← 输入文件名
  └── MergeViaPowerShell()         ← 生成 .ps1 写到 TEMP
        └── Process.Start(         ← 起独立 PowerShell -STA 进程
              powershell.exe)
              ├── Word/WPS COM     ← 正常创建、合并、保存
              ├── 封面 + 签名表
              ├── 目录页（TOC 域 + F9 提示）
              ├── 逐文件 Heading 1 标题 + RTF 内容合并
              └── 页脚页码（PAGE / NUMPAGES 域）
```

**为何必须是 PowerShell 子进程？**
Quicker Roslyn 的 COM 激活上下文有沙箱限制，`CoCreateInstance({000209FF-...})` 始终失败。
PowerShell 子进程不受此限制，且 `-STA` 参数确保兼容 COM 单线程套间要求。

## 五、经验教训

1. **Quicker 里做 Office 自动化，直接上 PowerShell 子进程**——不要在 Roslyn 内绑 COM，这是死胡同。模式可复用：C# 生成 .ps1 → flag 文件回传 OK/ERR → 清理临时文件。

2. **WPS COM 和 Word COM 差异比想象中大**：InsertFile 不支持、Quit 必抛异常、SaveAs2 不存在、Fields.Add 的 Range 定位不可靠。代码要对 WPS 做最大兼容，所有 COM 调用包 try-catch。

3. **页脚域代码用 SeekView + Selection.Fields.Add + PrintPreview**：ZIP post-processing 方案依赖 `System.IO.Compression.ZipArchive`，但在 .NET Framework 4.x + PowerShell 5.1 下该类型不可用（尽管 `Add-Type` 能加载 assembly）。最可靠的做法还是 COM 的 `Fields.Add`，配合 PrintPreview 周期触发域解析。核心步骤：① `GoTo(1,1,2)` 跳到第 2 页 ② `SeekView=4` 进入页脚视图 ③ `Selection.Fields.Add(Range, 33/26)` 插入 PAGE/NUMPAGES 域 ④ `PrintPreview()` + `ClosePrintPreview()` 强制刷新。

4. **两节方案在 WPS 中不可靠**：LinkToPrevious、RestartNumberingAtSection 在 WPS 的 COM 实现中行为异常，应优先用单节 + DifferentFirstPage 方案。

5. **Unicode 转义 vs 直接中文字符串**：C# 代码的 `sb.AppendLine("$sel.TypeText('中文')")` 中的中文字符在 Roslyn 沙箱中极易编码损坏（非 BOM UTF-8 或无 BOM 都会导致"Unrecognized escape sequence"编译错误）。最终方案：PowerShell 端中文字符全部改用 `[char]0xXXXX` 拼接；C# 字符串中保留中文（对话框、title等）必须确保文件存为 UTF-8 with BOM。

6. **TOC 域不会自动填充**：Word/WPS COM 的 `Fields.Add(Range, 97=wdFieldTOC, \o "1-1" \h)` 只能插入 TOC 域，但 PrintPreview 和 Fields.Update 都不会在 COM 中触发 TOC 的实际条目收集。用户打开 .docx 后需要 Ctrl+A → F9 来手动刷新所有域（TOC + PAGE + NUMPAGES）。目录页底部灰色斜体提示文字"F9更新目录"即为此设计。

7. **OutlineLevel 比 Style 名称更可靠**：WPS 的 `Styles.Item('Heading 1')` 在很多版本中会返回 null 或错误的中文样式名，而 `Selection.ParagraphFormat.OutlineLevel = 1` 是数值属性，不受本地化影响，TOC 的 `\o` 开关也是按 OutlineLevel 级别收集的。因此标题段落同时设置 `set_Style(-2)` 和 `OutlineLevel=1` 作为双保险。

8. **页码起始值设为 0 是工程报告规范**：`StartingNumber = 0` 配合 `DifferentFirstPageHeaderFooter = -1`，封面为页码 1（隐藏），正文第 2 页起页码显示为 1。这是勘测设计报告、可研报告、施工图计算书的通用规范。

9. **死循环陷阱反思**：v5.5.1 开发末期，一行 TOC 目录页的提示文字引发了大量无意义的修改和构建（约 10+ 次）。教训：单行提示文字属于"完成了就不再碰"的低优先级 UI 细节，设定"只改一次，改完就提交"的纪律。

10. **Pad 值方向决定短序列排位**：多段数值排序中，短数组补齐值 `int.MaxValue`（短序列排后面）vs `int.MinValue`（短序列排前面）只差 2 个字符，但排序结果完全不同。PWC 编号习惯要求 MinValue。

## 六、v5.5.x 排版功能踩坑实录（2026-07-26）

### 新增目标
6. 封面+目录无页码，正文"第 X 页"（无 NUMPAGES）
7. 全文 A3 横向
8. 封面+目录 A3 横向一栏，正文 A3 横向两栏
9. 删除"验算项目:"分隔线
10. 抗拔-受拉承载力间字体 10pt

### 核心问题：分栏始终不生效

**现象**：`$master.Sections.Item(2).PageSetup.TextColumns.SetCount(2)` 执行后 COM 返回值确认为 2，OOXML 中也显示 `<w:cols w:num="2"/>`，但用户打开 Word 文件后正文仍然是单栏。

**尝试过的方向**：
- 分节符位置：封面之后、目录之后、正文之前 → 均无效
- 分节符类型：连续分节符(3)、下一页分节符(2) → 均无效
- 分栏设置顺序：在页脚之前、页脚之后 → 均无效
- 变量引用：用旧引用 vs 用 `$master.Sections.Item(2)` → 均无效
- COM 会话：合并脚本内、独立后处理进程 → 均无效
- 空白文档 **纯测试**：COM 创建空白 A3 docx + 分节 + 分栏，**能工作**（OOXML 确认 S1=1, S2=2）

### 根本原因分析

**√ 关键发现**：空白文档中 COM 分栏正常，合并后的复杂文档中 COM 分栏**OOXML 层面也是正常的**，但 Word 渲染时忽略了。原因是：合并过程中产生的文档包含**多个隐式分节符**。当 `InsertBreak(2)` 插入新分节符时，文档至少已有 2+ 节。即 `Sections.Item(2)` 指向的节与预期的正文节不同——可能指向一个空的隐式节或错误的节。这就是为什么 OOXML 中有 cols=2 但正文仍为单栏——那个 cols=2 设置应用到了错误的节上。

### 最终解决方向

放弃 COM 分节+分栏。改用 OOXML 后处理：
1. 合并脚本只做合并+封面+TOC+页码字段（不碰分栏/A3/分节）
2. 后处理独立 PS 打开 docx 的 ZIP，直接修改 XML：
   - 在封面+目录之后插入 `<w:sectPr>` 分节符
   - 第 2 节的 `<w:cols>` 设为 `num="2"`
   - 同时处理 A3 横向、页码、字体调整

### 新增经验教训

11. **COM 中操作分节非常不可靠**：Word 在 Copy/Paste 等复杂操作中会产生隐式分节符，导致 Sections.Item(N) 引用不可预测。如果在复杂文档中需要精确控制分节和分栏，唯一的可靠方法是 OOXML 后处理。

12. **OOXML 本地验证是可靠的诊断工具**：任何 COM 操作后都应该用 ZIP 读取 docx 检查 OOXML 是否被正确写入。COM 返回值可能成功，但 OOXML 才是最终真相。

13. **从空白测试到复杂文档的差距**：一个技术在空白文档中能工作不代表在复杂文档中也能工作。复杂文档中的伪分节和隐式分节是 COM 排版的最大障碍。
