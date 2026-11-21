using System;
using System.Collections.Generic;
using System.Globalization;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// Reads the current Windows state so a toggle can show whether its change is on.
    /// </summary>
    public static class WinUtilState
    {
        public static bool IsApplied(WinUtilEntry entry)
        {
            if (entry == null || entry.Registry.Count == 0)
            {
                return false;
            }

            foreach (WinUtilRegistryChange change in entry.Registry)
            {
                if (change.HasChoices)
                {
                    if (FindComboChoice(change) == null)
                    {
                        return false;
                    }

                    continue;
                }

                if (WinUtilRegistry.IsRemove(change.Value))
                {
                    object ignored;
                    if (WinUtilRegistry.TryRead(change, out ignored))
                    {
                        return false;
                    }

                    continue;
                }

                string current = EffectiveValue(change);
                if (!string.Equals(current, change.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The choice of a combo box that matches the current registry values, or null.
        /// </summary>
        public static string FindComboChoice(WinUtilEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            foreach (WinUtilRegistryChange change in entry.Registry)
            {
                if (change.HasChoices)
                {
                    return FindComboChoice(change);
                }
            }

            return null;
        }

        private static string FindComboChoice(WinUtilRegistryChange change)
        {
            if (change == null || !change.HasChoices)
            {
                return null;
            }

            string current = EffectiveValue(change);

            foreach (KeyValuePair<string, string> choice in change.Values)
            {
                string expected = WinUtilRegistry.IsRemove(choice.Value)
                    ? (change.DefaultValue ?? string.Empty)
                    : choice.Value;

                if (string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return choice.Key;
                }
            }

            return null;
        }

        private static string EffectiveValue(WinUtilRegistryChange change)
        {
            object value;
            if (WinUtilRegistry.TryRead(change, out value))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            if (!string.IsNullOrEmpty(change.DefaultValue))
            {
                return change.DefaultValue;
            }

            if (string.Equals(change.DefaultState, "true", StringComparison.OrdinalIgnoreCase))
            {
                return change.Value ?? string.Empty;
            }

            if (string.Equals(change.DefaultState, "false", StringComparison.OrdinalIgnoreCase))
            {
                return change.OriginalValue ?? string.Empty;
            }

            return string.Empty;
        }
    }
}
