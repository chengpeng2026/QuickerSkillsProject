# CHANGELOG_GLOBAL

> QuickerSkillsProject 全系列技能迭代记录，按时间倒序

---

## [工程标准化 v1.0.0] - 2026-07-12

### 新增
- 建立标准化工程目录：`01_SourceCode/` / `02_BuildOutput/` / `03_DocsPublic/` / `04_ReleaseArchive/`
- `.gitignore` 过滤打包成品与临时文件
- `README_工程说明.md` 维护规范文档
- `CHANGELOG_GLOBAL.md` 全局变更日志
- 根目录 `VERSION_MAP.json` 作为项目级版本单一事实来源
- 两个动作均补充 `Version` 字段、技能级 `CHANGELOG.md`
- 两个动作均通过本地构建验证

### 重构
- `ObsidianInsertDateTime` → `01_SourceCode/ObsidianTools/ObsidianInsertDateTime/src/`
- `SimplePopup` → `01_SourceCode/CommonTools/SimplePopup/src/`

---

## [ObsidianInsertDateTime v1.1.0] - 2026-07-12

### 修复
- 移除剪贴板末尾的 `Environment.NewLine`，避免粘贴后出现双换行
- 改用单独模拟 Enter 键（VK_RETURN）控制光标跳到下一行

---

## [SimplePopup v1.0.0] - 2026-07-10

### 新增
- 基础 WPF 弹窗提示动作
- 支持自定义弹窗文本（`popup_text`）和标题（`popup_title`）

---

## [ObsidianInsertDateTime v1.0.0] - 2025-03-22

### 新增
- 在 Obsidian 编辑状态下插入 `yyyy-MM-dd 星期X HH:mm` 格式时间戳
- 选中文本自动替换
- 右下角气泡提示
- 前台窗口 Obsidian 进程校验
- 剪贴板粘贴方式发送文本（天然支持光标插入与选中替换）
