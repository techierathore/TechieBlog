-- ============================================================================
-- Script: 031-SpeakingEngagements.sql
-- Purpose: Let UserEvents carry speaking engagements as well as work history,
--          which needs exactly one column it does not already have: the
--          registration link a FUTURE session is announced with.
-- Author: flow-master (*fix-issues, owner request)
-- Created: 2026-08-22
-- Requirements: UAT-006 (Speaker Profile page + its admin screen)
-- Depends on:   012-ResumeAndImageManagement.sql (UserEvents table)
--
-- ----------------------------------------------------------------------------
-- WHY THIS IS ONE COLUMN AND NOT A NEW TABLE
-- ----------------------------------------------------------------------------
-- The obvious move for "add speaking engagements" is a SpeakingEngagement table.
-- It would have been wrong here, because UserEvents is already the generic
-- timeline-entry table and already carries every field the new page needs:
--
--     eventtitle    the conference               sessiontitle  the talk
--     eventurl      link to the event page       eventdate     when it happened
--     description   the abstract                 displayorder  manual ordering
--     type          the discriminator            userid        whose it is
--
-- `type` exists precisely so this table can hold more than one kind of row; it
-- has simply never held anything but 'Experience' until now. The model even
-- says so already — UserEvent.StartDate is documented as "Experience rows only:
-- null on TALK rows" — so talks were the anticipated second type. A parallel
-- table would have duplicated eight columns, a repository, an interface and a
-- service to gain nothing but a narrower `type` check.
--
-- The one genuinely new fact about a speaking engagement is that an UPCOMING
-- one has somewhere to register, and a past one does not. That is this column.
--
-- ----------------------------------------------------------------------------
-- WHY PAST vs FUTURE IS DERIVED AND NOT STORED
-- ----------------------------------------------------------------------------
-- No `IsPast` flag is added, deliberately. A stored flag has to be maintained
-- by something — a job, a save hook, or the owner remembering — and the day it
-- is not maintained the page starts advertising a talk that happened last month
-- as upcoming. `eventdate` already holds the fact both sections are derived
-- from, so the split is computed at render time and is correct forever with
-- nobody doing anything. Note `iscurrent` is NOT reused for this: it means
-- "this job is ongoing" for Experience rows and would read as a second,
-- contradictory source of truth here.
-- ============================================================================

-- Nullable with no default: a past talk genuinely has no registration link, and
-- an empty string would be a lie the UI would then have to special-case. The
-- 500 is deliberately wider than the 350 the older URL columns use — event
-- registration links routinely carry campaign query strings, and a silently
-- truncated URL is worse than a rejected one.
ALTER TABLE UserEvents
    ADD COLUMN IF NOT EXISTS RegistrationUrl VARCHAR(500);

COMMENT ON COLUMN UserEvents.RegistrationUrl IS
    'Sign-up link for an upcoming speaking engagement. NULL for past sessions and for Experience rows.';

-- Speaking rows are read as one set, filtered by user and type and ordered by
-- date. The existing IdxUserEventsUserId only covers userid, so every read of
-- the Speaker Profile page would filter and sort the whole of a user''s
-- timeline in memory. This index answers the page''s exact query shape.
CREATE INDEX IF NOT EXISTS IdxUserEventsUserTypeDate
    ON UserEvents (UserId, Type, EventDate DESC);
