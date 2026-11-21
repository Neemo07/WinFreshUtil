# WinFreshUtil

A small post-install utility for fresh Windows 10 and 11 computers. It is one
program file that does four things:

- **Drivers** – it looks for the drivers of the devices that Windows reports a
  problem for, and installs them. The driver is looked for in Windows Update
  first, then in the Microsoft Update Catalog, and the official tool of the
  manufacturer of the computer is offered when nothing is found.
- **WinUtil** – the changes of the WinUtil project, with the same names and the
  same texts as the original project: the tweaks, the Windows features, the
  built-in applications that can be removed, the presets, the name servers and
  the **RapidOS** button.
- **Install apps** – the list of programs of the WinUtil project, installed with
  the package manager of Windows or with Chocolatey.
- **Windows & Office** – the licenses of Windows and of Office, and the local
  activation scripts of the Microsoft Activation Scripts project, with a button
  that updates those scripts from GitHub.

Everything happens inside the program. No other window is opened.

The window follows the light or the dark theme of Windows; the button at the
bottom of the column switches it, and the choice is kept.

Most of it works without the internet. The lists of the projects, the activation
scripts and the sources of RapidOS are carried inside the program. Two parts
need a connection: **Drivers**, because the drivers come from Windows Update or
the Microsoft Update Catalog, and **Install apps**, because the programs come
from the package manager. The **Update MAS from GitHub** button also needs a
connection, but it is optional: activation itself runs from the local scripts.

## Building

The program is made with the .NET Framework 4.8 and WPF. It needs no library
from the internet: only the framework itself. .NET Framework 4.8 is built into
Windows 10 from version 1903 and into Windows 11, so nothing has to be installed
before the program runs.

```
dotnet build -c Release
```

The program is then in `WinFreshUtil\bin\Release\net48\WinFreshUtil.exe`.

## The checks

There is a small program with the checks of the parts that are easy to break: the
building of a command line, the checking of the files that the application and
the elevated instance use to talk to each other, the reading of the table of the
package manager, the reading of the percent of a line, the hash of the script of
the activation project, the lists of WinUtil and the building of every page with
the theme.

```
dotnet build -c Release
Tests\bin\Release\net48\WinFreshUtil.Tests.exe
```

It prints one line for every check and returns 0 when all of them passed.

## The folder layout

| | |
|---|---|
| `WinFreshUtil\App.xaml.cs` | the start of the program and the work of the elevated instance |
| `WinFreshUtil\Models` | the things that the pages show |
| `WinFreshUtil\Services` | the work: the drivers, the programs, the changes, the activation |
| `WinFreshUtil\Services\Driver` | the places where a driver can be found |
| `WinFreshUtil\Views` | the pages and the windows |
| `WinFreshUtil\Theme` | the colours, the controls and the switch of the theme |
| `WinFreshUtil\WinUtil` | the WinUtil section: the lists, the engine and the helper |
| `WinFreshUtil\Resources` | the lists of the original projects |
| `WinFreshUtil\Resources\winutil_data` | the WinUtil lists that the section reads |
| `WinFreshUtil\Resources\Payloads` | the scripts and the archive carried inside the program |
| `Tools` | the scripts that bring the lists up to date |

## The WinUtil page

The page reads its lists from `WinFreshUtil\Resources\winutil_data`. They are
embedded in the program, so nothing is fetched when the page is opened or when a
change is applied:

| File | What it holds |
|---|---|
| `tweaks.json` | the tweaks, with their registry values and their services |
| `features.json` | the Windows features and the buttons that open a window of Windows |
| `appx.json` | the built-in applications that can be removed |
| `dns.json` | the name servers, with the template for DNS over HTTPS |
| `presets.json` | the groups of changes |

A change is applied with the Win32 API for the registry and for the services,
with `dism.exe` for a Windows feature, and with a local script for the few
entries that the original project writes as PowerShell text. The scripts are
part of the lists; the engine refuses a line that would download something.

Entries that cannot work without the internet are left out of the lists, so the
page does not offer a change that would fail: O&O ShutUp10, the block of the
Adobe network, the install of a program with the package manager and the fixes
that need it. The name server choice and the two quick settings of the original
page are drawn by the page itself.

The changes of one run are picked first and applied together with **Apply
selected**, so a single prompt for administrator rights covers them all.

## The lists of the original projects

The lists of the programs and of the changes are taken from the original
projects, so that the names and the texts are the same there. They are written
again out of a local copy of the project, so the script needs no connection:

```
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Update-WinUtilData.ps1 -Script ..\winutil\winutil.ps1
```

When nothing is given, the script looks for `winutil.ps1` in the folder above the
application and then in `..\winutil`. The lists of the WinUtil page in
`Resources\winutil_data` are the reviewed files and are not touched by this
script; the script writes the four lists of the "Install apps" page into
`WinFreshUtil\Resources`.

The script writes the four files in `WinFreshUtil\Resources` byte for byte the
same as they are now, so that a change of the original project can be taken
over without anything else.

## The script of the activation project

The scripts of the Microsoft Activation Scripts project are carried inside the
application, in `WinFreshUtil\Resources\Payloads`, so that activation runs on a
computer without the internet. They are put in the folder of the application when
they are needed. Because they are run with administrator rights, two things are
done with them:

1. They are **not** taken from the moving branch of the project, but from a fixed
   commit, so that a change of the project does not change the scripts under the
   feet of the user.
2. Their **hashes** are checked before they are run, both when they are taken out
   of the application and again when the elevated instance is about to run them.
   A script that is not the one the application knows is refused.

The commit `ScriptHash.MasCommit` and the hashes of the four scripts are written
down in `WinFreshUtil\Services\ScriptHash.cs`.

The **Update MAS from GitHub** button on the Windows & Office page downloads the
latest scripts and stores them in the folder of the application. It writes the
commit and the hashes of the new scripts to `mas.manifest` in that folder, and the
hash check uses those hashes from then on. This is the only step of the activation
section that needs internet. A missing or changed script makes the application
forget the update and use the bundled copies again.

When the project publishes a new script, the files in
`WinFreshUtil\Resources\Payloads` and the hashes are written down again:

```
# the commit that changed the script last
$api = "https://api.github.com/repos/massgravel/Microsoft-Activation-Scripts/commits?path=MAS/All-In-One-Version-KL/MAS_AIO.cmd&per_page=1"
(Invoke-RestMethod $api)[0].sha

# the hash of the script of that commit
$url = "https://raw.githubusercontent.com/massgravel/Microsoft-Activation-Scripts/<commit>/MAS/All-In-One-Version-KL/MAS_AIO.cmd"
(Get-FileHash (New-TemporaryFile) -Algorithm SHA256)
```

## The administrator rights

Installing a driver and running the activation script need administrator
rights. The program starts itself a second time with the right rights and hands
the work over with a small file. Only an instance that really has those rights
takes that file, so a second window of the program cannot take the work away
from the instance that has them.

The work of the driver is done in a program with administrator rights, so
everything it does is checked before it is done: the place where the driver
comes from, the name of the driver and the id of it. The driver is never
handed over to a command interpreter, so that a value can never be read as a
second command.

## RapidOS

The **RapidOS** button on the WinUtil page applies the RapidOS modification of
Windows: it removes the programs that are not needed and changes the settings of
the privacy, the services, the security and the look of Windows.

The sources are taken from a fixed commit of the project
<https://github.com/rapid-community/RapidOS/>, so that a change of the project
does not change what is applied to the computer without the user seeing it, and
the hash of the archive is checked before it is used. The archive is carried
inside the application, in `WinFreshUtil\Resources\Payloads`, so that no
connection is needed. The commit and the hash are written down in
`WinFreshUtil\Services\RapidOsService.cs`.

The project is made for Windows 10 22H2 and Windows 11 22H2 up to 25H2. The
button is greyed out on other versions of Windows.

The work needs administrator rights, and the helper scripts of the project are
put in the folder `RapidScripts` of the Windows folder, which is where the
project expects them.

## The company

Copyright (c) 2026 Nemo07.

## License

This project is released under the MIT License. See [LICENSE](LICENSE).

