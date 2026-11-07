# Answers BlogApp's OS file-open dialog by UI Automation, scoped to the BlogApp process's own dialog
# window (never global keyboard/mouse input — verify-phase §3b). Run it right after the spec clicked
# the app's "choose" button; it waits for the dialog, types the path into the dialog's File name box
# through ValuePattern and presses the dialog's own Open button through InvokePattern.
#
#   powershell.exe -NoProfile -File blogapp-pick-file.ps1 -File C:\path\to\image.png [-TimeoutSeconds 40]
# Prints PICKED=<path> on success; exits 2 when no dialog appeared.
param([Parameter(Mandatory)][string]$File, [int]$TimeoutSeconds = 40)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$exe = (Resolve-Path (Join-Path $PSScriptRoot '..\..\source\BlogApp\bin\Debug\net10.0-windows10.0.19041.0\win-x64\BlogApp.exe')).Path
$appPid = (Get-Process BlogApp -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Select-Object -First 1).Id
if (-not $appPid) { Write-Error 'BlogApp is not running'; exit 3 }

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $appPid)
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$dialog = $null
while ((Get-Date) -lt $deadline -and -not $dialog) {
    $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)
    foreach ($w in $wins) {
        $cls = $w.Current.ClassName
        # The shell's common file dialog is a #32770 window; it is a top-level child of the desktop or of the app window.
        if ($cls -eq '#32770') { $dialog = $w; break }
        $inner = $w.FindFirst([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32770')))
        if ($inner) { $dialog = $inner; break }
    }
    if (-not $dialog) { Start-Sleep -Milliseconds 400 }
}
if (-not $dialog) { Write-Output 'NO_DIALOG'; exit 2 }
Write-Output ("DIALOG=" + $dialog.Current.Name)

# The common file dialog exposes its parts as legacy Win32 controls: the File name box is the Edit
# (AutomationId 1148, ClassName Edit) and the Open button is AutomationId 1 (ClassName Button).
function Find-Part($parent, [string]$id, [string]$class) {
    $and = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, $class)))
    for ($i = 0; $i -lt 20; $i++) {
        $e = $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $and)
        if ($e) { return $e }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

# Both parts are plain Win32 controls with their own window handles, so they are driven by messages
# addressed to THOSE handles (WM_SETTEXT, BM_CLICK) — still element-scoped, never global input.
Add-Type -Namespace Native -Name Win -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern System.IntPtr SendMessage(System.IntPtr hWnd, uint msg, System.IntPtr wParam, string lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern System.IntPtr SendMessage(System.IntPtr hWnd, uint msg, System.IntPtr wParam, System.IntPtr lParam);
'@

$edit = Find-Part $dialog '1148' 'Edit'
if (-not $edit) { Write-Output 'NO_EDIT'; exit 4 }
[void][Native.Win]::SendMessage([System.IntPtr]$edit.Current.NativeWindowHandle, 0x000C, [System.IntPtr]::Zero, $File)   # WM_SETTEXT

$open = Find-Part $dialog '1' 'Button'
if (-not $open) { Write-Output 'NO_OPEN_BUTTON'; exit 5 }
[void][Native.Win]::SendMessage([System.IntPtr]$open.Current.NativeWindowHandle, 0x00F5, [System.IntPtr]::Zero, [System.IntPtr]::Zero)   # BM_CLICK
Write-Output "PICKED=$File"
