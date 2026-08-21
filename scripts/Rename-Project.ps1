<#
.SYNOPSIS
    Renames the TechieBlog template to your custom project name.

.DESCRIPTION
    This script renames the main application from "TechieBlog" to your chosen name.
    It updates solution files, project files, namespaces, and folder names.
    The component libraries (BlogUI, BlogEngine, BlogModels, BlogDb) are kept as-is.

.PARAMETER NewName
    The new name for your blog application (e.g., "MyBlog", "DevNotes", "TechJournal")

.PARAMETER DryRun
    If specified, shows what would be changed without making actual changes.

.EXAMPLE
    .\Rename-Project.ps1 -NewName "MyBlog"

.EXAMPLE
    .\Rename-Project.ps1 -NewName "DevNotes" -DryRun

.NOTES
    Run this script from the repository root directory.
    Make sure to commit or backup your changes before running.
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-zA-Z][a-zA-Z0-9]*$')]
    [string]$NewName,

    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$OldName = "TechieBlog"

# Ensure we're in the right directory
if (-not (Test-Path "TechieBlog.slnx")) {
    Write-Error "Please run this script from the repository root directory (where TechieBlog.slnx is located)."
    exit 1
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  TechieBlog Template Rename Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "`nRenaming: $OldName -> $NewName" -ForegroundColor Yellow

if ($DryRun) {
    Write-Host "(DRY RUN - No changes will be made)`n" -ForegroundColor Magenta
}

# Track changes
$changes = @()

function Log-Change {
    param([string]$Action, [string]$Path)
    $script:changes += [PSCustomObject]@{Action = $Action; Path = $Path}
    Write-Host "  $Action : $Path" -ForegroundColor Gray
}

# Step 1: Update solution file content
Write-Host "`n[1/8] Updating solution file references..." -ForegroundColor Green
$slnContent = Get-Content "TechieBlog.slnx" -Raw
$newSlnContent = $slnContent -replace "source/$OldName/$OldName.csproj", "source/$NewName/$NewName.csproj"
$newSlnContent = $newSlnContent -replace "source/$OldName/", "source/$NewName/"

if ($slnContent -ne $newSlnContent) {
    Log-Change "UPDATE" "TechieBlog.slnx (project references)"
    if (-not $DryRun) {
        $newSlnContent | Set-Content "TechieBlog.slnx" -NoNewline
    }
}

# Step 2: Update .cs files in TechieBlog project (namespaces)
Write-Host "`n[2/8] Updating namespace references in source files..." -ForegroundColor Green
$csFiles = Get-ChildItem -Path "source/$OldName" -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue

foreach ($file in $csFiles) {
    $content = Get-Content $file.FullName -Raw
    $newContent = $content -replace "namespace $OldName", "namespace $NewName"
    $newContent = $newContent -replace "using $OldName", "using $NewName"

    if ($content -ne $newContent) {
        Log-Change "UPDATE" $file.FullName
        if (-not $DryRun) {
            $newContent | Set-Content $file.FullName -NoNewline
        }
    }
}

# Step 3: Update .razor files
Write-Host "`n[3/8] Updating Razor component references..." -ForegroundColor Green
$razorFiles = Get-ChildItem -Path "source/$OldName" -Filter "*.razor" -Recurse -ErrorAction SilentlyContinue

foreach ($file in $razorFiles) {
    $content = Get-Content $file.FullName -Raw
    $newContent = $content -replace "@namespace $OldName", "@namespace $NewName"
    $newContent = $newContent -replace "@using $OldName", "@using $NewName"

    if ($content -ne $newContent) {
        Log-Change "UPDATE" $file.FullName
        if (-not $DryRun) {
            $newContent | Set-Content $file.FullName -NoNewline
        }
    }
}

# Step 4: Update Program.cs
Write-Host "`n[4/8] Updating Program.cs..." -ForegroundColor Green
$programCs = "source/$OldName/Program.cs"

if (Test-Path $programCs) {
    $content = Get-Content $programCs -Raw
    $newContent = $content -replace "// $OldName Application", "// $NewName Application"
    $newContent = $newContent -replace "using $OldName\.", "using $NewName."
    $newContent = $newContent -replace "logs/$($OldName.ToLower())-", "logs/$($NewName.ToLower())-"
    $newContent = $newContent -replace "Starting $OldName application", "Starting $NewName application"
    $newContent = $newContent -replace "$OldName application shutting down", "$NewName application shutting down"
    $newContent = $newContent -replace "$OldName\.Components\.App", "$NewName.Components.App"

    if ($content -ne $newContent) {
        Log-Change "UPDATE" $programCs
        if (-not $DryRun) {
            $newContent | Set-Content $programCs -NoNewline
        }
    }
}

# Step 5: Update launchSettings.json (Visual Studio project dropdown name)
Write-Host "`n[5/8] Updating launchSettings.json..." -ForegroundColor Green
$launchSettings = "source/$OldName/Properties/launchSettings.json"

if (Test-Path $launchSettings) {
    $content = Get-Content $launchSettings -Raw
    # Update the profile name (the key in the profiles object)
    $newContent = $content -replace "`"$OldName`":", "`"$NewName`":"

    if ($content -ne $newContent) {
        Log-Change "UPDATE" $launchSettings
        if (-not $DryRun) {
            $newContent | Set-Content $launchSettings -NoNewline
        }
    }
}

# Step 6: Update appsettings and other config files
Write-Host "`n[6/8] Updating configuration files..." -ForegroundColor Green
$configFiles = @(
    "source/$OldName/appsettings.json",
    "source/$OldName/appsettings.Development.json",
    "source/$OldName/appsettings.Production.json"
)

foreach ($configPath in $configFiles) {
    if (Test-Path $configPath) {
        $content = Get-Content $configPath -Raw
        $newContent = $content -replace $OldName, $NewName

        if ($content -ne $newContent) {
            Log-Change "UPDATE" $configPath
            if (-not $DryRun) {
                $newContent | Set-Content $configPath -NoNewline
            }
        }
    }
}

# Step 7: Rename project file
Write-Host "`n[7/8] Renaming project file..." -ForegroundColor Green
$oldCsproj = "source/$OldName/$OldName.csproj"
$newCsproj = "source/$OldName/$NewName.csproj"

if (Test-Path $oldCsproj) {
    Log-Change "RENAME" "$oldCsproj -> $newCsproj"
    if (-not $DryRun) {
        Rename-Item -Path $oldCsproj -NewName "$NewName.csproj"
    }
}

# Step 8: Rename project folder and solution file
Write-Host "`n[8/8] Renaming folders and solution file..." -ForegroundColor Green

# Rename project folder
$oldFolder = "source/$OldName"
$newFolder = "source/$NewName"

if (Test-Path $oldFolder) {
    Log-Change "RENAME" "$oldFolder -> $newFolder"
    if (-not $DryRun) {
        Rename-Item -Path $oldFolder -NewName $NewName
    }
}

# Rename solution file
$oldSln = "$OldName.slnx"
$newSln = "$NewName.slnx"

if (Test-Path $oldSln) {
    Log-Change "RENAME" "$oldSln -> $newSln"
    if (-not $DryRun) {
        Rename-Item -Path $oldSln -NewName $newSln
    }
}

# Summary
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "`nTotal changes: $($changes.Count)" -ForegroundColor Yellow

if ($DryRun) {
    Write-Host "`nThis was a DRY RUN. No changes were made." -ForegroundColor Magenta
    Write-Host "Run without -DryRun to apply changes." -ForegroundColor Magenta
} else {
    Write-Host "`nRename complete!" -ForegroundColor Green
    Write-Host "`nNext steps:" -ForegroundColor Yellow
    Write-Host "  1. Open $NewName.slnx in your IDE"
    Write-Host "  2. Build to verify: dotnet build"
    Write-Host "  3. Run: dotnet run --project source/$NewName"
    Write-Host "  4. Delete this scripts folder if no longer needed"
}

Write-Host ""
