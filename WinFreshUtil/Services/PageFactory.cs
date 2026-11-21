using System.Windows.Controls;
using WinFreshUtil.Views;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Creates view instances on demand. Pages are only instantiated the first
    /// time they are shown, which keeps application start-up fast.
    /// </summary>
    public static class PageFactory
    {
        public const string Drivers = "Drivers";
        public const string WinUtil = "WinUtil";
        public const string Apps = "Apps";
        public const string WindowsOffice = "WindowsOffice";
        public const string About = "About";

        public static UserControl Create(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            switch (key)
            {
                case Drivers:
                    return new DriversPage();
                case WinUtil:
                    return new WinUtilPage();
                case Apps:
                    return new AppsPage();
                case WindowsOffice:
                    return new WindowsOfficePage();
                case About:
                    return new AboutPage();
                default:
                    return null;
            }
        }
    }
}
