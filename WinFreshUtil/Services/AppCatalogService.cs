using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// The list of programs that can be installed. The list is read from the original
    /// WinUtil project, so the names, the texts and the names of the packages are the
    /// same there.
    /// </summary>
    public static class AppCatalogService
    {
        private static List<AppEntry> _apps;
        private static List<string> _categories;

        /// <summary>
        /// The colour of the sign of a program. The colour comes from the group, so
        /// that the page looks calm.
        /// </summary>
        private static readonly string[] Colors =
        {
            "#2F6FED", "#0E9F6E", "#D9480F", "#7048E8", "#C2255C",
            "#1098AD", "#5C7CFA", "#E8590C", "#495057", "#1F7A8C"
        };

        /// <summary>
        /// The programs of the list.
        /// </summary>
        public static IList<AppEntry> GetApps()
        {
            if (_apps != null)
            {
                return _apps;
            }

            _apps = new List<AppEntry>();

            foreach (KeyValuePair<string, object> entry in ReadJson("winutil-apps.json"))
            {
                IDictionary<string, object> value = entry.Value as IDictionary<string, object>;
                if (value == null)
                {
                    continue;
                }

                string category = Get(value, "category");
                if (string.IsNullOrEmpty(category))
                {
                    category = "Utilities";
                }

                _apps.Add(new AppEntry
                {
                    Key = entry.Key,
                    Category = category,
                    Name = Get(value, "content"),
                    Description = Get(value, "description"),
                    Link = Get(value, "link"),
                    WingetId = Get(value, "winget"),
                    ChocoId = Get(value, "choco"),
                    IsFoss = GetBool(value, "foss")
                });
            }

            // The colour of the sign is worked out once, so that the page does not have
            // to do it again for every program.
            Dictionary<string, string> colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (AppEntry app in _apps)
            {
                string color;

                if (!colors.TryGetValue(app.Category, out color))
                {
                    color = Colors[colors.Count % Colors.Length];
                    colors[app.Category] = color;
                }

                app.IconColor = color;
            }

            return _apps;
        }

        /// <summary>
        /// The groups of the list, in the order of the original project.
        /// </summary>
        public static IList<string> GetCategories()
        {
            if (_categories != null)
            {
                return _categories;
            }

            _categories = new List<string>();

            foreach (AppEntry app in GetApps())
            {
                if (!_categories.Contains(app.Category))
                {
                    _categories.Add(app.Category);
                }
            }

            // The groups are shown in the order of the alphabet, so that the list is
            // easy to find.
            _categories.Sort(StringComparer.OrdinalIgnoreCase);

            return _categories;
        }

        private static string Get(IDictionary<string, object> map, string name)
        {
            object value;
            if (map.TryGetValue(name, out value) && value != null)
            {
                return Convert.ToString(value);
            }

            return string.Empty;
        }

        private static bool GetBool(IDictionary<string, object> map, string name)
        {
            object value;
            if (!map.TryGetValue(name, out value) || value == null)
            {
                return false;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            bool result;
            return bool.TryParse(Convert.ToString(value), out result) && result;
        }

        private static IEnumerable<KeyValuePair<string, object>> ReadJson(string resourceName)
        {
            List<KeyValuePair<string, object>> result = new List<KeyValuePair<string, object>>();

            Assembly assembly = Assembly.GetExecutingAssembly();
            string name = "WinFreshUtil.Resources." + resourceName;

            using (Stream stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    return result;
                }

                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();

                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    Dictionary<string, object> map = serializer.Deserialize<Dictionary<string, object>>(json);

                    if (map != null)
                    {
                        foreach (KeyValuePair<string, object> entry in map)
                        {
                            result.Add(entry);
                        }
                    }
                }
            }

            return result;
        }
    }
}
