# Fast-Track Backlog: Minimum Path to Dev-Ready

**Goal:** Get TechieBlog running locally with core blog functionality as fast as possible.

**Completion Criteria:**
- Can login as admin
- Can view blog posts on public pages
- Can create/edit posts in admin
- Categories and tags functional

**Estimated Time:** 4-5 working days at 3-4 stories/day

**Last Updated:** 2024-12-20

---

## Story Files Status

| Phase | Stories Created | Stories Pending | Ready to Start |
|-------|-----------------|-----------------|----------------|
| Phase 1 (P0) | 4/4 | 0 | Yes |
| Phase 2 (P1) | 5/5 | 0 | Yes |
| Phase 3 (P2) | 4/4 | 0 | Yes |

---

## Priority Legend

| Priority | Meaning |
|----------|---------|
| **P0** | Critical path - blog won't run without this |
| **P1** | Core functionality - basic blog features |
| **P2** | Usability - makes the blog actually usable |
| **DEFER** | Not needed for dev-ready MVP |

---

## Phase 1: Critical Path (P0) - Day 1

These stories must be completed first. Without them, nothing works.

| # | Story | File | Est. | Why Critical |
|---|-------|------|------|--------------|
| 1 | **2.1: JWT Authentication Service** | [2.1.story.md](2.1.story.md) | 0.5 day | Login won't work without auth backend |
| 2 | **2.4: Role-Based Authorization** | [2.4.story.md](2.4.story.md) | 0.5 day | Admin pages need protection |
| 3 | **3.1: Blog Post CRUD Operations** | [3.1.story.md](3.1.story.md) | 0.5 day | No posts = no blog |
| 4 | **3.8: Public Blog Display** | [3.8.story.md](3.8.story.md) | 0.5 day | Wire up Home and PostView pages to real data |

**Phase 1 Output:** Can login, see posts on homepage, view individual posts.

### Phase 1 Task Summary

| Story | Tasks | Key Deliverables |
|-------|-------|------------------|
| 2.1 | 5 tasks | BlogAuthStateProvider, TokenStorageService, Login wiring |
| 2.4 | 6 tasks | Authorization policies, [Authorize] attributes, AccessDenied page |
| 3.1 | 6 tasks | Slug generation, Enhanced BlogSvc, ManagePost wiring |
| 3.8 | 6 tasks | GetPublishedPosts, Home.razor wiring, PostView.razor wiring |

---

## Phase 2: Core Content (P1) - Days 2-3

Basic content management to make the blog functional.

| # | Story | File | Est. | Why Needed |
|---|-------|------|------|------------|
| 5 | **3.3: Category Management** | [3.3.story.md](3.3.story.md) | 0.5 day | Posts need categories for organization |
| 6 | **3.4: Tag Management** | [3.4.story.md](3.4.story.md) | 0.5 day | Posts need tags for discoverability |
| 7 | **3.2: Markdown Editor Integration** | [3.2.story.md](3.2.story.md) | 1 day | Need to write posts (can use textarea fallback) |
| 8 | **2.2: User Registration Flow** | [2.2.story.md](2.2.story.md) | 0.5 day | Wire up registration page |
| 9 | **3.5: Draft and Preview Workflow** | [3.5.story.md](3.5.story.md) | 0.5 day | Save drafts before publishing |

**Phase 2 Output:** Full post lifecycle working - create, edit, categorize, tag, publish.

### Phase 2 Task Summary

| Story | Tasks | Key Deliverables |
|-------|-------|------------------|
| 3.3 | 7 tasks | Category model/repo/service, Admin CRUD, Selector in post editor |
| 3.4 | 8 tasks | Enhanced BlogTag, PostTag junction, Tag autocomplete, Tag cloud |
| 3.2 | 6 tasks | Markdig integration, MarkdownEditor component, Auto-save |
| 2.2 | 5 tasks | RegisterUser method, PasswordValidator, Form wiring |
| 3.5 | 7 tasks | Save Draft/Publish actions, PreviewPost page, Status badges |

---

## Phase 3: Polish for Usability (P2) - Days 4-5

Makes the dev experience smooth but not strictly required.

| # | Story | File | Est. | Why Helpful |
|---|-------|------|------|-------------|
| 10 | **2.3: Password Reset Flow** | [2.3.story.md](2.3.story.md) | 0.5 day | Nice to have for testing auth flows |
| 11 | **2.5: User Profile Management** | [2.5.story.md](2.5.story.md) | 0.5 day | Edit profile, change password |
| 12 | **3.6: Post Scheduling** | [3.6.story.md](3.6.story.md) | 0.5 day | Schedule future posts |
| 13 | **3.7: Series/Collections Feature** | [3.7.story.md](3.7.story.md) | 0.5 day | Group related posts |

**Phase 3 Output:** Complete authoring experience.

### Phase 3 Task Summary

| Story | Tasks | Key Deliverables |
|-------|-------|------------------|
| 2.3 | 5 tasks | PasswordResetToken, RequestReset, ResetPassword, Form wiring |
| 2.5 | 5 tasks | ProfilePage, UpdateProfile, ChangePassword, Social links |
| 3.6 | 6 tasks | ScheduledPublishOn field, Date/time picker, Background publisher |
| 3.7 | 7 tasks | BlogSeries model, SeriesSvc, Admin pages, Series navigation |

---

## DEFER: Not Needed for Dev-Ready

These are explicitly deferred. Do NOT work on these until Phases 1-3 are complete.

### Epic 1 Remainder (Skip for Now)
- ~~1.9: User Dashboard UI Scaffolds~~ - User dashboard not needed for core blog
- ~~1.10: Content Management Pages UI Scaffolds~~ - Already have basic admin pages
- ~~1.11: Admin Dashboard UI Scaffold~~ - Already exists from 1.8
- ~~1.12: Admin Management Pages UI Scaffolds~~ - Already exists
- ~~1.13: CI/CD Pipeline Setup~~ - Nice to have, not blocking local dev
- ~~1.14: Testing Infrastructure Setup~~ - Tests can come after MVP works

### Epic 4: Engagement (Defer)
- 4.1-4.4: Comments, moderation, ratings, favorites - Nice features, not core blog

### Epic 5: Media & Analytics (Defer)
- 5.1-5.7: Image upload, subscribers, newsletter, analytics - Post-MVP features

### Epic 6: Production (Defer)
- 6.1-6.7: RSS, sitemap, themes polish, settings, docs - Production prep, not dev-ready

---

## Recommended Daily Plan

### Day 1 (4 stories) - Phase 1
| Order | Story | File | Description |
|-------|-------|------|-------------|
| AM-1 | 2.1 | [2.1.story.md](2.1.story.md) | JWT Authentication Service - wire up login |
| AM-2 | 2.4 | [2.4.story.md](2.4.story.md) | Role-Based Authorization - protect admin routes |
| PM-1 | 3.1 | [3.1.story.md](3.1.story.md) | Blog Post CRUD Operations - repository + service |
| PM-2 | 3.8 | [3.8.story.md](3.8.story.md) | Public Blog Display - wire up Home + PostView |

**End of Day 1:** Login works, can see posts on homepage.

### Day 2 (4 stories) - Phase 2a
| Order | Story | File | Description |
|-------|-------|------|-------------|
| AM-1 | 3.3 | [3.3.story.md](3.3.story.md) | Category Management - CRUD + selector in admin |
| AM-2 | 3.4 | [3.4.story.md](3.4.story.md) | Tag Management - CRUD + multi-select in admin |
| PM-1 | 2.2 | [2.2.story.md](2.2.story.md) | User Registration Flow - wire up register page |
| PM-2 | 3.5 | [3.5.story.md](3.5.story.md) | Draft and Preview Workflow - save/publish flow |

**End of Day 2:** Can create categorized, tagged posts with drafts.

### Day 3 (1-2 stories) - Phase 2b
| Order | Story | File | Description |
|-------|-------|------|-------------|
| Full Day | 3.2 | [3.2.story.md](3.2.story.md) | Markdown Editor Integration (may take longer) |

**End of Day 3:** Full authoring experience with Markdown.

### Day 4 (4 stories) - Phase 3
| Order | Story | File | Description |
|-------|-------|------|-------------|
| AM-1 | 2.3 | [2.3.story.md](2.3.story.md) | Password Reset Flow - reset via email token |
| AM-2 | 2.5 | [2.5.story.md](2.5.story.md) | User Profile Management - edit profile + change password |
| PM-1 | 3.6 | [3.6.story.md](3.6.story.md) | Post Scheduling - schedule future publication |
| PM-2 | 3.7 | [3.7.story.md](3.7.story.md) | Series/Collections Feature - group related posts |

**End of Day 4:** Complete core blog functionality.

### Day 5 (Buffer + Testing)
- Integration testing
- Bug fixes from testing
- UI polish if time permits

---

## Quick Start Checklist

Before starting, verify:
- [ ] PostgreSQL database running locally
- [ ] Connection string configured in `appsettings.Development.json`
- [ ] Database migrations have run successfully
- [ ] Solution builds with 0 errors

---

## Dependencies to Watch

```
2.1 (Auth) ──┬──> 2.2 (Registration)
             ├──> 2.3 (Password Reset)
             ├──> 2.4 (Roles) ──> All Admin Pages
             └──> 2.5 (Profile)

3.1 (Post CRUD) ──┬──> 3.3 (Categories)
                  ├──> 3.4 (Tags)
                  ├──> 3.5 (Drafts)
                  ├──> 3.6 (Scheduling)
                  ├──> 3.7 (Series)
                  └──> 3.8 (Public Display)

3.2 (Markdown) ──> No dependencies, can be done anytime
```

---

## Story File Index

### Phase 1 - Critical Path (All Created)
| Story | Title | File |
|-------|-------|------|
| 2.1 | JWT Authentication Service | [2.1.story.md](2.1.story.md) |
| 2.4 | Role-Based Authorization | [2.4.story.md](2.4.story.md) |
| 3.1 | Blog Post CRUD Operations | [3.1.story.md](3.1.story.md) |
| 3.8 | Public Blog Display | [3.8.story.md](3.8.story.md) |

### Phase 2 - Core Content (All Created)
| Story | Title | File |
|-------|-------|------|
| 3.3 | Category Management | [3.3.story.md](3.3.story.md) |
| 3.4 | Tag Management | [3.4.story.md](3.4.story.md) |
| 3.2 | Markdown Editor Integration | [3.2.story.md](3.2.story.md) |
| 2.2 | User Registration Flow | [2.2.story.md](2.2.story.md) |
| 3.5 | Draft and Preview Workflow | [3.5.story.md](3.5.story.md) |

### Phase 3 - Polish (All Created)
| Story | Title | File |
|-------|-------|------|
| 2.3 | Password Reset Flow | [2.3.story.md](2.3.story.md) |
| 2.5 | User Profile Management | [2.5.story.md](2.5.story.md) |
| 3.6 | Post Scheduling | [3.6.story.md](3.6.story.md) |
| 3.7 | Series/Collections Feature | [3.7.story.md](3.7.story.md) |

---

## Success Metrics

**Dev-Ready Definition:**
1. `dotnet run` starts the application without errors
2. Navigate to homepage - see list of posts
3. Click a post - see full post content
4. Login as admin - access admin dashboard
5. Create a new post with category and tags
6. Publish post - appears on homepage
7. Register new user - can login

---

## Notes

- **Markdown Editor (3.2):** If integration takes too long, use a simple `<textarea>` as fallback. Editor polish can come later.
- **Email (2.3):** Password reset requires SMTP. For dev-ready, can log reset links to console instead.
- **Authentication:** Use cookie-based auth for Blazor Server. JWT is for future API if needed.
- **Skip Icons:** Don't spend time on icon issues - use text labels as fallback.

---

## Revision History

| Date | Changes |
|------|---------|
| 2024-12-20 | Initial backlog created |
| 2024-12-20 | Phase 1 stories created (4 files) |
| 2024-12-20 | Phase 2 stories created (5 files) |
| 2024-12-20 | Phase 3 stories created (4 files) - All 13 stories complete |

---

*Generated by Sarah (PO) - TechieBlog Fast-Track MVP*
