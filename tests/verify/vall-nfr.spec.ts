/**
 * vall-nfr.spec.ts — `*verify all` 2026-08-08, backend / platform / non-functional cluster.
 *
 * Runtime evidence for the REQ IDs that have no screen of their own:
 *   REQ-FN-020 (listings, featured, related, reading time), REQ-FN-037 (RSS), REQ-FN-038
 *   (sitemap + robots), REQ-FN-039 (theme CSS variables), REQ-FN-040 (settings take effect),
 *   REQ-FN-041 (seed data), REQ-FN-052 (svctoken gone), REQ-NFR-001 (page load < 2 s),
 *   REQ-NFR-004 (HTTPS/HSTS), REQ-NFR-006 (XSS), REQ-NFR-007 (axe), REQ-NFR-010 (breakpoints),
 *   REQ-NFR-013/015 (Serilog + correlation id), REQ-NFR-014 (health), REQ-NFR-018 (caching).
 *
 * Everything is READ-ONLY against the already-running host — no data is created or mutated,
 * because seven sibling verifiers share this instance and its database.
 *
 * Screenshots go to `.verify/shots/nfr/` — Playwright wipes `test-results/` at the start of every
 * run and the siblings run concurrently, so anything saved there is destroyed.
 */
import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import * as fs from 'fs';
import * as path from 'path';
import { BASE, login, nav } from './_gates';

const SHOTS = path.join(process.cwd(), '.verify', 'shots', 'nfr');
fs.mkdirSync(SHOTS, { recursive: true });

const AXE_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

/** Public routes exercised by the timing, axe and breakpoint passes. */
const PUBLIC_ROUTES: Array<[string, string]> = [
  ['home', '/'],
  ['post', '/post/the-markdown-kitchen-sink'],
  ['newsletters', '/newsletters'],
  ['search', '/search'],
];

/** Collected findings printed as one block at the end so the report is greppable. */
const findings: string[] = [];
function record(req: string, line: string) {
  findings.push(`${req} :: ${line}`);
  console.log(`FINDING ${req} :: ${line}`);
}

/** Settles a public page: DOM parsed, circuit connected, render quiesced. */
async function settle(page: import('@playwright/test').Page, url: string) {
  await page.goto(BASE + url, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 30000 }).catch(() => {});
  await page.waitForTimeout(2500);
}

// ---------------------------------------------------------------------------
// REQ-NFR-001 — page load under 2 s (the concurrency half is NOT re-measured here)
// ---------------------------------------------------------------------------
test('NFR001 public page load timings', async ({ page }) => {
  const rows: any[] = [];
  for (const [name, url] of PUBLIC_ROUTES) {
    await settle(page, url);
    const t = await page.evaluate(() => {
      const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming;
      const fcp = performance.getEntriesByName('first-contentful-paint')[0];
      return {
        ttfb: Math.round(nav.responseStart - nav.startTime),
        domContentLoaded: Math.round(nav.domContentLoadedEventEnd - nav.startTime),
        load: Math.round(nav.loadEventEnd - nav.startTime),
        fcp: fcp ? Math.round(fcp.startTime) : -1,
      };
    });
    rows.push({ name, ...t });
    record('REQ-NFR-001', `${name} ttfb=${t.ttfb}ms fcp=${t.fcp}ms load=${t.load}ms`);
  }
  const worst = Math.max(...rows.map((r) => Math.max(r.load, r.fcp)));
  record('REQ-NFR-001', `worst single figure across ${rows.length} routes = ${worst}ms (budget 2000ms)`);
  fs.writeFileSync(path.join(SHOTS, 'nfr001-timings.json'), JSON.stringify(rows, null, 2));
  expect(worst, 'worst page-load figure').toBeGreaterThanOrEqual(0);
});

// ---------------------------------------------------------------------------
// REQ-FN-020 — published listings, featured post, related posts, reading time
// ---------------------------------------------------------------------------
test('FN020 listings featured related reading time', async ({ page }) => {
  await settle(page, '/');
  const home = await page.evaluate(() => {
    const body = document.body.innerText || '';
    const postLinks = Array.from(document.querySelectorAll('a[href^="/post/"]'))
      .map((a) => (a as HTMLAnchorElement).getAttribute('href'))
      .filter((h, i, arr) => h && arr.indexOf(h) === i);
    return {
      postLinks: postLinks.length,
      readingTimeHits: (body.match(/\d+\s*min(ute)?s?\s*read/gi) || []).length,
      featuredMarker: /featured/i.test(body),
      sample: postLinks.slice(0, 5),
    };
  });
  record('REQ-FN-020', `home: ${home.postLinks} distinct published post links, readingTime tokens=${home.readingTimeHits}, "featured" text present=${home.featuredMarker}`);
  await page.screenshot({ path: path.join(SHOTS, 'fn020-home.png'), fullPage: false });

  await settle(page, '/post/the-markdown-kitchen-sink');
  const post = await page.evaluate(() => {
    const body = document.body.innerText || '';
    const related = document.querySelector('[data-testid*="related"], [class*="related"]');
    const relatedLinks = related
      ? Array.from(related.querySelectorAll('a[href^="/post/"]')).length
      : 0;
    return {
      readingTime: (body.match(/\d+\s*min(ute)?s?\s*read/i) || [''])[0],
      relatedContainer: !!related,
      relatedLinks,
      relatedHeading: /related\s+(post|article)/i.test(body),
    };
  });
  record('REQ-FN-020', `post: readingTime="${post.readingTime}" relatedContainer=${post.relatedContainer} relatedLinks=${post.relatedLinks} relatedHeading=${post.relatedHeading}`);
  await page.screenshot({ path: path.join(SHOTS, 'fn020-post.png'), fullPage: true });
  expect(home.postLinks, 'published posts rendered on home').toBeGreaterThan(0);
});

// ---------------------------------------------------------------------------
// REQ-FN-037 / REQ-FN-038 — RSS, sitemap, robots
// ---------------------------------------------------------------------------
test('FN037 FN038 feed sitemap robots', async ({ request, page }) => {
  // The /rss route the requirement points at.
  const rss = await request.get(BASE + '/rss');
  const rssType = rss.headers()['content-type'] ?? '';
  const rssBody = await rss.text();
  const isXmlFeed = /^\s*<\?xml/.test(rssBody) && /<rss[\s>]/.test(rssBody);
  record('REQ-FN-037', `GET /rss -> ${rss.status()} content-type="${rssType}" isRss2Xml=${isXmlFeed} firstBytes="${rssBody.slice(0, 60).replace(/\s+/g, ' ')}"`);

  // The URL the /rss landing page tells subscribers to paste into their reader.
  await settle(page, '/rss');
  const advertised = await page
    .locator('[data-testid="rss-url"]')
    .inputValue()
    .catch(() => '(no rss-url input)');
  record('REQ-FN-037', `landing page advertises feed URL "${advertised}"`);
  await page.screenshot({ path: path.join(SHOTS, 'fn037-rss-page.png'), fullPage: false });

  for (const candidate of ['/feed.xml', '/rss.xml', '/feed', '/atom.xml', '/rss/feed']) {
    const r = await request.get(BASE + candidate);
    const ct = r.headers()['content-type'] ?? '';
    record('REQ-FN-037', `candidate ${candidate} -> ${r.status()} ${ct}`);
  }

  // Sitemap.
  const sm = await request.get(BASE + '/sitemap.xml');
  const smBody = await sm.text();
  const locs = (smBody.match(/<loc>/g) || []).length;
  const hasPosts = /\/post\//.test(smBody);
  const hasCategory = /\/category\/|\/categories?\//.test(smBody);
  const hasTag = /\/tag\/|\/tags?\//.test(smBody);
  record('REQ-FN-038', `GET /sitemap.xml -> ${sm.status()} ${sm.headers()['content-type']} urls=${locs} posts=${hasPosts} categories=${hasCategory} tags=${hasTag}`);
  const badBase = /https:\/\/localhost:5001/.test(smBody);
  record('REQ-FN-038', `sitemap <loc> base URL uses the appsettings SiteSettings:BaseUrl placeholder https://localhost:5001 = ${badBase}`);

  const rb = await request.get(BASE + '/robots.txt');
  const rbBody = await rb.text();
  record('REQ-FN-038', `GET /robots.txt -> ${rb.status()} body=${JSON.stringify(rbBody)}`);
  expect(sm.status(), 'sitemap.xml served').toBe(200);
  expect(rb.status(), 'robots.txt served').toBe(200);
});

// ---------------------------------------------------------------------------
// REQ-FN-039 — theme service, provider and CSS custom properties
// ---------------------------------------------------------------------------
test('FN039 theme css variables and dark mode', async ({ page }) => {
  await settle(page, '/');
  const light = await page.evaluate(() => {
    const root = document.documentElement;
    const cs = getComputedStyle(root);
    const vars = ['--background', '--foreground', '--primary', '--card', '--border', '--muted'];
    const read: Record<string, string> = {};
    for (const v of vars) read[v] = cs.getPropertyValue(v).trim();
    return {
      siteTheme: root.getAttribute('data-site-theme'),
      classList: root.className,
      vars: read,
      definedCount: Object.values(read).filter((x) => x.length > 0).length,
      bodyBg: getComputedStyle(document.body).backgroundColor,
    };
  });
  record('REQ-FN-039', `light: data-site-theme="${light.siteTheme}" root.class="${light.classList}" ${light.definedCount}/6 core CSS custom properties defined, body bg=${light.bodyBg}`);
  await page.screenshot({ path: path.join(SHOTS, 'fn039-light.png'), fullPage: false });

  // Flip to dark the way the ThemeProvider does and confirm the tokens actually change.
  await page.evaluate(() => document.documentElement.classList.add('dark'));
  await page.waitForTimeout(400);
  const dark = await page.evaluate(() => {
    const cs = getComputedStyle(document.documentElement);
    return {
      background: cs.getPropertyValue('--background').trim(),
      foreground: cs.getPropertyValue('--foreground').trim(),
      bodyBg: getComputedStyle(document.body).backgroundColor,
    };
  });
  const changed = dark.background !== light.vars['--background'] || dark.bodyBg !== light.bodyBg;
  record('REQ-FN-039', `dark class flips tokens = ${changed} (light --background="${light.vars['--background']}" -> dark "${dark.background}", body bg ${light.bodyBg} -> ${dark.bodyBg})`);
  await page.screenshot({ path: path.join(SHOTS, 'fn039-dark.png'), fullPage: false });

  // A hardcoded-colour sweep: how much of the rendered page ignores the token system.
  const hardcoded = await page.evaluate(() => {
    const styles = Array.from(document.querySelectorAll('style'))
      .map((s) => s.textContent || '')
      .join('\n');
    const hexes = styles.match(/#[0-9a-fA-F]{3,8}\b/g) || [];
    return { inlineStyleBlocks: document.querySelectorAll('style').length, hexLiterals: hexes.length };
  });
  record('REQ-FN-039', `page-level <style> blocks=${hardcoded.inlineStyleBlocks} containing ${hardcoded.hexLiterals} hex colour literals`);
  expect(light.definedCount, 'CSS custom properties defined on :root').toBeGreaterThan(0);
});

// ---------------------------------------------------------------------------
// REQ-NFR-006 — rendered Markdown carries no executable markup
// ---------------------------------------------------------------------------
test('NFR006 rendered markdown is sanitised', async ({ page }) => {
  const dialogs: string[] = [];
  page.on('dialog', async (d) => {
    dialogs.push(d.message());
    await d.dismiss();
  });
  await settle(page, '/post/the-markdown-kitchen-sink');
  const probe = await page.evaluate(() => {
    const scope = document.querySelector('article, main') || document.body;
    const scripts = scope.querySelectorAll('script').length;
    const iframes = scope.querySelectorAll('iframe, object, embed').length;
    let onAttrs = 0;
    let jsUrls = 0;
    scope.querySelectorAll('*').forEach((el) => {
      for (const a of Array.from(el.attributes)) {
        if (/^on/i.test(a.name)) onAttrs++;
        if (/^\s*(javascript|data|vbscript):/i.test(a.value) && /^(href|src|action)$/i.test(a.name)) jsUrls++;
      }
    });
    return { scripts, iframes, onAttrs, jsUrls };
  });
  record('REQ-NFR-006', `kitchen-sink post render: script=${probe.scripts} iframe/object/embed=${probe.iframes} on*=${probe.onAttrs} js:/data: urls=${probe.jsUrls} dialogs fired=${dialogs.length}`);
  await page.screenshot({ path: path.join(SHOTS, 'nfr006-markdown.png'), fullPage: true });
  expect(probe.onAttrs + probe.jsUrls + dialogs.length, 'executable markup surviving the markdown sanitiser').toBe(0);
});

// ---------------------------------------------------------------------------
// REQ-NFR-010 — four breakpoints, public page
// ---------------------------------------------------------------------------
const BREAKPOINTS: Array<[string, number, number]> = [
  ['320', 320, 640],
  ['768', 768, 900],
  ['1024', 1024, 800],
  ['1440', 1440, 900],
];

test('NFR010 responsive public breakpoints', async ({ page }) => {
  for (const [label, w, h] of BREAKPOINTS) {
    await page.setViewportSize({ width: w, height: h });
    await settle(page, '/');
    const geom = await page.evaluate(() => {
      const hScroll = document.documentElement.scrollWidth - document.documentElement.clientWidth;
      const vw = document.documentElement.clientWidth;
      const wide: string[] = [];
      document.querySelectorAll('body *').forEach((el) => {
        const r = el.getBoundingClientRect();
        if (r.width > 0 && (r.right > vw + 2 || r.left < -2)) {
          const cs = getComputedStyle(el);
          if (cs.overflowX === 'auto' || cs.overflowX === 'scroll' || cs.position === 'fixed') return;
          if (wide.length < 6) wide.push(`${el.tagName.toLowerCase()}.${(el.className || '').toString().split(' ')[0]}@${Math.round(r.left)}..${Math.round(r.right)}`);
        }
      });
      return { hScroll, vw, overflowing: wide };
    });
    record('REQ-NFR-010', `home @${label}px: horizontalScroll=${geom.hScroll}px overflowingElements=${geom.overflowing.length} ${JSON.stringify(geom.overflowing)}`);
    await page.screenshot({ path: path.join(SHOTS, `nfr010-home-${label}.png`), fullPage: false });
  }
});

/**
 * Login retried once: seven verifiers share this host and REQ-NFR-005 caps /login at 10 per
 * 60 s per IP, so a single 429 is a neighbour's traffic, not a defect in the page under test.
 */
async function loginResilient(page: import('@playwright/test').Page) {
  try {
    await login(page, 'admin');
  } catch {
    await page.waitForTimeout(65000);
    await login(page, 'admin');
  }
}

test('NFR010 responsive admin breakpoints', async ({ page }) => {
  test.setTimeout(240000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginResilient(page);
  for (const [label, w, h] of BREAKPOINTS) {
    await page.setViewportSize({ width: w, height: h });
    await page.waitForTimeout(1200);
    const geom = await page.evaluate(() => {
      const hScroll = document.documentElement.scrollWidth - document.documentElement.clientWidth;
      const vw = document.documentElement.clientWidth;
      let overflowing = 0;
      const sample: string[] = [];
      document.querySelectorAll('body *').forEach((el) => {
        const r = el.getBoundingClientRect();
        if (r.width > 0 && r.right > vw + 2) {
          const cs = getComputedStyle(el);
          if (cs.overflowX === 'auto' || cs.overflowX === 'scroll' || cs.position === 'fixed') return;
          overflowing++;
          if (sample.length < 5) sample.push(`${el.tagName.toLowerCase()}.${(el.className || '').toString().split(' ')[0]}`);
        }
      });
      return { hScroll, overflowing, sample, url: location.pathname };
    });
    record('REQ-NFR-010', `admin ${geom.url} @${label}px: horizontalScroll=${geom.hScroll}px overflowingElements=${geom.overflowing} ${JSON.stringify(geom.sample)}`);
    await page.screenshot({ path: path.join(SHOTS, `nfr010-admin-${label}.png`), fullPage: false });
  }
});

// ---------------------------------------------------------------------------
// REQ-NFR-007 — axe over public routes and the admin dashboard
// ---------------------------------------------------------------------------
for (const [name, url] of PUBLIC_ROUTES.concat([['rss', '/rss'], ['login', '/login']])) {
  test(`NFR007 axe ${name}`, async ({ page }) => {
    test.setTimeout(180000);
    const rows: any[] = [];
    for (const [vp, w, h] of [['1440', 1440, 900], ['390', 390, 844]] as Array<[string, number, number]>) {
      await page.setViewportSize({ width: w, height: h });
      await settle(page, url);
      const res = await new AxeBuilder({ page }).withTags(AXE_TAGS).analyze();
      const nodes = res.violations.reduce((n, v) => n + v.nodes.length, 0);
      const crit = res.violations
        .filter((v) => v.impact === 'critical')
        .reduce((n, v) => n + v.nodes.length, 0);
      const detail = res.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length, target: v.nodes[0]?.target?.join(' ') }));
      rows.push({ page: name, viewport: vp, nodes, crit, detail });
      record('REQ-NFR-007', `axe ${name}@${vp} -> ${nodes} nodes (${crit} critical) ${JSON.stringify(detail)}`);
      if (nodes > 0) await page.screenshot({ path: path.join(SHOTS, `nfr007-${name}-${vp}.png`), fullPage: false });
    }
    fs.writeFileSync(path.join(SHOTS, `nfr007-axe-${name}.json`), JSON.stringify(rows, null, 2));
  });
}

test('NFR007 axe admin', async ({ page }) => {
  test.setTimeout(240000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginResilient(page);
  await page.waitForTimeout(2500);
  const res = await new AxeBuilder({ page }).withTags(AXE_TAGS).analyze();
  const nodes = res.violations.reduce((n, v) => n + v.nodes.length, 0);
  const crit = res.violations.filter((v) => v.impact === 'critical').reduce((n, v) => n + v.nodes.length, 0);
  const detail = res.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length, target: v.nodes[0]?.target?.join(' ') }));
  record('REQ-NFR-007', `axe admin ${new URL(page.url()).pathname} -> ${nodes} nodes (${crit} critical) ${JSON.stringify(detail)}`);
  await page.screenshot({ path: path.join(SHOTS, 'nfr007-admin.png'), fullPage: false });
  fs.writeFileSync(path.join(SHOTS, 'nfr007-admin-axe.json'), JSON.stringify(detail, null, 2));
});

// ---------------------------------------------------------------------------
// REQ-NFR-004 / 013 / 014 / 015 / 018 — headers, health, correlation, caching
// ---------------------------------------------------------------------------
test('NFR004 NFR014 NFR015 NFR018 endpoint behaviour', async ({ request }) => {
  // Health.
  const live = await request.get(BASE + '/health');
  const liveJson = await live.json();
  record('REQ-NFR-014', `/health -> ${live.status()} status=${liveJson.status} checks=${JSON.stringify(liveJson.checks)} (empty array = liveness only, verifies no dependency)`);
  const ready = await request.get(BASE + '/health/ready');
  const readyJson = await ready.json();
  record('REQ-NFR-014', `/health/ready -> ${ready.status()} status=${readyJson.status} checks=${JSON.stringify(readyJson.checks?.map((c: any) => `${c.name}:${c.status}:${c.description}`))}`);

  // Correlation id surfaced on the response and echoed in the health payload.
  const h = await request.get(BASE + '/');
  const cid = h.headers()['x-correlation-id'];
  record('REQ-NFR-015', `GET / response carries X-Correlation-ID="${cid}"; /health payload correlationId="${liveJson.correlationId}"`);

  // Security headers (HSTS is intentionally absent under Development).
  const secHeaders = Object.fromEntries(
    Object.entries(h.headers()).filter(([k]) =>
      /strict-transport|content-security|x-frame|x-content-type|referrer-policy/i.test(k)),
  );
  record('REQ-NFR-004', `GET / security headers ${JSON.stringify(secHeaders)} (host runs Development, so UseHsts is skipped by design)`);

  // Output caching on the feed/listing policies: two hits, compare timing and any cache header.
  const t0 = Date.now();
  const s1 = await request.get(BASE + '/sitemap.xml');
  const d1 = Date.now() - t0;
  const t1 = Date.now();
  const s2 = await request.get(BASE + '/sitemap.xml');
  const d2 = Date.now() - t1;
  const b1 = await s1.text();
  const b2 = await s2.text();
  record('REQ-NFR-018', `/sitemap.xml first=${d1}ms second=${d2}ms identicalBody=${b1 === b2} age/cache headers=${JSON.stringify(Object.fromEntries(Object.entries(s2.headers()).filter(([k]) => /cache|age|etag|vary/i.test(k))))}`);

  expect(readyJson.checks?.length ?? 0, '/health/ready must verify at least one dependency').toBeGreaterThan(0);
});

// ---------------------------------------------------------------------------
// REQ-FN-040 / REQ-FN-041 — settings take effect; seed data present
// ---------------------------------------------------------------------------
test('FN040 FN041 settings effect and seed data', async ({ page }) => {
  test.setTimeout(240000);
  await settle(page, '/');
  const seeds = await page.evaluate(() => {
    const links = Array.from(document.querySelectorAll('a[href^="/post/"]'))
      .map((a) => (a as HTMLAnchorElement).getAttribute('href'))
      .filter((h, i, arr) => arr.indexOf(h) === i);
    const cats = Array.from(document.querySelectorAll('a[href*="/category"]')).length;
    const tags = Array.from(document.querySelectorAll('a[href*="/tag"]')).length;
    return { posts: links.length, cats, tags, title: document.title };
  });
  record('REQ-FN-041', `home shows ${seeds.posts} seeded posts, ${seeds.cats} category links, ${seeds.tags} tag links; document.title="${seeds.title}"`);

  // Settings persistence, read-only: the admin settings form must render the STORED values,
  // and the public site must already reflect them (site name in the header / title).
  await loginResilient(page);
  // The site-settings screen is routed at /settings, NOT /admin/settings.
  await nav(page, '/settings').catch(() => {});
  await page.waitForTimeout(4000);
  const settings = await page.evaluate(() => {
    const inputs = Array.from(document.querySelectorAll('input, textarea, select')) as HTMLInputElement[];
    const filled = inputs.filter((i) => (i.value || '').trim().length > 0);
    return {
      url: location.pathname,
      inputs: inputs.length,
      filled: filled.length,
      sample: filled.slice(0, 6).map((i) => `${i.name || i.getAttribute('data-testid') || i.type}="${(i.value || '').slice(0, 30)}"`),
    };
  });
  record('REQ-FN-040', `${settings.url}: ${settings.filled}/${settings.inputs} inputs render a persisted value ${JSON.stringify(settings.sample)}`);
  await page.screenshot({ path: path.join(SHOTS, 'fn040-settings.png'), fullPage: true });
  expect(seeds.posts, 'seeded posts on the public home page').toBeGreaterThan(0);
});

test.afterAll(() => {
  fs.writeFileSync(path.join(SHOTS, 'findings.txt'), findings.join('\n'));
  console.log('\n===== VALL-NFR FINDINGS =====\n' + findings.join('\n') + '\n=============================\n');
});
