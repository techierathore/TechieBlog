#!/usr/bin/env bash
# Injects psql truth as env vars, then runs ONLY the admin-cluster spec.
# Truth is read at run time so a sibling agent's concurrent write cannot stale an assertion.
set -euo pipefail
cd /mnt/c/1MyCode/TechieBlog

q() { docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -c "$1" | tr -d '\r' | head -1; }

export TB_POSTS=$(q "SELECT COUNT(*) FROM blogpost WHERE IsDeleted = FALSE OR IsDeleted IS NULL")
export TB_PUBLISHED=$(q "SELECT COUNT(*) FROM blogpost WHERE published=true AND (IsDeleted=FALSE OR IsDeleted IS NULL)")
export TB_SCHEDULED=$(q "SELECT COUNT(*) FROM blogpost WHERE published=false AND scheduledpublishon IS NOT NULL AND (IsDeleted=FALSE OR IsDeleted IS NULL)")
export TB_DRAFTS=$(q "SELECT COUNT(*) FROM blogpost WHERE published=false AND scheduledpublishon IS NULL AND (IsDeleted=FALSE OR IsDeleted IS NULL)")
export TB_COMMENTS=$(q "SELECT COUNT(*) FROM blogcomment")
export TB_PENDING=$(q "SELECT COUNT(*) FROM blogcomment WHERE published=false")
export TB_CATEGORIES=$(q "SELECT COUNT(*) FROM category")
export TB_CATEGORISED_POSTS=$(q "SELECT COUNT(*) FROM blogpost WHERE categoryid IS NOT NULL AND (IsDeleted=FALSE OR IsDeleted IS NULL)")
export TB_TAGS=$(q "SELECT COUNT(*) FROM tag")
export TB_POSTTAG=$(q "SELECT COUNT(*) FROM posttag")
export TB_USERS=$(q "SELECT COUNT(*) FROM bloguser")
export TB_SUBSCRIBERS=$(q "SELECT COUNT(*) FROM subscriber")
export TB_POSTVIEWS=$(q "SELECT COUNT(*) FROM postviews")

s() { docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -c "SELECT SettingValue FROM SiteSetting WHERE SettingKey='$1'" | tr -d '\r' | head -1; }
export TB_SITE_TITLE=$(s General.SiteTitle)
export TB_SITE_TAGLINE=$(s General.SiteTagline)
export TB_ADMIN_EMAIL=$(s General.AdminEmail)
export TB_POSTS_PER_PAGE=$(s Blog.PostsPerPage)
export TB_PAGINATION_WORDS=$(s Blog.PaginationWordCount)
export TB_MODERATED=$(s Blog.AreCommentsModerated)
export TB_SITE_THEME=$(s Theme.SiteTheme)
export TB_META_DESCRIPTION=$(s Seo.MetaDescription)
export TB_SMTP_PORT=$(s Smtp.Port)
export TB_SMTP_FROM_NAME=$(s Smtp.FromName)
export TB_STORAGE_PROVIDER=$(s Storage.ProviderName)

# Analytics ranges. The page's default window is the last 30 days ending "now" (exclusive upper
# bound at the start of tomorrow); the 7-day preset is the same shape.
export TB_COMMENTS_30D=$(q "SELECT COUNT(*) FROM blogcomment WHERE givenon >= (CURRENT_DATE - INTERVAL '29 days') AND givenon < (CURRENT_DATE + INTERVAL '1 day')")
export TB_COMMENTS_7D=$(q "SELECT COUNT(*) FROM blogcomment WHERE givenon >= (CURRENT_DATE - INTERVAL '6 days') AND givenon < (CURRENT_DATE + INTERVAL '1 day')")
export TB_RATINGS_30D=$(q "SELECT COUNT(*) FROM postrating WHERE createdon >= (CURRENT_DATE - INTERVAL '29 days') AND createdon < (CURRENT_DATE + INTERVAL '1 day')")

echo "psql truth: posts=$TB_POSTS pub=$TB_PUBLISHED drafts=$TB_DRAFTS sched=$TB_SCHEDULED comments=$TB_COMMENTS pending=$TB_PENDING cats=$TB_CATEGORIES(catposts=$TB_CATEGORISED_POSTS) tags=$TB_TAGS(posttag=$TB_POSTTAG) users=$TB_USERS subs=$TB_SUBSCRIBERS views=$TB_POSTVIEWS theme=$TB_SITE_THEME c30=$TB_COMMENTS_30D c7=$TB_COMMENTS_7D r30=$TB_RATINGS_30D"

npx playwright test tests/verify/vall-admin.spec.ts --reporter=line --timeout=420000 "$@"
