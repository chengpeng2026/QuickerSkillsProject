# CHANGELOG — ObsidianInsertDateTime

本文件仅记录本技能的迭代历史。

---

## [v1.1.0] - 2026-07-12

### 修复
- 移除剪贴板文本末尾 `Environment.NewLine`，避免粘贴后出现双换行
- 改用单独模拟 Enter 键（VK_RETURN）控制光标跳到下一行

### 优化
- 剪贴板粘贴 + 换行逻辑分离，行为更可控

---

## [v1.0.0] - 2025-03-22

### 新增
- 在 Obsidian 编辑状态下插入 `yyyy-MM-dd 星期X HH:mm` 格式时间戳
- 选中文本自动替换
- 右下角气泡提示（动态反射定位 Quicker Toast Notifier）
- 前台窗口 Obsidian 进程校验
- 剪贴板粘贴方式发送文本，天然支持光标插入与选中替换
