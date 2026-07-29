## [v1.1.0] - 2026-07-26
### 修复
- set_Style()→.Style=：PS COM 属性赋值
- Footers()→Footers.Item(1)：PS COM 集合索引
- PaperSize 枚举→PageWidth/PageHeight (cm)：跨 Word 版本兼容
- PageBreakBefore→InsertBreak(2)：避免与 RTF sect 冲突
- 每个 RTF 插入后用 Fix-A3Page 立即修正当前节
- 分节索引改硬编码 (1=封面,2=目录,3+=正文)

### 验证
- 14 节全部 A3 横向, 封面/目录单栏, 正文双栏, 页码从正文首页=1

## [v1.1.1] - 2026-07-26
### 修复
- WaitForExit 放在 ReadToEnd 之后，防止管道缓冲区满导致死锁

## [v1.2.0] - 2026-07-26
### 新增
- 封面模板自动向上搜索 RTF 目录及所有父目录

## [v1.2.1] - 2026-07-26
### 新增
- 封面+目录页脚用 Range.Delete() 彻底清除页码

## [v1.3.0] - 2026-07-26
### 尝试
- 封面模板 Base64 内嵌方案

### 回退
- Base64 编码方案导致合并后乱码，回退到 v1.2.0

## [v1.4.0] - 2026-07-26
### 新增
- RTF 预处理：抗拔承载力验算结果 → 受拉承载力验算结果 区间字号设为目标值

### 修复
- 临时文件命名改为原始 RTF 文件名（如 1-1.RTF），解决 Heading 标题显示为 GUID 的问题

## [v1.5.0] - 2026-07-26
### 新增
- 双栏栏间竖线移除
- 页码右下角对齐 (wdAlignPageNumberRight)


## [v1.5.1] - 2026-07-26
### 新增
- 抗拔承载力→受拉承载力 间距字体改为小五号 (9pt / fs18)

## [v1.6.0] - 2026-07-26
### 重大重构
- PS 脚本生成改为 StreamWriter 逐行写入独立 .ps1 文件，避免 C# here-string 中文编码问题
- 字号修正从 RTF 预处理改为 PS COM Find 后处理（独立 PS 验证通过）
- 中文字符使用 \uXXXX 转义写入，彻底杜绝 Roslyn 误解析

### 修复
- 抗拔承载力→受拉承载力 区间字号设置为小五号 (9pt) — 通过 COM Find + Range.Font.Size=9 实现
- 原 RTF 预处理方案 (FixFontSizeInRtf) 删除（GB2312/ASCII/字节数组三种方案均无法准确定位标题）