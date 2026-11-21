using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Reads the table that the package manager of Windows prints. The table has a
    /// fixed width, and the place of every column is taken from the line with the
    /// names.
    /// </summary>
    public static class WingetTable
    {
        /// <summary>
        /// Reads the programs out of the table.
        /// </summary>
        public static IList<AppPackage> Parse(string output)
        {
            List<AppPackage> results = new List<AppPackage>();

            if (string.IsNullOrEmpty(output))
            {
                return results;
            }

            string[] lines = output.Replace("\r", string.Empty).Split('\n');

            int[] columns = null;

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.Length == 0)
                {
                    continue;
                }

                if (columns == null)
                {
                    if (line.IndexOf("Name", StringComparison.OrdinalIgnoreCase) < 0 ||
                        line.IndexOf("Id", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    columns = GetColumns(line);
                    continue;
                }

                if (line.Trim().Length == 0 || line.Trim().StartsWith("---", StringComparison.Ordinal))
                {
                    continue;
                }

                AppPackage package = new AppPackage
                {
                    Name = Cut(line, columns, 0),
                    Id = Cut(line, columns, 1),
                    Version = Cut(line, columns, 2),
                    Source = Cut(line, columns, 4)
                };

                if (package.Id.Length == 0)
                {
                    continue;
                }

                results.Add(package);
            }

            return results;
        }

        /// <summary>
        /// The place where every column of the table starts.
        /// </summary>
        private static int[] GetColumns(string header)
        {
            List<int> positions = new List<int>();
            int index = 0;

            while (index < header.Length)
            {
                while (index < header.Length && header[index] == ' ')
                {
                    index++;
                }

                if (index >= header.Length)
                {
                    break;
                }

                positions.Add(index);

                while (index < header.Length && header[index] != ' ')
                {
                    index++;
                }
            }

            return positions.ToArray();
        }

        /// <summary>
        /// The text of one column of a line of the table.
        /// </summary>
        private static string Cut(string line, int[] columns, int column)
        {
            if (columns == null || column >= columns.Length)
            {
                return string.Empty;
            }

            int start = columns[column];
            if (start >= line.Length)
            {
                return string.Empty;
            }

            int end = column + 1 < columns.Length ? columns[column + 1] : line.Length;
            if (end > line.Length)
            {
                end = line.Length;
            }

            return line.Substring(start, end - start).Trim();
        }
    }

    /// <summary>
    /// Reads how far a program is out of the line that the package manager printed.
    ///
    /// The package manager only prints it when it writes to a console, so most of the
    /// time there is no number and the progress bar runs as a band.
    /// </summary>
    public static class InstallOutput
    {
        /// <summary>
        /// Reads the percent out of a line, or -1 when the line holds no number.
        /// </summary>
        public static int ReadPercent(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return -1;
            }

            // A line such as "  12.5 MB / 79.4 MB".
            Match sizes = Regex.Match(line, @"([0-9]+(?:\.[0-9]+)?)\s*(KB|MB|GB)\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*(KB|MB|GB)",
                RegexOptions.IgnoreCase);

            if (sizes.Success)
            {
                double done = ToBytes(sizes.Groups[1].Value, sizes.Groups[2].Value);
                double total = ToBytes(sizes.Groups[3].Value, sizes.Groups[4].Value);

                if (total > 0)
                {
                    int ratio = (int)Math.Round(done / total * 100);

                    if (ratio >= 0 && ratio <= 100)
                    {
                        return ratio;
                    }
                }
            }

            // A line such as "  45%".
            Match percent = Regex.Match(line, @"([0-9]{1,3})\s*%");

            if (percent.Success)
            {
                int value;
                if (int.TryParse(percent.Groups[1].Value, out value) && value >= 0 && value <= 100)
                {
                    return value;
                }
            }

            return -1;
        }

        private static double ToBytes(string number, string unit)
        {
            double value;
            if (!double.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return 0;
            }

            if (unit.Equals("KB", StringComparison.OrdinalIgnoreCase))
            {
                return value * 1024;
            }

            if (unit.Equals("MB", StringComparison.OrdinalIgnoreCase))
            {
                return value * 1024 * 1024;
            }

            if (unit.Equals("GB", StringComparison.OrdinalIgnoreCase))
            {
                return value * 1024 * 1024 * 1024;
            }

            return value;
        }
    }
}
