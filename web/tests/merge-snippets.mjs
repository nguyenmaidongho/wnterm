#!/usr/bin/env node
/*
 * merge-snippets.mjs — chạy bộ gộp snippet THẬT của pwa/vault.js (WNVault.mergeSnippets) trong sandbox node, để so sánh chéo với CLI C#.
 *
 * CONTRACT
 *   stdin  : {"local":{snippets?,deletedSnippets?,...},"remote":{snippets?,deletedSnippets?,...}|null,"now":"<ISO-8601>"}
 *            snippet = {id,name,command,vmId|null,autoEnter,createdAt,updatedAt}; tombstone = {id,deletedAt}; trường vắng = rỗng.
 *            Chỉ trường snippet được gộp; sessions/deleted bị bỏ qua.
 *   stdout : {"version":2,"sessions":[],"deleted":[],"snippets":[sắp theo id thường],"deletedSnippets":[sắp theo id],
 *             "stats":{"added","updated","deleted","localChanged"}}
 *   "now"  : đồng hồ cố định cho hạn giữ dấu xóa 180 ngày.
 *   Quy tắc: id so sánh/ghi ra bằng chữ thường; mỗi id: sự kiện mới nhất thắng; HÒA thì thứ tự: dấu xóa local > dấu xóa remote > snippet local > snippet remote
 *            (dấu xóa thắng); dấu xóa của snippet được tạo lại muộn hơn bị bỏ; dấu xóa quá 180 ngày bị loại; remote null/thiếu trường = rỗng.
 *   Dùng:  echo '{"local":{},"remote":null,"now":"2026-10-09T00:00:00Z"}' | node merge-snippets.mjs
 */
import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const input = JSON.parse(fs.readFileSync(0, 'utf8'));
const NOW = Date.parse(input.now);
if (isNaN(NOW)) { console.error('missing/invalid "now"'); process.exit(2); }

const sandbox = { TextEncoder, TextDecoder, console, crypto: globalThis.crypto };
sandbox.window = sandbox; sandbox.global = sandbox;
vm.createContext(sandbox);
vm.runInContext(
  'var __NOW = ' + NOW + ';' +
  'globalThis.Date = class extends Date { constructor(...a) { if (a.length === 0) super(__NOW); else super(...a); } static now() { return __NOW; } };',
  sandbox);
vm.runInContext(fs.readFileSync(path.join(here, '..', 'pwa', 'vault.js'), 'utf8'), sandbox, { filename: 'vault.js' });
const V = sandbox.WNVault;

const local = input.local || {};
const vault = new V.Vault({ device: 'test' });
vault.state.sessions = (local.sessions || []).map(V.clean).filter(V.isValid);
vault.state.trash = (local.deleted || []).map((t) => ({ id: String(t.id).toLowerCase(), name: t.name || '', subtitle: '', deletedAt: t.at, localOnly: false, session: null }));
vault.state.snippets = (local.snippets || []).map(V.cleanSnip).filter((s) => s.id);
vault.state.snipTombs = (local.deletedSnippets || []).map((t) => ({ id: String(t.id).toLowerCase(), deletedAt: t.deletedAt }));

const remote = input.remote ? Object.assign({ sessions: [], deleted: [] }, input.remote) : null;
// Chỉ gộp snippet (sessions/deleted luôn rỗng ở đầu ra) — cùng hình dạng với CLI C#.
const sm = V.mergeSnippets(vault.state.snippets, vault.state.snipTombs, remote);
process.stdout.write(JSON.stringify({
  version: 2, sessions: [], deleted: [], snippets: sm.snippets, deletedSnippets: sm.tombs,
  stats: { added: sm.added, updated: sm.updated, deleted: sm.deleted, localChanged: sm.changed }
}));
