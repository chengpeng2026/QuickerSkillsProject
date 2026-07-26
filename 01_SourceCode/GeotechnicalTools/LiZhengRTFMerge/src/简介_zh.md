# LiZhengRTFMerge v1.1.0 — 理正深基 RTF 计算书合并

## 功能

将理正深基软件导出的**多个 RTF 计算书**按文件名排序合并为一个 **A3 横向 Word 文档** (.docx)。

- **封面**：使用模板 `封面.docx`，单栏布局
- **目录**：自动生成（仅提取一级标题/Heading 1）
- **正文**：双栏布局 + 栏间分隔线
- **页码**：封面、目录无页码；正文首页从 1 开始

## 输出结构（14 节）

| 节 | 内容 | 列数 | 页码 |
|---|---|---|---|
| S1 | 封面 | 单栏 | 无 |
| S2 | 目录 | 单栏 | 无 |
| S3-S14 | 正文 (12 个 RTF) | 双栏+分隔线 | 从 1 开始 |

## 使用场景

理正深基生成的计算书为 A3 横向版式，每个文档一个或多个 RTF 文件，出计算方案初稿前批量合并到 Word。

## 技术要点

1. **PageWidth/PageHeight 控制纸张** — 不用 PaperSize 枚举（跨 Word 版本不可靠），直接用 `CentimetersToPoints(42.0)` / `(29.7)`
2. **每个 RTF 独立节** — 文件名作为一级标题（Heading 1），后面插入 RTF 原始内容
3. **RTF InsertFile 后立即修正** — RTF 自带 Letter 纸张覆盖全局设置，插入后立即用 `Fix-A3Page` 还原
4. **InsertBreak + 硬编码节索引** — `PageBreakBefore` 与 RTF `\sect` 冲突导致保存失败；改 `InsertBreak(2)` + 按固定偏移追踪节
5. **PowerShell COM 适配** — `Footers.Item(1)` 替代 `Footers(1)`，`.Style =` 替代 `.set_Style()`
6. **PowerShell 子进程 Word COM** — 避免 Roslyn 线程创建 Word Object 的经典 `CLSID` 错误
7. **反向兼容** — 无封面模板时，仅生成目录+正文

## 运行依赖

- Windows + Word 2016+ 或 WPS Office（含 VBA 组件）
- Quicker 客户端（Roslyn v2 运行时）
- `C:\Users\12089\Desktop\最终计算书\封面.docx` 作为封面模板

## 输出文件

- `合并计算书.docx` — A3 横向，含封面、目录、正文
