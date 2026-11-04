import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { resolve, extname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(fileURLToPath(new URL('../release/wwwroot', import.meta.url)));
const port = Number(process.env.PORT || 5180);
const base = process.env.VITE_BASE_PATH || '/AnotherFSM/';
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript',
  '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm',
  '.svg': 'image/svg+xml', '.png': 'image/png', '.dat': 'application/octet-stream',
  '.dll': 'application/octet-stream', '.pdb': 'application/octet-stream' };
const server = createServer(async (req, res) => {
  try {
    const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    if (pathname === base.slice(0, -1)) { res.writeHead(302, { Location: base }); res.end(); return; }
    if (!pathname.startsWith(base)) { res.writeHead(404); res.end('Not found'); return; }
    let path = resolve(root, pathname.slice(base.length) || 'index.html');
    if (!path.startsWith(root + sep)) { res.writeHead(403); res.end(); return; }
    if ((await stat(path)).isDirectory()) path = resolve(path, 'index.html');
    const body = await readFile(path);
    res.writeHead(200, { 'Content-Type': mime[extname(path)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
    res.end(body);
  } catch { res.writeHead(404); res.end('Not found'); }
});
server.listen(port, '127.0.0.1', () => console.log(`Static React WASM: http://127.0.0.1:${port}${base}`));
