using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace WinFreshUtil.Models
{
    /// <summary>
    /// One program that can be installed with a package manager. The list comes from
    /// the original WinUtil project, so the names and the texts are the same there.
    /// </summary>
    public sealed class AppEntry : INotifyPropertyChanged
    {
        private bool _isWorking;
        private bool _isIndeterminate;
        private double _progress;
        private string _progressText = string.Empty;
        private bool _isInstalled;
        private bool _hasFailed;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>The name of the entry in the list of the original project.</summary>
        public string Key { get; set; }

        /// <summary>The group the program is shown in.</summary>
        public string Category { get; set; }

        /// <summary>The name of the program.</summary>
        public string Name { get; set; }

        /// <summary>What the program does.</summary>
        public string Description { get; set; }

        /// <summary>The page of the program.</summary>
        public string Link { get; set; }

        /// <summary>The name of the program in the package manager of Windows.</summary>
        public string WingetId { get; set; }

        /// <summary>The name of the program in Chocolatey.</summary>
        public string ChocoId { get; set; }

        /// <summary>True when the program is free and open source.</summary>
        public bool IsFoss { get; set; }

        /// <summary>
        /// True when the program is on the computer. The colour of the sign on the
        /// button changes with it.
        /// </summary>
        public bool IsInstalled
        {
            get { return _isInstalled; }
            set { Set(ref _isInstalled, value, "IsInstalled"); }
        }

        /// <summary>
        /// True while the program is being installed. The button of the program then
        /// shows a progress bar.
        /// </summary>
        public bool IsWorking
        {
            get { return _isWorking; }
            set { Set(ref _isWorking, value, "IsWorking"); }
        }

        /// <summary>
        /// True when the package manager does not tell how far it is. The progress bar
        /// then runs as a band instead of showing a number.
        /// </summary>
        public bool IsIndeterminate
        {
            get { return _isIndeterminate; }
            set { Set(ref _isIndeterminate, value, "IsIndeterminate"); }
        }

        /// <summary>
        /// How far the program is, in percent.
        /// </summary>
        public double Progress
        {
            get { return _progress; }
            set { Set(ref _progress, value, "Progress"); }
        }

        /// <summary>
        /// The number under the progress bar of the program, for example "45%".
        /// It says "Working..." when the package manager does not tell how far it is.
        /// </summary>
        public string ProgressText
        {
            get { return _progressText; }
            set
            {
                if (string.Equals(_progressText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _progressText = value;
                Raise("ProgressText");
            }
        }

        /// <summary>
        /// True when the program could not be installed.
        /// </summary>
        public bool HasFailed
        {
            get { return _hasFailed; }
            set { Set(ref _hasFailed, value, "HasFailed"); }
        }

        /// <summary>The colour of the sign of the program.</summary>
        public string IconColor { get; set; }

        /// <summary>
        /// The letters on the sign of the program. They are made from the name, so
        /// that the page works without any picture.
        /// </summary>
        public string Initials
        {
            get
            {
                if (string.IsNullOrEmpty(Name))
                {
                    return "?";
                }

                string[] words = Name.Split(new[] { ' ', '-', '_', '.', '+' }, StringSplitOptions.RemoveEmptyEntries);

                if (words.Length == 0)
                {
                    return "?";
                }

                if (words.Length == 1)
                {
                    string word = words[0];
                    return word.Length == 1
                        ? word.ToUpperInvariant()
                        : (word.Substring(0, 1) + word.Substring(word.Length - 1)).ToUpperInvariant();
                }

                return (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant();
            }
        }

        /// <summary>
        /// The name of the program in the package manager that is used.
        /// </summary>
        public string GetPackageId(bool useChocolatey)
        {
            if (useChocolatey)
            {
                return ChocoId;
            }

            return WingetId;
        }

        /// <summary>
        /// Puts the program back into the state it was in before it was installed.
        /// </summary>
        public void Reset()
        {
            IsWorking = false;
            IsIndeterminate = false;
            Progress = 0;
            ProgressText = string.Empty;
            HasFailed = false;
        }

        private void Set(ref bool field, bool value, string name)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Raise(name);
        }

        private void Set(ref double field, double value, string name)
        {
            if (Math.Abs(field - value) < 0.001)
            {
                return;
            }

            field = value;
            Raise(name);
        }

        private void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;

            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
