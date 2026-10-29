# TechieBlog — decisions I need from you

| | |
|---|---|
| App | TechieBlog |
| Written | 2026-10-28 |
| Waiting on | 3 decisions. The build carries on without them; nothing is stuck. |

## What happened

The build pass rebuilt and re-checked every open row. 156 of 159 rows now pass on one build. Three
questions came up that only you can answer, because each one changes what the product should do.

## What I need you to decide

### 1. Can BlogApp be tested against the local database?

BlogApp is set up to talk to your production database, so I did not start it. Five rows need it
started: the full admin surface in BlogApp (REQ-UI-052) and the BlogApp halves of four others.

| Option | What happens | What it costs |
|---|---|---|
| **A — Point it at the local database for testing** | I start BlogApp against the local `TechieBlog` database, run the checks, then put your production connection back. | About an hour. Your production data is never touched. |
| **B — Leave BlogApp untested** | The five rows stay as they are, and BlogApp is checked by hand when you next use it. | Nothing now; those rows stay open. |

**My recommendation: A.** It is the only way to prove "published in BlogApp, visible on the site".

### 2. Should an admin be able to remove a subscriber?

The checklist says "remove", but the app only deactivates a subscriber. That is on purpose: deleting
the row would erase the record of their consent (REQ-FN-059).

| Option | What happens | What it costs |
|---|---|---|
| **A — Keep "deactivate" and reword the requirement** | The checklist says "deactivate" instead of "remove". No code changes. | About five minutes. |
| **B — Add a real delete** | A delete button removes the subscriber, and the consent record goes with it. | About an hour, and you lose the proof of consent. |

**My recommendation: A.** Keeping proof of consent matters more than removing the row.

### 3. Should the profile page have an admin user picker?

The experience, skills and awards pages let an admin switch between users. The profile page does
not; it always shows your own profile (REQ-UI-040).

| Option | What happens | What it costs |
|---|---|---|
| **A — Reword the requirement** | Admins edit other users' profiles from the Users page, as today. | About five minutes. |
| **B — Add the picker** | The profile page gets the same user picker as the other resume pages. | About an hour. |

**My recommendation: A.** You are the only person with a resume on this site.

## What I do when you answer

1. For decision 1 A: start BlogApp against the local database, run its checks, restore the production connection.
2. For decisions 2 and 3: reword the checklist rows (option A) or build the feature (option B), then re-check those rows.

## Copy this back to me

```
TechieBlog: decision 1 — go with option A. Test BlogApp against the local database, then restore production.
```

```
TechieBlog: decision 2 — go with option A. Keep "deactivate" and reword REQ-UI-025.
```

```
TechieBlog: decision 3 — go with option A. Reword REQ-UI-040; no picker on the profile page.
```
