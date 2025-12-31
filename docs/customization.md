# TechieBlog Customization Guide

This guide covers how to customize TechieBlog's appearance, configuration, and functionality.

---

## Table of Contents

1. [Theming Overview](#theming-overview)
2. [CSS Variables Reference](#css-variables-reference)
3. [Pre-built Themes](#pre-built-themes)
4. [Creating Custom Themes](#creating-custom-themes)
5. [Dark Mode](#dark-mode)
6. [Configuration Options](#configuration-options)
7. [Extending Functionality](#extending-functionality)
8. [Component Customization](#component-customization)

---

## Theming Overview

TechieBlog uses a **CSS Custom Properties (variables)** system for theming. This means you can completely change the visual appearance without modifying any C# or Razor code.

### How It Works

```
source/BlogUI/wwwroot/Themes/
├── _variables.css      # Base design system (loaded first)
├── fluent-modern.css   # Default theme
├── developer-dark.css  # Developer-focused dark theme
└── minimal-clean.css   # Minimal, content-focused theme
```

1. `_variables.css` defines the base design system
2. Theme files override specific variables
3. Dark mode is handled via `[data-theme="dark"]` selector

### Quick Customization

To change the primary brand color across the entire site:

```css
/* In your theme file or _variables.css */
:root {
    --color-primary: #your-brand-color;
    --color-primary-hover: #your-brand-color-darker;
}
```

That's it! All buttons, links, and accent elements update automatically.

---

## CSS Variables Reference

### Color Palette

| Variable | Default (Light) | Purpose |
|----------|-----------------|---------|
| `--color-primary` | `#0078D4` | Brand color, CTAs, links, focus states |
| `--color-primary-hover` | `#106EBE` | Primary color hover state |
| `--color-secondary` | `#6B6B6B` | Supporting UI, less emphasis |
| `--color-accent` | `#107C10` | Highlights, success callouts |
| `--color-background` | `#FFFFFF` | Page background |
| `--color-surface` | `#F5F5F5` | Card backgrounds, elevated elements |
| `--color-surface-hover` | `#EBEBEB` | Surface hover state |
| `--color-success` | `#107C10` | Success messages, positive actions |
| `--color-warning` | `#FFB900` | Warning states |
| `--color-error` | `#D13438` | Error messages, destructive actions |
| `--color-neutral` | `#323130` | Primary text color |
| `--color-neutral-light` | `#605E5C` | Secondary text, captions |
| `--color-border` | `#E1E1E1` | Dividers, input borders |
| `--color-link` | `#0078D4` | Link text color |

### Typography

| Variable | Default | Purpose |
|----------|---------|---------|
| `--font-family` | `'Segoe UI', -apple-system, ...` | Primary font stack |
| `--font-family-mono` | `'Cascadia Code', Consolas, ...` | Code snippets |
| `--font-size-h1` | `2rem` | Page titles |
| `--font-size-h2` | `1.5rem` | Section headings |
| `--font-size-h3` | `1.25rem` | Subsection headings |
| `--font-size-body` | `1rem` | Body text |
| `--font-size-small` | `0.875rem` | Secondary text |
| `--font-size-xs` | `0.75rem` | Labels, badges |
| `--font-weight-regular` | `400` | Body text weight |
| `--font-weight-semibold` | `600` | Headings, emphasis |
| `--line-height-tight` | `1.25` | Headings |
| `--line-height-normal` | `1.6` | Body text |

### Spacing Scale

Based on 4px unit for consistent rhythm:

| Variable | Value | Use Case |
|----------|-------|----------|
| `--spacing-xs` | `4px` | Tight spacing, inline elements |
| `--spacing-sm` | `8px` | Small gaps, compact components |
| `--spacing-md` | `16px` | Standard spacing, form fields |
| `--spacing-lg` | `24px` | Section spacing, card padding |
| `--spacing-xl` | `32px` | Large gaps between sections |
| `--spacing-2xl` | `48px` | Page section margins |
| `--spacing-3xl` | `64px` | Hero sections |

### Layout

| Variable | Default | Purpose |
|----------|---------|---------|
| `--max-content-width` | `1200px` | Main content max width |
| `--sidebar-width` | `280px` | Sidebar panel width |
| `--header-height` | `56px` | Header height |
| `--border-radius` | `4px` | Standard corner rounding |
| `--border-radius-lg` | `8px` | Cards, modals |

### Shadows

| Variable | Purpose |
|----------|---------|
| `--shadow-sm` | Subtle lift for hover states |
| `--shadow-md` | Cards, dropdowns |
| `--shadow-lg` | Modals, dialogs |

### Transitions

| Variable | Value | Purpose |
|----------|-------|---------|
| `--transition-fast` | `150ms ease` | Hover states, toggles |
| `--transition-normal` | `200ms ease-out` | Expanding, sliding |

---

## Pre-built Themes

### Fluent Modern (Default)

Microsoft Fluent Design-inspired theme with clean lines and professional appearance.

- Primary: Blue (`#0078D4`)
- Clean, professional look
- Good for corporate/business blogs

### Developer Dark

Dark theme optimized for developer blogs and technical content.

- Primary: Cyan/Teal accent
- High contrast for code readability
- Easy on the eyes for long reading sessions

### Minimal Clean

Content-focused theme with minimal chrome.

- Neutral colors
- Maximum focus on content
- Great for writing-focused blogs

### Switching Themes

Update `appsettings.json`:

```json
{
  "SiteSettings": {
    "Theme": "developer-dark"
  }
}
```

Valid values: `fluent-modern`, `developer-dark`, `minimal-clean`

---

## Creating Custom Themes

### Step 1: Create Theme File

Create a new CSS file in `source/BlogUI/wwwroot/Themes/`:

```css
/* my-custom-theme.css */

/* Light mode overrides */
:root {
    --color-primary: #E91E63;        /* Pink */
    --color-primary-hover: #C2185B;
    --color-accent: #9C27B0;         /* Purple */
    --color-background: #FAFAFA;
    --color-surface: #FFFFFF;
}

/* Dark mode overrides */
[data-theme="dark"] {
    --color-primary: #F48FB1;
    --color-primary-hover: #F06292;
    --color-accent: #CE93D8;
    --color-background: #121212;
    --color-surface: #1E1E1E;
}
```

### Step 2: Register Theme

Add theme to the theme loader (varies by implementation).

### Step 3: Test Both Modes

Always test your theme in both light and dark modes:

1. Check text contrast ratios (WCAG AA minimum)
2. Verify hover states are visible
3. Test with actual content

### Theme Checklist

- [ ] Primary color has sufficient contrast
- [ ] Links are distinguishable from regular text
- [ ] Error/warning/success colors are distinct
- [ ] Dark mode has appropriate contrast
- [ ] Code blocks are readable
- [ ] Forms have clear focus states

---

## Dark Mode

### How Dark Mode Works

Dark mode is controlled via the `data-theme` attribute on the root element:

```html
<!-- Light mode -->
<html>

<!-- Dark mode -->
<html data-theme="dark">
```

### User Preference

The theme toggle in the header allows users to switch. Their preference is stored in `localStorage`.

### System Preference

To respect system preference, the app checks `prefers-color-scheme`:

```javascript
// Automatically applied on load
if (window.matchMedia('(prefers-color-scheme: dark)').matches) {
    document.documentElement.setAttribute('data-theme', 'dark');
}
```

### Customizing Dark Mode

Override dark mode variables in your theme:

```css
[data-theme="dark"] {
    --color-background: #0D1117;  /* GitHub-style dark */
    --color-surface: #161B22;
    --color-border: #30363D;
}
```

---

## Configuration Options

### Site Settings (appsettings.json)

```json
{
  "SiteSettings": {
    "SiteName": "My Blog",
    "SiteDescription": "A blog about awesome things",
    "SiteUrl": "https://myblog.com",
    "Theme": "fluent-modern",
    "PostsPerPage": 10,
    "AllowRegistration": true,
    "AllowComments": true,
    "RequireCommentApproval": false,
    "EnableRssFeed": true,
    "EnableSitemap": true
  }
}
```

### Email Settings

```json
{
  "EmailSettings": {
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "SmtpUsername": "your-email@example.com",
    "SmtpPassword": "your-password",
    "FromAddress": "noreply@myblog.com",
    "FromName": "My Blog"
  }
}
```

### Storage Settings

```json
{
  "StorageSettings": {
    "Provider": "Local",
    "LocalPath": "BlogImages",
    "MaxFileSizeMB": 10,
    "AllowedExtensions": [".jpg", ".jpeg", ".png", ".gif", ".webp"]
  }
}
```

---

## Extending Functionality

### Adding a New Service

1. **Define Interface** in `BlogModel/Interfaces/`:

```csharp
public interface IMyService
{
    Task<MyResult> DoSomethingAsync();
}
```

2. **Implement Service** in `BlogEngine/Services/`:

```csharp
public class MyService : IMyService
{
    public async Task<MyResult> DoSomethingAsync()
    {
        // Implementation
    }
}
```

3. **Register in DI** in `TechieBlog/Program.cs`:

```csharp
builder.Services.AddScoped<IMyService, MyService>();
```

### Adding a New Page

1. Create Razor component in `BlogUI/Pages/`:

```razor
@page "/my-page"
@inject IMyService MyService

<PageTitle>My Page</PageTitle>

<h1>My Custom Page</h1>
```

2. Add to navigation if needed in `BlogUI/Components/Navigation.razor`

### Adding a New Component

1. Create in `BlogUI/Components/`:

```razor
<!-- MyComponent.razor -->
<div class="my-component">
    @ChildContent
</div>

@code {
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
```

2. Use in pages:

```razor
<MyComponent>
    <p>Content here</p>
</MyComponent>
```

---

## Component Customization

### SCSS Structure

```
source/BlogUI/Styles/
├── _variables.scss    # SCSS variables (compiled)
├── _base.scss         # Base element styles
├── _typography.scss   # Text styles
├── _button.scss       # Button components
├── _card.scss         # Card components
├── _form.scss         # Form elements
├── _table.scss        # Table styles
├── _tabs.scss         # Tab components
├── _alert.scss        # Alert/notification styles
├── _badge.scss        # Badge/tag styles
└── blogui.scss        # Main entry point
```

### Modifying Components

Edit the relevant SCSS partial:

```scss
/* _button.scss */
.btn {
    padding: var(--spacing-sm) var(--spacing-md);
    border-radius: var(--border-radius);
    transition: var(--transition-fast);

    &:hover {
        transform: translateY(-1px);
    }
}
```

### Compile SCSS

After changes, rebuild:

```bash
dotnet build
```

Or use a SCSS watcher in your IDE.

---

## Best Practices

### Theming

1. **Always use CSS variables** - Never hardcode colors
2. **Test both modes** - Light and dark
3. **Check contrast** - Use tools like WebAIM Contrast Checker
4. **Keep it simple** - Subtle changes have big impact

### Extending

1. **Follow existing patterns** - Look at how similar features are built
2. **Use dependency injection** - Don't create tight coupling
3. **Keep components small** - Single responsibility
4. **Document your changes** - Future you will thank you

### Performance

1. **Minimize CSS overrides** - Use variables, not !important
2. **Lazy load images** - Use built-in lazy loading
3. **Keep bundle size small** - Only add what you need

---

## Troubleshooting

### Theme Not Applying

1. Check browser cache - Hard refresh (`Ctrl+Shift+R`)
2. Verify theme file is in correct location
3. Check `appsettings.json` theme value
4. Inspect element to see which CSS is loading

### Dark Mode Not Working

1. Check `data-theme` attribute on `<html>`
2. Verify `[data-theme="dark"]` selectors in CSS
3. Check localStorage for saved preference

### SCSS Changes Not Reflecting

1. Ensure SCSS compiler is running
2. Check for syntax errors in SCSS
3. Clear `obj/` and `bin/` folders and rebuild

---

## Need Help?

- Check the [Architecture Guide](architecture.md) for technical details
- Review the [PRD](prd.md) for feature specifications
- Open an issue on GitHub for bugs or questions
