using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WinFreshUtil.Services;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// Runs the local PowerShell fragments of the WinUtil definitions. The fragments are
    /// bundled text, never fetched. The small helper functions that the original project
    /// provides are written at the top of the script.
    /// </summary>
    public static class WinUtilScripts
    {
        /// <summary>
        /// Runs the scripts of one entry. Returns true when every script finished with 0.
        /// </summary>
        public static bool Run(
            WinUtilEntry entry,
            bool undo,
            Action<string> onLine,
            out string error)
        {
            error = null;

            List<string> scripts = undo ? entry.UndoScripts : entry.InvokeScripts;
            if (scripts == null || scripts.Count == 0)
            {
                return true;
            }

            if (undo && string.Equals(entry.Key, "WPFTweaksRemoveEdge", StringComparison.Ordinal))
            {
                if (onLine != null)
                {
                    onLine("Offline build: " + (entry.UndoOfflineNote ?? "Microsoft Edge is not reinstalled."));
                }
            }

            if (undo && string.Equals(entry.Key, "WPFTweaksRemoveOneDrive", StringComparison.Ordinal))
            {
                if (onLine != null)
                {
                    onLine("Offline build: " + (entry.UndoOfflineNote ?? "Microsoft OneDrive is not reinstalled."));
                }
            }

            string scriptPath = null;

            try
            {
                scriptPath = WriteScript(entry, scripts);
                int exitCode = WinUtilProcess.RunPowerShell(scriptPath, onLine, out error, 30 * 60 * 1000);

                if (exitCode != 0)
                {
                    if (string.IsNullOrEmpty(error))
                    {
                        error = "The local script of " + entry.Key + " ended with exit code " + exitCode + ".";
                    }

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "The local script of " + entry.Key + " could not be run. " + ex.Message;
                return false;
            }
            finally
            {
                if (scriptPath != null)
                {
                    try
                    {
                        File.Delete(scriptPath);
                    }
                    catch (Exception)
                    {
                        // A leftover script in the application folder does no harm.
                    }
                }
            }
        }

        /// <summary>
        /// True when the entry has a script for the chosen direction.
        /// </summary>
        public static bool HasScript(WinUtilEntry entry, bool undo)
        {
            List<string> scripts = undo ? entry.UndoScripts : entry.InvokeScripts;
            return scripts != null && scripts.Count > 0;
        }

        private static string WriteScript(WinUtilEntry entry, List<string> scripts)
        {
            string folder = Path.Combine(AppPaths.GetDataFolder(), "winutil");
            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, "run-" + Guid.NewGuid().ToString("N") + ".ps1");

            StringBuilder text = new StringBuilder();
            text.AppendLine("$ErrorActionPreference = 'Stop'");
            text.AppendLine("$ProgressPreference = 'SilentlyContinue'");
            text.AppendLine("$sync = @{}");
            text.AppendLine(GetPrelude());

            foreach (string script in scripts)
            {
                foreach (string line in SafeLines(script))
                {
                    text.AppendLine(line);
                }
            }

            text.AppendLine("Write-Host 'DONE'");
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            return path;
        }

        private static IEnumerable<string> SafeLines(string script)
        {
            string[] lines = (script ?? string.Empty).Replace("\r\n", "\n").Split('\n');

            foreach (string line in lines)
            {
                // The source definitions are bundled and already checked. These guards make
                // sure that a later update cannot turn a local fragment into a download.
                string trimmed = line.Trim();

                if (trimmed.IndexOf("Invoke-WebRequest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("Invoke-RestMethod", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("DownloadString", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("DownloadFile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("Start-BitsTransfer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.StartsWith("winget ", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("choco ", StringComparison.OrdinalIgnoreCase))
                {
                    yield return "Write-Warning 'Offline build: a web command in this text was skipped.'";
                    continue;
                }

                yield return line;
            }
        }

        private static string GetPrelude()
        {
            return @"
function Write-WinUtilLog {
    param($Message, $Level, $Component)
    if ($null -ne $Message -and ([string]$Message).Length -gt 0) { Write-Host ([string]$Message) }
}

function Step-WinUtilJob {
    param($Status, $Percent, $State)
    if ($null -ne $Status -and ([string]$Status).Length -gt 0) { Write-Host ([string]$Status) }
}

function Invoke-WinUtilExplorerUpdate {
    param([string]$action = 'refresh')

    if ($action -eq 'restart') {
        taskkill.exe /F /IM explorer.exe 2>$null | Out-Null
        Start-Process explorer.exe
        return
    }

    if (-not ([System.Management.Automation.PSTypeName]'WinUtilBroadcast').Type) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class WinUtilBroadcast {
    [DllImport(""user32.dll"", CharSet = CharSet.Auto, SetLastError = false)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}
'@
    }

    $result = [IntPtr]::Zero
    [WinUtilBroadcast]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [IntPtr]::Zero, 'ImmersiveColorSet', 0x2, 100, [ref]$result) | Out-Null
}

function Invoke-WinutilThemeChange {
    param([string]$theme = 'Auto')
    # The WinUtil window is not part of this application. The Windows setting is already
    # changed by the registry entry of the tweak.
}

function Set-WinUtilClassicContextMenu {
    param([switch]$Undo)

    $clsidPath = 'HKCU:\Software\Classes\CLSID'
    $legacyPath = ""$clsidPath\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}""
    $workaroundPath = ""$clsidPath\{51668040-5e31-4023-bbe3-149163bd80f1}""
    $serverPath = ""$workaroundPath\InprocServer32""
    $markerName = 'WinUtilClassicContextMenu'

    $workaroundKey = $null
    try { $workaroundKey = Get-Item -LiteralPath $workaroundPath -ErrorAction Stop } catch [System.Management.Automation.ItemNotFoundException] { }
    $marker = if ($null -ne $workaroundKey) { $workaroundKey.GetValue($markerName) } else { $null }
    $ownsWorkaround = $marker -is [int] -and $marker -eq 1

    if ($Undo) {
        try { Remove-Item -LiteralPath $legacyPath -Recurse -Force -ErrorAction Stop } catch [System.Management.Automation.ItemNotFoundException] { }
        if ($ownsWorkaround) {
            try {
                if ($workaroundKey.ValueCount -gt 1 -or ($workaroundKey.GetSubKeyNames() | Where-Object { $_ -ne 'InprocServer32' })) {
                    throw ""The classic context menu registry key '$workaroundPath' has additional entries and was left intact.""
                }
                if ($workaroundKey.SubKeyCount -gt 0) {
                    $serverKey = Get-Item -LiteralPath $serverPath -ErrorAction Stop
                    if ($serverKey.SubKeyCount -gt 0 -or $serverKey.ValueCount -gt 1 -or ($serverKey.ValueCount -eq 1 -and ($serverKey.GetValueNames() -notcontains '' -or $serverKey.GetValueKind('') -ne [Microsoft.Win32.RegistryValueKind]::String -or $serverKey.GetValue('') -ne ''))) {
                        throw ""The classic context menu registry key '$serverPath' has been changed and was left intact.""
                    }
                }
                Remove-Item -LiteralPath $workaroundPath -Recurse -Force -ErrorAction Stop
            } catch [System.Management.Automation.ItemNotFoundException] { }
        }
    } else {
        New-Item -Path $legacyPath -Name InprocServer32 -Value '' -Force -ErrorAction Stop | Out-Null
        if ($null -eq $workaroundKey) {
            $workaroundKey = New-Item -Path $workaroundPath -ErrorAction Stop
            try { New-ItemProperty -LiteralPath $workaroundPath -Name $markerName -Value 1 -PropertyType DWord -ErrorAction Stop | Out-Null }
            catch {
                if ($workaroundKey.ValueCount -eq 0 -and $workaroundKey.SubKeyCount -eq 0) { Remove-Item -LiteralPath $workaroundPath -ErrorAction Stop }
                throw
            }
            $ownsWorkaround = $true
        }
        if ($ownsWorkaround -and -not (Test-Path -LiteralPath $serverPath -ErrorAction Stop)) {
            New-Item -Path $serverPath -Value '' -ErrorAction Stop | Out-Null
        } elseif ($ownsWorkaround) {
            $serverKey = Get-Item -LiteralPath $serverPath -ErrorAction Stop
            if ($serverKey.ValueCount -eq 0 -and $serverKey.SubKeyCount -eq 0) { Set-Item -LiteralPath $serverPath -Value '' -Force -ErrorAction Stop }
        }
    }

    $sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    Get-Process -Name explorer -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $sessionId } | ForEach-Object { Stop-Process -Id $_.Id -ErrorAction SilentlyContinue }
}
";
        }
    }
}
