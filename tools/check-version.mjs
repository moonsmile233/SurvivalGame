#!/usr/bin/env node
// 版本一致性检查（验收门禁，不参与构建）。
//
// 规则：web/index.html 里所有 `?v=` 必须相同，并且等于 web/unity/build-info.json 的 productVersion。
// 目的：避免“H5 改了但构建没更新”或“部署后浏览器仍跑旧 app.js”（协作协议 §6 的缓存规则）。
//
// 用法：node tools/check-version.mjs
// 退出码：0 = 一致；1 = 不一致/缺字段（打印具体值，方便改）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..');
const htmlPath = path.join(repoRoot, 'web', 'index.html');
const infoPath = path.join(repoRoot, 'web', 'unity', 'build-info.json');

function fail(message) {
  console.error('[check-version] FAIL: ' + message);
  process.exitCode = 1;
}

function main() {
  if (!fs.existsSync(htmlPath)) {
    fail('找不到 ' + htmlPath);
    return;
  }

  const html = fs.readFileSync(htmlPath, 'utf8');
  const versions = [...html.matchAll(/\?v=([^"'&\s>]+)/g)].map((match) => match[1]);
  if (versions.length === 0) {
    fail('web/index.html 没有找到任何 ?v= 版本串');
    return;
  }

  const unique = [...new Set(versions)];
  if (unique.length !== 1) {
    fail('web/index.html 的 ?v= 不一致：' + unique.join(', ') + '（共 ' + versions.length + ' 处）');
    return;
  }

  if (!fs.existsSync(infoPath)) {
    fail('找不到 ' + infoPath + '（先构建 WebGL，或临时复制 1.2.0 产物）');
    return;
  }

  let info;
  try {
    info = JSON.parse(fs.readFileSync(infoPath, 'utf8'));
  } catch (error) {
    fail('build-info.json 解析失败：' + error.message);
    return;
  }

  const productVersion = info.productVersion;
  if (!productVersion) {
    fail('build-info.json 缺少 productVersion');
    return;
  }

  if (unique[0] !== productVersion) {
    fail(
      'H5 版本 ' + unique[0] + '（' + versions.length + ' 处 ?v=）与构建 productVersion ' +
      productVersion + ' 不一致；改 web/index.html 或放上对应构建产物',
    );
    return;
  }

  console.log(
    '[check-version] OK: ?v=' + unique[0] + '（' + versions.length + ' 处） == build-info.productVersion ' +
    productVersion + '；builtAt=' + (info.builtAt || '-'),
  );
}

main();
