using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// Sets the name servers of the active network interfaces with the local netsh tool and
    /// records the DNS over HTTPS template in the registry.
    /// </summary>
    public static class WinUtilDns
    {
        public static bool Set(string providerKey, Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            WinUtilCatalog catalog = WinUtilCatalogLoader.Load();
            WinUtilDnsProvider provider = catalog.FindDns(providerKey);

            bool reset = string.Equals(providerKey, "DHCP", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(providerKey, "Default", StringComparison.OrdinalIgnoreCase);

            if (!reset && provider == null)
            {
                error = "The name server " + providerKey + " is not part of the local definitions.";
                return false;
            }

            List<NetworkInterface> interfaces = ActiveInterfaces();
            if (interfaces.Count == 0)
            {
                error = "No active network interface was found.";
                return false;
            }

            foreach (NetworkInterface adapter in interfaces)
            {
                if (reset)
                {
                    if (!RunNetsh("interface ipv4 set dnsservers name=" + WinUtilProcess.Quote(adapter.Name) + " source=dhcp", log, out error) ||
                        !RunNetsh("interface ipv6 set dnsservers name=" + WinUtilProcess.Quote(adapter.Name) + " source=dhcp", log, out error))
                    {
                        return false;
                    }

                    Report(log, "The name servers of " + adapter.Name + " come from DHCP again.", WinUtilLogLevel.Info);
                    continue;
                }

                if (!string.IsNullOrEmpty(provider.Primary))
                {
                    if (!RunNetsh("interface ipv4 set dnsservers name=" + WinUtilProcess.Quote(adapter.Name) +
                                  " static " + provider.Primary + " primary", log, out error))
                    {
                        return false;
                    }

                    if (!string.IsNullOrEmpty(provider.Secondary))
                    {
                        RunNetsh("interface ipv4 add dnsservers name=" + WinUtilProcess.Quote(adapter.Name) +
                                 " " + provider.Secondary + " index=2", log, out error);
                    }
                }

                if (!string.IsNullOrEmpty(provider.Primary6))
                {
                    if (!RunNetsh("interface ipv6 set dnsservers name=" + WinUtilProcess.Quote(adapter.Name) +
                                  " static " + provider.Primary6 + " primary", log, out error))
                    {
                        return false;
                    }

                    if (!string.IsNullOrEmpty(provider.Secondary6))
                    {
                        RunNetsh("interface ipv6 add dnsservers name=" + WinUtilProcess.Quote(adapter.Name) +
                                 " " + provider.Secondary6 + " index=2", log, out error);
                    }
                }

                RegisterDoh(adapter, provider, log);
                Report(log, "The name servers of " + adapter.Name + " are set to " + provider.Title + ".", WinUtilLogLevel.Info);
            }

            return true;
        }

        private static List<NetworkInterface> ActiveInterfaces()
        {
            List<NetworkInterface> result = new List<NetworkInterface>();

            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                result.Add(adapter);
            }

            return result;
        }

        private static void RegisterDoh(NetworkInterface adapter, WinUtilDnsProvider provider, Action<WinUtilLogLine> log)
        {
            if (string.IsNullOrEmpty(provider.DohTemplate) || string.IsNullOrEmpty(adapter.Id))
            {
                return;
            }

            List<string> addresses = new List<string>();
            AddIfPresent(addresses, provider.Primary);
            AddIfPresent(addresses, provider.Secondary);
            AddIfPresent(addresses, provider.Primary6);
            AddIfPresent(addresses, provider.Secondary6);

            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    foreach (string address in addresses)
                    {
                        string leaf = address.IndexOf(':') >= 0 ? "Doh6" : "Doh";
                        string path = @"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\" +
                                      adapter.Id + @"\DohInterfaceSettings\" + leaf + @"\" + address;

                        using (RegistryKey key = baseKey.CreateSubKey(path))
                        {
                            if (key == null)
                            {
                                continue;
                            }

                            key.SetValue("DohFlags", 1L, RegistryValueKind.QWord);
                            key.SetValue("DohTemplate", provider.DohTemplate, RegistryValueKind.String);
                        }
                    }

                    using (RegistryKey parameters = baseKey.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters"))
                    {
                        if (parameters != null)
                        {
                            parameters.SetValue("EnableAutoDoh", 2, RegistryValueKind.DWord);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Report(log, "The DNS over HTTPS template could not be recorded. " + ex.Message, WinUtilLogLevel.Warning);
            }
        }

        private static void AddIfPresent(List<string> list, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                list.Add(value);
            }
        }

        private static bool RunNetsh(string arguments, Action<WinUtilLogLine> log, out string error)
        {
            int code = WinUtilProcess.Run(
                "netsh.exe",
                arguments,
                delegate(string line) { Report(log, line, WinUtilLogLevel.Info); },
                out error,
                2 * 60 * 1000);

            if (code != 0)
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "netsh ended with exit code " + code + ".";
                }

                Report(log, error, WinUtilLogLevel.Error);
                return false;
            }

            return true;
        }

        private static void Report(Action<WinUtilLogLine> log, string text, WinUtilLogLevel level)
        {
            if (log != null && !string.IsNullOrEmpty(text))
            {
                log(new WinUtilLogLine(text, level));
            }
        }
    }
}
