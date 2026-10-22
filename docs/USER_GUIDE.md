# DeadLinks — User Guide

Uninstalled programs often leave their shortcuts behind: the desktop and Start menu fill up with entries that open an error. DeadLinks finds them, shows where each one pointed, and removes the ones you choose.

## What counts as dead
A shortcut (.lnk), an internet shortcut (.url) to a local file, or a symbolic link or junction whose **target no longer exists** on a drive or network share that is connected right now. Environment variables in a target (such as `%ProgramFiles%`) are expanded before the check, and a shortcut whose target was moved is followed if Windows can still find it.

## What is never ticked or removed on its own
| Listed as | Why | What DeadLinks does |
|---|---|---|
| **Drive offline** | The drive letter or network share is not connected now (USB drive, NAS). The shortcut may work again later. | Listed, never ticked. You can tick it yourself. |
| **Not checked** | Microsoft Store apps, system shortcuts, installer (on-demand) shortcuts, web addresses (DeadLinks never goes online), places it has no permission to look into, damaged shortcut files. | Shown only if you tick *Also list…*. Cannot be selected. |
| **Target exists** | Fine. | Shown only if you tick *Also list…*. |

## Scanning
The default locations are your Desktop, the public Desktop, your Start menu, the all-users Start menu and the taskbar pins; **Add folder…** adds any folder (all its sub-folders are searched). Symbolic links and junctions are listed but never followed out of the tree. Locations that need administrator rights can be scanned; removing there may fail with a clear message, and DeadLinks never asks for elevation.

## Removing
Press **Remove selected**, read the confirmation and press OK. Before anything is removed, DeadLinks saves a copy of each shortcut and a manifest. Then:
- **Shortcuts** (.lnk, .url) go to the Recycle Bin.
- **Symbolic links and junctions** are deleted (a dead link has nothing to recycle); the manifest keeps their target so they can be recreated. Only the link is removed, never anything it pointed to.
Each removal is added to `removal-log.csv`.

## History and Restore
**History** lists every removal batch. **Restore** puts the shortcuts back (and recreates links where Windows allows it). Anything that already exists again is left alone, never overwritten. **Open the log and backup folder** shows `%AppData%\DeadLinks`.

## Problems
Errors are logged to `%AppData%\DeadLinks\error.log`. Open an [issue on GitHub](https://github.com/md-anamul-hasan/deadlinks/issues) and attach that file (remove anything private first).
