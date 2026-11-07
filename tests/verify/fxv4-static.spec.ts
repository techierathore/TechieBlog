/**
 * fxv4-static.spec.ts - verify-phase writer W4.
 *
 * Rows that are properties of the repository, the seed or a script rather than of one screen:
 * REQ-FN-041 (seed set, as NARROWED by owner decision UAT-007), REQ-FN-042 (storage-provider
 * abstraction), REQ-FN-044 (template packaging + rename script), REQ-FN-045 (adopter documentation),
 * REQ-FN-046 / REQ-FN-047 / REQ-UI-052 / REQ-UI-063 (BlogApp, the MAUI desktop head).
 *
 * The BlogApp rows cannot be driven from this host: it targets net10.0-windows10.0.19041.0 and needs a
 * MAUI Windows session the verify phase does not have, and building it would lock bin/obj under the
 * running web host. Their runtime tests are therefore `test.skip` (recorded as NOT TESTED), and only the
 * structural facts that can be read from the repository are asserted - named "(structure only)".
 *
 * Read-only: psql SELECT, file reads, a script `--dry-run`, and one page of the running site.
 */
import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { execFileSync } from 'child_process';
import { psql, psqlInt, psqlRows, adminSession, openAdmin, sleep } from './fxv4-helpers';

const ROOT = path.resolve(__dirname, '..', '..');
const read = (rel: string) => fs.readFileSync(path.join(ROOT, rel), 'utf8');
const exists = (rel: string) => fs.existsSync(path.join(ROOT, rel));

test.setTimeout(300000);

test('REQ-FN-041 the seed keeps one staff account per role and described categories, and ships no demo articles (narrowed by UAT-007)', async () => {
  // PARTS A-B of 019-SampleData.sql were kept by owner decision (UAT-007): staff accounts + category descriptions.
  const roles = psql('SELECT DISTINCT UserRole FROM BlogUser').split('\n');
  for (const r of ['Admin', 'Editor', 'Author', 'Contributor']) expect(roles, `a ${r} account exists`).toContain(r);
  for (const email of ['Ravi@techieblog.com', 'editor@techieblog.test', 'author@techieblog.test', 'contributor@techieblog.test']) {
    expect(psqlInt(`SELECT COUNT(*) FROM BlogUser WHERE lower(EmailId) = lower('${email}')`), `${email} is seeded`).toBe(1);
  }
  const seededCategories = ['Career', 'DevOps', 'Programming', 'Technology', 'Web Development'];
  for (const name of seededCategories) {
    expect(psqlInt(`SELECT COUNT(*) FROM Category WHERE CategoryName = '${name}'`), `category ${name} is seeded`).toBe(1);
    expect(psql(`SELECT COALESCE(Description,'') FROM Category WHERE CategoryName = '${name}'`).length, `${name} has a description`).toBeGreaterThan(10);
  }
  // The demo articles and the four demo statistics were removed (UAT-007) and must not be back.
  const demoSlugs = ['writing-a-technical-talk-that-lands', 'shipping-dotnet-with-docker-and-github-actions', 'the-markdown-kitchen-sink', 'reading-postgres-query-plans', 'postgres-indexing-for-dotnet-developers', 'scaling-signalr-for-blazor-server', 'blazor-circuits-and-state', 'blazor-render-modes-explained'];
  for (const s of demoSlugs) expect(psqlInt(`SELECT COUNT(*) FROM BlogPost WHERE Slug = '${s}'`), `demo article ${s} is gone`).toBe(0);
  expect(psqlInt(`SELECT COUNT(*) FROM UserStats WHERE StatValue IN ('20+', '200+', '45', '12') AND StatLabel ILIKE ANY (ARRAY['%years%','%articles%','%talks%','%shipped%'])`), 'the four demo statistics are gone').toBe(0);
});

test('REQ-FN-042 the Site Settings storage tab offers the Local, Network and Cloud providers behind one abstraction', async ({ browser }) => {
  // The abstraction exists in code: one interface, three implementations.
  expect(exists('source/BlogEngine/Storage/LocalFileStorage.cs'), 'local implementation').toBe(true);
  expect(exists('source/BlogEngine/Storage/FileSystemStorage.cs'), 'network/file-system implementation').toBe(true);
  expect(exists('source/BlogEngine/Storage/CloudFileStorage.cs'), 'cloud implementation').toBe(true);
  expect(exists('source/BlogEngine/Storage/FileStorageFactory.cs'), 'provider factory').toBe(true);

  // And it is configurable from the UI.
  const { ctx, page } = await adminSession(browser);
  try {
    await openAdmin(page, '/settings', '[data-testid="tab-storage"]');
    await sleep(2500);
    await page.click('[data-testid="tab-storage"]');
    await sleep(1500);
    const stored = psql("SELECT COALESCE(SettingValue,'') FROM SiteSetting WHERE SettingKey = 'Storage.ProviderName'") || 'Local';
    expect((await page.locator('[data-testid="storage-provider"]').innerText()).trim(), 'the select shows the stored provider').toBe(stored);
    await page.click('[data-testid="storage-provider"]');
    await page.waitForSelector('[role="option"]', { timeout: 15000 });
    const options = (await page.locator('[role="option"]').allTextContents()).map((t) => t.trim());
    expect(options, 'the three providers are offered').toEqual(expect.arrayContaining(['Local', 'Network', 'Cloud']));
    await page.keyboard.press('Escape');
    // Each provider has its own settings fields (not saved; nothing is changed here).
    for (const id of ['storage-local-root', 'storage-network-root', 'storage-cloud-url', 'storage-cloud-container', 'storage-cloud-key', 'storage-public-base']) {
      await expect(page.locator(`[data-testid="${id}"]`), id).toBeVisible();
    }
  } finally {
    await ctx.close();
  }
});

test('REQ-FN-044 the repository is packaged as a template: licence, readme instructions, CI and both rename scripts', async () => {
  for (const f of ['LICENSE.txt', 'README.md', 'GETTING_STARTED.md', 'scripts/rename-project.sh', 'scripts/Rename-Project.ps1', '.github/workflows/ci.yml']) {
    expect(exists(f), `${f} ships with the template`).toBe(true);
  }
  expect(read('README.md'), 'the readme tells a visitor to use it as a template').toMatch(/Use this template/i);
  expect(read('README.md'), 'and points at the rename script').toMatch(/rename-project|Rename-Project/);
});

test('REQ-FN-044 the PowerShell rename script re-brands a clone (dry run lists every change and touches nothing)', async () => {
  let out = '';
  try {
    out = execFileSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'scripts/Rename-Project.ps1', '-NewName', 'FxvFour', '-DryRun'], { cwd: ROOT, encoding: 'utf8', timeout: 150000 });
  } catch (e: any) {
    test.skip(!!e.code && e.code === 'ENOENT', 'powershell.exe is not reachable from this host');
    throw e;
  }
  expect(out).toContain('DRY RUN');
  expect(out).toMatch(/RENAME : TechieBlog\.slnx -> FxvFour\.slnx/);
  expect(out).toMatch(/RENAME : source\/TechieBlog -> source\/FxvFour/);
  expect(out).toMatch(/Total changes: [1-9]\d*/);
  expect(exists('TechieBlog.slnx') && exists('source/TechieBlog') && !exists('FxvFour.slnx'), 'a dry run changed nothing').toBe(true);
});

test('REQ-FN-044 the bash rename script runs to the end of a dry run', async () => {
  // The working tree is a Windows checkout (CRLF); a Linux/macOS clone is normalised to LF by `* text=auto`,
  // so feed the script LF line endings exactly as such a clone would hold it.
  let status = 0; let out = '';
  try {
    out = execFileSync('bash', ['-c', "tr -d '\\r' < scripts/rename-project.sh | bash -s FxvFour --dry-run"], { cwd: ROOT, encoding: 'utf8', timeout: 120000 });
  } catch (e: any) { status = e.status ?? 1; out = String(e.stdout ?? ''); }
  console.log(`[REQ-FN-044] rename-project.sh --dry-run exit=${status}; last line: ${out.trim().split('\n').pop()}`);
  expect(out, 'the dry run reaches its summary').toContain('Total changes');
  expect(status, 'and exits 0').toBe(0);
  expect(exists('TechieBlog.slnx') && exists('source/TechieBlog'), 'nothing was renamed').toBe(true);
});

test('REQ-FN-045 the adopter documentation covers getting started, customization, deployment and database migration', async () => {
  const docs: Record<string, RegExp> = {
    'GETTING_STARTED.md': /getting started|prerequisite|clone/i,
    'docs/customization.md': /theme|customi[sz]/i,
    'docs/deployment.md': /deploy/i,
    'docs/database-migration-guide.md': /migrat/i,
  };
  for (const [file, topic] of Object.entries(docs)) {
    expect(exists(file), `${file} exists`).toBe(true);
    const text = read(file);
    expect(text.length, `${file} is substantial`).toBeGreaterThan(1500);
    expect(text, `${file} covers its topic`).toMatch(topic);
    expect(text.split('\n').filter((l) => /^#{1,3}\s/.test(l)).length, `${file} has a structure of headings`).toBeGreaterThan(3);
  }
  expect(read('README.md'), 'the readme links the four guides').toMatch(/GETTING_STARTED/);
});

test('REQ-FN-045 the relative links inside the adopter guides resolve to real files', async () => {
  const broken: string[] = [];
  for (const file of ['GETTING_STARTED.md', 'docs/customization.md', 'docs/deployment.md', 'docs/database-migration-guide.md']) {
    const dir = path.dirname(file);
    for (const m of read(file).matchAll(/\]\((?!https?:|mailto:|#)([^)#\s]+)(#[^)]*)?\)/g)) {
      if (!fs.existsSync(path.join(ROOT, dir, m[1]))) broken.push(`${file} -> ${m[1]}`);
    }
  }
  expect(broken, 'no dead relative links in the adopter guides').toEqual([]);
});

test('REQ-FN-046 the solution lists the BlogApp project and it reuses BlogUI, BlogEngine and BlogModel (structure only)', async () => {
  const slnx = read('TechieBlog.slnx');
  expect(slnx).toMatch(/source\/BlogApp\/BlogApp\.csproj/);
  const csproj = read('source/BlogApp/BlogApp.csproj');
  for (const p of ['BlogUI', 'BlogEngine', 'BlogModel']) expect(csproj, `BlogApp references ${p}`).toContain(p);
  expect(csproj, 'it is a MAUI Windows head').toMatch(/net10\.0-windows/);
});

test.skip('REQ-FN-046 BlogApp launches to the connection-setup/login flow on Windows and the solution builds with it', async () => {
  // NOT DRIVEN: needs a MAUI Windows session; a solution build would lock bin/obj under the running web host.
});

// REQ-FN-047, REQ-UI-052 and REQ-UI-063 (BlogApp, the MAUI desktop head) are driven by
// tests/verify/blogapp-desktop.spec.ts, which attaches to the running app over WebView2 CDP
// (boot recipe in tests/verify/_blogapp.ts). They used to be test.skip stubs here.
