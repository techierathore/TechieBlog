import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

/**
 * REQ-FN-043 acceptance — every PackageReference across the solution's csproj files is pinned to
 * an exact version; none uses a floating wildcard (e.g. `4.*`), which is what caused the NU1605
 * restore failure this row fixed.
 *
 * The build-succeeds half of the acceptance is covered by the build check that gates every row
 * (`tf-verify-boot.sh`); this test covers the half a green build alone does not prove — that no
 * reference regressed back to a floating version.
 */

const PROJECT_FILES = [
  'tests/TechieBlog.Tests/TechieBlog.Tests.csproj',
  'source/BlogApp/BlogApp.csproj',
  'source/BlogDb/BlogDb.csproj',
  'source/BlogEngine/BlogEngine.csproj',
  'source/BlogModel/BlogModels.csproj',
  'source/BlogUI/BlogUI.csproj',
  'source/TechieBlog/TechieBlog.csproj',
];

test('REQ-FN-043 — no PackageReference in the solution uses a floating version', async () => {
  const floating: string[] = [];
  const versionAttr = /<PackageReference\s+[^>]*Version="([^"]+)"[^>]*\/?>/g;

  for (const rel of PROJECT_FILES) {
    const full = path.resolve(rel);
    if (!fs.existsSync(full)) {
      continue;
    }
    const xml = fs.readFileSync(full, 'utf-8');
    let m: RegExpExecArray | null;
    versionAttr.lastIndex = 0;
    while ((m = versionAttr.exec(xml)) !== null) {
      const version = m[1];
      if (version.includes('*')) {
        floating.push(`${rel}: ${version}`);
      }
    }
  }

  expect(floating, `floating PackageReference version(s) found:\n${floating.join('\n')}`).toEqual([]);
});
