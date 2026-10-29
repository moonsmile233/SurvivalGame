#!/usr/bin/env bash
# 把 web/ 目录部署到 Netlify。要求：已构建 WebGL（web/unity/build-info.json 存在），
# 且已执行过 netlify login / netlify link。
set -euo pipefail

cd "$(dirname "$0")/.."

if [ ! -f "web/unity/build-info.json" ]; then
  echo "缺少 web/unity/build-info.json，请先构建 WebGL：" >&2
  echo "  编辑器开着：菜单 Survival/Build WebGL" >&2
  echo "  编辑器关着：\"D:/unity hub/Editor/Unity.exe\" -batchmode -quit \\" >&2
  echo "              -projectPath \"$PWD\" -executeMethod BuildScript.BuildWebGL" >&2
  exit 1
fi

echo "→ 部署目录：$PWD/web"
echo "→ 产物：$(date -r web/unity/build-info.json '+%Y-%m-%d %H:%M:%S' 2>/dev/null || echo 未知)"

exec npx --yes netlify-cli deploy --dir=web --prod "$@"
