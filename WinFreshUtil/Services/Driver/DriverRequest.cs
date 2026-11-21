using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WinFreshUtil.Services.Driver
{
    /// <summary>
    /// The file that the elevated instance reads its work from.
    ///
    /// The file holds the stage and the id of the driver and nothing else. The places
    /// for the progress and the result are not in it, because the elevated instance has
    /// to write them where the application expects them and not where a file asks it
    /// to.
    ///
    /// The file is read by an instance that has administrator rights, so every value in
    /// it is checked before it is used.
    /// </summary>
    public static class DriverRequest
    {
        /// <summary>
        /// Writes the file.
        /// </summary>
        public static void Write(string path, DriverCandidate candidate)
        {
            StringBuilder content = new StringBuilder();

            content.Append("M=").Append(candidate.Stage.ToString()).AppendLine();
            content.Append("U=").Append(Sanitize(candidate.UpdateId)).AppendLine();
            content.Append("T=").Append(Sanitize(candidate.Name)).AppendLine();
            content.Append("H=").Append(Sanitize(candidate.HardwareId)).AppendLine();

            File.WriteAllText(path, content.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// Reads the file. Returns null when it cannot be read.
        /// </summary>
        public static Dictionary<string, string> Read(string path)
        {
            try
            {
                Dictionary<string, string> request = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (line.Length < 2 || line[1] != '=')
                    {
                        continue;
                    }

                    request[line[0].ToString()] = line.Substring(2);
                }

                return request;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Checks the request that the elevated instance reads. Only the forms that are
        /// needed are accepted.
        /// </summary>
        public static bool Validate(Dictionary<string, string> request, out string error)
        {
            error = null;

            DriverStage stage;

            string stageText;
            if (!request.TryGetValue("M", out stageText) || !TryGetStage(stageText, out stage))
            {
                error = "The request does not say what has to be done.";
                return false;
            }

            string id;
            request.TryGetValue("U", out id);

            if (stage == DriverStage.OemTool)
            {
                if (!IsPackageId(id))
                {
                    error = "The request holds a name that is not a package of the package manager.";
                    return false;
                }
            }
            else if (!IsGuid(id))
            {
                error = "The request holds an id that is not a driver of Windows.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads the stage out of the request. The name has to be exactly one of the
        /// stages, so that a text with two of them is refused.
        /// </summary>
        public static bool TryGetStage(string text, out DriverStage stage)
        {
            stage = DriverStage.WindowsUpdate;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string name = text.Trim();

            foreach (DriverStage value in Enum.GetValues(typeof(DriverStage)))
            {
                if (string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    stage = value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the text is a name that the package manager of Windows can use.
        /// </summary>
        public static bool IsPackageId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 80)
            {
                return false;
            }

            foreach (char character in value)
            {
                if (!char.IsLetterOrDigit(character) &&
                    character != '.' && character != '-' && character != '_' && character != '+')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when the text is the id of an update of Windows.
        /// </summary>
        public static bool IsGuid(string value)
        {
            Guid id;

            return !string.IsNullOrEmpty(value) && Guid.TryParse(value, out id);
        }

        /// <summary>
        /// Removes the characters that have no place in the request file and makes the
        /// text short enough for one line.
        /// </summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder clean = new StringBuilder(value.Length);

            foreach (char character in value)
            {
                clean.Append(character < ' ' || character == '|' ? ' ' : character);
            }

            string text = clean.ToString().Trim();

            return text.Length > 200 ? text.Substring(0, 200) : text;
        }
    }
}
