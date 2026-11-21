using System;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinFreshUtil.Models;
using WinFreshUtil.Services;
using WinFreshUtil.Theme;
using WinFreshUtil.WinUtil;

namespace WinFreshUtil
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // The downloads are only made with TLS 1.2. The older versions are not
            // used any more, so that a connection cannot be made with them.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            ThemeManager.Initialize();
            RegisterErrorHandlers();

            // The application can start itself again with administrator rights. Because the
            // elevated instance does not get the command line, the action it should carry
            // out is read from a small file. Only an instance that really has
            // administrator rights takes that file, so that a second window of the
            // application cannot take the work away from the elevated instance.
            string action = null;

            if (AdminService.IsElevated())
            {
                action = StartupAction.Take();
            }

            if (action == StartupAction.MasHelper)
            {
                // A helper for the activation scripts. It runs the script in a hidden
                // console and types the answers into it, so no window is shown.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                int exitCode;
                try
                {
                    exitCode = MasHelper.Run(ActivationService.GetRequestPath());
                }
                catch (Exception ex)
                {
                    LogService.Error("The activation helper failed.", ex);
                    exitCode = -1;
                }

                Shutdown(exitCode);
                return;
            }

            if (action == StartupAction.DriverInstaller)
            {
                // A helper that installs a driver. A driver needs administrator rights,
                // so the work is done by a second, elevated instance of the application.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                int exitCode;
                try
                {
                    exitCode = DriverInstaller.Run(DriverInstaller.GetRequestPath());
                }
                catch (Exception ex)
                {
                    LogService.Error("The driver helper failed.", ex);
                    exitCode = -1;
                }

                Shutdown(exitCode);
                return;
            }

            if (action == StartupAction.WinUtil)
            {
                // The registry, service and feature changes of the WinUtil section need
                // administrator rights. The elevated instance does the work and writes the
                // log that the page follows.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                int exitCode;
                try
                {
                    exitCode = WinUtilRunner.RunHelper();
                }
                catch (Exception ex)
                {
                    LogService.Error("The WinUtil helper failed.", ex);
                    exitCode = -1;
                }

                Shutdown(exitCode);
                return;
            }

            if (action == StartupAction.RapidOs)
            {
                // A helper that applies RapidOS. The work needs administrator rights,
                // so it is done by a second, elevated instance of the application.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                int exitCode;
                try
                {
                    exitCode = RapidOsService.Run(RapidOsService.GetRequestPath());
                }
                catch (Exception ex)
                {
                    LogService.Error("The helper of RapidOS failed.", ex);
                    exitCode = -1;
                }

                Shutdown(exitCode);
                return;
            }

            base.OnStartup(e);

            // The main window is created here instead of through the StartupUri so that the
            // helper mode above can run without showing a window.
            MainWindow window = new MainWindow();
            MainWindow = window;
            window.Show();

            LogService.Info("WinFreshUtil started, version " + AppInfo.Version + ".");


        }

        /// <summary>
        /// Catches the errors that would otherwise end the application without a word.
        /// </summary>
        private void RegisterErrorHandlers()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        /// <summary>
        /// The last thing that happens when the application is closed. An installation
        /// that is still running is stopped, so that it does not go on without the user
        /// seeing it.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            AppInstallService.KillRunning();

            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogService.Error("An unexpected error ended an action.", e.Exception);

            MessageBox.Show(
                "Something went wrong.\r\n\r\n" + e.Exception.Message +
                "\r\n\r\nThe details are written to the log file.",
                "WinFreshUtil",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // The application keeps running, a single action is not worth ending the app for.
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogService.Error("An unexpected error ended the application.", e.ExceptionObject as Exception);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogService.Error("A background task ended with an error.", e.Exception);
            e.SetObserved();
        }
    }
}
