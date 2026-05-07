#!/bin/bash
# Stage 2 完了コミットスクリプト
# Usage: chmod +x commit_stage2_complete.sh && ./commit_stage2_complete.sh

set -e

cd "$(dirname "$0")"

# Remove stale worktree references (safe - these are old Claude sessions)
echo "Cleaning up stale git worktrees..."
git worktree prune 2>/dev/null || true

# Remove index lock if exists
rm -f .git/index.lock

echo "Staging changes..."
git add \
  "src/prototype/unity/Assets/BJJSimulator/Runtime/Platform/BJJVolumeController.cs" \
  "src/prototype/unity/Assets/BJJSimulator/Runtime/Platform/BJJImpactFeedback.cs" \
  "src/prototype/unity/Assets/BJJSimulator/Editor/BJJSceneSetup.cs" \
  "docs/design/stage2_port_progress.md"

# Also stage scene and assets if modified
git add \
  "src/prototype/unity/Assets/Scenes/" \
  "src/prototype/unity/Assets/BJJSimulator/Runtime/UI/BJJHudPanelSettings.asset" \
  "src/prototype/unity/Assets/BJJSimulator/Runtime/Render/BJJVolumeProfile.asset" 2>/dev/null || true

git add \
  "src/prototype/unity/Assets/Scenes/BJJ.unity" \
  "src/prototype/unity/Assets/Scenes/BJJ.unity.meta" 2>/dev/null || true

echo ""
echo "Files staged:"
git diff --cached --name-only

echo ""
git commit -m "$(cat <<'EOF'
fix: Stage 2 完了 — WhiteBalance/HUD二重表示修正、SetupScene完全自動化

- BJJVolumeController: Awake()→Start()、sharedProfile使用でMissingReferenceException修正
- BJJImpactFeedback: 同様のsharedProfile修正 (Awake→Start+BindVolumeComponents)
- BJJSceneSetup: UI Toolkit HUD追加後にBJJDebugHud無効化で二重表示解消
- docs: stage2_port_progress.md 全行🟢に更新 (BJJAvatarBinder, BJJVolumeController)

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"

echo ""
echo "✅ Commit complete."
git log --oneline -3
