using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Serilog;

using SystemProcesses.Desktop.Helpers;
using SystemProcesses.Desktop.Models;
using SystemProcesses.Desktop.Services.Export;

namespace SystemProcesses.Desktop.Services;

/// <summary>
/// Orchestrates export of a process snapshot: selects the format writer, renders off the UI thread,
/// and writes the file asynchronously. Never blocks the caller's thread on rendering or I/O.
/// </summary>
/// <remarks>
/// <para>
/// Implementations of <see cref="IProcessExporter"/> are stateless and registered by
/// <see cref="ExportFormat"/>. Adding a format means adding one exporter file and one registry entry
/// (open/closed principle); no switch statements fan out over formats here.
/// </para>
/// <para>
/// Failure is reported through the project's non-generic <see cref="Result"/> type rather than
/// exceptions, keeping failures as data for the UI layer to render.
/// </para>
/// </remarks>
public sealed class ProcessExportService : IProcessExportService
{
    private readonly IReadOnlyDictionary<ExportFormat, IProcessExporter> exporters;

    public ProcessExportService()
        : this(BuildDefaultExporters().Values)
    {
    }

    // Internal seam for tests.
    internal ProcessExportService(IEnumerable<IProcessExporter> exporters)
    {
        var map = new Dictionary<ExportFormat, IProcessExporter>();
        foreach (var exporter in exporters)
        {
            map[exporter.Format] = exporter;
        }
        this.exporters = map;
    }

    private static IReadOnlyDictionary<ExportFormat, IProcessExporter> BuildDefaultExporters()
    {
        IProcessExporter[] writers =
        [
            new CsvProcessExporter(),
            new JsonProcessExporter(),
            new MarkdownProcessExporter()
        ];
        var map = new Dictionary<ExportFormat, IProcessExporter>(writers.Length);
        foreach (var writer in writers)
        {
            map[writer.Format] = writer;
        }
        return map;
    }

    public async Task<Result> ExportAsync(IReadOnlyList<ProcessInfo> snapshot, string filePath, ExportFormat format)
    {
        // Validate the path up front so we fail fast without touching the disk if it is unusable.
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new Result.Failure(new InvalidOperationException("No export path selected."), "Export");
        }

        if (!exporters.TryGetValue(format, out var exporter))
        {
            return new Result.Failure(new InvalidOperationException($"No exporter is registered for format '{format}'."), "Export");
        }

        try
        {
            // Render the document off the UI thread from the immutable snapshot, then write asynchronously.
            string content = await Task.Run(() => Render(exporter, snapshot)).ConfigureAwait(false);
            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8).ConfigureAwait(false);
            return new Result.Success();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export process snapshot to {FilePath}: {Message}", filePath, ex.Message);
            return new Result.Failure(ex, $"Export to '{filePath}' failed.");
        }
    }

    private static string Render(IProcessExporter exporter, IReadOnlyList<ProcessInfo> snapshot)
    {
        using var rented = StringBuilderPool.Rent();
        var sb = rented.Builder;
        using (var writer = new StringWriter(sb))
        {
            exporter.Render(writer, snapshot);
        }
        return sb.ToString();
    }
}