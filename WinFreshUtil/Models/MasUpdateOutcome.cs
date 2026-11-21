namespace WinFreshUtil.Models
{
    /// <summary>
    /// Result of updating the activation scripts from the project on GitHub.
    /// </summary>
    public sealed class MasUpdateOutcome
    {
        public bool Success { get; set; }

        /// <summary>The commit that was downloaded, when the update succeeded.</summary>
        public string Commit { get; set; }

        public string Message { get; set; }
    }
}
