/**
 * Token-pair matrix: every foreground token against every surface it can land on, in all 3
 * themes x light/dark, resolved by the browser so OKLCH is evaluated exactly as painted.
 *
 * REQ-UI-033's acceptance is dark mode; the light rows are reported for REQ-NFR-007's owner.
 */
import { test } from '@playwright/test';

const BASE = process.env.TB_BASE ?? 'https://localhost:7373';
test.use({ ignoreHTTPSErrors: true });
test.setTimeout(180000);

const THEMES = ['trblaze-modern', 'developer', 'minimal'];

test('token pair matrix', async ({ page }) => {
  await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
  let darkTotal = 0;
  let lightTotal = 0;

  for (const theme of THEMES) {
    for (const dark of [false, true]) {
      const res = await page.evaluate(
        ([t, d]) => {
          const root = document.documentElement;
          root.setAttribute('data-site-theme', t as string);
          root.classList.toggle('dark', d as boolean);
          const cs = getComputedStyle(root);
          const v = (n: string) => cs.getPropertyValue(n).trim();

          const cv = document.createElement('canvas');
          cv.width = cv.height = 1;
          const cx = cv.getContext('2d', { willReadFrequently: true })!;
          const px = (c: string) => {
            cx.fillStyle = '#010203';
            cx.fillStyle = c;
            cx.clearRect(0, 0, 1, 1);
            cx.fillRect(0, 0, 1, 1);
            const q = cx.getImageData(0, 0, 1, 1).data;
            return [q[0], q[1], q[2], q[3] / 255];
          };
          const over = (f: number[], b: number[]) => {
            const a = f[3];
            return [f[0] * a + b[0] * (1 - a), f[1] * a + b[1] * (1 - a), f[2] * a + b[2] * (1 - a), 1];
          };
          const lum = (c: number[]) => {
            const f = (x: number) => {
              x /= 255;
              return x <= 0.03928 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4);
            };
            return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2]);
          };
          const ratio = (a: number[], b: number[]) => {
            const [l1, l2] = [lum(a), lum(b)].sort((x, y) => y - x);
            return +((l1 + 0.05) / (l2 + 0.05)).toFixed(2);
          };

          const surfaces = ['--background', '--card', '--muted', '--secondary', '--accent', '--popover'];
          const texts = [
            '--foreground',
            '--muted-foreground',
            '--primary',
            '--alert-success',
            '--alert-info',
            '--alert-warning',
            '--alert-danger',
            '--destructive',
          ];

          const fails: any[] = [];
          for (const s of surfaces) {
            const sc = px(v(s));
            if (!sc) continue;
            for (const f of texts) {
              const fc = px(v(f));
              if (!fc) continue;
              const r = ratio(fc[3] < 1 ? over(fc, sc) : fc, sc);
              if (r < 4.5) fails.push({ fg: f, bg: s, ratio: r, fgVal: v(f), need: 4.5 });
            }
            const ic = px(v('--input'));
            if (ic) {
              const r = ratio(ic[3] < 1 ? over(ic, sc) : ic, sc);
              if (r < 3) fails.push({ fg: '--input', bg: s, ratio: r, fgVal: v('--input'), need: 3 });
            }
          }
          return fails;
        },
        [theme, dark] as [string, boolean]
      );

      if (dark) darkTotal += res.length;
      else lightTotal += res.length;
      console.log(`\n### ${theme} / ${dark ? 'DARK' : 'light'} — ${res.length} pair(s) below threshold`);
      for (const f of res) console.log(`   ${f.ratio}:1 (need ${f.need})  ${f.fg} on ${f.bg}   [${f.fgVal}]`);
    }
  }
  console.log(`\n######## DARK total=${darkTotal}   light total=${lightTotal} ########`);
});
