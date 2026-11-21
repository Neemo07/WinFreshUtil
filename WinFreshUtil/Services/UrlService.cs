using System;
using System.Diagnostics;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Opens links in the user's default browser.
    /// </summary>
    public static class UrlService
    {
        /// <summary>
        /// The schemes that may be handed to Windows. Everything else is refused, so
        /// that a link that comes from the computer itself cannot start another
        /// program.
        /// </summary>
        private static readonly string[] AllowedSchemes = { "http", "https", "ms-settings" };

        /// <summary>
        /// Opens <paramref name="url"/> with the default browser.
        /// Returns false and sets <paramref name="errorMessage"/> when the link could not be opened.
        /// </summary>
        public static bool TryOpen(string url, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrEmpty(url))
            {
                errorMessage = "The link is empty.";
                return false;
            }

            if (!IsAllowed(url))
            {
                errorMessage = "This kind of link is not opened.";
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// True when the link is a web address or a page of Windows.
        /// </summary>
        public static bool IsAllowed(string url)
        {
            Uri uri;

            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return false;
            }

            foreach (string scheme in AllowedSchemes)
            {
                if (string.Equals(uri.Scheme, scheme, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
