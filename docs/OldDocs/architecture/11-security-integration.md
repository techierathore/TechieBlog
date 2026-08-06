# 11. Security Integration

### 11.1 Existing Security Measures

| Measure | Current Implementation |
|---------|----------------------|
| **Authentication** | JWT tokens with custom claims |
| **Authorization** | Role claim in JWT (single role per user) |
| **Data Protection** | Custom encryption (AppEncrypt) for sensitive data |
| **Security Tools** | None |

### 11.2 Enhancement Security Requirements

| Requirement | Implementation |
|-------------|----------------|
| **New Security Measures** | Rate limiting on auth endpoints, password reset token expiry |
| **Integration Points** | AuthSvc for all auth, middleware for rate limiting |
| **Compliance Requirements** | HTTPS enforcement, secure password hashing (verify BCrypt) |

### 11.3 Security Testing

| Aspect | Approach |
|--------|----------|
| **Existing Security Tests** | None |
| **New Security Test Requirements** | SQL injection prevention, XSS prevention, auth bypass attempts |
| **Penetration Testing** | Manual review post-MVP, no automated pentest tooling |

### 11.4 Accessibility Architecture (MANDATORY)

**Compliance Target:** WCAG 2.1 Level AA

#### 11.4.1 Semantic HTML Standards

All Blazor components MUST use semantic HTML elements:

| Purpose | Required Element | Avoid |
|---------|------------------|-------|
| **Page Title** | `<h1>` (one per page) | Multiple h1s, div with large font |
| **Sections** | `<section>`, `<article>`, `<aside>` | Generic divs |
| **Navigation** | `<nav>` with `aria-label` | Div with links |
| **Lists** | `<ul>`, `<ol>`, `<dl>` | Divs with line breaks |
| **Buttons** | `<button>` or `<FluentButton>` | Clickable divs/spans |
| **Links** | `<a href>` for navigation | Buttons for navigation |
| **Forms** | `<form>` with `<label>` associations | Inputs without labels |
| **Tables** | `<table>` with `<th scope>` | Divs styled as tables |

#### 11.4.2 ARIA Implementation Guidelines

```razor
@*
    Component: PostCard.razor
    Accessibility: Implements article landmark with descriptive aria-label
*@
<article aria-labelledby="post-title-@PostId" class="post-card">
    <h2 id="post-title-@PostId">@Title</h2>

    @* Rating component with accessible label *@
    <div role="img" aria-label="Rating: @Rating out of 5 stars">
        <RatingStars Value="@Rating" ReadOnly="true" />
    </div>

    @* Read more link with descriptive text for screen readers *@
    <a href="/post/@Slug" aria-describedby="post-title-@PostId">
        Read more<span class="visually-hidden"> about @Title</span>
    </a>
</article>
```

##### Required ARIA Patterns by Component

| Component | ARIA Pattern | Implementation |
|-----------|--------------|----------------|
| **Main Layout** | Landmarks | `role="banner"`, `role="main"`, `role="contentinfo"` |
| **Navigation** | Menu | `role="navigation"`, `aria-label="Main navigation"` |
| **Theme Toggle** | Switch | `role="switch"`, `aria-checked`, `aria-label="Dark mode"` |
| **Search** | Combobox | `role="combobox"`, `aria-expanded`, `aria-controls` |
| **Modal Dialogs** | Dialog | `role="dialog"`, `aria-modal="true"`, `aria-labelledby` |
| **Notifications** | Alert | `role="alert"`, `aria-live="polite"` |
| **Loading States** | Status | `aria-busy="true"`, `aria-live="polite"` |
| **Form Errors** | Alert | `role="alert"`, `aria-describedby` on input |
| **Data Tables** | Table | `role="table"`, scope attributes on headers |
| **Pagination** | Navigation | `role="navigation"`, `aria-label="Pagination"` |

#### 11.4.3 Keyboard Navigation Requirements

##### Focus Management

```csharp
/// <summary>
/// JavaScript interop for managing focus in Blazor components.
/// Required for modal dialogs, dropdown menus, and dynamic content.
/// </summary>
public class FocusManager
{
    private readonly IJSRuntime jsRuntime;

    /// <summary>
    /// Traps focus within a modal dialog.
    /// Focus cycles through focusable elements within the container.
    /// </summary>
    /// <param name="containerId">ID of the modal container element.</param>
    public async Task TrapFocusAsync(string containerId)
    {
        await jsRuntime.InvokeVoidAsync("accessibilityHelpers.trapFocus", containerId);
    }

    /// <summary>
    /// Restores focus to the element that triggered a modal.
    /// Called when modal is closed.
    /// </summary>
    public async Task RestoreFocusAsync(string triggerElementId)
    {
        await jsRuntime.InvokeVoidAsync("accessibilityHelpers.restoreFocus", triggerElementId);
    }
}
```

##### Keyboard Shortcuts

| Action | Keyboard Shortcut | Context |
|--------|-------------------|---------|
| **Skip to main content** | `Tab` → Enter on skip link | All pages |
| **Toggle theme** | `Alt + T` | Global |
| **Open search** | `/` or `Ctrl + K` | When not in text input |
| **Close modal** | `Escape` | Any open modal |
| **Navigate menu** | `Arrow keys` | Dropdown menus |
| **Submit form** | `Enter` | Form inputs |
| **Cancel action** | `Escape` | Forms, modals |

##### Focus Indicator Standards

```css
/*
 * Focus indicators must be visible and have sufficient contrast.
 * Minimum 3:1 contrast ratio against adjacent colors.
 * Must not rely on color alone.
 */

/* Default focus style for all interactive elements */
:focus-visible {
    outline: 2px solid var(--focus-ring-color, #0078d4);
    outline-offset: 2px;
}

/* Never remove focus indicators */
:focus {
    outline: none; /* Only if custom focus style is applied */
}

/* High contrast mode support */
@media (forced-colors: active) {
    :focus-visible {
        outline: 3px solid CanvasText;
    }
}
```

#### 11.4.4 Screen Reader Compatibility

##### Visually Hidden Text Utility

```css
/*
 * Text that is hidden visually but available to screen readers.
 * Used for additional context that sighted users don't need.
 */
.visually-hidden {
    position: absolute;
    width: 1px;
    height: 1px;
    padding: 0;
    margin: -1px;
    overflow: hidden;
    clip: rect(0, 0, 0, 0);
    white-space: nowrap;
    border: 0;
}

/* Show on focus for skip links */
.visually-hidden-focusable:focus {
    position: static;
    width: auto;
    height: auto;
    margin: 0;
    overflow: visible;
    clip: auto;
    white-space: normal;
}
```

##### Announcements for Dynamic Content

```razor
@*
    LiveRegion component for announcing dynamic changes.
    Use aria-live="polite" for non-urgent updates.
    Use aria-live="assertive" for errors or critical alerts.
*@
<div aria-live="polite" aria-atomic="true" class="visually-hidden">
    @AnnouncementText
</div>

@code {
    [Parameter]
    public string AnnouncementText { get; set; }

    /// <summary>
    /// Announces a message to screen readers.
    /// Message is automatically cleared after announcement.
    /// </summary>
    public async Task AnnounceAsync(string message)
    {
        AnnouncementText = message;
        StateHasChanged();
        await Task.Delay(100); // Allow screen reader to pick up
        AnnouncementText = "";
        StateHasChanged();
    }
}
```

#### 11.4.5 Accessibility Testing Requirements

| Test Type | Tool | Frequency | Pass Criteria |
|-----------|------|-----------|---------------|
| **Automated Scan** | axe DevTools | Every PR | No critical/serious violations |
| **Keyboard Testing** | Manual | Every new component | All functions keyboard accessible |
| **Screen Reader** | NVDA / VoiceOver | Major features | Content fully navigable and understandable |
| **Color Contrast** | WebAIM Contrast Checker | Theme changes | 4.5:1 for text, 3:1 for large text |
| **Zoom Testing** | Browser zoom 200% | Major layouts | No horizontal scroll, content readable |

##### Accessibility Checklist per Component

Before any Blazor component is considered complete:

- [ ] Semantic HTML elements used appropriately
- [ ] All interactive elements are keyboard accessible
- [ ] Focus order is logical (follows visual order)
- [ ] Focus indicator is visible (2px solid outline minimum)
- [ ] ARIA labels provided where native labels insufficient
- [ ] Color is not sole means of conveying information
- [ ] Text contrast meets WCAG AA (4.5:1 normal, 3:1 large)
- [ ] Tested with screen reader (at least one)
- [ ] No keyboard traps (focus can always escape)
- [ ] Dynamic content changes are announced

---
