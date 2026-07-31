#!/usr/bin/env bash
# SessionStart hook — health check + load vault context into Claude
# Runs at the start of every session.

VAULT_DIR="E:/Aiku2026"
PROJECT_DIR="$CLAUDE_PROJECT_DIR"
ERRORS=0

echo ""
echo "=========================================="
echo "=== CLAUDE CODE ↔ OBSIDIAN 健康检查 ==="
echo "=========================================="

# --- Health Checks ---
echo ""
echo "--- 基础设施检查 ---"

# 1. CLAUDE.md exists
if [ -f "$PROJECT_DIR/CLAUDE.md" ]; then
  echo "  ✅ CLAUDE.md 存在"
else
  echo "  ❌ CLAUDE.md 缺失！"
  ERRORS=$((ERRORS + 1))
fi

# 2. settings.json has SessionStart hook
if grep -q "SessionStart" "$PROJECT_DIR/.claude/settings.json" 2>/dev/null; then
  echo "  ✅ settings.json 含 SessionStart hook"
else
  echo "  ❌ settings.json 缺少 SessionStart hook！"
  ERRORS=$((ERRORS + 1))
fi

# 3. obsidian-vault skill exists
if [ -f "$PROJECT_DIR/.agents/skills/obsidian-vault/SKILL.md" ]; then
  echo "  ✅ obsidian-vault skill 存在"
else
  echo "  ❌ obsidian-vault skill 缺失！"
  ERRORS=$((ERRORS + 1))
fi

# 4. knowledge-capture skill exists
if [ -f "$PROJECT_DIR/.agents/skills/knowledge-capture/SKILL.md" ]; then
  echo "  ✅ knowledge-capture skill 存在"
else
  echo "  ❌ knowledge-capture skill 缺失！"
  ERRORS=$((ERRORS + 1))
fi

# 5. Vault directory accessible
if [ -d "$VAULT_DIR" ]; then
  VAULT_COUNT=$(find "$VAULT_DIR" -name "*.md" -not -path "*/.obsidian/*" 2>/dev/null | wc -l)
  echo "  ✅ Vault 可访问 ($VAULT_COUNT 篇笔记)"
else
  echo "  ❌ Vault 目录不可访问: $VAULT_DIR"
  ERRORS=$((ERRORS + 1))
fi

# 6. At least 1 MOC note exists
MOC_COUNT=$(ls "$VAULT_DIR"/*知识库*.md "$VAULT_DIR"/*避坑*.md "$VAULT_DIR"/*平台*.md "$VAULT_DIR"/*规范*.md 2>/dev/null | wc -l)
if [ "$MOC_COUNT" -gt 0 ]; then
  echo "  ✅ MOC 笔记存在 ($MOC_COUNT 篇)"
else
  echo "  ⚠️  MOC 笔记缺失——vault 可能被清空"
fi

# --- Summary ---
echo ""
if [ "$ERRORS" -eq 0 ]; then
  echo "=== 健康检查通过 ✅ ==="
else
  echo "=== 健康检查失败 ❌ ($ERRORS 项异常) ==="
  echo "请修复以上问题后重新开始会话。"
fi

# --- Vault Context ---
echo ""
echo "--- Vault 最近动态 (7天) ---"
if [ -d "$VAULT_DIR" ]; then
  find "$VAULT_DIR" -name "*.md" -mtime -7 -not -path "*/.obsidian/*" 2>/dev/null | while read -r f; do
    rel="${f#$VAULT_DIR/}"
    echo "  $rel"
  done
fi

# --- Action hint ---
echo ""
echo "下一步: 根据用户任务关键词，搜索 vault 加载 1-3 篇相关笔记。"
echo "  Grep pattern=\"<keyword>\" path=\"$VAULT_DIR\" glob=\"**/*.md\" output_mode=\"files_with_matches\""

# --- Daily Review Reminder ---
echo ""
echo "--- 每日复盘检查 ---"
YESTERDAY=$(date -d "yesterday" +%Y-%m-%d 2>/dev/null || date -v-1d +%Y-%m-%d 2>/dev/null)
if [ -n "$YESTERDAY" ]; then
  REVIEW_FILE="$VAULT_DIR/Reviews/Daily/$YESTERDAY.md"
  if [ -f "$REVIEW_FILE" ]; then
    echo "  ✅ 昨日复盘已完成: Reviews/Daily/$YESTERDAY.md"
  else
    echo "  ⚠️  昨日 ($YESTERDAY) 尚未复盘！"
    echo "     今日任务结束后请运行每日复盘，模板见 [[Claude Code 复盘体系]]"
  fi
else
  echo "  ⚠️  无法计算昨日日期，跳过复盘检查"
fi

echo "=========================================="
echo ""

exit 0
