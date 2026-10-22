<#
.SYNOPSIS
  Builds (or removes) the neutral demo fixture used for the README/website screenshots and manual tests.
.DESCRIPTION
  Everything lives in a throw-away folder that is mapped to a drive letter with `subst`, so the app only ever shows
  paths like X:\Desktop\... and never the real user name. Shortcuts point at realistic, harmless targets.
  The folder contains: working shortcuts, dead shortcuts (target deleted / never existed), a dead and a live junction,
  shortcuts to an offline drive, and web (.url) shortcuts. Nothing outside the fixture is touched.
.EXAMPLE
  powershell -File scripts/demo-fixture.ps1 -Create      # prints the root (X:\)
  powershell -File scripts/demo-fixture.ps1 -Remove
#>
param([switch]$Create, [switch]$Remove, [string]$Drive = 'X:', [string]$Backing = (Join-Path $env:TEMP 'deadlinks-demo-fixture'))

$ErrorActionPreference = 'Stop'

function Remove-Fixture {
  if (Test-Path "$Drive\") { cmd /c "subst $Drive /d" | Out-Null }
  if (Test-Path $Backing) { Remove-Item $Backing -Recurse -Force }
}

if ($Remove) { Remove-Fixture; "removed"; exit 0 }
if (-not $Create) { Write-Error 'Pass -Create or -Remove'; exit 1 }

if (Test-Path "$Drive\") { throw "$Drive is already in use; pass -Drive with a free letter" }
if (Test-Path "Z:\") { throw 'Z: exists; the offline-drive demo needs Z: to be absent' }
if (Test-Path $Backing) { Remove-Item $Backing -Recurse -Force }
New-Item -ItemType Directory -Force $Backing | Out-Null
cmd /c "subst $Drive `"$Backing`"" | Out-Null

$shell = New-Object -ComObject WScript.Shell
function New-Lnk($path, $target, $args_ = '', $desc = '') {
  New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
  $s = $shell.CreateShortcut($path); $s.TargetPath = $target; if ($args_) { $s.Arguments = $args_ }
  if ($desc) { $s.Description = $desc }; $s.Save()
}
function New-Url($path, $url) {
  New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
  "[InternetShortcut]`r`nURL=$url`r`n" | Set-Content -Path $path -Encoding ASCII
}
function New-File($path, $text = 'demo') { New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null; Set-Content -Path $path -Value $text }

$r = "$Drive\Demo"
# --- things that exist (shortcuts to these are fine) ---
New-File "$r\Apps\Notes\notes.exe"
New-File "$r\Apps\Terminal\term.exe"
New-File "$r\Documents\Project plan.docx"
New-File "$r\Documents\Budget 2026.xlsx"
New-Item -ItemType Directory -Force "$r\Projects\Current" | Out-Null

# --- working shortcuts ---
New-Lnk "$r\Desktop\Notes.lnk" "$r\Apps\Notes\notes.exe"
New-Lnk "$r\Desktop\Terminal.lnk" "$r\Apps\Terminal\term.exe"
New-Lnk "$r\Desktop\Project plan.lnk" "$r\Documents\Project plan.docx"
New-Lnk "$r\Desktop\Current projects.lnk" "$r\Projects\Current"

# --- dead shortcuts: apps that were uninstalled long ago ---
New-Lnk "$r\Desktop\Old CAD Viewer.lnk" 'C:\Program Files\Old CAD Viewer\viewer.exe'
New-Lnk "$r\Desktop\Photo Studio 2019.lnk" 'C:\Program Files (x86)\Photo Studio 2019\studio.exe'
New-Lnk "$r\Start Menu\Programs\Legacy Updater\Legacy Updater.lnk" "$r\Apps\Updater\update.exe"
New-Lnk "$r\Start Menu\Programs\Legacy Updater\Uninstall Legacy Updater.lnk" "$r\Apps\Updater\uninstall.exe"
New-Lnk "$r\Start Menu\Programs\Media Converter Pro.lnk" 'C:\Program Files\Media Converter Pro\convert.exe'
New-Lnk "$r\Desktop\Quarterly report.lnk" "$r\Documents\Q3 report.xlsx"
New-Url "$r\Desktop\Game Launcher.url" 'file:///C:/Games/Launcher/start.exe'

# --- shortcuts to an offline drive (Z: is not connected): listed, never ticked ---
New-Lnk "$r\Desktop\Backups (NAS).lnk" 'Z:\Backups\office-2026.zip'
New-Lnk "$r\Desktop\Photos archive.lnk" 'Z:\Photos\Archive'

# --- web shortcuts: cannot be checked, left alone ---
New-Url "$r\Desktop\Company portal.url" 'https://example.com/portal'
New-Url "$r\Desktop\Documentation.url" 'https://example.com/docs'

# --- junctions: one alive, one dead ---
New-Item -ItemType Directory -Force "$r\Shared\Live" | Out-Null
New-Item -ItemType Directory -Force "$r\Shared\Gone" | Out-Null
cmd /c "mklink /J `"$r\Desktop\Shared (live)`" `"$r\Shared\Live`"" | Out-Null
cmd /c "mklink /J `"$r\Desktop\Old share`" `"$r\Shared\Gone`"" | Out-Null
Remove-Item "$r\Shared\Gone" -Force

# --- targets that vanished after the shortcuts were made ---
foreach ($t in @("$r\Apps\Updater\update.exe", "$r\Apps\Updater\uninstall.exe", "$r\Documents\Q3 report.xlsx")) {
  New-File $t; $null = $t
}
Remove-Item "$r\Apps\Updater" -Recurse -Force
Remove-Item "$r\Documents\Q3 report.xlsx" -Force

"fixture ready at $r (backing folder: $Backing)"
