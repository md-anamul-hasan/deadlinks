<#
.SYNOPSIS
  Captures the real DeadLinks window (light and dark) for the README and the website by driving the published exe with
  UI Automation against a neutral demo fixture (scripts/demo-fixture.ps1). Every image is the app's own pixels.
  Needs an unlocked desktop. Your real shortcuts are never scanned or removed; %APPDATA%\DeadLinks is restored.
.EXAMPLE
  dotnet publish DeadLinks.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist/stage
  pwsh -File scripts/capture-media.ps1
#>
param(
  [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\media\app'),
  [int]$Width = 1280, [int]$Height = 800
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
public static class Mouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  public static void Wheel(int x, int y, int notches) { SetCursorPos(x, y); mouse_event(0x0800, 0, 0, unchecked((uint)(notches * 120)), UIntPtr.Zero); }
  public static void Click(int x, int y) { SetCursorPos(x, y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
}
"@
[void][Win]::SetProcessDPIAware()
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'dist\stage\DeadLinks.exe'
$fixture = Join-Path $PSScriptRoot 'demo-fixture.ps1'
$dataDir = Join-Path $env:APPDATA 'DeadLinks'
if (-not (Test-Path $exe)) { throw "Publish first: dotnet publish DeadLinks.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist/stage" }
New-Item -ItemType Directory -Force $Out | Out-Null
Get-ChildItem $Out -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force   # every shot is regenerated

$dataExisted = Test-Path $dataDir
$before = if ($dataExisted) { Get-ChildItem $dataDir -Recurse -Force | ForEach-Object FullName } else { @() }
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$ok, [string]$what) { $checks.Add(("{0} {1}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $what)); if (-not $ok) { throw "check failed: $what" } }

function Snap($proc, [string]$name, [switch]$Screen) {
  Start-Sleep -Milliseconds 700
  $h = $proc.MainWindowHandle
  $r = New-Object Win+RECT; [void][Win]::GetWindowRect($h, [ref]$r)
  $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  if ($Screen) {   # real screen pixels: includes the separate message-box window
    [void][Win]::SetWindowPos($h, [IntPtr](-1), $r.L, $r.T, 0, 0, 0x0001 -bor 0x0040)   # topmost, no move/size
    Start-Sleep -Milliseconds 300
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    [void][Win]::SetWindowPos($h, [IntPtr](-2), $r.L, $r.T, 0, 0, 0x0001 -bor 0x0040)
  } else {
    $dc = $g.GetHdc(); [void][Win]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc)
  }
  $g.Dispose()
  $file = Join-Path $Out "$name.png"; $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  "captured $name"
}
function Start-App([string]$folder) {
  $p = Start-Process $exe -ArgumentList $folder -PassThru
  for ($i = 0; $i -lt 40 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 500; $p.Refresh() }
  [void][Win]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 60, 20, $Width, $Height, 0x0040)
  Start-Sleep -Seconds 6   # first paint and scan of the fixture
  $p
}
function Win-Root($p) { $AE::FromHandle($p.MainWindowHandle) }
function Find-ByName($scope, [string]$name, $type = $null) {
  $all = $scope.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  foreach ($e in $all) { if ($e.Current.Name -like $name -and (-not $type -or $e.Current.ControlType.ProgrammaticName -like "*$type")) { return $e } }
  throw "UI element '$name' not found"
}
function Click($e) {
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  for ($node = $e; $node -and $node -ne $AE::RootElement; $node = $walker.GetParent($node)) {
    $inv = $null; $sel = $null; $tog = $null
    if ($node.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); return }
    if ($node.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { $sel.Select(); return }
    if ($node.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tog)) { $tog.Toggle(); return }
    if ($node.Current.ControlType.ProgrammaticName -like '*Window') { break }
  }
  $r = $e.Current.BoundingRectangle
  [Mouse]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)); Start-Sleep -Milliseconds 300
}
function Click-Mouse($e) {   # real mouse click: UI Automation Invoke would block while a modal MessageBox is open
  $r = $e.Current.BoundingRectangle
  [Mouse]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)); Start-Sleep -Milliseconds 300
}
function Nav($p, [int]$index) {   # sidebar order: Scan, History, How it works
  $radios = (Win-Root $p).FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)))
  Click $radios[$index]; Start-Sleep -Milliseconds 700
}
function Scroll-Page($p, [int]$notches) {   # negative = down; real wheel events over the page content
  $r = New-Object Win+RECT; [void][Win]::GetWindowRect($p.MainWindowHandle, [ref]$r)
  $step = if ($notches -lt 0) { -1 } else { 1 }
  for ($i = 0; $i -lt [math]::Abs($notches); $i++) { [Mouse]::Wheel($r.L + 800, $r.T + 560, $step); Start-Sleep -Milliseconds 40 }
  Start-Sleep -Milliseconds 500
}
function Drop-Focus($p) {   # click the empty title bar so no control shows a keyboard-focus outline
  $r = New-Object Win+RECT; [void][Win]::GetWindowRect($p.MainWindowHandle, [ref]$r)
  [Mouse]::Click($r.L + 900, $r.T + 18); Start-Sleep -Milliseconds 300
}
function Stop-App($p) { if (-not $p.HasExited) { [void]$p.CloseMainWindow(); Start-Sleep 1; if (-not $p.HasExited) { $p.Kill() } } }
function Message-Box($p) {   # the app's confirmation MessageBox: an owned #32770 window, listed under its owner in UI Automation
  for ($i = 0; $i -lt 30; $i++) {
    $scopes = @($AE::RootElement) + @(Win-Root $p)
    foreach ($scope in $scopes) {
      foreach ($w in $scope.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition ($AE::ClassNameProperty, '#32770')))) {
        if ($w.Current.ProcessId -eq $p.Id) { return $w }
      }
    }
    Start-Sleep -Milliseconds 300
  }
  throw 'confirmation dialog did not appear'
}

try {
  $ps = { param($a) & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fixture $a | Out-Null }
  $deadFiles = 'Old CAD Viewer.lnk', 'Photo Studio 2019.lnk', 'Quarterly report.lnk', 'Game Launcher.url'
  foreach ($theme in 'light', 'dark') {
    & $ps '-Remove'; & $ps '-Create'
    $p = Start-App 'X:\Demo'
    $want = (Get-Culture).TextInfo.ToTitleCase($theme)
    for ($i = 0; $i -lt 3 -and (Find-ByName (Win-Root $p) 'Theme:*').Current.Name -notlike "*$want*"; $i++) { Click (Find-ByName (Win-Root $p) 'Theme:*'); Start-Sleep -Milliseconds 700 }
    Drop-Focus $p; Snap $p "scan-results-$theme"
    Scroll-Page $p -7; Drop-Focus $p; Snap $p "scan-list-$theme"
    Scroll-Page $p 20
    foreach ($f in $deadFiles) { Check (Test-Path "X:\Demo\Desktop\$f") "before removal: $f exists" }
    Click-Mouse (Find-ByName (Win-Root $p) 'Remove selected' 'Button')
    $box = Message-Box $p
    Snap $p "remove-confirm-$theme" -Screen
    [void][Win]::SetForegroundWindow([IntPtr]$box.Current.NativeWindowHandle); Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Seconds 3
    foreach ($f in $deadFiles) { Check (-not (Test-Path "X:\Demo\Desktop\$f")) "after removal: $f is gone" }
    Check (Test-Path 'X:\Demo\Desktop\Notes.lnk') 'a working shortcut is untouched'
    Check (Test-Path 'X:\Demo\Desktop\Backups (NAS).lnk') 'an offline-drive shortcut is untouched'
    Scroll-Page $p 20; Drop-Focus $p; Snap $p "removed-$theme"
    Nav $p 1; Drop-Focus $p; Snap $p "history-$theme"
    Click (Find-ByName (Win-Root $p) 'Restore' 'Button'); Start-Sleep -Seconds 2
    foreach ($f in $deadFiles) { Check (Test-Path "X:\Demo\Desktop\$f") "after restore: $f is back" }
    Nav $p 2; Drop-Focus $p; Snap $p "how-it-works-$theme"
    Stop-App $p
  }
  $checks | ForEach-Object { $_ }
}
finally {
  Get-Process DeadLinks -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch {} }
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fixture -Remove | Out-Null
  if (Test-Path $dataDir) {
    Get-ChildItem $dataDir -Recurse -Force | Sort-Object FullName -Descending | Where-Object { $before -notcontains $_.FullName } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    if (-not $dataExisted) { Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue }
  }
  "fixture removed, app data restored"
}
