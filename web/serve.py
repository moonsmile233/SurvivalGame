#!/usr/bin/env python3
"""本地预览服务器：静态文件 + `Cache-Control: no-store`。

为什么不用 `python -m http.server`：它不发 `Cache-Control`，浏览器会把 `app.js?v=...`
按旧 URL 缓存住，H5 改了但用户仍看到旧界面（本项目已踩过两次）。这里显式 no-store，
并补上 `.unityweb` / `.wasm` 等 MIME，避免 Windows 注册表把 `.js` / `.css` 猜成错误类型。

用法：python serve.py [port]（默认 8000；监听 0.0.0.0，手机可用本机局域网 IP 访问）
"""
import os
import sys
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

MIME = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.wasm': 'application/wasm',
    '.unityweb': 'application/octet-stream',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.svg': 'image/svg+xml',
    '.otf': 'font/otf',
    '.ico': 'image/x-icon',
}


class Handler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, **MIME}

    def end_headers(self):
        self.send_header('Cache-Control', 'no-store, max-age=0')
        super().end_headers()

    def log_message(self, fmt, *args):
        sys.stderr.write('%s - %s\n' % (self.address_string(), fmt % args))


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8000
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    server = ThreadingHTTPServer(('0.0.0.0', port), Handler)
    print('SurvivalGame 本地预览：http://localhost:%d（局域网用本机 IP）' % port)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == '__main__':
    main()
