using System.Collections.Generic;

namespace SystemProcesses.Desktop.Models;

/// <summary>
/// Provides deep-copy helpers for <see cref="ProcessInfo"/> trees so that a stable,
/// immutable snapshot can be exported without racing a running refresh cycle.
/// </summary>
/// <remarks>
/// <see cref="ProcessService.GetProcessTreeAsync"/> returns and reuses the same
/// <see cref="ProcessInfo"/> instances on every call (mutating CPU/memory/handles via
/// <see cref="ProcessInfo.Update"/> and clearing/rebuilding <see cref="ProcessInfo.Children"/>
/// inside <c>RebuildTreeStructure</c>). Retaining a direct reference to that tree is therefore
/// unsafe for a concurrent consumer. A deep clone guarantees the exporter renders a consistent
/// point-in-time view regardless of when the next refresh runs.
/// </remarks>
public static class ProcessTreeSnapshot
{
    /// <summary>
    /// Recursively clones a process tree into an independent, immutable snapshot.
    /// </summary>
    /// <param name="roots">The source root processes. Not modified.</param>
    /// <returns>A deep copy of the tree with freshly allocated child lists.</returns>
    public static List<ProcessInfo> DeepClone(List<ProcessInfo> roots)
    {
        var clone = new List<ProcessInfo>(roots.Count);
        for (int i = 0; i < roots.Count; i++)
        {
            clone.Add(CloneNode(roots[i]));
        }
        return clone;
    }

    private static ProcessInfo CloneNode(ProcessInfo source)
    {
        var copy = new ProcessInfo
        {
            Pid = source.Pid,
            Name = source.Name,
            CpuPercentage = source.CpuPercentage,
            MemoryBytes = source.MemoryBytes,
            VirtualMemoryBytes = source.VirtualMemoryBytes,
            Parameters = source.Parameters,
            IsService = source.IsService,
            ParentPid = source.ParentPid,
            ProcessPath = source.ProcessPath,
            ThreadCount = source.ThreadCount,
            HandleCount = source.HandleCount,
            CreateTime = source.CreateTime
        };

        var children = source.Children;
        for (int i = 0; i < children.Count; i++)
        {
            copy.Children.Add(CloneNode(children[i]));
        }

        return copy;
    }
}