using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Services.Export;

/// <summary>
/// Renders a process snapshot as a flat CSV table (RFC-4180 quoting), one row per process.
/// The tree is flattened with <c>Depth</c> and <c>ParentPID</c> columns so hierarchy is preserved.
/// </summary>
public sealed class CsvProcessExporter : IProcessExporter
{
    private static readonly char[] SpecialChars = [',', '"', '\r', '\n'];

    public ExportFormat Format => ExportFormat.Csv;
    public string FileExtension => ".csv";

    public void Render(TextWriter writer, IReadOnlyList<ProcessInfo> snapshot)
    {
        writer.WriteLine(
            "PID,Parent PID,Depth,Name,CPU %,Working Set,Virtual Memory,Threads,Handles,Is Service,Created UTC (ticks),Path,Command Line");

        for (int i = 0; i < snapshot.Count; i++)
        {
            WriteNode(writer, snapshot[i], 0);
        }
    }

    private static void WriteNode(TextWriter writer, ProcessInfo node, int depth)
    {
        WriteField(writer, node.PidText);
        WriteField(writer, node.ParentPid);
        WriteField(writer, depth);
        WriteField(writer, node.Name);
        WriteField(writer, node.CpuPercentage);
        WriteField(writer, node.MemoryBytes);
        WriteField(writer, node.VirtualMemoryBytes);
        WriteField(writer, node.ThreadCount);
        WriteField(writer, node.HandleCount);
        WriteField(writer, node.IsService ? "Yes" : "No");
        WriteField(writer, node.CreateTime);
        WriteField(writer, node.ProcessPath);
        WriteField(writer, node.Parameters);
        writer.WriteLine();

        var children = node.Children;
        for (int i = 0; i < children.Count; i++)
        {
            WriteNode(writer, children[i], depth + 1);
        }
    }

    private static void WriteField(TextWriter writer, long value)
    {
        writer.Write(value);
        writer.Write(',');
    }

    private static void WriteField(TextWriter writer, double value)
    {
        writer.Write(value.ToString("F2", CultureInfo.InvariantCulture));
        writer.Write(',');
    }

    private static void WriteField(TextWriter writer, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.Write(',');
            return;
        }

        // RFC-4180: quote only when the field contains a delimiter, quote, or newline.
        if (value.IndexOfAny(SpecialChars) < 0)
        {
            writer.Write(value);
        }
        else
        {
            writer.Write('"');
            writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            writer.Write('"');
        }
        writer.Write(',');
    }
}