using System.Collections.Generic;
using System.Threading.Tasks;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Services;

public interface IProcessService
{
    /// <summary>
    /// Returns the root nodes of the process tree.
    /// Subsequent calls return the SAME ProcessInfo instances with updated properties.
    /// </summary>
    Task<(List<ProcessInfo> Roots, SystemStats Stats)> GetProcessTreeAsync();
}