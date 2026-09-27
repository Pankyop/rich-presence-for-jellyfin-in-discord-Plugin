# 💡 Feature Request & Modification Template & Example

> **Notice for Users**: Use this template when requesting a new feature, improvement, or modification to the **Discord Rich Presence for Jellyfin** plugin.  
> Below is both a **Blank Template** you can copy-paste and a **Filled-In Example** to help make your proposal clear, actionable, and well-structured.

---

## 📋 Blank Template (Copy & Paste)

```markdown
### Feature / Modification Summary
<!-- A clear, 1-2 sentence overview of what you are proposing. -->

### Motivation / Problem Statement
<!-- Why is this modification desirable? Is it addressing a current limitation or adding an exciting new capability? -->
<!-- e.g. "Currently, Discord Rich Presence only displays..., but it would be great if..." -->

### Proposed Solution
<!-- A detailed description of how you envision this feature or modification working. -->
<!-- What should the Discord profile show? How should users configure it in Jellyfin Dashboard? -->

### Configuration / UI Impact (If applicable)
<!-- Does this require new toggles or input fields in the plugin's web dashboard? -->
- [ ] New option in Plugin Configuration page (e.g., checkbox, dropdown)
- [ ] Discord Presence visual change only
- [ ] Performance / caching optimization
- [ ] Metadata provider expansion

### Alternatives or Workarounds Considered
<!-- Did you consider any alternative implementations or workarounds? Why is your proposed solution preferred? -->

### Visual Mockup / Examples (Optional)
<!-- Attach any mockups, screenshots, or examples from other Discord integrations (e.g. PreMiD, Spotify, Plex). -->

### Additional Context
<!-- Any other relevant technical details, API documentation links, or ideas. -->
```

---

## 💡 Filled-In Example (Reference)

```markdown
### Feature / Modification Summary
Add an optional configuration toggle to display the production studio / network logo (e.g., Warner Bros, Studio Ghibli, HBO, Netflix) as the small image icon instead of the generic media type icon.

### Motivation / Problem Statement
Currently, the small badge icon in the Discord Rich Presence always shows a generic film strip or play icon. For animation enthusiasts and cinephiles, knowing the studio (like MAPPA, Ufotable, Pixar, A24) at a glance adds extra personality and detail to the presence status.

### Proposed Solution
1. In `ActivityBuilder.cs`, inspect `item.Studios`.
2. If `ShowStudioLogo` is enabled in plugin settings, query a curated mapping or ClearLogo/Studio image URL.
3. If an icon is found or mapped, set `SmallImageKey` to the studio badge URL and `SmallImageText` to the studio name (e.g., "A24").
4. If no studio is available, fall back gracefully to the standard media type badge.

### Configuration / UI Impact (If applicable)
- [x] New option in Plugin Configuration page:
  - Add a toggle: `Display Studio Badge (where available)`
- [x] Discord Presence visual change:
  - Small circular badge will display the studio emblem instead of the play/film icon.
- [ ] Performance / caching optimization
- [x] Metadata provider expansion

### Alternatives or Workarounds Considered
- Keeping only the generic play icon (current behavior).
- Placing the studio name in the status text (takes away precious character space in Discord's 128-char limit).

### Visual Mockup / Examples (Optional)
Mockup of Discord activity with studio badge:
- **Title**: Spirited Away (2001)
- **Details**: Watching Movie (01:15:20 / 02:05:00)
- **Large Image**: Theatrical poster for Spirited Away
- **Small Image**: Studio Ghibli emblem
- **Small Image Hover**: "Studio Ghibli"

### Additional Context
The Jellyfin item model exposes `BaseItem.Studios` which contains the studio names without needing additional network calls.
```
