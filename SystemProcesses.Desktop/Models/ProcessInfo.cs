using System.Collections.Generic;
using System.Windows.Media;

namespace SystemProcesses.Desktop.Models
{
    public class ProcessInfo
    {
        public int Pid { get; set; }
        public string Name { get; set; } = string.Empty;
        public double CpuPercentage { get; set; }
        public long MemoryBytes { get; set; }
        public long VirtualMemoryBytes { get; set; }
        public string Parameters { get; set; } = string.Empty;
        public bool IsService { get; set; }
        public int ParentPid { get; set; }
        public ImageSource? Icon { get; set; }
        public List<ProcessInfo> Children { get; set; } = new List<ProcessInfo>();
    }
}
