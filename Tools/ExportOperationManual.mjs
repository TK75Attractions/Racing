// Usage: node Tools/ExportOperationManual.mjs [path-to-node_modules]
// Requires the marked package; generated HTML needs no runtime or network.
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(process.argv[2] ? path.join(path.resolve(process.argv[2]), '..', 'package.json') : import.meta.url);
const { marked } = await import(pathToFileURL(require.resolve('marked')).href);
const source = path.join(root, 'Documentation/CustomerServiceManual.md');
let body = marked.parse(fs.readFileSync(source, 'utf8'), { gfm: true });
const toc = [];
body = body.replace(/<h2>(.*?)<\/h2>/g, (_, label) => {
  const id = `section-${toc.length + 1}`;
  toc.push(`<a href="#${id}">${label}</a>`);
  return `<h2 id="${id}">${label}</h2>`;
});
body = body.replace(/<img src="([^"]+)" alt="([^"]*)"[^>]*>/g, (_, relative, alt) => {
  const file = path.resolve(path.dirname(source), relative);
  const type = file.endsWith('.svg') ? 'image/svg+xml' : 'image/png';
  return `<figure><img src="data:${type};base64,${fs.readFileSync(file).toString('base64')}" alt="${alt}"><figcaption>${alt}</figcaption></figure>`;
});
body = body.replace(/<table>/g, '<div class="table-wrap"><table>').replace(/<\/table>/g, '</table></div>');
const html = `<!doctype html>
<html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>RACING PC・Unity 操作マニュアル</title>
<style>
:root{color-scheme:light;--ink:#172c41;--teal:#086e77;--line:#d5e1e8}
*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;background:#edf3f7;color:var(--ink);font:16px/1.9 -apple-system,BlinkMacSystemFont,"Yu Gothic","Hiragino Sans",sans-serif}
a{color:var(--teal);text-underline-offset:3px}header{background:#172c41;color:white;padding:20px max(24px,calc((100vw - 1080px)/2));display:flex;gap:20px;justify-content:space-between;align-items:center}header strong{letter-spacing:.1em}header span{font-size:13px;color:#cbdde6}button{border:1px solid #a7c7d2;background:white;color:#172c41;border-radius:6px;padding:9px 18px;font:inherit;white-space:nowrap;flex-shrink:0;cursor:pointer}
.layout{display:grid;grid-template-columns:240px minmax(0,1fr);gap:30px;max-width:1180px;margin:30px auto;padding:0 24px}nav{align-self:start;position:sticky;top:20px;padding:20px;background:white;border:1px solid var(--line);border-radius:12px;font-size:13px}nav strong{display:block;margin-bottom:12px}nav a{display:block;text-decoration:none;margin:11px 0;line-height:1.6}main{min-width:0;background:white;border:1px solid var(--line);border-radius:12px;padding:36px 42px}h1{font-size:30px;line-height:1.5;letter-spacing:.01em;margin:0 0 24px}h2{font-size:24px;line-height:1.5;border-top:3px solid var(--teal);padding-top:18px;margin:52px 0 22px;scroll-margin-top:20px}h3{font-size:19px;margin:30px 0 14px;color:var(--teal)}p{margin:15px 0}li{margin:9px 0}ol,ul{padding-left:26px}strong{font-weight:700}code{background:#edf3f6;border-radius:4px;padding:2px 5px;font-size:.88em;overflow-wrap:anywhere}pre{white-space:pre-wrap;background:#172c41;color:#eef8fc;padding:18px;border-radius:8px;line-height:1.7}pre code{background:none;padding:0;color:inherit}.table-wrap{overflow-x:auto;margin:20px 0}table{border-collapse:collapse;width:100%;font-size:14px;line-height:1.8}th{background:#e9f3f4;text-align:left;font-weight:700}td,th{border:1px solid var(--line);padding:10px 12px;vertical-align:top}td:first-child{min-width:95px}figure{margin:26px 0}figure img{display:block;max-width:100%;height:auto;border:1px solid var(--line);border-radius:8px}figcaption{font-size:12px;color:#5d7382;margin-top:8px}input[type=checkbox]{accent-color:var(--teal)}footer{font-size:12px;color:#5d7382;padding-top:30px;border-top:1px solid var(--line);margin-top:42px}
@media(max-width:880px){.layout{grid-template-columns:1fr;margin:18px auto;gap:16px;padding:0 14px}nav{position:static}nav a{display:inline-block;margin:5px 14px 5px 0}main{padding:25px 22px}h1{font-size:25px}h2{font-size:21px}header{padding:16px 22px}header strong{font-size:13px}header button{font-size:13px;padding:9px 12px}header span{display:none}}
@media print{body{background:white;font-size:10pt;line-height:1.75}header,nav,.print-hide{display:none}.layout{display:block;max-width:none;padding:0;margin:0}main{border:0;padding:0}h1{font-size:22pt}h2{font-size:16pt;break-after:avoid}h3{break-after:avoid}figure,pre,tr{break-inside:avoid}figure img{max-height:190mm;max-width:100%}table{font-size:9pt}.table-wrap{overflow:visible}a{color:inherit}header button{display:none}@page{size:A4;margin:17mm}}
</style></head><body><header><div><strong>RACING / OPERATIONS</strong><br><span>PC・Unity 操作ガイド · 2026.10.27</span></div><button onclick="window.print()">印刷 / PDF保存</button></header>
<div class="layout"><nav aria-label="目次"><strong>操作を探す</strong>${toc.join('\n')}</nav><main>${body}<footer>図・画像はこのHTMLに埋め込んでいます。オフラインで閲覧・印刷できます。関連資料へのリンクはプロジェクトのDocumentation内で使用してください。</footer></main></div></body></html>`;
const destination = path.join(root, 'Documentation/CustomerServiceManual.html');
fs.writeFileSync(destination, html);
console.log(`Exported ${destination} (${toc.length} sections, 5 embedded figures)`);
