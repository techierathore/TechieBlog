/**
 * w2-foundation.spec.ts — verify-phase group W2 (2026-10-28): the Phase 1 foundation rows, which carry no
 * acceptance line of their own in the checklist, so each is derived from the row title and proven against the
 * running app (HTTP), the database (psql READS only) and the repository files (read only).
 *
 *   REQ-FN-001  all projects migrated to .NET 10 LTS
 *   REQ-FN-002  the BlogSvc API project is gone; the UI reaches services through DI, not HTTP
 *   REQ-FN-003  PostgreSQL schema + DbUp migration runner applied every shipped script
 *   REQ-FN-004  Blazorise replaced (by Fluent UI Blazor, itself retired by REQ-UI-048 in favour of TrBlazeUI):
 *               no Blazorise or Fluent UI reference remains
 */
import { test, expect } from '@playwright/test';
import { execSync } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'http://localhost:5450';

function sql(query: string): string[] {
  const out = execSync(`docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -c "${query.replace(/"/g, '\\"')}"`, { encoding: 'utf8' });
  return out.split('\n').map((r) => r.trim()).filter((r) => r.length > 0);
}

const PROJECTS = [
  'source/TechieBlog/TechieBlog.csproj',
  'source/BlogApp/BlogApp.csproj',
  'source/BlogDb/BlogDb.csproj',
  'source/BlogEngine/BlogEngine.csproj',
  'source/BlogModel/BlogModels.csproj',
  'source/BlogUI/BlogUI.csproj',
  'tests/TechieBlog.Tests/TechieBlog.Tests.csproj',
];

test('REQ-FN-001 every project targets .NET 10 and the running host serves requests', async ({ request }) => {
  for (const rel of PROJECTS) {
    const xml = fs.readFileSync(path.resolve(rel), 'utf8');
    const targets = [...xml.matchAll(/<TargetFrameworks?[^>]*>([^<]+)</g)].map((m) => m[1]);
    expect(targets.length, `${rel} declares a target framework`).toBeGreaterThan(0);
    for (const t of targets) expect(t, `${rel} target ${t}`).toMatch(/^net10\.0/);
    expect(xml, `${rel} has no leftover older target`).not.toMatch(/net[5-9]\.0|netcoreapp|netstandard/);
  }
  const res = await request.get(`${BASE}/`);
  expect(res.status(), 'the .NET 10 host answers').toBe(200);
});

test('REQ-FN-002 the BlogSvc API project is removed and the UI calls services through DI, not an HTTP API', async ({ request }) => {
  const slnx = fs.readFileSync(path.resolve('TechieBlog.slnx'), 'utf8');
  expect(slnx).not.toMatch(/BlogSvc/);
  expect(fs.existsSync(path.resolve('source/BlogSvc')), 'source/BlogSvc folder is gone').toBe(false);
  for (const rel of PROJECTS) expect(fs.readFileSync(path.resolve(rel), 'utf8'), `${rel} references BlogSvc`).not.toMatch(/BlogSvc/);
  // the old REST surface answers 404: nothing serves it any more
  for (const route of ['/api/blogposts', '/api/BlogPost', '/api/auth/login']) {
    const res = await request.get(`${BASE}${route}`);
    expect(res.status(), `${route} is not served`).toBe(404);
  }
  // the UI project depends on the engine project directly (DI), not on an API client
  const ui = fs.readFileSync(path.resolve('source/BlogUI/BlogUI.csproj'), 'utf8');
  expect(ui).toMatch(/ProjectReference[^>]*BlogEngine/);
});

test('REQ-FN-003 the database holds the schema and DbUp recorded every shipped migration script', async () => {
  const scripts = fs.readdirSync(path.resolve('source/BlogDb/PostgresScripts')).filter((f) => f.endsWith('.sql'));
  expect(scripts.length).toBeGreaterThan(0);
  const applied = sql('SELECT scriptname FROM schemaversions').map((n) => n.split('.').slice(-2).join('.'));
  const missing = scripts.filter((s) => !applied.some((a) => a.endsWith(s) || a === s));
  expect(missing, `scripts shipped but not recorded by DbUp: ${missing.join(', ')}`).toEqual([]);
  for (const table of ['bloguser', 'blogpost', 'blogcomment', 'userlogins', 'passwordresettoken', 'subscriber']) {
    expect(sql(`SELECT to_regclass('public.${table}') IS NOT NULL`)[0], `table ${table} exists`).toBe('t');
  }
  // the seeded roles exist, so the migration runner ran its seed script too
  expect(Number(sql('SELECT count(*) FROM bloguser')[0])).toBeGreaterThan(0);
});

test('REQ-FN-004 no Blazorise (or Fluent UI, retired by REQ-UI-048) reference remains in code, projects or served HTML', async ({ request }) => {
  const offenders: string[] = [];
  const scan = (dir: string) => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      if (['bin', 'obj', 'node_modules', 'wwwroot'].includes(e.name) || e.name.startsWith('.')) continue;
      const full = path.join(dir, e.name);
      if (e.isDirectory()) { scan(full); continue; }
      if (!/\.(csproj|razor|cs|props)$/.test(e.name)) continue;
      const text = fs.readFileSync(full, 'utf8');
      if (/Blazorise|Microsoft\.FluentUI|FluentUI\.AspNetCore/i.test(text)) offenders.push(full);
    }
  };
  scan(path.resolve('source'));
  expect(offenders, `UI-library leftovers: ${offenders.join(', ')}`).toEqual([]);
  const html = await (await request.get(`${BASE}/`)).text();
  expect(html).not.toMatch(/blazorise/i);
  expect(html).not.toMatch(/fluent-ui|FluentUI/i);
  expect(html, 'the replacement component library is what renders').toMatch(/TrBlazeUI/);
});
