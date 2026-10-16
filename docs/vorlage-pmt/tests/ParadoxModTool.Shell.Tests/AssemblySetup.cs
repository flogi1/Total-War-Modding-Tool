using Xunit;

// Die Einstellungen (AppSettings-Instanzen) sind je Test eigen; WPF-Steuerelemente laufen aber auf eigenen STA-Threads: nacheinander ist sicherer.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
