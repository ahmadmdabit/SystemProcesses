using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Services.Export;

/// <summary>
/// Renders a process snapshot as a flat GitHub-flavored Markdown table, one row per process.
/// The tree is flattened with <c>Depth</c> and <c>ParentPID</c> columns so hierarchy is preserved.
/// Cell values escape pipe and newline characters to keep the table well-formed.
/// </summary>
public sealed class MarkdownProcessExporter : IProcessExporter
{
    public ExportFormat Format => ExportFormat.Markdown;
    public string FileExtension => ".md";

    public void Render(TextWriter writer, IReadOnlyList<ProcessInfo> snapshot)
    {
        writer.WriteLine("# System Processes Snapshot");
        writer.WriteLine();
        writer.WriteLine($"*{snapshot.Count} processes at {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss 'UTC'}*");
        writer.WriteLine();
        writer.WriteLine("| PID | Parent PID | Depth | Name | CPU % | Working Set | Virtual Memory | Threads | Handles | Is Service | Created (UTC) | Path | Command Line |");
        writer.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");

        for (int i = 0; i < snapshot.Count; i++)
        {
            WriteNode(writer, snapshot[i], 0);
        }
    }

    private static void WriteNode(TextWriter writer, ProcessInfo node, int depth)
    {
        writer.Write("| ");
        WriteCell(writer, node.PidText);
        WriteCell(writer, node.ParentPid);
        WriteCell(writer, depth);
        WriteCell(writer, node.Name);
        WriteCell(writer, node.CpuPercentage);
        WriteCell(writer, node.MemoryBytes);
        WriteCell(writer, node.VirtualMemoryBytes);
        WriteCell(writer, node.ThreadCount);
        WriteCell(writer, node.HandleCount);
        WriteCell(writer, node.IsService ? "Yes" : "No");
        WriteCell(writer, node.CreateTime);
        WriteCell(writer, node.ProcessPath);
        WriteCell(writer, node.Parameters);
        writer.WriteLine(" |");

        var children = node.Children;
        for (int i = 0; i < children.Count; i++)
        {
            WriteNode(writer, children[i], depth + 1);
        }
    }

    private static void WriteCell(TextWriter writer, long value)
    {
        writer.Write(value);
        writer.Write(" | ");
    }

    private static void WriteCell(TextWriter writer, double value)
    {
        writer.Write(value.ToString("F2", CultureInfo.InvariantCulture));
        writer.Write(" | ");
    }

    private static void WriteCell(TextWriter writer, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.Write(" | ");
            return;
        }

        // Escape pipe and newline so a command line containing them can't break the table.
        var escaped = value.Replace("|", "\\|", StringComparison.Ordinal)
                           .Replace("\r\n", " ", StringComparison.Ordinal)
                           .Replace('\r', ' ')
                           .Replace('\n', ' ');

        writer.Write(escaped);
        writer.Write(" | ");
    }
}