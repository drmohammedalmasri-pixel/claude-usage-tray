# Changelog

## 1.2.0 (2026-11-08)

### New
- **English by default**: a new install now starts in English. Pick another language in Settings → General → Language (or *Automatic (Windows)*). Existing settings are kept.
- **Delete a session**: right-click a session in the history (or select it and press Delete) to remove it from the list and the average, after a question.

### Fixed
- A 5-hour session that began while the PC was off or the app was not running looked as if it was counted wrong: the list showed only the part the app saw (e.g. 14 %) while the popup showed 43 %. The count itself is right (usage from before the first reading cannot be split), but the list now shows the range (e.g. *14 % (29→43)*) and a note explains it.

## 1.1.0 (2026-11-07)

> **Coming from 1.0.0?** Version 1.0.0 cannot update itself. Download 1.1.0 once by hand from [Releases](../../releases); from then on new versions install themselves.

### New
- **Updates**: *Check for updates* in the menu. Shows the new version and its notes, installs it and restarts the app.
- **Automatic updates** switch in Settings → General (on by default): checks once a day and installs new versions by itself, with a notice after the restart. When it is off, the app only tells you a new version exists and the menu item becomes *Install version X*.
- Downloads come only from this project's GitHub releases and are checked before use (size, Windows program header, SHA-256 checksum from GitHub). If the swap fails, the old version is put back.

### Fixed
- A finished 5-hour session could be missing from the session list. It was only added after the next reading, so when the PC slept or the app could not fetch, the session stayed hidden. It now moves to the list within a minute after its 5-hour window ends.
- When fetching usage failed, nothing was written to the log, so the cause could not be found. Every failure is now logged (the same error again only every 30 minutes), plus a line when fetching works again.
- A fetch that never returned could block all later updates. After 2 minutes a new fetch now starts anyway.

## 1.0.0 (2026-11-07)

First public version.
