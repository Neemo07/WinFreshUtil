using System;
using System.Collections.Generic;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// The control the original WinUtil uses for an entry.
    /// </summary>
    public enum WinUtilEntryKind
    {
        CheckBox,
        Toggle,
        ComboBox,
        Button
    }

    /// <summary>
    /// One registry value that an entry changes.
    /// </summary>
    public sealed class WinUtilRegistryChange
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public string OriginalValue { get; set; }
        public string DefaultValue { get; set; }
        public string DefaultState { get; set; }

        /// <summary>
        /// The value to use for each choice of a combo box. Empty for a normal entry.
        /// </summary>
        public Dictionary<string, string> Values { get; set; }

        public WinUtilRegistryChange()
        {
            Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public bool HasChoices
        {
            get { return Values != null && Values.Count > 0; }
        }
    }

    /// <summary>
    /// One service that an entry changes.
    /// </summary>
    public sealed class WinUtilServiceChange
    {
        public string Name { get; set; }
        public string StartupType { get; set; }
        public string OriginalType { get; set; }
    }

    /// <summary>
    /// One tweak or one item of the features page.
    /// </summary>
    public sealed class WinUtilEntry
    {
        public string Key { get; set; }
        public string Content { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Panel { get; set; }
        public WinUtilEntryKind Kind { get; set; }
        public bool CheckedByDefault { get; set; }
        public bool IsFeature { get; set; }
        public string Function { get; set; }
        public string UndoOfflineNote { get; set; }

        public List<string> ComboItems { get; set; }
        public Dictionary<string, string> ComboDescriptions { get; set; }
        public List<WinUtilRegistryChange> Registry { get; set; }
        public List<WinUtilServiceChange> Services { get; set; }
        public List<string> InvokeScripts { get; set; }
        public List<string> UndoScripts { get; set; }
        public List<string> FeatureNames { get; set; }
        public List<string> AppxPackages { get; set; }

        public WinUtilEntry()
        {
            ComboItems = new List<string>();
            ComboDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Registry = new List<WinUtilRegistryChange>();
            Services = new List<WinUtilServiceChange>();
            InvokeScripts = new List<string>();
            UndoScripts = new List<string>();
            FeatureNames = new List<string>();
            AppxPackages = new List<string>();
        }

        public bool HasRegistryChoices
        {
            get
            {
                foreach (WinUtilRegistryChange change in Registry)
                {
                    if (change.HasChoices)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// The text shown on a button of the original project.
        /// </summary>
        public string ButtonText
        {
            get { return string.IsNullOrEmpty(Content) ? Key : Content; }
        }
    }

    /// <summary>
    /// One application of the appx page.
    /// </summary>
    public sealed class WinUtilAppxEntry
    {
        public string Key { get; set; }
        public string Content { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string PackageId { get; set; }
        public string StoreId { get; set; }
    }

    /// <summary>
    /// One name server that the DNS choice can set.
    /// </summary>
    public sealed class WinUtilDnsProvider
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Primary { get; set; }
        public string Secondary { get; set; }
        public string Primary6 { get; set; }
        public string Secondary6 { get; set; }
        public string DohTemplate { get; set; }
        public bool BenchmarkEligible { get; set; }
    }

    /// <summary>
    /// One entry of the work that the elevated helper should do.
    /// </summary>
    public sealed class WinUtilAction
    {
        public string Key { get; set; }
        public bool Undo { get; set; }
        public string Choice { get; set; }

        public WinUtilAction()
        {
            Choice = string.Empty;
        }
    }

    /// <summary>
    /// Everything that was picked on the page.
    /// </summary>
    public sealed class WinUtilSelection
    {
        public List<WinUtilAction> Actions { get; set; }
        public List<string> AppxKeys { get; set; }
        public string DnsProvider { get; set; }

        public WinUtilSelection()
        {
            Actions = new List<WinUtilAction>();
            AppxKeys = new List<string>();
            DnsProvider = string.Empty;
        }

        public bool IsEmpty
        {
            get
            {
                return (Actions == null || Actions.Count == 0) &&
                       (AppxKeys == null || AppxKeys.Count == 0) &&
                       string.IsNullOrEmpty(DnsProvider);
            }
        }
    }

    /// <summary>
    /// The result of one run.
    /// </summary>
    public sealed class WinUtilRunResult
    {
        public bool Success { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// The definitions that the WinUtil section reads. They come out of the assembly.
    /// </summary>
    public sealed class WinUtilCatalog
    {
        public List<WinUtilEntry> Tweaks { get; set; }
        public List<WinUtilEntry> Features { get; set; }
        public List<WinUtilAppxEntry> Apps { get; set; }
        public List<WinUtilDnsProvider> DnsProviders { get; set; }
        public Dictionary<string, List<string>> Presets { get; set; }

        private readonly Dictionary<string, WinUtilEntry> _tweaksByKey;
        private readonly Dictionary<string, WinUtilEntry> _featuresByKey;
        private readonly Dictionary<string, WinUtilAppxEntry> _appsByKey;

        public WinUtilCatalog()
        {
            Tweaks = new List<WinUtilEntry>();
            Features = new List<WinUtilEntry>();
            Apps = new List<WinUtilAppxEntry>();
            DnsProviders = new List<WinUtilDnsProvider>();
            Presets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            _tweaksByKey = new Dictionary<string, WinUtilEntry>(StringComparer.Ordinal);
            _featuresByKey = new Dictionary<string, WinUtilEntry>(StringComparer.Ordinal);
            _appsByKey = new Dictionary<string, WinUtilAppxEntry>(StringComparer.Ordinal);
        }

        public void Index()
        {
            _tweaksByKey.Clear();
            _featuresByKey.Clear();
            _appsByKey.Clear();

            foreach (WinUtilEntry entry in Tweaks)
            {
                _tweaksByKey[entry.Key] = entry;
            }

            foreach (WinUtilEntry entry in Features)
            {
                _featuresByKey[entry.Key] = entry;
            }

            foreach (WinUtilAppxEntry entry in Apps)
            {
                _appsByKey[entry.Key] = entry;
            }
        }

        public WinUtilEntry FindTweak(string key)
        {
            WinUtilEntry entry;
            return key != null && _tweaksByKey.TryGetValue(key, out entry) ? entry : null;
        }

        public WinUtilEntry FindFeature(string key)
        {
            WinUtilEntry entry;
            return key != null && _featuresByKey.TryGetValue(key, out entry) ? entry : null;
        }

        public WinUtilEntry FindEntry(string key)
        {
            return FindTweak(key) ?? FindFeature(key);
        }

        public WinUtilAppxEntry FindApp(string key)
        {
            WinUtilAppxEntry entry;
            return key != null && _appsByKey.TryGetValue(key, out entry) ? entry : null;
        }

        public WinUtilDnsProvider FindDns(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            foreach (WinUtilDnsProvider provider in DnsProviders)
            {
                if (string.Equals(provider.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return provider;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// One line of the live log.
    /// </summary>
    public sealed class WinUtilLogLine
    {
        public string Text { get; set; }
        public WinUtilLogLevel Level { get; set; }

        public WinUtilLogLine(string text, WinUtilLogLevel level)
        {
            Text = text;
            Level = level;
        }
    }

    public enum WinUtilLogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }
}
