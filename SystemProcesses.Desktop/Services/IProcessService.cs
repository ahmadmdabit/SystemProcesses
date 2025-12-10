using System.Collections.Generic;
using System.Threading.Tasks;
using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Services
{
    public interface IProcessService
    {
        Task<List<ProcessInfo>> GetProcessTreeAsync();
    }
}
