namespace SystemProcesses.Desktop.Models;

/// <summary>
/// Represents the user's export choices: the destination file path, the serialization format,
/// and which portion of the process tree to export.
/// Produced by the export dialog and consumed by the export service.
/// </summary>
/// <param name="FilePath">The absolute path to write the snapshot to.</param>
/// <param name="Format">The serialization format (CSV, JSON, or Markdown).</param>
/// <param name="Mode">Whether to export the full snapshot or only the visible (filtered) processes.</param>
public readonly record struct ProcessExportSettings(string FilePath, ExportFormat Format, ExportMode Mode);
