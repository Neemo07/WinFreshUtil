using System;
using System.Collections.Generic;
using System.Globalization;
using WinFreshUtil.Services;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// Applies the local WinUtil definitions. Registry and service changes are done with
    /// Win32 APIs; only the definitions that the original project writes as PowerShell text
    /// run as a local PowerShell script. Nothing is downloaded.
    /// </summary>
    public static class WinUtilEngine
    {
        /// <summary>
        /// Runs the picked actions. This method is meant for a background thread.
        /// </summary>
        public static WinUtilRunResult Run(WinUtilSelection selection, Action<WinUtilLogLine> log)
        {
            WinUtilRunResult result = new WinUtilRunResult
            {
                Success = true,
                Message = "Nothing was picked."
            };

            if (selection == null || selection.IsEmpty)
            {
                return result;
            }

            WinUtilCatalog catalog = WinUtilCatalogLoader.Load();
            int completed = 0;
            int failed = 0;

            List<WinUtilAction> actions = OrderActions(catalog, selection.Actions);

            foreach (WinUtilAction action in actions)
            {
                if (action == null || string.IsNullOrEmpty(action.Key))
                {
                    continue;
                }

                WinUtilEntry entry = catalog.FindEntry(action.Key);
                if (entry == null)
                {
                    Report(log, "The entry " + action.Key + " is not part of the local definitions.", WinUtilLogLevel.Error);
                    failed++;
                    continue;
                }

                Report(log, (action.Undo ? "Undo: " : "Apply: ") + entry.ButtonText, WinUtilLogLevel.Info);

                string error;
                bool ok = action.Undo ? Undo(entry, log, out error) : Apply(entry, action.Choice, log, out error);

                if (ok)
                {
                    completed++;
                    Report(log, "Finished: " + entry.ButtonText, WinUtilLogLevel.Success);
                }
                else
                {
                    failed++;
                    Report(log, "Failed: " + entry.ButtonText + ". " + error, WinUtilLogLevel.Error);
                }
            }

            if (selection.AppxKeys != null)
            {
                foreach (string key in selection.AppxKeys)
                {
                    WinUtilAppxEntry app = catalog.FindApp(key);
                    if (app == null)
                    {
                        Report(log, "The application " + key + " is not part of the local definitions.", WinUtilLogLevel.Error);
                        failed++;
                        continue;
                    }

                    Report(log, "Remove application: " + app.Content, WinUtilLogLevel.Info);

                    string appError;
                    if (WinUtilAppx.Remove(app, log, out appError))
                    {
                        completed++;
                        Report(log, "Removed: " + app.Content, WinUtilLogLevel.Success);
                    }
                    else
                    {
                        failed++;
                        Report(log, "Failed: " + app.Content + ". " + appError, WinUtilLogLevel.Error);
                    }
                }
            }

            if (!string.IsNullOrEmpty(selection.DnsProvider) &&
                !string.Equals(selection.DnsProvider, "Default", StringComparison.OrdinalIgnoreCase))
            {
                string dnsError;
                if (WinUtilDns.Set(selection.DnsProvider, log, out dnsError))
                {
                    completed++;
                    Report(log, "The name servers are set to " + selection.DnsProvider + ".", WinUtilLogLevel.Success);
                }
                else
                {
                    failed++;
                    Report(log, "The name servers could not be changed. " + dnsError, WinUtilLogLevel.Error);
                }
            }

            result.Completed = completed;
            result.Failed = failed;
            result.Success = failed == 0;
            result.Message = failed == 0
                ? completed.ToString(CultureInfo.InvariantCulture) + " change(s) finished."
                : completed.ToString(CultureInfo.InvariantCulture) + " change(s) finished, " +
                  failed.ToString(CultureInfo.InvariantCulture) + " failed.";

            return result;
        }

        /// <summary>
        /// Applies one entry. This is used by the toggle switches, which apply at once.
        /// </summary>
        public static bool Apply(WinUtilEntry entry, string choice, Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            if (entry == null)
            {
                error = "The entry is empty.";
                return false;
            }

            // A button that only opens a window of Windows must not run the local script of
            // the definition as well, because that script starts the same window again.
            if (WinUtilActions.IsPanel(entry))
            {
                return WinUtilActions.Run(entry, log, out error);
            }

            bool ok = true;

            foreach (WinUtilRegistryChange change in entry.Registry)
            {
                try
                {
                    string value = ChooseValue(change, choice, false);
                    WinUtilRegistry.Apply(change, value);
                    Report(log, "Registry: " + change.Path + "\\" + change.Name + " = " + Describe(value), WinUtilLogLevel.Info);
                }
                catch (Exception ex)
                {
                    ok = false;
                    error = JoinError(error, ex.Message);
                    Report(log, "Registry: " + change.Path + "\\" + change.Name + " failed. " + ex.Message, WinUtilLogLevel.Error);
                }
            }

            foreach (WinUtilServiceChange change in entry.Services)
            {
                if (string.IsNullOrEmpty(change.Name) || string.IsNullOrEmpty(change.StartupType))
                {
                    continue;
                }

                string expected = change.OriginalType;
                string current = WinUtilServices.ReadStartupType(change.Name);

                if (string.Equals(current, "Missing", StringComparison.OrdinalIgnoreCase))
                {
                    Report(log, "Service " + change.Name + " does not exist on this computer; it was left alone.", WinUtilLogLevel.Warning);
                    continue;
                }

                if (!string.IsNullOrEmpty(expected) &&
                    !string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
                {
                    Report(log, "Service " + change.Name + " is " + current + ", not " + expected +
                               " as this change expects; it was left alone.", WinUtilLogLevel.Warning);
                    continue;
                }

                string serviceError;
                if (WinUtilServices.SetStartupType(change.Name, change.StartupType, out serviceError))
                {
                    Report(log, "Service " + change.Name + " is set to " + change.StartupType + ".", WinUtilLogLevel.Info);
                }
                else
                {
                    ok = false;
                    error = JoinError(error, serviceError);
                    Report(log, "Service " + change.Name + " failed. " + serviceError, WinUtilLogLevel.Error);
                }
            }

            if (entry.IsFeature && entry.FeatureNames.Count > 0)
            {
                foreach (string feature in entry.FeatureNames)
                {
                    if (!EnableFeature(feature, log, out error))
                    {
                        ok = false;
                    }
                }

                if (!ok)
                {
                    // The original project skips the dependent scripts after a feature failed.
                    return false;
                }
            }

            if (WinUtilScripts.HasScript(entry, false))
            {
                string scriptError;
                if (!WinUtilScripts.Run(entry, false, delegate(string line) { Report(log, line, WinUtilLogLevel.Info); }, out scriptError))
                {
                    ok = false;
                    error = JoinError(error, scriptError);
                }
            }

            if (entry.IsFeature && entry.Kind == WinUtilEntryKind.Button && entry.FeatureNames.Count == 0 &&
                !WinUtilScripts.HasScript(entry, false))
            {
                return WinUtilActions.Run(entry, log, out error);
            }

            if (entry.Kind == WinUtilEntryKind.Button && !WinUtilScripts.HasScript(entry, false) &&
                entry.Registry.Count == 0 && entry.Services.Count == 0 && !entry.IsFeature)
            {
                return WinUtilActions.Run(entry, log, out error);
            }

            return ok;
        }

        /// <summary>
        /// Puts one entry back.
        /// </summary>
        public static bool Undo(WinUtilEntry entry, Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            if (entry == null)
            {
                error = "The entry is empty.";
                return false;
            }

            bool ok = true;

            foreach (WinUtilRegistryChange change in entry.Registry)
            {
                try
                {
                    string value = ChooseValue(change, null, true);
                    WinUtilRegistry.Apply(change, value);
                    Report(log, "Registry: " + change.Path + "\\" + change.Name + " = " + Describe(value), WinUtilLogLevel.Info);
                }
                catch (Exception ex)
                {
                    ok = false;
                    error = JoinError(error, ex.Message);
                    Report(log, "Registry: " + change.Path + "\\" + change.Name + " failed. " + ex.Message, WinUtilLogLevel.Error);
                }
            }

            foreach (WinUtilServiceChange change in entry.Services)
            {
                if (string.IsNullOrEmpty(change.Name) || string.IsNullOrEmpty(change.OriginalType))
                {
                    continue;
                }

                string current = WinUtilServices.ReadStartupType(change.Name);
                if (string.Equals(current, "Missing", StringComparison.OrdinalIgnoreCase))
                {
                    Report(log, "Service " + change.Name + " does not exist on this computer; it was left alone.", WinUtilLogLevel.Warning);
                    continue;
                }

                if (!string.Equals(current, change.StartupType, StringComparison.OrdinalIgnoreCase))
                {
                    Report(log, "Service " + change.Name + " is " + current + ", not " + change.StartupType +
                               " as this change expects; it was left alone.", WinUtilLogLevel.Warning);
                    continue;
                }

                string serviceError;
                if (WinUtilServices.SetStartupType(change.Name, change.OriginalType, out serviceError))
                {
                    Report(log, "Service " + change.Name + " is set back to " + change.OriginalType + ".", WinUtilLogLevel.Info);
                }
                else
                {
                    ok = false;
                    error = JoinError(error, serviceError);
                    Report(log, "Service " + change.Name + " failed. " + serviceError, WinUtilLogLevel.Error);
                }
            }

            if (WinUtilScripts.HasScript(entry, true))
            {
                string scriptError;
                if (!WinUtilScripts.Run(entry, true, delegate(string line) { Report(log, line, WinUtilLogLevel.Info); }, out scriptError))
                {
                    ok = false;
                    error = JoinError(error, scriptError);
                }
            }

            return ok;
        }

        private static bool EnableFeature(string feature, Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(feature))
            {
                return true;
            }

            Report(log, "Windows feature: " + feature, WinUtilLogLevel.Info);

            string arguments = "/Online /Enable-Feature /FeatureName:" + feature + " /All /NoRestart";
            int exitCode = WinUtilProcess.Run(
                "dism.exe",
                arguments,
                delegate(string line) { Report(log, line, WinUtilLogLevel.Info); },
                out error,
                60 * 60 * 1000);

            if (exitCode == 0 || exitCode == 3010)
            {
                return true;
            }

            if (string.IsNullOrEmpty(error))
            {
                error = "The feature " + feature + " could not be enabled (exit code " + exitCode + ").";
            }

            Report(log, error, WinUtilLogLevel.Error);
            return false;
        }

        private static string ChooseValue(WinUtilRegistryChange change, string choice, bool undo)
        {
            if (change.HasChoices && !undo)
            {
                string value;
                if (!string.IsNullOrEmpty(choice) && change.Values.TryGetValue(choice, out value))
                {
                    return value;
                }

                return change.DefaultValue;
            }

            return undo ? change.OriginalValue : change.Value;
        }

        private static List<WinUtilAction> OrderActions(WinUtilCatalog catalog, List<WinUtilAction> actions)
        {
            List<WinUtilAction> ordered = new List<WinUtilAction>();

            if (actions == null)
            {
                return ordered;
            }

            // The restore point must be made before the other changes.
            foreach (WinUtilAction action in actions)
            {
                if (action != null && string.Equals(action.Key, "WPFTweaksRestorePoint", StringComparison.Ordinal))
                {
                    ordered.Add(action);
                }
            }

            foreach (WinUtilAction action in actions)
            {
                if (action != null && !string.Equals(action.Key, "WPFTweaksRestorePoint", StringComparison.Ordinal))
                {
                    ordered.Add(action);
                }
            }

            return ordered;
        }

        private static string Describe(string value)
        {
            if (WinUtilRegistry.IsRemove(value))
            {
                return "<removed>";
            }

            return value;
        }

        private static string JoinError(string current, string next)
        {
            if (string.IsNullOrEmpty(current))
            {
                return next;
            }

            if (string.IsNullOrEmpty(next))
            {
                return current;
            }

            return current + " " + next;
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
