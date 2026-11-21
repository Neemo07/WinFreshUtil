using System;
using System.IO;
using System.Reflection;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// The files that are carried inside the application, so that it works on a
    /// computer without the internet.
    ///
    /// The activation scripts and the sources of RapidOS are put next to the
    /// application when they are needed. A file that is already there is kept when
    /// it has the expected hash, so a changed file is never trusted.
    /// </summary>
    public static class PayloadService
    {
        private const string ResourcePrefix = "WinFreshUtil.Payloads.";

        /// <summary>
        /// Makes sure that the file with the given name is on the computer and has
        /// the expected hash. Returns false and sets <paramref name="error"/> when the
        /// file is not part of the application or could not be written.
        /// </summary>
        public static bool TryRestore(string fileName, string targetPath, string expectedSha256, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(targetPath))
            {
                error = "The name of the file is empty.";
                return false;
            }

            try
            {
                if (File.Exists(targetPath) && IsExpected(targetPath, expectedSha256))
                {
                    return true;
                }

                string folder = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + fileName))
                {
                    if (resource == null)
                    {
                        error = "The file " + fileName + " is not part of this application.";
                        return false;
                    }

                    // The bytes are written to a temporary file first, so that a failure
                    // does not leave a half written script behind.
                    string temporary = targetPath + ".tmp";

                    using (FileStream file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        resource.CopyTo(file);
                    }

                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }

                    File.Move(temporary, targetPath);

                    if (!IsExpected(targetPath, expectedSha256))
                    {
                        File.Delete(targetPath);
                        error = "The file " + fileName + " that this application carries is not the one it knows.";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "The file " + fileName + " could not be put in place. " + ex.Message;
                return false;
            }
        }

        private static bool IsExpected(string path, string expectedSha256)
        {
            if (string.IsNullOrEmpty(expectedSha256))
            {
                return true;
            }

            string hash = ScriptHash.OfFile(path);
            return string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
    }
}
