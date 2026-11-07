import { test } from '@playwright/test';
import { loginHard, open, psql, SHOT } from './bp-a-helpers';
import { pickSelect, setMarkdown, fillCommitted } from './vall-authoring-helpers';
import * as fs from 'fs';
const OUT = `${SHOT}/editor`;
test.use({ actionTimeout: 25000 });

async function until(page: any, fn: () => Promise<boolean>, ms = 40000) {
  const t = Date.now();
  while (Date.now() - t < ms) { if (await fn().catch(() => false)) return true; await page.waitForTimeout(700); }
  return false;
}
async function readEditor(page: any) {
  const textOf = async (s: string) => { const e = page.locator(s).first(); return (await e.count()) === 0 ? '' : ((await e.textContent()) || '').trim(); };
  const img = page.locator('[data-testid="selected-image"]').first();
  return {
    title: await page.inputValue('[data-testid="post-title-input"]'),
    slug: await page.inputValue('[data-testid="post-slug-input"]'),
    excerpt: await page.inputValue('[data-testid="post-excerpt-input"]'),
    body: await page.inputValue('[data-testid="markdown-input"]'),
    category: await textOf('[data-testid="category-select"]'),
    series: await textOf('[data-testid="series-select"]'),
    part: await textOf('[data-testid="series-part-badge"]'),
    tags: (await page.locator('[data-testid="selected-tag"]').allTextContents()).map((t: string) => t.trim()),
    image: (await img.count()) === 0 ? '' : (await img.getAttribute('src')) || '',
  };
}
test('superseded by w3-authoring.spec.ts (seed-dependent / psql-writing, not a product verdict): REQ-UI-016 editor', async ({ browser }) => {
  test.setTimeout(560000);
  fs.mkdirSync(OUT, { recursive: true });
  const R: any = {}; const T0 = Date.now(); const log = (m: string) => console.log(Math.round((Date.now() - T0) / 1000) + 's ' + m);
  const page = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  const errs: string[] = [];
  page.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  try {
    // fixtures for B (psql) : series, tag, post
    psql("insert into blogseries(name,slug,description,authorid,status) values('fxa EdSeries','fxa-edseries','fxa editor series',1,'In Progress')");
    const sid = psql("select seriesid from blogseries where slug='fxa-edseries'")[0][0];
    psql("insert into tag(tagname,slug) values('fxa-ed-b','fxa-ed-b')");
    const tagB = psql("select tagid from tag where slug='fxa-ed-b'")[0][0];
    psql(`insert into blogpost(title,abstract,postcontent,userid,featuredimage,published,slug,categoryid,seriesid,seriespartnumber) values('fxa-ed B Title','fxa B abstract text',E'## Beta heading\\n\\nBody of B only','1','/media/blog/fxa-ed-b.png',false,'fxa-ed-b',1,${sid},1)`);
    const idB = psql("select postid from blogpost where slug='fxa-ed-b'")[0][0];
    psql(`insert into posttag(postid,tagid) values(${idB},${tagB})`);
    R.idB = idB;
    await loginHard(page, 'admin');
    log('login');
    // ---- create A through the UI
    await open(page, '/ManagePost', '[data-testid="markdown-input"]');
    await page.waitForTimeout(3000);
    await page.fill('[data-testid="post-title-input"]', 'fxa-ed A Title');
    await page.dispatchEvent('[data-testid="post-title-input"]', 'change');
    await page.waitForTimeout(1500);
    R.autoSlug = await page.inputValue('[data-testid="post-slug-input"]');
    R.slugPreview = (await page.textContent('[data-testid="post-slug-preview"]').catch(() => 'ABSENT'))?.trim();
    await page.fill('[data-testid="post-slug-input"]', 'fxa-ed-a'); await page.dispatchEvent('[data-testid="post-slug-input"]', 'change');
    await fillCommitted(page, 'post-excerpt-input', 'fxa A abstract text');
    await page.click('[data-testid="markdown-view-split"]').catch(() => {});
    await page.waitForTimeout(1000);
    const ok = await setMarkdown(page, '# Alpha heading\n\nThis is **bold A** and a list:\n\n- one\n- two\n');
    R.mdAccepted = ok;
    await until(page, async () => (await page.innerHTML('[data-testid="markdown-preview-content"]')).includes('<strong'));
    R.previewHtml = (await page.innerHTML('[data-testid="markdown-preview-content"]')).slice(0, 400);
    // live: type more then preview changes
    await setMarkdown(page, '# Alpha heading\n\nThis is **bold A** and ~~gone~~ text\n');
    R.livePreviewUpdated = await until(page, async () => (await page.innerHTML('[data-testid="markdown-preview-content"]')).includes('<del'), 20000);
    await pickSelect(page, 'category-select', 'fxa Category');
    await fillCommitted(page, 'tag-input', 'fxa-ed-a');
    await page.click('[data-testid="add-tag"]');
    await page.waitForTimeout(1500);
    await pickSelect(page, 'series-select', 'fxa EdSeries');
    await page.waitForTimeout(1000);
    await page.screenshot({ path: `${OUT}/new-before-save.png`, fullPage: true });
    await page.click('[data-testid="save-draft"]');
    const saved = await until(page, async () => psql("select count(*) from blogpost where slug='fxa-ed-a'")[0][0] === '1', 40000);
    R.savedA = saved;
    log('saved A ' + saved);
    R.dbA = psql("select postid,title,slug,abstract,replace(postcontent,E'\\n','\\n'),categoryid,seriesid,seriespartnumber,published,userid from blogpost where slug='fxa-ed-a'")[0];
    R.dbATags = psql("select t.tagname from posttag p join tag t using(tagid) join blogpost b using(postid) where b.slug='fxa-ed-a'").map((r) => r[0]);
    const idA = R.dbA?.[0];
    await page.waitForTimeout(3000);
    R.afterSaveUrl = new URL(page.url()).pathname;
    R.statusMsg = (await page.textContent('[data-testid="post-status-message"]').catch(() => 'none'))?.trim();
    // ---- reload A + B truth
    const truth = (slug: string) => {
      const r = psql(`select title,abstract,postcontent,coalesce(featuredimage,''),slug,categoryid,coalesce(seriesid,0),coalesce(seriespartnumber,0) from blogpost where slug='${slug}'`)[0];
      return r;
    };
    R.truthA = truth('fxa-ed-a'); R.truthB = truth('fxa-ed-b');
    await open(page, `/ManagePost/${idA}`, '[data-testid="markdown-input"]');
    await page.waitForTimeout(4000);
    R.stateA = await readEditor(page);
    // client side switch A -> B
    await open(page, `/ManagePost/${idB}`, '[data-testid="markdown-input"]');
    await page.waitForTimeout(500);
    const t0 = Date.now();
    await until(page, async () => (await page.inputValue('[data-testid="post-title-input"]')) === 'fxa-ed B Title', 30000);
    R.switchMs = Date.now() - t0;
    await page.waitForTimeout(4000);
    R.stateB = await readEditor(page);
    await page.screenshot({ path: `${OUT}/switched-to-B.png`, fullPage: true });
    // save from B state, verify A untouched, B untouched in content
    const beforeA = psql(`select title,abstract,postcontent,slug,categoryid,coalesce(seriesid,0),updatedon from blogpost where postid=${idA}`)[0];
    await page.click('[data-testid="save-draft"]');
    await page.waitForTimeout(8000);
    const afterA = psql(`select title,abstract,postcontent,slug,categoryid,coalesce(seriesid,0),updatedon from blogpost where postid=${idA}`)[0];
    R.saveAfterSwitch = { aUnchanged: JSON.stringify(beforeA) === JSON.stringify(afterA), bAfter: psql(`select title,abstract,slug,categoryid,seriesid,seriespartnumber,featuredimage from blogpost where postid=${idB}`)[0], bContent: psql(`select postcontent from blogpost where postid=${idB}`)[0][0] };
    // back to A
    await open(page, `/ManagePost/${idA}`, '[data-testid="markdown-input"]');
    await until(page, async () => (await page.inputValue('[data-testid="post-title-input"]')) === 'fxa-ed A Title', 30000);
    await page.waitForTimeout(3000);
    R.stateBackA = await readEditor(page);
    R.errs = errs.slice(0, 8);
  } catch (e) { R.error = String(e).slice(0, 400); }
  finally {
    fs.writeFileSync(`${OUT}/result.json`, JSON.stringify(R, null, 1));
    for (const s of ["delete from posttag where postid in (select postid from blogpost where slug in ('fxa-ed-a','fxa-ed-b'))",
      "delete from postcategory where postid in (select postid from blogpost where slug in ('fxa-ed-a','fxa-ed-b'))",
      "delete from blogpost where slug in ('fxa-ed-a','fxa-ed-b')", "delete from tag where slug in ('fxa-ed-a','fxa-ed-b')", "delete from blogseries where slug='fxa-edseries'"]) {
      try { psql(s); } catch (e) { console.log('cleanup fail', s, String(e).slice(0, 200)); }
    }
  }
});
