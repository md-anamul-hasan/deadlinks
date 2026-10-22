# Changelog

All notable changes to DeadLinks. Versioning follows [Semantic Versioning](https://semver.org/); the version lives in `Directory.Build.props`.

## [1.1.0] - 2026-10-22

First open-source release (MIT). Earlier builds were a paid product; this release removes licensing entirely, so every feature is free.

### Added
- Finds dead shortcuts (.lnk), internet shortcuts (.url) to local files, and symbolic links and junctions on the desktop, Start menu, taskbar pins and any folder you add.
- Review-first removal with Recycle Bin, backup copy, removal log and a History page with Restore.
- Offline drives and non-checkable shortcuts are never ticked or removed on their own.
- Light and dark themes; `DeadLinks.exe <folder>` command line.
- Self-contained single-file exe (.NET 10 included).

### Removed
- Licence-key activation, the licence dialog, evaluation limits and the licence-server connection. Everything is unlocked.
