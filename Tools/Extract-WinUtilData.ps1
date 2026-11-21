<#
    .SYNOPSIS
    Takes the lists of programs and of changes out of the winutil.ps1 of the
    original project and writes them into the folder Resources of the
    application.

    .DESCRIPTION
    The application carries the lists of the original project, so that the names
    and the texts are the same there and do not have to be asked for every time
    the application is opened. This script writes those lists again, so that the
    application can be brought up to date when the original project changes.

    The script is run with the winutil.ps1 of the original project next to it:

        powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Extract-WinUtilData.ps1 -Script ..\winutil\winutil.ps1

    .PARAMETER Script
    The winutil.ps1 of the original project. When it is not given, the script
    looks for it in the folder above the application, then in ..\winutil.

    .PARAMETER Output
    The folder the lists are written into. It is the folder Resources of the
    application when nothing else is given.
#>
[CmdletBinding()]
param(
    [string] $Script,
    [string] $Output
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($Script)) {
    $root = Split-Path $PSScriptRoot -Parent

    $candidates = @(
        (Join-Path $root 'winutil.ps1'),
        (Join-Path $root 'winutil\winutil.ps1')
    )

    $Script = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ([string]::IsNullOrEmpty($Script)) {
        $Script = Join-Path $root 'winutil.ps1'
    }
}

if ([string]::IsNullOrEmpty($Output)) {
    $Output = Join-Path (Split-Path $PSScriptRoot -Parent) 'WinFreshUtil\Resources'
}

if (-not (Test-Path $Script)) {
    throw "The script $Script was not found. Give the path of the winutil.ps1 of the original project."
}

$lines = Get-Content $Script

function Get-Block([string] $Name) {
    <#
        Takes the text between the line with the name and the line that ends it.
        The name is a line such as: $sync.configs.applications = @'
    #>
    $start = ($lines | Select-String -Pattern ('^\$sync\.configs\.' + [regex]::Escape($Name) + '\s*=\s*@''\s*$') |
        Select-Object -First 1).LineNumber

    if (-not $start) {
        throw "The block $Name was not found in the script."
    }

    $end = $null

    for ($i = $start; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "^'@") {
            $end = $i
            break
        }
    }

    if (-not $end) {
        throw "The block $Name in the script has no end."
    }

    $text = ($lines[$start..($end - 1)] -join "`n")

    # The first line holds the name of the block, it is not part of the text.
    $text = $text -replace "^@'\r?\n", ''

    return $text
}

function Write-Block([string] $Name, [string] $FileName) {
    $text = Get-Block $Name

    # The text has to be JSON, otherwise a mistake in the script is written into
    # the application.
    $null = $text | ConvertFrom-Json

    $path = Join-Path $Output $FileName

    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $false))

    $parsed = $text | ConvertFrom-Json
    $count = @($parsed.PSObject.Properties).Count

    Write-Host "   $FileName : $count entries"
}

if (-not (Test-Path $Output)) {
    New-Item -ItemType Directory -Force -Path $Output | Out-Null
}

Write-Host "The lists are taken out of $Script"

Write-Block 'applications' 'winutil-apps.json'
Write-Block 'tweaks' 'mas-tweaks.json'
Write-Block 'appx' 'mas-appx.json'
Write-Block 'dns' 'mas-dns.json'

Write-Host "They are written into $Output"
