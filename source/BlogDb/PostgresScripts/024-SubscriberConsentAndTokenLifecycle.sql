-- ============================================================================
-- Script: 024-SubscriberConsentAndTokenLifecycle.sql
-- Purpose: Split the subscriber consent record out of the single IsConfirmed
--          bit, and give the unsubscribe token a lifecycle (issued / burned)
--          so it stops being an unlimited-lifetime credential.
-- Author: flow-master (build phase FIX pass, Cluster E)
-- Created: 2026-08-10
-- Requirements: REQ-FN-059 (subscriber consent model + unsubscribe token scope)
-- Depends on:   001-CreateTables.sql (Subscriber)
--               015-NewsletterAndAnalytics.sql (Subscriber.UnsubscribeToken,
--                                               its DEFAULT and unique index)
--
-- ----------------------------------------------------------------------------
-- THE DEFECT
-- ----------------------------------------------------------------------------
-- Subscriber.IsConfirmed carried two different facts in one bit: "never
-- completed double opt-in" and "explicitly opted out". Unsubscribing ran
--   UPDATE Subscriber SET IsConfirmed = FALSE
-- which ERASED the proof of consent instead of recording a withdrawal. After
-- that write an address that deliberately left was indistinguishable from one
-- that never confirmed, so a "resend confirmation" sweep would mail people who
-- had opted out, and the site could no longer show WHEN consent was given or
-- WHEN it was withdrawn - both of which a GDPR-style reading requires.
--
-- Recorded alongside it: the unsubscribe token was never rotated and never
-- burned, so the same value shipped in every issue a subscriber ever received.
--
-- ----------------------------------------------------------------------------
-- CHANGES
-- ----------------------------------------------------------------------------
--   PART A - Consent record columns on Subscriber:
--              ConfirmedOn        - when consent was most recently GIVEN
--              UnsubscribedOn     - when consent was most recently WITHDRAWN
--              IsConsentUnknown   - one-way marker for rows this migration
--                                   could not interpret (see PART B)
--            Neither timestamp is ever cleared, so a resubscribe keeps the
--            history of the earlier withdrawal. The state is derived by
--            COMPARING them (see the state table below) rather than by nulling
--            one out, which is what "preserve both" requires.
--
--   PART B - Backfill of the existing rows, deliberately conservative.
--
--   PART C - Unsubscribe token lifecycle columns:
--              UnsubscribeTokenIssuedOn - when the CURRENT token was issued;
--                                         NULL means "legacy, no recorded
--                                         issuance" and therefore no expiry
--              UnsubscribeTokenUsedOn   - when the token was burned
--
--   PART D - TrgSubscriberConsentChange, a BEFORE INSERT OR UPDATE trigger that
--            stamps the consent columns whenever IsConfirmed changes, and
--            re-issues a fresh, unburned token on re-consent. It exists so that
--            writers this migration does not own - NewsletterRepo's
--            DeactivateSubscriberAsync, SubscriberRepo.UpdateStatus, the admin
--            grid, and any future one - cannot erase the consent record or
--            leave a re-activated subscriber holding a burned link.
--
--   PART E - Supporting indexes.
--
-- ----------------------------------------------------------------------------
-- DERIVED CONSENT STATE (mirrored in BlogModels.SubscriberConsentState)
-- ----------------------------------------------------------------------------
--   Unknown   : IsConsentUnknown AND ConfirmedOn IS NULL AND UnsubscribedOn IS NULL
--   Withdrawn : UnsubscribedOn IS NOT NULL
--               AND (ConfirmedOn IS NULL OR UnsubscribedOn >= ConfirmedOn)
--   Confirmed : ConfirmedOn IS NOT NULL
--               AND (UnsubscribedOn IS NULL OR UnsubscribedOn < ConfirmedOn)
--   Pending   : anything else (signed up, opt-in link not yet redeemed)
-- IsConfirmed keeps its meaning as the single MAILABILITY bit and is unchanged
-- by this script, so every existing send query keeps working untouched.
--
-- ----------------------------------------------------------------------------
-- BACKFILL INTERPRETATION - read this before changing PART B
-- ----------------------------------------------------------------------------
-- Rows written before this migration carry one bit where two facts belong, so
-- some of them CANNOT be interpreted. The rules chosen, and why:
--
--   IsConfirmed = TRUE
--     -> ConfirmedOn := SubscribedOn.
--        The row is currently mailable, which under double opt-in can only be
--        true of an address that consented. The exact moment of confirmation
--        was never recorded, so the earliest defensible instant is used - the
--        sign-up date. It is an UNDER-statement of the consent age, never an
--        over-statement, and it never invents mailability the row did not
--        already have.
--
--   IsConfirmed = FALSE or NULL
--     -> ConfirmedOn stays NULL, UnsubscribedOn stays NULL,
--        IsConsentUnknown := TRUE (for rows that predate this script only).
--        This row is genuinely ambiguous: it is either a pending opt-in or a
--        withdrawal. The two wrong answers are not symmetric.
--          * Writing ConfirmedOn would FABRICATE proof of consent for an
--            address that may never have given any. That is the one outcome
--            this requirement forbids outright, so it is not done.
--          * Writing UnsubscribedOn would fabricate a data-subject action that
--            may never have happened, and would freeze a legitimately pending
--            sign-up in a withdrawn state it could not leave.
--        Marking the row Unknown asserts nothing about the person. It is
--        already unmailable (IsConfirmed is not TRUE and this script does not
--        touch that), so the marker costs nothing operationally, and it lets a
--        future "resend confirmation" sweep exclude exactly the rows whose
--        consent history is unknown rather than guessing.
--
--   The cutoff literal TIMESTAMP '2026-08-10 00:00:00' bounds the Unknown
--   marker to pre-existing rows, so a re-run of this script can never mark a
--   genuinely new pending subscriber as Unknown.
--
--   Measured against the live TechieBlog database on 2026-08-10: 6 subscriber
--   rows, all IsConfirmed = TRUE, so 6 rows took the ConfirmedOn branch and 0
--   took the Unknown branch.
--
-- ----------------------------------------------------------------------------
-- TOKEN LIFETIME - 400 days, and why an expiry cannot strand anybody
-- ----------------------------------------------------------------------------
-- An unsubscribe link must still work for someone who opens a mail weeks or
-- months later, so a short expiry would recreate the original harm: a mailing
-- nobody can get off. The token is therefore long-lived but ROTATABLE and
-- BURNABLE, which is where the real security comes from:
--   * burned  - redeeming a token stamps UnsubscribeTokenUsedOn in the same
--               UPDATE that records the withdrawal, so one link performs at
--               most one state change.
--   * rotated - any re-consent (confirm, admin re-activate, resubscribe)
--               replaces the token and clears the burn, so an old link that
--               leaked out of an archived mailbox cannot opt the address out
--               again after the owner deliberately came back.
--   * expiry  - 400 days after UnsubscribeTokenIssuedOn. 400 days is longer
--               than any plausible "I finally read that email" gap for a
--               periodic newsletter, and matches the 400-day cap browsers now
--               impose on cookie lifetimes, which is the closest widely
--               accepted precedent for how long a bearer credential in a
--               user's possession should stay live.
--   * legacy  - tokens already sitting in delivered mail have NO recorded
--               issuance (UnsubscribeTokenIssuedOn stays NULL here) and are
--               therefore NOT expirable. They cannot be recalled, so expiring
--               them could only ever strand a subscriber. They are still
--               burnable, and the first re-consent or send-time rotation
--               replaces them with a stamped, expiring token.
--
-- ----------------------------------------------------------------------------
-- IDEMPOTENCY
-- ----------------------------------------------------------------------------
-- DbUp journals scripts, but this file is written to survive re-application:
-- every ALTER uses IF NOT EXISTS, every backfill is guarded so it can only
-- affect rows it has not already filled, the function uses CREATE OR REPLACE,
-- and the trigger is dropped before being created.
--
-- ----------------------------------------------------------------------------
-- ROLLBACK
-- ----------------------------------------------------------------------------
--   DROP TRIGGER IF EXISTS TrgSubscriberConsentChange ON Subscriber;
--   DROP FUNCTION IF EXISTS RecordSubscriberConsentChange();
--   DROP INDEX IF EXISTS IdxSubscriberUnsubscribedOn;
--   DROP INDEX IF EXISTS IdxSubscriberConfirmedOn;
--   ALTER TABLE Subscriber
--     DROP COLUMN IF EXISTS ConfirmedOn,
--     DROP COLUMN IF EXISTS UnsubscribedOn,
--     DROP COLUMN IF EXISTS IsConsentUnknown,
--     DROP COLUMN IF EXISTS UnsubscribeTokenIssuedOn,
--     DROP COLUMN IF EXISTS UnsubscribeTokenUsedOn;
-- ============================================================================


-- ============================================================================
-- PART A: Consent record columns
-- ============================================================================
ALTER TABLE Subscriber ADD COLUMN IF NOT EXISTS ConfirmedOn TIMESTAMP;
ALTER TABLE Subscriber ADD COLUMN IF NOT EXISTS UnsubscribedOn TIMESTAMP;
ALTER TABLE Subscriber ADD COLUMN IF NOT EXISTS IsConsentUnknown BOOLEAN NOT NULL DEFAULT FALSE;

COMMENT ON COLUMN Subscriber.ConfirmedOn IS
    'When consent was most recently given. Never cleared - proof of consent.';
COMMENT ON COLUMN Subscriber.UnsubscribedOn IS
    'When consent was most recently withdrawn. Never cleared - proof of withdrawal. '
    'Compare against ConfirmedOn to derive the current state.';
COMMENT ON COLUMN Subscriber.IsConsentUnknown IS
    'Set once by migration 024 for pre-existing rows whose IsConfirmed = FALSE could not be '
    'interpreted as either pending or withdrawn. Never written by application code.';


-- ============================================================================
-- PART B: Conservative backfill (see the header block for the reasoning)
-- ============================================================================

-- A mailable row can only be a consented row under double opt-in. SubscribedOn
-- is the earliest defensible instant, so this under-states the consent age
-- rather than over-stating it. Guarded on ConfirmedOn IS NULL so a re-run
-- cannot overwrite a real confirmation timestamp recorded since.
UPDATE Subscriber
SET ConfirmedOn = SubscribedOn
WHERE COALESCE(IsConfirmed, FALSE) = TRUE
  AND ConfirmedOn IS NULL;

-- An unconfirmed pre-existing row is ambiguous by definition. It is marked, not
-- interpreted: no consent is invented and no withdrawal is invented. The
-- SubscribedOn cutoff keeps a re-run from marking genuinely new pending rows.
UPDATE Subscriber
SET IsConsentUnknown = TRUE
WHERE COALESCE(IsConfirmed, FALSE) = FALSE
  AND ConfirmedOn IS NULL
  AND UnsubscribedOn IS NULL
  AND IsConsentUnknown = FALSE
  AND SubscribedOn < TIMESTAMP '2026-08-10 00:00:00';


-- ============================================================================
-- PART C: Unsubscribe token lifecycle columns
-- ============================================================================
ALTER TABLE Subscriber ADD COLUMN IF NOT EXISTS UnsubscribeTokenIssuedOn TIMESTAMP;
ALTER TABLE Subscriber ADD COLUMN IF NOT EXISTS UnsubscribeTokenUsedOn TIMESTAMP;

COMMENT ON COLUMN Subscriber.UnsubscribeTokenIssuedOn IS
    'When the current UnsubscribeToken was issued. NULL means a legacy token with no recorded '
    'issuance, which never expires because it cannot be recalled from delivered mail.';
COMMENT ON COLUMN Subscriber.UnsubscribeTokenUsedOn IS
    'When the current UnsubscribeToken was redeemed. A burned token performs no further state '
    'change; re-consent rotates the token and clears this.';

-- Deliberately NO backfill of UnsubscribeTokenIssuedOn: stamping today's date
-- on tokens that were mailed months ago would start a 400-day clock on links
-- whose real age is unknown. Legacy tokens stay unexpiring until rotated.


-- ============================================================================
-- PART D: Consent-recording trigger
--
-- Business rules:
--   - IsConfirmed TRUE -> FALSE  : record the withdrawal instant, unless the
--                                  caller already recorded one in the same
--                                  statement (the application does).
--   - IsConfirmed FALSE -> TRUE  : record the consent instant, and re-issue the
--                                  unsubscribe token so a re-consented address
--                                  never holds a burned or leaked link.
--   - Timestamps are never cleared, so both facts survive a resubscribe cycle.
--   - now() AT TIME ZONE 'UTC' matches the application's convention of writing
--     DateTime.UtcNow into a TIMESTAMP WITHOUT TIME ZONE column.
-- ============================================================================
CREATE OR REPLACE FUNCTION RecordSubscriberConsentChange()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF COALESCE(NEW.IsConfirmed, FALSE) = TRUE AND NEW.ConfirmedOn IS NULL THEN
            NEW.ConfirmedOn := now() AT TIME ZONE 'UTC';
        END IF;
        RETURN NEW;
    END IF;

    IF COALESCE(NEW.IsConfirmed, FALSE) IS DISTINCT FROM COALESCE(OLD.IsConfirmed, FALSE) THEN
        IF COALESCE(NEW.IsConfirmed, FALSE) = TRUE THEN
            -- Re-consent. Record it unless the caller already did.
            IF NEW.ConfirmedOn IS NOT DISTINCT FROM OLD.ConfirmedOn THEN
                NEW.ConfirmedOn := now() AT TIME ZONE 'UTC';
            END IF;

            -- Re-issue the link unless the caller already rotated it, so an old
            -- token cannot opt the address out again after it came back.
            IF NEW.UnsubscribeToken IS NOT DISTINCT FROM OLD.UnsubscribeToken THEN
                NEW.UnsubscribeToken := md5(random()::text || clock_timestamp()::text || NEW.SubscriberId::text)
                                     || md5(clock_timestamp()::text || random()::text || NEW.SubscriberId::text);
                NEW.UnsubscribeTokenIssuedOn := now() AT TIME ZONE 'UTC';
            END IF;

            IF NEW.UnsubscribeTokenUsedOn IS NOT DISTINCT FROM OLD.UnsubscribeTokenUsedOn THEN
                NEW.UnsubscribeTokenUsedOn := NULL;
            END IF;
        ELSE
            -- Withdrawal. Record it unless the caller already did.
            IF NEW.UnsubscribedOn IS NOT DISTINCT FROM OLD.UnsubscribedOn THEN
                NEW.UnsubscribedOn := now() AT TIME ZONE 'UTC';
            END IF;
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS TrgSubscriberConsentChange ON Subscriber;

CREATE TRIGGER TrgSubscriberConsentChange
    BEFORE INSERT OR UPDATE ON Subscriber
    FOR EACH ROW
    EXECUTE FUNCTION RecordSubscriberConsentChange();


-- ============================================================================
-- PART E: Indexes
-- Both are partial: the interesting rows are the minority in each case, and the
-- admin roster filters on exactly these predicates.
-- ============================================================================
CREATE INDEX IF NOT EXISTS IdxSubscriberUnsubscribedOn
    ON Subscriber(UnsubscribedOn DESC)
    WHERE UnsubscribedOn IS NOT NULL;

CREATE INDEX IF NOT EXISTS IdxSubscriberConfirmedOn
    ON Subscriber(ConfirmedOn DESC)
    WHERE ConfirmedOn IS NOT NULL;

-- ============================================================================
-- End of Migration
-- ============================================================================
