namespace Leostrap.Models
{
    internal class WatcherData
    {
        public int ProcessId { get; set; }

        public string ProcessName { get; set; } = null!;

        public string? LogFile { get; set; }

        public List<int>? AutoclosePids { get; set; }

        public bool ShowNotifyIcon { get; set; } = true;
    }
}
