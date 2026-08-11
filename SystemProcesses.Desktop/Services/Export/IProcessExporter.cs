using System.Collections.Generic;
using System.IO;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Services.Export;

/// <summary>
/// Renders a process snapshot into a specific export format.
/// </summary>
/// <remarks>
/// Implementations must be stateless and thread-safe: the exporter renders into the
/// caller-provided <see cref="TextWriter"/> (backed by a pooled <see cref="System.Text.StringBuilder"/>)
/// in a single pass so the hot writing path allocates as little as possible. Writers rely only on
/// the BCL and the project's existing <c>StringBuilderPool</c>; no reflection, no third-party
/// serializers. Implementations are keyed by <see cref="Format"/> in
/// <see cref="ProcessExportService"/> so a new format is a drop-in file + one registry entry (OCP).
/// </remarks>
public interface IProcessExporter
{
    /// <summary>The format this exporter emits.</summary>
    ExportFormat Format { get; }

    /// <summary>Default file extension including the leading dot (e.g. ".csv").</summary>
    string FileExtension { get; }

    /// <summary>
    /// Writes <paramref name="snapshot"/> to <paramref name="writer"/> in this exporter's format.
    /// </summary>
    /// <param name="writer">Target writer. The caller owns the writer's lifetime.</param>
    /// <param name="snapshot">The immutable process snapshot to export.</param>
    void Render(TextWriter writer, IReadOnlyList<ProcessInfo> snapshot);
}