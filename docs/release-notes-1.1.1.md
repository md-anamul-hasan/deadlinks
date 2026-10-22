## DeadLinks 1.1.1

![DeadLinks](https://raw.githubusercontent.com/md-anamul-hasan/deadlinks/main/docs/media/screenshot-dark.png)

### Fixed
- **Light theme:** the text on the primary buttons (Scan, Remove selected) was dark on the blue accent and is now white.

DeadLinks is free and open source (MIT): no licence key, no activation, no network connections.

### Files
| File | Purpose |
|---|---|
| `DeadLinks-Setup-1.1.1.exe` | Installer (self-contained, no .NET install needed) |
| `SHA256SUMS.txt` | Checksum for the file above |

Verify a download: `Get-FileHash .\DeadLinks-Setup-1.1.1.exe -Algorithm SHA256` and compare with `SHA256SUMS.txt`.

Builds are not code-signed yet, so Windows SmartScreen may warn about an unknown publisher.

Full list of changes: [CHANGELOG.md](https://github.com/md-anamul-hasan/deadlinks/blob/main/CHANGELOG.md).
