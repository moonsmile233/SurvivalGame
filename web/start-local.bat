@echo off
rem SurvivalGame 本地预览：WebGL 不能用 file:// 打开，必须起一个 HTTP 服务。
rem 优先 Python 3（py -3 / python），没有再用 Node 的 http-server。
setlocal
cd /d "%~dp0"

echo.
echo   SurvivalGame 本地预览
echo   地址：http://localhost:8000
echo   关闭此窗口即停止服务。
echo.

where py >nul 2>nul
if %errorlevel%==0 (
  start "" http://localhost:8000
  py -3 -m http.server 8000
  goto :eof
)

where python >nul 2>nul
if %errorlevel%==0 (
  start "" http://localhost:8000
  python -m http.server 8000
  goto :eof
)

where node >nul 2>nul
if %errorlevel%==0 (
  start "" http://localhost:8000
  npx --yes http-server -p 8000 -c-1
  goto :eof
)

echo 未找到 Python 3 或 Node。
echo 请先安装 Python 3（https://www.python.org/downloads/），再双击本脚本。
pause
