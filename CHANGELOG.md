# Changelog

Changes in this fork relative to upstream YourBuddy 1.0.9.

## Unreleased

### Added
- Status command and button, including group status with a name for each buddy.
- State-aware greetings replace the fixed greeting, with fear taking priority over routine work.
- Fetch suit, Oxygen on and Climate on conversation shortcuts using the existing task implementations.
- Build-and-install helper for updating the local game plugins.
- Automated command and status regression checks.

### Fixed
- Keywords embedded inside unrelated words no longer issue commands.
- Requests containing recognized negation words ask for a positive instruction instead of executing an action.
- “Stop following” and “stop moving” request Stay.
- “Come and tidy” and “come and eat” start the requested task instead of Follow.

### Changed
- Replies use player-facing language instead of navigation-debug details.
- Follow starts beyond 2.4 metres and stops at 1.8 metres, increasing the gap between starting and stopping.

### Removed
- No existing mod features removed.
