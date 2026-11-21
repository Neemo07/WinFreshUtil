using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// Reads and writes the registry entries of the WinUtil definitions without a command
    /// interpreter. The 64-bit view is used, which is the view the original project sees.
    /// </summary>
    public static class WinUtilRegistry
    {
        public const string RemoveEntry = "<RemoveEntry>";

        /// <summary>
        /// Sets one value, or removes it when the definition says so.
        /// </summary>
        public static void Apply(WinUtilRegistryChange change, string value)
        {
            if (change == null)
            {
                throw new ArgumentNullException("change");
            }

            if (string.IsNullOrEmpty(change.Path) || string.IsNullOrEmpty(change.Name))
            {
                throw new InvalidOperationException("The registry entry has no path or no name.");
            }

            RegistryHive hive;
            string subKey;
            ParsePath(change.Path, out hive, out subKey);

            if (IsRemove(value))
            {
                RemoveValue(hive, subKey, change.Name);
                return;
            }

            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            using (RegistryKey key = baseKey.CreateSubKey(subKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("The registry key " + change.Path + " could not be opened.");
                }

                RegistryValueKind kind = ToKind(change.Type);
                object data = ToData(value, kind);

                key.SetValue(change.Name, data, kind);
            }
        }

        /// <summary>
        /// Reads one value. A missing key or value is not an error.
        /// </summary>
        public static bool TryRead(WinUtilRegistryChange change, out object value)
        {
            value = null;

            if (change == null || string.IsNullOrEmpty(change.Path) || string.IsNullOrEmpty(change.Name))
            {
                return false;
            }

            RegistryHive hive;
            string subKey;
            ParsePath(change.Path, out hive, out subKey);

            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            using (RegistryKey key = baseKey.OpenSubKey(subKey, false))
            {
                if (key == null)
                {
                    return false;
                }

                value = key.GetValue(change.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return value != null;
            }
        }

        /// <summary>
        /// The value of one registry entry as text, with the default of the definition used
        /// when the value is not there.
        /// </summary>
        public static string ReadText(WinUtilRegistryChange change)
        {
            object value;
            if (TryRead(change, out value))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
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

        /// <summary>
        /// True when the current registry state matches the value the entry applies.
        /// </summary>
        public static bool IsApplied(WinUtilRegistryChange change)
        {
            if (change == null)
            {
                return false;
            }

            string current = ReadText(change);

            if (change.HasChoices)
            {
                foreach (KeyValuePair<string, string> choice in change.Values)
                {
                    string expected = IsRemove(choice.Value)
                        ? (change.DefaultValue ?? string.Empty)
                        : choice.Value;
                    if (string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            string target = change.Value;
            if (IsRemove(target))
            {
                return string.IsNullOrEmpty(current);
            }

            return string.Equals(current, target, StringComparison.OrdinalIgnoreCase);
        }

        private static void RemoveValue(RegistryHive hive, string subKey, string name)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            using (RegistryKey key = baseKey.OpenSubKey(subKey, true))
            {
                // A key or value that is already absent is already in the requested state.
                if (key == null)
                {
                    return;
                }

                key.DeleteValue(name, false);
            }
        }

        public static bool IsRemove(string value)
        {
            return string.IsNullOrEmpty(value) ||
                   string.Equals(value, RemoveEntry, StringComparison.OrdinalIgnoreCase);
        }

        private static void ParsePath(string path, out RegistryHive hive, out string subKey)
        {
            string text = (path ?? string.Empty).Trim();
            string root;
            int separator = text.IndexOf(":\\", StringComparison.Ordinal);

            if (separator > 0)
            {
                root = text.Substring(0, separator);
                subKey = text.Substring(separator + 2);
            }
            else if (text.EndsWith(":", StringComparison.Ordinal))
            {
                root = text.Substring(0, text.Length - 1);
                subKey = string.Empty;
            }
            else
            {
                int slash = text.IndexOf('\\');
                if (slash > 0)
                {
                    root = text.Substring(0, slash);
                    subKey = text.Substring(slash + 1);
                }
                else
                {
                    root = text;
                    subKey = string.Empty;
                }
            }

            switch (root.ToUpperInvariant())
            {
                case "HKLM":
                case "HKEY_LOCAL_MACHINE":
                    hive = RegistryHive.LocalMachine;
                    break;
                case "HKCU":
                case "HKEY_CURRENT_USER":
                    hive = RegistryHive.CurrentUser;
                    break;
                case "HKU":
                case "HKEY_USERS":
                    hive = RegistryHive.Users;
                    break;
                case "HKCR":
                case "HKEY_CLASSES_ROOT":
                    hive = RegistryHive.ClassesRoot;
                    break;
                default:
                    throw new InvalidOperationException("The registry root " + root + " is not known.");
            }
        }

        private static RegistryValueKind ToKind(string type)
        {
            if (string.IsNullOrEmpty(type))
            {
                return RegistryValueKind.String;
            }

            switch (type.Trim().ToUpperInvariant())
            {
                case "DWORD":
                    return RegistryValueKind.DWord;
                case "QWORD":
                    return RegistryValueKind.QWord;
                case "STRING":
                case "REG_SZ":
                    return RegistryValueKind.String;
                case "EXPANDSTRING":
                case "REG_EXPAND_SZ":
                    return RegistryValueKind.ExpandString;
                case "BINARY":
                case "REG_BINARY":
                    return RegistryValueKind.Binary;
                case "MULTISTRING":
                case "REG_MULTI_SZ":
                    return RegistryValueKind.MultiString;
                case "NONE":
                case "REG_NONE":
                    return RegistryValueKind.None;
                default:
                    return RegistryValueKind.String;
            }
        }

        private static object ToData(string value, RegistryValueKind kind)
        {
            string text = value ?? string.Empty;

            switch (kind)
            {
                case RegistryValueKind.DWord:
                    return ToInt32(text);
                case RegistryValueKind.QWord:
                    return ToInt64(text);
                case RegistryValueKind.Binary:
                    return ToBytes(text);
                case RegistryValueKind.MultiString:
                    return text.Replace("\r\n", "\n").Split(new[] { '\n' }, StringSplitOptions.None);
                case RegistryValueKind.None:
                    return new byte[0];
                default:
                    return text;
            }
        }

        private static int ToInt32(string text)
        {
            string value = text.Trim();
            int result;

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result))
                {
                    return result;
                }
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            throw new InvalidOperationException("The value " + text + " is not a whole number.");
        }

        private static long ToInt64(string text)
        {
            string value = text.Trim();
            long result;

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result))
                {
                    return result;
                }
            }

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            throw new InvalidOperationException("The value " + text + " is not a whole number.");
        }

        private static byte[] ToBytes(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return new byte[0];
            }

            try
            {
                return Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                // The definitions use a hexadecimal text when they need binary data.
            }

            string[] parts = value.Split(new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries);
            List<byte> bytes = new List<byte>();

            foreach (string part in parts)
            {
                byte item;
                if (!byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out item))
                {
                    throw new InvalidOperationException("The value " + text + " is not binary data.");
                }

                bytes.Add(item);
            }

            return bytes.ToArray();
        }
    }
}
