using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// Reads the WinUtil definitions that are carried inside the application. Nothing is
    /// fetched: the files are embedded resources, so the section works without a connection.
    /// </summary>
    public static class WinUtilCatalogLoader
    {
        private const string ResourcePrefix = "WinFreshUtil.Resources.winutil_data.";

        /// <summary>
        /// Entries that cannot work without the internet. They are left out of the local
        /// section instead of failing when the user picks one.
        /// </summary>
        private static readonly HashSet<string> OnlineOnly = new HashSet<string>(StringComparer.Ordinal)
        {
            "WPFOOSUbutton",
            "WPFTweaksBlockAdobeNet",
            "WPFFeatureInstall",
            "WPFPanelAutologin",
            "WPFFixesUpdate",
            "WPFFixesWinget",
            "WPFWinUtilInstallPSProfile",
            "WPFWinUtilUninstallPSProfile"
        };

        /// <summary>
        /// Entries whose control is drawn by the page itself instead of the generic card.
        /// The name server choice is the combo box of the name server card.
        /// </summary>
        private static readonly HashSet<string> DrawnByThePage = new HashSet<string>(StringComparer.Ordinal)
        {
            "WPFchangedns"
        };

        private static WinUtilCatalog _catalog;

        public static WinUtilCatalog Load()
        {
            if (_catalog != null)
            {
                return _catalog;
            }

            WinUtilCatalog catalog = new WinUtilCatalog();

            foreach (KeyValuePair<string, object> item in ReadObject("tweaks.json"))
            {
                if (OnlineOnly.Contains(item.Key))
                {
                    continue;
                }

                if (DrawnByThePage.Contains(item.Key))
                {
                    // The combo box of the name server card makes this choice.
                    continue;
                }

                WinUtilEntry entry = ToEntry(item.Key, item.Value, false);
                if (entry != null)
                {
                    catalog.Tweaks.Add(entry);
                }
            }

            foreach (KeyValuePair<string, object> item in ReadObject("features.json"))
            {
                if (OnlineOnly.Contains(item.Key))
                {
                    continue;
                }

                WinUtilEntry entry = ToEntry(item.Key, item.Value, true);
                if (entry != null)
                {
                    catalog.Features.Add(entry);
                }
            }

            foreach (KeyValuePair<string, object> item in ReadObject("appx.json"))
            {
                WinUtilAppxEntry app = ToApp(item.Key, item.Value);
                if (app != null)
                {
                    catalog.Apps.Add(app);
                }
            }

            foreach (KeyValuePair<string, object> item in ReadObject("dns.json"))
            {
                WinUtilDnsProvider provider = ToDns(item.Key, item.Value);
                if (provider != null)
                {
                    catalog.DnsProviders.Add(provider);
                }
            }

            foreach (KeyValuePair<string, object> item in ReadObject("presets.json"))
            {
                catalog.Presets[item.Key] = ToStringList(item.Value);
            }

            catalog.Index();
            _catalog = catalog;
            return catalog;
        }

        /// <summary>
        /// Reads a value that is either a single text or a list of texts.
        /// </summary>
        public static List<string> ToStringList(object value)
        {
            List<string> result = new List<string>();

            if (value == null)
            {
                return result;
            }

            // The JSON reader of the framework gives a list as an ArrayList, so anything
            // that can be walked is treated as a list.
            System.Collections.IEnumerable list = value as System.Collections.IEnumerable;
            if (list != null && !(value is string))
            {
                foreach (object item in list)
                {
                    string text = ToText(item).Trim();
                    if (text.Length > 0)
                    {
                        result.Add(text);
                    }
                }

                return result;
            }

            string single = ToText(value).Trim();
            if (single.Length > 0)
            {
                result.Add(single);
            }

            return result;
        }

        /// <summary>
        /// The original file gives the choices of a combo box as one text separated by
        /// vertical bars, for example "Enabled|Disabled|Fully Disabled".
        /// </summary>
        private static List<string> ToComboItems(object value)
        {
            List<string> items = new List<string>();
            string text = ToText(value);

            if (text.IndexOf('|') >= 0)
            {
                foreach (string part in text.Split('|'))
                {
                    string item = part.Trim();
                    if (item.Length > 0)
                    {
                        items.Add(item);
                    }
                }

                return items;
            }

            return ToStringList(value);
        }

        private static WinUtilEntry ToEntry(string key, object value, bool isFeature)
        {
            IDictionary<string, object> map = value as IDictionary<string, object>;
            if (map == null)
            {
                return null;
            }

            WinUtilEntry entry = new WinUtilEntry
            {
                Key = key,
                Content = Get(map, "Content"),
                Description = Get(map, "Description"),
                Category = Get(map, "category"),
                Panel = Get(map, "panel"),
                IsFeature = isFeature,
                Function = Get(map, "function"),
                UndoOfflineNote = Get(map, "UndoOfflineNote"),
                CheckedByDefault = GetBool(map, "Checked"),
                FeatureNames = ToStringList(GetValue(map, "feature")),
                AppxPackages = ToStringList(GetValue(map, "appx")),
                InvokeScripts = ToStringList(GetValue(map, "InvokeScript")),
                UndoScripts = ToStringList(GetValue(map, "UndoScript")),
                ComboItems = ToComboItems(GetValue(map, "ComboItems"))
            };

            entry.Kind = ToKind(Get(map, "Type"), entry);

            object comboDescriptions = GetValue(map, "ComboDescriptions");
            IDictionary<string, object> descriptions = comboDescriptions as IDictionary<string, object>;
            if (descriptions != null)
            {
                foreach (KeyValuePair<string, object> item in descriptions)
                {
                    entry.ComboDescriptions[item.Key] = ToText(item.Value);
                }
            }

            foreach (object item in ToObjectList(GetValue(map, "registry")))
            {
                WinUtilRegistryChange change = ToRegistryChange(item);
                if (change != null)
                {
                    entry.Registry.Add(change);
                }
            }

            foreach (object item in ToObjectList(GetValue(map, "service")))
            {
                WinUtilServiceChange change = ToServiceChange(item);
                if (change != null)
                {
                    entry.Services.Add(change);
                }
            }

            return entry;
        }

        private static WinUtilEntryKind ToKind(string type, WinUtilEntry entry)
        {
            if (string.Equals(type, "Toggle", StringComparison.OrdinalIgnoreCase))
            {
                return WinUtilEntryKind.Toggle;
            }

            if (string.Equals(type, "Combobox", StringComparison.OrdinalIgnoreCase))
            {
                return WinUtilEntryKind.ComboBox;
            }

            if (string.Equals(type, "Button", StringComparison.OrdinalIgnoreCase))
            {
                return WinUtilEntryKind.Button;
            }

            if (entry.IsFeature)
            {
                if (entry.FeatureNames.Count > 0)
                {
                    return WinUtilEntryKind.CheckBox;
                }

                // A feature entry without a Windows feature is a button in the original.
                return WinUtilEntryKind.Button;
            }

            return WinUtilEntryKind.CheckBox;
        }

        private static WinUtilRegistryChange ToRegistryChange(object value)
        {
            IDictionary<string, object> map = value as IDictionary<string, object>;
            if (map == null)
            {
                return null;
            }

            WinUtilRegistryChange change = new WinUtilRegistryChange
            {
                Path = Get(map, "Path"),
                Name = Get(map, "Name"),
                Type = Get(map, "Type"),
                Value = Get(map, "Value"),
                OriginalValue = Get(map, "OriginalValue"),
                DefaultValue = Get(map, "DefaultValue"),
                DefaultState = Get(map, "DefaultState")
            };

            object values = GetValue(map, "Values");
            IDictionary<string, object> choices = values as IDictionary<string, object>;
            if (choices != null)
            {
                foreach (KeyValuePair<string, object> item in choices)
                {
                    change.Values[item.Key] = ToText(item.Value);
                }
            }

            return change;
        }

        private static WinUtilServiceChange ToServiceChange(object value)
        {
            IDictionary<string, object> map = value as IDictionary<string, object>;
            if (map == null)
            {
                return null;
            }

            return new WinUtilServiceChange
            {
                Name = Get(map, "Name"),
                StartupType = Get(map, "StartupType"),
                OriginalType = Get(map, "OriginalType")
            };
        }

        private static WinUtilAppxEntry ToApp(string key, object value)
        {
            IDictionary<string, object> map = value as IDictionary<string, object>;
            if (map == null)
            {
                return null;
            }

            return new WinUtilAppxEntry
            {
                Key = key,
                Content = Get(map, "Content"),
                Description = Get(map, "Description"),
                Category = Get(map, "Category"),
                PackageId = Get(map, "PackageId"),
                StoreId = Get(map, "StoreId")
            };
        }

        private static WinUtilDnsProvider ToDns(string key, object value)
        {
            IDictionary<string, object> map = value as IDictionary<string, object>;
            if (map == null)
            {
                return null;
            }

            return new WinUtilDnsProvider
            {
                Key = key,
                Title = key.Replace("_", " "),
                Primary = Get(map, "Primary"),
                Secondary = Get(map, "Secondary"),
                Primary6 = Get(map, "Primary6"),
                Secondary6 = Get(map, "Secondary6"),
                DohTemplate = Get(map, "DohTemplate"),
                BenchmarkEligible = GetBool(map, "BenchmarkEligible")
            };
        }

        private static IEnumerable<object> ToObjectList(object value)
        {
            System.Collections.IEnumerable list = value as System.Collections.IEnumerable;
            if (list != null && !(value is string))
            {
                List<object> items = new List<object>();
                foreach (object item in list)
                {
                    items.Add(item);
                }

                return items;
            }

            if (value == null)
            {
                return new object[0];
            }

            return new object[] { value };
        }

        private static object GetValue(IDictionary<string, object> map, string name)
        {
            object value;
            return map.TryGetValue(name, out value) ? value : null;
        }

        private static string Get(IDictionary<string, object> map, string name)
        {
            return ToText(GetValue(map, name));
        }

        private static bool GetBool(IDictionary<string, object> map, string name)
        {
            object value = GetValue(map, name);
            if (value == null)
            {
                return false;
            }

            bool result;
            if (value is bool)
            {
                return (bool)value;
            }

            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out result) && result;
        }

        private static string ToText(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static IEnumerable<KeyValuePair<string, object>> ReadObject(string fileName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string name = ResourcePrefix + fileName;

            using (Stream stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("The local WinUtil file " + fileName + " is not part of this application.");
                }

                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    Dictionary<string, object> map = serializer.Deserialize<Dictionary<string, object>>(json);

                    if (map == null)
                    {
                        throw new InvalidOperationException("The local WinUtil file " + fileName + " is empty.");
                    }

                    return map;
                }
            }
        }
    }
}
