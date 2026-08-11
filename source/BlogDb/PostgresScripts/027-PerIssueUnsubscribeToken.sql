-- ============================================================================
-- Script: 027-PerIssueUnsubscribeToken.sql
-- Purpose: Give every newsletter ISSUE its own unsubscribe token, so the
--          credential mailed to a subscriber is scoped to one send instead of
--          being the single row-level token that shipped in every issue they
--          ever received.
-- Author: flow-master (build phase, Cluster D)
-- Created: 2026-08-11
-- Requirements: REQ-FN-060 (per-issue unsubscribe token scope)
-- Depends on:   001-CreateTables.sql (Subscriber)
--               015-NewsletterAndAnalytics.sql (Newsletter,
--                                               Subscriber.UnsubscribeToken)
--               024-SubscriberConsentAndTokenLifecycle.sql
--                                              (Subscriber.ConfirmedOn,
--                                               UnsubscribeTokenIssuedOn/UsedOn,
--                                               TrgSubscriberConsentChange)
--
-- ----------------------------------------------------------------------------
-- THE DEFECT
-- ----------------------------------------------------------------------------
-- REQ-FN-059 gave Subscriber.UnsubscribeToken a real lifecycle - burned on use,
-- 400-day expiry, rotated on re-consent - and added
-- SubscriberSvc.IssueUnsubscribeTokenAsync so a send could rotate it. Nothing
-- ever called it: NewsletterSvc.BuildMessage read subscriber.UnsubscribeToken
-- straight off the recipient row. One token therefore shipped in EVERY issue an
-- address received, and its blast radius was "every mail ever sent to this
-- address" rather than one issue. A single archived newsletter forwarded to a
-- colleague handed them a credential that works against every past and future
-- issue.
--
-- ----------------------------------------------------------------------------
-- THE DESIGN DECISION - read this before "finishing" it differently
-- ----------------------------------------------------------------------------
-- The literal reading of the acceptance sentence is "rotate the row's token on
-- every send, so a token from an older issue is refused once a newer one is
-- issued". That is ONE row-level token that the newest send supersedes, and it
-- is rejected here, because it breaks a case that matters more than the one it
-- fixes:
--
--     A subscriber receives issue #1 on Monday and issue #2 on Friday. On
--     Saturday they open MONDAY's mail and click Unsubscribe.
--
-- Under the superseding design that click is refused - the Friday send replaced
-- the token - and the reader is told their link is invalid while they are still
-- on the list. Refusing a genuine opt-out is a CAN-SPAM / GDPR-shaped failure,
-- strictly worse than the over-broad credential being narrowed. It is also the
-- exact failure mode REQ-FN-059 spent its whole header block avoiding when it
-- chose a 400-day expiry over a short one.
--
-- So this migration takes the other defensible design the acceptance explicitly
-- permits, and records the reasoning here as the acceptance requires:
--
--     PER-SEND TOKEN ROWS. Every (SubscriberId, NewsletterId) send issues its
--     own token row. Each row stays valid until it is USED, EXPIRED, or
--     SUPERSEDED BY A RE-CONSENT. Blast radius collapses from "every issue ever"
--     to "this one issue", which is the property the requirement is actually
--     about, and every link in every already-delivered issue keeps working.
--
-- "Rotation on re-consent only" - the requirement's own title - is preserved
-- exactly, and is enforced WITHOUT a revocation column: a token row is stale
-- when it was issued before the subscriber's current consent instant, i.e.
--     UnsubscribeToken.IssuedOn < Subscriber.ConfirmedOn
-- Re-consent moves ConfirmedOn forward, which invalidates every token issued
-- under the previous consent in one stroke. That is a pure read-side rule, so no
-- writer anywhere in the solution can forget to revoke - the same "the database
-- guarantees it, not the caller" reasoning that put TrgSubscriberConsentChange
-- in migration 024.
--
-- ----------------------------------------------------------------------------
-- CHANGES
-- ----------------------------------------------------------------------------
--   PART A - Table UnsubscribeToken: one row per issued per-issue credential.
--   PART B - Indexes: unique on Token (it is the lookup key AND a credential),
--            plus the two foreign-key columns.
--   PART C - Column comments recording the lifecycle rules.
--
--   NOT DONE, deliberately:
--     * No backfill. Tokens already sitting in delivered mail are row-level
--       tokens on Subscriber and must keep resolving; inventing per-issue rows
--       for past sends would not make those delivered links per-issue, it would
--       only duplicate credentials.
--     * Subscriber.UnsubscribeToken is NOT dropped and NOT changed. It stays as
--       the fallback the send path uses when a per-issue token cannot be issued,
--       and as the resolver for every link already in someone's inbox. Dropping
--       it would strand every previously mailed unsubscribe link - the precise
--       harm this whole requirement family exists to prevent.
--     * No trigger. The staleness rule above is evaluated on read, so there is
--       nothing for a trigger to keep in step.
--
-- ----------------------------------------------------------------------------
-- TOKEN LIFECYCLE (mirrored in SubscriberSvc.UnsubscribeByTokenAsync)
-- ----------------------------------------------------------------------------
--   issued     - one row per (SubscriberId, NewsletterId) at send time, from a
--                cryptographically secure RNG in C# (RandomNumberGenerator), NOT
--                from md5(random()) as the legacy column DEFAULT does. The token
--                authorises a state change on a stranger's row, so a predictable
--                PRNG is not acceptable for it.
--   used       - UsedOn stamped in the same statement that records the
--                withdrawal, so one link performs at most one state change.
--   expired    - 400 days after IssuedOn, the same lifetime REQ-FN-059 chose and
--                for the same reason (a mail opened months later must still
--                work; the cap matches the 400-day browser cookie cap).
--   superseded - IssuedOn < Subscriber.ConfirmedOn, i.e. a re-consent happened
--                after this token was issued. Rotation on re-consent, and the
--                ONLY event that invalidates an otherwise-live token.
--   withdrawn  - once the subscriber is already off the list, ANY of their
--                tokens (per-issue or legacy) reports "already unsubscribed"
--                rather than an error, so re-opening an old mail is a no-op.
--
-- ----------------------------------------------------------------------------
-- IDEMPOTENCY
-- ----------------------------------------------------------------------------
-- DbUp journals scripts, but this file is written to survive re-application:
-- CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS, and COMMENT ON is
-- idempotent by nature. There is no data write of any kind.
--
-- ----------------------------------------------------------------------------
-- ROLLBACK
-- ----------------------------------------------------------------------------
--   DROP INDEX IF EXISTS IdxUnsubscribeTokenNewsletterId;
--   DROP INDEX IF EXISTS IdxUnsubscribeTokenSubscriberId;
--   DROP INDEX IF EXISTS IdxUnsubscribeTokenToken;
--   DROP TABLE IF EXISTS UnsubscribeToken;
-- Rolling back is safe at any time: the send path falls back to
-- Subscriber.UnsubscribeToken when no per-issue token can be issued, so the
-- feature degrades to its pre-REQ-FN-060 behaviour rather than breaking.
-- ============================================================================


-- ============================================================================
-- PART A: The per-issue token table
--
-- SubscriberId is ON DELETE CASCADE: a token is meaningless without the row it
-- authorises, and subscribers are soft-deleted everywhere in this schema anyway,
-- so the cascade is a safety net rather than a routine path.
--
-- NewsletterId is nullable and ON DELETE SET NULL. Nullable because a token may
-- legitimately be issued outside a send (an operator re-issuing one link by
-- hand); SET NULL because losing the issue an old token belonged to must never
-- delete the token and strand the subscriber holding it.
-- ============================================================================
CREATE TABLE IF NOT EXISTS UnsubscribeToken (
    UnsubscribeTokenId  BIGSERIAL PRIMARY KEY,
    SubscriberId        BIGINT NOT NULL REFERENCES Subscriber(SubscriberId) ON DELETE CASCADE,
    NewsletterId        BIGINT NULL REFERENCES Newsletter(NewsletterId) ON DELETE SET NULL,
    Token               VARCHAR(64) NOT NULL,
    IssuedOn            TIMESTAMP NOT NULL,
    UsedOn              TIMESTAMP NULL
);


-- ============================================================================
-- PART B: Indexes
-- ============================================================================

-- UNIQUE, not merely indexed. The token is the whole authorisation for an
-- anonymous state change, so two rows sharing one value would make "which
-- subscriber does this link belong to?" ambiguous. The uniqueness is also what
-- lets the redemption statement key on Token alone.
CREATE UNIQUE INDEX IF NOT EXISTS IdxUnsubscribeTokenToken
    ON UnsubscribeToken(Token);

-- Supports "every token this subscriber holds", which is how the staleness rule
-- and any future audit read the table.
CREATE INDEX IF NOT EXISTS IdxUnsubscribeTokenSubscriberId
    ON UnsubscribeToken(SubscriberId);

-- Partial: rows issued outside a send carry NULL and are never looked up by
-- issue, so they are kept out of the index.
CREATE INDEX IF NOT EXISTS IdxUnsubscribeTokenNewsletterId
    ON UnsubscribeToken(NewsletterId)
    WHERE NewsletterId IS NOT NULL;


-- ============================================================================
-- PART C: Column documentation
-- ============================================================================
COMMENT ON TABLE UnsubscribeToken IS
    'One unsubscribe credential per newsletter issue per subscriber (REQ-FN-060). Rows are never '
    'deleted and never revoked in place: a token is spent when UsedOn is stamped, and superseded '
    'when Subscriber.ConfirmedOn moves past its IssuedOn (re-consent). Every issued token stays '
    'valid until one of those happens or it passes its 400-day expiry, so an unsubscribe link in an '
    'older issue keeps working after a newer issue goes out.';

COMMENT ON COLUMN UnsubscribeToken.SubscriberId IS
    'The subscriber this credential opts out. The token, not an identity, is the authorisation for '
    'the anonymous /unsubscribe/{token} page.';

COMMENT ON COLUMN UnsubscribeToken.NewsletterId IS
    'The issue this token was mailed in - what makes the credential SCOPED. NULL means the token '
    'was issued outside a send and belongs to no single issue.';

COMMENT ON COLUMN UnsubscribeToken.Token IS
    '64 lower-case hex characters: 256 bits from a cryptographically secure RNG in the application, '
    'never md5(random()). A bearer credential; never log it and never render it outside the '
    'unsubscribe URL of an outbound message.';

COMMENT ON COLUMN UnsubscribeToken.IssuedOn IS
    'When this token was mailed. Starts the 400-day expiry clock, and is compared against '
    'Subscriber.ConfirmedOn to decide whether a later re-consent has superseded it.';

COMMENT ON COLUMN UnsubscribeToken.UsedOn IS
    'When this token was redeemed. Stamped in the same statement that records the withdrawal, so '
    'one link performs at most one state change. Never cleared - a re-consent supersedes tokens '
    'through ConfirmedOn instead of rewriting their history.';

-- ============================================================================
-- End of Migration
-- ============================================================================
