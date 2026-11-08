# v1.3.0 部署与干净克隆证据（2026-11-08）

- Netlify production deploy：`6af049771a4cbd7448877fcc`，<https://survivalgame-demo.netlify.app>
- 线上 `build-info.json` = `1.3.0` / `2026-11-07T13:46:43Z`；`?v=1.3.0` 5 处；`check-version.mjs` PASS
- 线上完整 smoke（headed 真 GPU）：9/9 PASS，console 0 未预期 error —— `smoke-online-chrome-desktop-*`
- 干净克隆 `main`（`2f38b710`）→ `web/serve.py` → 本地 smoke：9/9 PASS，console 0 —— `clean-clone/`
- Release `v1.3.0`：zip 9,464,620 B，sha256 `b62bd1dbcd7cdea778854257360118cccaf824fd3685eac56d164d7472c61a9d`
- 回滚：Netlify 上一部署 `6ae88c99…`（1.2.1）或 Release v1.0.0
