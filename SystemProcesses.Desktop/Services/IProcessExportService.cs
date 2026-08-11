using System.Collections.Generic;
using System.Threading.Tasks;

using SystemProcesses.Desktop.Helpers;
using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Services;

/// <summary>
/// Writes a process snapshot to disk in a chosen format.
/// </summary>
public interface IProcessExportService
{
    /// <summary>
    /// Renders <paramref name="snapshot"/> to <paramref name="filePath"/> in <paramref name="format"/>.
    /// </summary>
    /// <param name="snapshot">An immutable snapshot of the processes to export. Never mutated.</param>
    /// <param name="filePath">Destination file path.</param>
    /// <param name="format">Serialization format (CSV, JSON, or Markdown).</param>
    /// <returns>
    /// <see cref="Result.Success"/> on success; <see cref="Result.Failure"/> if the path is invalid,
    /// the exporter is unavailable, or writing the file fails.
    /// </returns>
    Task<Result> ExportAsync(IReadOnlyList<ProcessInfo> snapshot, string filePath, ExportFormat format);
}