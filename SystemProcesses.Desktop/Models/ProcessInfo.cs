using System.Collections.Generic;
using System.Windows.Media;

namespace SystemProcesses.Desktop.Models;

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

    // Initialized once, reused forever.
    public List<ProcessInfo> Children { get; } = new List<ProcessInfo>();

    // Helper for differential updates
    public void Update(double cpu, long mem, long virtualMem)
    {
        CpuPercentage = cpu;
        MemoryBytes = mem;
        VirtualMemoryBytes = virtualMem;
    }
}