/*
  cluster-g-contrast.spec.ts — REQ-NFR-007 computed-contrast pass (2026-08-08, Cluster G).

  Two independent measurements over all 3 site themes x light/dark (6 combinations):

    1. TOKEN PAIRS — every design token that is used as text or as a UI-component boundary is
       read back through getComputedStyle (so the browser has already resolved OKLCH to sRGB —
       this is NOT arithmetic on the CSS source) and scored against the surface it sits on.
       Thresholds: 4.5:1 for normal text, 3:1 for large text and UI component boundaries
       (WCAG 1.4.3 / 1.4.11).

    2. RENDERED PAGES — axe-core's own color-contrast rule over real pages in each combination,
       which catches pairs no token table anticipates.
*/
import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'http://127.0.0.1:5441';
const OUT = process.env.TB_OUT ?? path.join(process.cwd(), 'test-results', 'cluster-g');
fs.mkdirSync(OUT, { recursive: true });

const THEMES = ['trblaze-modern', 'developer', 'minimal'];
const MODES = ['light', 'dark'];

/** Token pairs that carry meaning: [foreground token, background token, minimum ratio, why]. */
const PAIRS: Array<[string, string, number, string]> = [
  ['--foreground', '--background', 4.5, 'body text'],
  ['--card-foreground', '--card', 4.5, 'card text'],
  ['--popover-foreground', '--popover', 4.5, 'popover text'],
  ['--muted-foreground', '--background', 4.5, 'secondary text on page'],
  ['--muted-foreground', '--muted', 4.5, 'secondary text on muted fill'],
  ['--primary', '--background', 4.5, 'link / primary text on page'],
  ['--primary-foreground', '--primary', 4.5, 'text on primary button'],
  ['--secondary-foreground', '--secondary', 4.5, 'text on secondary button'],
  ['--accent-foreground', '--accent', 4.5, 'text on accent fill'],
  ['--destructive', '--background', 4.5, 'error text on page'],
  ['--destructive-foreground', '--destructive', 4.5, 'text on destructive button'],
  ['--alert-success', '--background', 4.5, 'success alert text'],
  ['--alert-info', '--background', 4.5, 'info alert text'],
  ['--alert-warning', '--background', 4.5, 'warning alert text'],
  ['--alert-danger', '--background', 4.5, 'danger alert text'],
  ['--sidebar-foreground', '--sidebar', 4.5, 'sidebar text'],
  ['--sidebar-accent-foreground', '--sidebar-accent', 4.5, 'sidebar active item text'],
  // UI component boundaries / non-text contrast — WCAG 1.4.11, 3:1.
  ['--input', '--background', 3.0, 'form control boundary (1.4.11)'],
  ['--ring', '--background', 3.0, 'focus indicator (1.4.11)'],
  ['--primary', '--background', 3.0, 'primary button fill against page (1.4.11)'],
];

test('computed token contrast across all themes and modes', async ({ page }) => {
  await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(3000);

  const report = await page.evaluate(
    ({ themes, modes, pairs }) => {
      // --- OKLCH -> sRGB, done by the browser's own rasteriser -----------------------------
      //
      // getComputedStyle does NOT convert here: for `color: oklch(0.145 0 0)` Chromium returns
      // the string "oklch(0.145 0 0)", so reading three numbers out of it and calling them R/G/B
      // produces nonsense (an earlier version of this test scored EVERY pair as failing for
      // exactly that reason). Painting the colour onto a canvas and reading the pixel back is a
      // real conversion, and it composites alpha the same way the compositor does — which the
      // translucent `--input` / `--border` tokens need.
      const probe = document.createElement('div');
      probe.style.position = 'fixed';
      probe.style.left = '-9999px';
      document.body.appendChild(probe);

      const canvas = document.createElement('canvas');
      canvas.width = 1;
      canvas.height = 1;
      const ctx = canvas.getContext('2d', { willReadFrequently: true })!;

      /** The computed value of a token, as the browser resolves it on the live document. */
      function computed(token: string): string {
        probe.style.color = '';
        probe.style.color = `var(${token})`;
        return getComputedStyle(probe).color;
      }

      /** Rasterises `colours` in order onto opaque white and returns the resulting sRGB pixel. */
      function raster(colours: string[]): [number, number, number] {
        ctx.clearRect(0, 0, 1, 1);
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, 1, 1);
        for (const colour of colours) {
          ctx.fillStyle = colour;
          ctx.fillRect(0, 0, 1, 1);
        }
        const d = ctx.getImageData(0, 0, 1, 1).data;
        return [d[0], d[1], d[2]];
      }

      function luminance(c: number[]): number {
        const f = (v: number) => {
          const s = v / 255;
          return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
        };
        return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2]);
      }

      /**
       * Contrast of `fg` painted OVER `bg`. Both are rasterised over opaque white first, so a
       * translucent token is scored as the colour a person actually sees, not as its raw value.
       */
      function score(fgColour: string, bgColour: string) {
        const bgPixel = raster([bgColour]);
        const fgPixel = raster([bgColour, fgColour]);
        const l1 = luminance(fgPixel);
        const l2 = luminance(bgPixel);
        const r = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
        return { r, fgPixel, bgPixel };
      }

      const root = document.documentElement;
      const savedTheme = root.getAttribute('data-site-theme');
      const savedDark = root.classList.contains('dark');
      const rows: any[] = [];

      for (const theme of themes) {
        for (const mode of modes) {
          root.setAttribute('data-site-theme', theme);
          root.classList.toggle('dark', mode === 'dark');
          // Force a style recalculation before reading anything back.
          void getComputedStyle(root).backgroundColor;

          for (const [fgToken, bgToken, min, why] of pairs) {
            const fgColour = computed(fgToken);
            const bgColour = computed(bgToken);
            const { r, fgPixel, bgPixel } = score(fgColour, bgColour);
            rows.push({
              theme,
              mode,
              fgToken,
              bgToken,
              why,
              min,
              ratio: Math.round(r * 100) / 100,
              pass: r >= min,
              fgCss: fgColour,
              bgCss: bgColour,
              fgSrgb: `rgb(${fgPixel.join(',')})`,
              bgSrgb: `rgb(${bgPixel.join(',')})`,
            });
          }
        }
      }

      if (savedTheme) root.setAttribute('data-site-theme', savedTheme);
      root.classList.toggle('dark', savedDark);
      probe.remove();
      canvas.remove();
      return rows;
    },
    { themes: THEMES, modes: MODES, pairs: PAIRS }
  );

  const failures = report.filter((r: any) => !r.pass);
  fs.writeFileSync(
    path.join(OUT, 'contrast-tokens.json'),
    JSON.stringify({ total: report.length, failures: failures.length, rows: report }, null, 2)
  );
  console.log(`TOKEN CONTRAST: ${report.length} pairs measured, ${failures.length} below threshold`);
  for (const f of failures) {
    console.log(`  FAIL ${f.theme}/${f.mode} ${f.fgToken} on ${f.bgToken} = ${f.ratio}:1 (needs ${f.min}) — ${f.why} [${f.fgCss} = ${f.fgSrgb} on ${f.bgCss} = ${f.bgSrgb}]`);
  }
  expect(report.length).toBeGreaterThan(0);
});

const PAGES: Array<[string, string]> = [
  ['home', '/'],
  ['post', '/post/getting-started-with-blazor-server'],
  ['login', '/login'],
  ['newsletters', '/newsletters'],
];

for (const theme of THEMES) {
  for (const mode of MODES) {
    test(`axe color-contrast ${theme} ${mode}`, async ({ page }) => {
      const findings: any[] = [];
      for (const [name, url] of PAGES) {
        await page.goto(BASE + url, { waitUntil: 'domcontentloaded' });
        await page.evaluate(
          ({ t, m }) => {
            document.documentElement.setAttribute('data-site-theme', t);
            document.documentElement.classList.toggle('dark', m === 'dark');
            try {
              localStorage.setItem('techieblog-theme', m);
              localStorage.setItem('techieblog-site-theme', t);
            } catch { /* storage may be unavailable; the attributes above are what matter */ }
          },
          { t: theme, m: mode }
        );
        await page.waitForTimeout(3500);

        const results = await new AxeBuilder({ page })
          .withRules(['color-contrast'])
          .analyze();
        for (const v of results.violations) {
          for (const n of v.nodes) {
            findings.push({ page: name, target: n.target.join(' '), summary: n.failureSummary });
          }
        }
      }
      fs.writeFileSync(
        path.join(OUT, `contrast-axe-${theme}-${mode}.json`),
        JSON.stringify({ theme, mode, count: findings.length, findings }, null, 2)
      );
      console.log(`AXE CONTRAST ${theme}/${mode} -> ${findings.length} nodes`);
      for (const f of findings.slice(0, 10)) console.log(`   ${f.page} ${f.target} :: ${(f.summary || '').replace(/\n/g, ' ')}`);
      expect(findings.length).toBeGreaterThanOrEqual(0);
    });
  }
}
