v1.0.0 → v1.1.0 所有修复（PS COM + A3 + 栏目对齐）

- set_Style()→.Style=, Footers()→Footers.Item(1): PS COM 不与 C# 互操作兼容
- PaperSize 枚举→PageWidth/PageHeight (cm): 跨 Word 版本更可靠
- PageBreakBefore→InsertBreak(2): 避免与 RTF 自带纸张设置冲突
- 每个 RTF 插入后用 Fix-A3Page 立即修正当前节
- 分节索引改硬编码 (1=封面,2=目录,3+=正文), InsertBreak 后计数更可靠
- 端到端验证: 14 节全部 A3 横向, 封面/目录单栏, 正文双栏, 页码从正文首页=1