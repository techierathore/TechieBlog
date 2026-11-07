# TechieBlog — decisions I need from you

| | |
|---|---|
| App | TechieBlog |
| Written | 2026-10-27 |
| Waiting on | 1 decision. Nothing has been changed yet. |

## What happened

I added the brand-assets work (logo in the header, favicon, BlogApp icon, README logo) to the plan:
four new requirements and four new checklist rows. The documents' own checker then refused to close
the step, because the checklist now has 171 rows and the plan has 105 requirements.

The reason is older than today. The project never recorded how big it is, so the checker assumes
"small", which allows 50 rows. TechieBlog passed that limit months ago; it only showed up now because
adding rows makes the count grow. Even "medium" allows only 100, so no size fixes it on its own. The
checker's way out is to mark the project as large, which means it is planned in phases, and either keep
the current plan whole as phase 1 or move new work into its own phase. How the plan is laid out is
yours to choose, so I stopped here instead of restructuring the documents myself.

## What I need you to decide

### 1. How to record TechieBlog's size

The choice is whether the existing plan stays one document, or the brand work starts a second one.

| Option | What happens | What it costs |
|---|---|---|
| **A — Large, keep it whole** | The project is recorded as large with one phase. The current plan stays exactly as it is, marked as kept whole on purpose. The size warnings stay visible but stop blocking. | About ten minutes. Adds a short phases list and a "Phase 1 of 1" line to the four main documents. Nothing is moved. |
| **B — Large, brand work as phase 2** | Same as A for everything existing, but the four brand items move into a new, small phase 2 plan and checklist of their own. | About thirty minutes. Two more files to keep in step; the brand work is tracked apart from the rest. |

**My recommendation: A** — the plan has worked as one document for months, and splitting off four small items adds files without making anything clearer.

## What I do when you answer

1. Record the size as large in the project settings and write the phases list.
2. Add the phase line to the plan, the architecture, the screen designs and the checklist.
3. For B only: move the four brand items and their rows into the new phase 2 files.
4. Re-run the document check, refresh the status page, and hand you the build command.

## Copy this back to me

```
TechieBlog: decision 1 — go with option A. Record the project as Large with one phase, and keep phase 1 whole.
```

```
TechieBlog: decision 1 — go with option B. Record the project as Large and move the brand work into phase 2.
```
