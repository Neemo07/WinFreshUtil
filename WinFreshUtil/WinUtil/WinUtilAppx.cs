using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using WinFreshUtil.Services;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// Removes one of the Windows applications of the list. The package name comes out of
    /// the bundled definitions and is checked before it is written into the local script.
    /// </summary>
    public static class WinUtilAppx
    {
        private static readonly Regex PackagePattern =
            new Regex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant);

        public static bool Remove(WinUtilAppxEntry app, Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            if (app == null || !IsValidPackageId(app.PackageId))
            {
                error = "The package name is not valid.";
                return false;
            }

            string folder = Path.Combine(AppPaths.GetDataFolder(), "winutil");
            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, "appx-" + Guid.NewGuid().ToString("N") + ".ps1");

            try
            {
                StringBuilder script = new StringBuilder();
                script.AppendLine("$ErrorActionPreference = 'Continue'");
                script.AppendLine("$package = '" + app.PackageId.Replace("'", "''") + "'");
                script.AppendLine("Get-AppxPackage -AllUsers -Name $package -ErrorAction SilentlyContinue | ForEach-Object {");
                script.AppendLine("    Write-Host ('Removing ' + $_.PackageFullName)");
                script.AppendLine("    Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction SilentlyContinue");
                script.AppendLine("}");
                script.AppendLine("Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -eq $package } | ForEach-Object {");
                script.AppendLine("    Write-Host ('Removing the provisioned package ' + $_.PackageName)");
                script.AppendLine("    Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction SilentlyContinue | Out-Null");
                script.AppendLine("}");
                script.AppendLine("Write-Host 'DONE'");

                File.WriteAllText(path, script.ToString(), new UTF8Encoding(false));

                int exitCode = WinUtilProcess.RunPowerShell(
                    path,
                    delegate(string line) { Report(log, line, WinUtilLogLevel.Info); },
                    out error,
                    10 * 60 * 1000);

                if (exitCode != 0)
                {
                    if (string.IsNullOrEmpty(error))
                    {
                        error = "The application " + app.Content + " could not be removed (exit code " + exitCode + ").";
                    }

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "The application " + app.Content + " could not be removed. " + ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception)
                {
                    // A leftover script in the application folder does no harm.
                }
            }
        }

        /// <summary>
        /// True when the package name is a plain name that can be written into a local
        /// script without a way to break out of it.
        /// </summary>
        public static bool IsValidPackageId(string packageId)
        {
            return !string.IsNullOrEmpty(packageId) && PackagePattern.IsMatch(packageId);
        }

        private static void Report(Action<WinUtilLogLine> log, string text, WinUtilLogLevel level)
        {
            if (log != null && !string.IsNullOrEmpty(text))
            {
                log(new WinUtilLogLine(text, level));
            }
        }
    }
}
