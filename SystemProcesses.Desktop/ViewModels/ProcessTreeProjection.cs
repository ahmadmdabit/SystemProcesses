using System.Collections.Generic;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.ViewModels;

/// <summary>
/// Flattens the visible (already-filtered) process tree from the UI's <see cref="ProcessItemViewModel"/>
/// shape into a plain <see cref="ProcessInfo"/> list, preserving the parent/child hierarchy via
/// <see cref="ProcessInfo.Children"/> so exporters can render the same structure as a full snapshot.
/// </summary>
/// <remarks>
/// Exists in the ViewModels layer (not the Desktop root) because it consumes the ViewModel type; it
/// performs no mutation and allocates only the output list, so it is safe to call from the command.
/// </remarks>
public static class ProcessTreeProjection
{
    /// <summary>
    /// Recursively collects every <see cref="ProcessInfo"/> reachable from the supplied view-model nodes.
    /// </summary>
    /// <param name="nodes">The root view models (e.g. <c>MainViewModel.Processes</c>). Not modified.</param>
    /// <returns>A flat list whose <see cref="ProcessInfo.Children"/> still encode the hierarchy.</returns>
    public static List<ProcessInfo> FlattenVisible(IEnumerable<ProcessItemViewModel> nodes)
    {
        var result = new List<ProcessInfo>();
        Collect(nodes, result);
        return result;
    }

    private static void Collect(IEnumerable<ProcessItemViewModel> nodes, List<ProcessInfo> result)
    {
        foreach (var node in nodes)
        {
            result.Add(node.ProcessInfo);
            Collect(node.Children, result);
        }
    }
}
