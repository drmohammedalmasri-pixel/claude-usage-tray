# Changelog

## 1.3.0 (2026-11-10)

### New
- **Sign in again button**: when the saved sign-in has expired, the popup shows a small button that opens Claude Code's sign-in (`claude auth login`); you confirm in the browser, the window closes and the app fetches again by itself. Needs the terminal version of Claude Code.
- **Open sign-in automatically** (Settings → General, off by default): opens that sign-in window by itself once per expiry.
- **Gray icon when updates stop**: if fetching fails for more than 10 minutes, the tray icon turns gray (it still shows the last number), so it is clear the value is not live.
- **5-hour sessions are kept for good** (the weeks setting now only limits how far back the average reaches), and a session that crosses the weekly reset now counts too.
- **Better log** (`%APPDATA%\ClaudeUsageTray\log.txt`): app start (version, how long Windows has been up), a second start that was ignored because the app was already running, sleep/wake, Windows shutdown, exit, unexpected errors, and a note when the app stops responding for 3 minutes. If the log cannot be written, the line goes to `%TEMP%\ClaudeUsageTray-log.txt`.

### Fixed
- **Sessions seemed to disappear**: when the app was started from inside the Claude desktop app, Windows saved its settings, history and log in a hidden second folder, so each kind of start saw a different history. The app now notices this and starts again outside it, so there is one data folder.
- **Expired sign-in fixes itself**: when the saved sign-in is rejected, the app no longer asks for a manual percentage. It watches Claude Code's sign-in file and fetches again a few seconds after Claude Code writes a new sign-in. A rejected token is never sent again (it could disturb Claude Code's own sign-in), and the log now shows the server's reason.

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
