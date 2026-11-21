<#
    .SYNOPSIS
    Brings the lists of the application up to date from a copy of WinUtil.

    .DESCRIPTION
    The program carries the lists of programs and of changes of the WinUtil
    project, so that the names and the texts are the same there. This script
    takes a local copy of winutil.ps1 and writes the list files again with
    Extract-WinUtilData.ps1. Nothing is downloaded, so the script also works on a
    computer without a connection.

    A copy of WinUtil can be kept next to this repository, for example in the
    folder ..\winutil. The project is on GitHub:
    https://github.com/ChrisTitusTech/winutil

    .PARAMETER Script
    The local winutil.ps1. When it is not given, the script looks for it in the
    folder above the application, then in the reference folder ..\winutil.

    .PARAMETER Output
    The folder the lists are written into. It is the folder Resources of the
    application when nothing else is given.

    .EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Update-WinUtilData.ps1

    .EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Update-WinUtilData.ps1 -Script D:\winutil\winutil.ps1
#>
[CmdletBinding()]
param(
    [string] $Script,
    [string] $Output
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent

if ([string]::IsNullOrEmpty($Script)) {
    $candidates = @(
        (Join-Path $root 'winutil.ps1'),
        (Join-Path $root 'winutil\winutil.ps1')
    )

    $Script = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ([string]::IsNullOrEmpty($Script)) {
    throw "A local winutil.ps1 was not found. Put a copy of the project next to this repository or give -Script."
}

if (-not (Test-Path $Script)) {
    throw "The file $Script was not found."
}

if ([string]::IsNullOrEmpty($Output)) {
    $Output = Join-Path $root 'WinFreshUtil\Resources'
}

Write-Host "The lists are taken from the local copy $Script"

& (Join-Path $PSScriptRoot 'Extract-WinUtilData.ps1') -Script $Script -Output $Output

Write-Host "The lists of the WinUtil section are not touched by this script. They are the reviewed files in WinFreshUtil\Resources\winutil_data."
