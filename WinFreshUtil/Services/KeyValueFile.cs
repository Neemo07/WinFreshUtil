using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// A small file with one value on every line. The line starts with one letter,
    /// then a sign, and then the value. The value may hold anything, including signs.
    /// </summary>
    public static class KeyValueFile
    {
        /// <summary>
        /// Reads the file. Returns an empty list when it cannot be read.
        /// </summary>
        public static Dictionary<string, string> Read(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (line.Length < 2 || line[1] != '=')
                    {
                        continue;
                    }

                    values[line[0].ToString()] = line.Substring(2);
                }
            }
            catch (Exception)
            {
                // The file is only used to tell the next instance what to do.
            }

            return values;
        }

        /// <summary>
        /// Writes the file. The order of the values is kept.
        /// </summary>
        public static void Write(string path, IEnumerable<KeyValuePair<string, string>> values)
        {
            StringBuilder content = new StringBuilder();

            if (values != null)
            {
                foreach (KeyValuePair<string, string> value in values)
                {
                    content.Append(value.Key).Append('=').Append(value.Value ?? string.Empty).AppendLine();
                }
            }

            File.WriteAllText(path, content.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// Makes a list of values for the file.
        /// </summary>
        public static List<KeyValuePair<string, string>> Values()
        {
            return new List<KeyValuePair<string, string>>();
        }

        /// <summary>
        /// Adds a value to the list.
        /// </summary>
        public static void Add(List<KeyValuePair<string, string>> values, string key, string value)
        {
            if (values == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            values.Add(new KeyValuePair<string, string>(key, value ?? string.Empty));
        }
    }
}
