#!/usr/bin/env sh
# SurvivalGame 本地预览：WebGL 不能用 file:// 打开，必须起一个 HTTP 服务。
# 用法：sh start-local.sh   然后打开 http://localhost:8000
set -eu
cd "$(dirname "$0")"
PORT="${PORT:-8000}"

echo
echo "  SurvivalGame 本地预览"
echo "  地址：http://localhost:$PORT"
echo "  Ctrl+C 停止服务。"
echo

if command -v python3 >/dev/null 2>&1; then
  exec python3 -m http.server "$PORT"
elif command -v python >/dev/null 2>&1; then
  exec python -m http.server "$PORT"
elif command -v node >/dev/null 2>&1; then
  exec npx --yes http-server -p "$PORT" -c-1
else
  echo "未找到 Python 3 或 Node，请先安装 Python 3。" >&2
  exit 1
fi
