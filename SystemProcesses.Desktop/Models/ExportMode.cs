namespace SystemProcesses.Desktop.Models;

/// <summary>
/// Selects which portion of the process tree the export writes to disk.
/// </summary>
public enum ExportMode
{
    /// <summary>The full, most-recent process snapshot (ignores search / isolation filters).</summary>
    Full,

    /// <summary>Only the processes currently visible in the tree view (search + isolation applied).</summary>
    Visible
}
