-- ============================================================================
-- Script: speaking-engagements.sql   (ONE-OFF DATA LOAD — NOT a migration)
--
-- *** THIS FILE IS DISPOSABLE. Delete it once the rows are in. ***
-- It lives under docs/ rather than in source/ precisely so that deleting it
-- cannot break a build: nothing references it, no test loads it, and DbUp does
-- not scan this folder. After it has been run against the databases that need
-- it, the engagements live in UserEvents and are maintained through
-- /admin/speaking — this script has no further purpose.
-- Purpose: Load S. Ravi Kumar's real speaking history into UserEvents so the
--          /speaker-profile page has content.
-- Created: 2026-08-22
-- Source:  https://www.c-sharpcorner.com/members/s.ravi-kumar/speakings
--          (both pages), plus each linked event page for the session titles.
--
-- ----------------------------------------------------------------------------
-- THIS IS DELIBERATELY NOT A NUMBERED MIGRATION
-- ----------------------------------------------------------------------------
-- TechieBlog ships as a clone-and-own template. A numbered migration runs on
-- EVERY database the template is ever deployed to, so putting one person's
-- speaking history in `PostgresScripts/` would hand Ravi's conference record to
-- everyone who clones the project. This file therefore lives outside the folder
-- DbUp scans, and is run by hand against the databases that should have it:
--
--     docker exec -i WinPostgre psql -U PgVectorAdmin -d TechieBlog \
--         < docs/data/speaking-engagements.sql
--
-- Run it against production the same way once migration 031 has been applied
-- there (i.e. after the website is deployed — BlogApp does not migrate).
--
-- ----------------------------------------------------------------------------
-- RE-RUNNABLE
-- ----------------------------------------------------------------------------
-- Rows are keyed on (userid, type, eventurl, sessiontitle) and skipped when
-- already present, so running this twice does not duplicate anything. Editing a
-- session afterwards through /admin/speaking is safe: a re-run will not revert
-- your edit, because the key it matches on is the event URL plus the session
-- title, and it only ever INSERTs.
--
-- ----------------------------------------------------------------------------
-- WHAT IS AND IS NOT IN HERE
-- ----------------------------------------------------------------------------
-- Present:  date, event title, event page URL, and — for the 14 rows where the
--           event page published an agenda naming the speaker — the session
--           title.
-- ABSENT:   session DESCRIPTIONS. Not one of the 18 event pages publishes a
--           per-session abstract, so there was nothing to copy. They are left
--           NULL rather than invented; the public page renders a dash and
--           /admin/speaking is where they get filled in.
-- ABSENT:   7 session titles, for events whose pages carry no agenda table at
--           all. Same reasoning — the event is recorded, the session title is
--           left blank rather than guessed from the event name.
--
-- All 21 rows are past sessions, so RegistrationUrl is NULL throughout; that
-- column exists for future engagements added through the admin screen.
-- ============================================================================

BEGIN;

WITH owner_row AS (
    -- The engagements attach to whoever the public pages are built from, which
    -- is the same account /resume and the landing page resolve. If no owner is
    -- flagged, the INSERT below matches nothing and the script is a safe no-op
    -- rather than attaching 21 rows to an arbitrary user id.
    SELECT UserId FROM BlogUser WHERE IsSiteOwner = TRUE LIMIT 1
),
incoming (eventdate, eventtitle, sessiontitle, eventurl, displayorder) AS (
    VALUES
    ('2024-12-14'::timestamp, 'Building Smarter Apps with Data and AI',
     NULL,
     'https://www.c-sharpcorner.com/events/building-smarter-apps-with-data-and-ai', 0),
    ('2023-12-02'::timestamp, '.NET Conference - 2023 Roundup',
     'AI for .NET Developers',
     'https://www.c-sharpcorner.com/events/net-conference-2023-roundup', 1),
    ('2023-09-16'::timestamp, 'How to Accelerate Your Career in AI',
     NULL,
     'https://www.c-sharpcorner.com/events/how-to-accelerate-your-career-in-ai', 2),
    ('2022-11-18'::timestamp, 'Understanding Programming Tools & Technologies With Future Technologies',
     '.NET Learning Road Map',
     'https://www.c-sharpcorner.com/events/understanding-programming-tools-technologies-with-future-technologie', 3),
    ('2022-11-18'::timestamp, 'Understanding Programming Tools & Technologies With Future Technologies',
     'Workshop on ASP.NET MVC',
     'https://www.c-sharpcorner.com/events/understanding-programming-tools-technologies-with-future-technologie', 4),
    ('2022-11-12'::timestamp, 'Deep Dive Into Blazor',
     'Blazor for MVC Developers',
     'https://www.c-sharpcorner.com/events/deep-dive-into-blazor', 5),
    ('2022-09-17'::timestamp, 'Blazor Day - Gurgaon',
     'What is Blazor',
     'https://www.c-sharpcorner.com/events/blazor-day-gurgaon', 6),
    ('2020-09-17'::timestamp, 'Indian IT Jobs Explained',
     NULL,
     'https://www.c-sharpcorner.com/events/indian-it-jobs-explained2', 7),
    ('2020-04-26'::timestamp, 'Future of IT Jobs',
     NULL,
     'https://www.c-sharpcorner.com/events/future-of-it-jobs', 8),
    ('2020-04-19'::timestamp, 'Indian IT Jobs Explained',
     NULL,
     'https://www.c-sharpcorner.com/events/indian-it-jobs-explained', 9),
    ('2020-02-09'::timestamp, 'Learn Full Stack Web Development, ReactJS, RPA, and Graphite Studio',
     'Full Stack Web Development without JavaScript',
     'https://www.c-sharpcorner.com/events/learn-reactjs-javascript-rpa-and-graphite-studio', 10),
    ('2019-12-21'::timestamp, 'Full Day Hands-On With Blazor',
     NULL,
     'https://www.c-sharpcorner.com/events/full-day-handson-with-blazor', 11),
    ('2019-08-31'::timestamp, 'Learn DevOps, Blockchain, Cloud computing, C# and Angular',
     'Introduction to C# & Career Opportunities',
     'https://www.c-sharpcorner.com/events/learn-devops-blockchain-cloud-computing-c-sharp-and-angular', 12),
    ('2018-03-17'::timestamp, 'Full Day Hands-On with Xamarin',
     NULL,
     'https://www.c-sharpcorner.com/events/full-day-handson-with-xamarin', 13),
    ('2017-09-09'::timestamp, 'Learn RealmDB & Cosmos DB With Xamarin & ASP.NET Core',
     'Introduction to C# Corner',
     'https://www.c-sharpcorner.com/events/learn-realmdb-cosmos-db-with-xamarin', 14),
    ('2017-09-09'::timestamp, 'Learn RealmDB & Cosmos DB With Xamarin & ASP.NET Core',
     'Overview of Xamarin',
     'https://www.c-sharpcorner.com/events/learn-realmdb-cosmos-db-with-xamarin', 15),
    ('2017-09-09'::timestamp, 'Learn RealmDB & Cosmos DB With Xamarin & ASP.NET Core',
     'Real time collaborative Apps with Realm',
     'https://www.c-sharpcorner.com/events/learn-realmdb-cosmos-db-with-xamarin', 16),
    ('2016-11-27'::timestamp, 'Learn Typescript, Xamarin, Azure, IoT and  Microsoft Dynamics 365',
     'Xamarin.Forms: Do More With Less',
     'https://www.c-sharpcorner.com/events/getting-started-with-microsoft-dynamics-365', 17),
    ('2016-09-25'::timestamp, 'Learn Node.js, ASP.NET, AngularJS, UWP, & Xamarin',
     'Xamarin.Forms: Do More With Less',
     'https://www.c-sharpcorner.com/events/dotnetconf-2016-ncr', 18),
    ('2016-09-17'::timestamp, 'Xamarin Meetup',
     'Meetup & Xamarin Introduction',
     'https://www.c-sharpcorner.com/events/xamarin-dev-day', 19),
    ('2016-08-27'::timestamp, 'Cloud, IoT, & Future of Tech Conference',
     NULL,
     'https://www.c-sharpcorner.com/events/cloud-iot-future-of-tech-conference', 20)
)
INSERT INTO UserEvents
    (userid, type, eventdate, eventtitle, sessiontitle, eventurl,
     description, registrationurl, startdate, iscurrent, displayorder)
SELECT
    o.UserId,
    'Speaking',
    i.eventdate,
    i.eventtitle,
    i.sessiontitle,
    i.eventurl,
    NULL,          -- description: see the header note; filled in via /admin/speaking
    NULL,          -- registrationurl: every row here is a past session
    NULL,          -- startdate: Experience rows only
    FALSE,         -- iscurrent: Experience rows only
    i.displayorder
FROM incoming i
CROSS JOIN owner_row o
WHERE NOT EXISTS (
    SELECT 1 FROM UserEvents e
    WHERE e.userid = o.UserId
      AND e.type = 'Speaking'
      AND e.eventurl = i.eventurl
      AND COALESCE(e.sessiontitle, '') = COALESCE(i.sessiontitle, '')
);

COMMIT;

-- Verify:
--   SELECT eventdate::date, eventtitle, sessiontitle
--   FROM UserEvents WHERE type = 'Speaking' ORDER BY eventdate DESC;
