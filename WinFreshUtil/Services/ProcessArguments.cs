using System.Collections.Generic;
using System.Text;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Builds the argument line of a program the way Windows expects it. The rules are
    /// the ones of the C runtime: a value with a space or a quote in it stays one
    /// argument, so that it is never read as a second command.
    /// </summary>
    public static class ProcessArguments
    {
        /// <summary>
        /// Joins the arguments into one line, with every argument in the right form.
        /// </summary>
        public static string Join(IEnumerable<string> arguments)
        {
            StringBuilder line = new StringBuilder();

            if (arguments != null)
            {
                foreach (string argument in arguments)
                {
                    if (line.Length > 0)
                    {
                        line.Append(' ');
                    }

                    line.Append(Quote(argument));
                }
            }

            return line.ToString();
        }

        /// <summary>
        /// Puts one argument in the form that Windows reads as one argument.
        /// </summary>
        public static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"', '\n', '\v' }) < 0)
            {
                return value;
            }

            StringBuilder quoted = new StringBuilder();
            quoted.Append('"');

            int backslashes = 0;

            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    // The backslashes in front of the quote are doubled and the quote
                    // itself gets a backslash in front of it.
                    quoted.Append('\\', (backslashes * 2) + 1);
                    quoted.Append('"');
                    backslashes = 0;
                    continue;
                }

                quoted.Append('\\', backslashes);
                backslashes = 0;
                quoted.Append(character);
            }

            // The backslashes at the end are doubled as well.
            quoted.Append('\\', backslashes * 2);
            quoted.Append('"');

            return quoted.ToString();
        }
    }
}
