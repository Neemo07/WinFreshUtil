using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// The hash of the activation scripts.
    ///
    /// A script is only run when it still has the hash that is known for it. The
    /// scripts are carried inside the application and come from a fixed commit of the
    /// activation project, so that a change of the project does not change the script
    /// under the feet of the user. The update of the application replaces the copies
    /// on the computer and writes the hashes of the new scripts down.
    /// </summary>
    public static class ScriptHash
    {
        /// <summary>
        /// The commit of the activation project that the scripts inside the
        /// application come from. The scripts of this commit are the ones that this
        /// application knows without an update.
        /// </summary>
        public const string MasCommit = "6cfd6f455ffb4983ce567231d800f08c44c39f00";

        /// <summary>
        /// The hash of the activation script of that commit.
        ///
        /// When the project publishes a new script, the files in the payload folder
        /// and the hashes here are written down again. The update button downloads the
        /// latest scripts and records their hashes in the manifest of the application.
        /// </summary>
        public const string MasSha256 = "FDB7D70AE8A9577C5C6B0B2947ED93E664AC4E2C7A58BBF3DB9ABE9D2F6B9F01";

        /// <summary>
        /// The hashes of the scripts that the activation project publishes apart from
        /// the all in one script. They come from the same commit.
        /// </summary>
        public const string CheckActivationStatusSha256 = "6DF2937385386D0C9682D351797E674583A410F4792BB7BE45302F40AB90287E";

        public const string ChangeWindowsEditionSha256 = "B6BFA311FBEEDE008823FD34E9D04ABE719B0C45F186838B8E7A06DCFC36D0BE";

        public const string ChangeOfficeEditionSha256 = "D4A6325030D484D6229A16170CB0215601BD18583E48A495C4E0BA6A572AF672";

        /// <summary>
        /// The name of the activation script.
        /// </summary>
        public const string MasFileName = "MAS_AIO.cmd";

        /// <summary>
        /// The hash that a file has to have, or null when the file was made by the
        /// application itself and is therefore trusted. A script that was updated from
        /// the project uses the hash of the manifest, the bundled script uses the hash
        /// that is written down here.
        /// </summary>
        public static string ExpectedFor(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string name = Path.GetFileName(path);

            string updated;
            if (MasUpdateService.TryGetHash(path, name, out updated))
            {
                return updated;
            }

            return BundledHashFor(name);
        }

        /// <summary>
        /// The hash of the script that is carried inside the application, or null when
        /// the name does not belong to an activation script.
        /// </summary>
        public static string BundledHashFor(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            if (string.Equals(fileName, MasFileName, StringComparison.OrdinalIgnoreCase))
            {
                return MasSha256;
            }

            if (string.Equals(fileName, "Check_Activation_Status.cmd", StringComparison.OrdinalIgnoreCase))
            {
                return CheckActivationStatusSha256;
            }

            if (string.Equals(fileName, "Change_Windows_Edition.cmd", StringComparison.OrdinalIgnoreCase))
            {
                return ChangeWindowsEditionSha256;
            }

            if (string.Equals(fileName, "Change_Office_Edition.cmd", StringComparison.OrdinalIgnoreCase))
            {
                return ChangeOfficeEditionSha256;
            }

            return null;
        }

        /// <summary>
        /// The hash of a file, in capital letters. Returns null when the file cannot
        /// be read.
        /// </summary>
        public static string OfFile(string path)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(path))
                {
                    return ToText(sha.ComputeHash(stream));
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The hash of bytes, in capital letters.
        /// </summary>
        public static string OfBytes(byte[] payload)
        {
            if (payload == null)
            {
                return null;
            }

            using (SHA256 sha = SHA256.Create())
            {
                return ToText(sha.ComputeHash(payload));
            }
        }

        /// <summary>
        /// True when the file has the hash that is expected for it. A file that was
        /// made by the application itself is always right.
        /// </summary>
        public static bool IsExpected(string path)
        {
            string expected = ExpectedFor(path);

            if (expected == null)
            {
                return true;
            }

            string hash = OfFile(path);

            return string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToText(byte[] hash)
        {
            StringBuilder text = new StringBuilder(hash.Length * 2);

            foreach (byte value in hash)
            {
                text.Append(value.ToString("X2"));
            }

            return text.ToString();
        }
    }
}
