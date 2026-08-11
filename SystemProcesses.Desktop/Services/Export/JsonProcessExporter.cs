using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Services.Export;

/// <summary>
/// Renders a process snapshot as a nested JSON document preserving the full process tree and
/// every metadata field. The document is emitted through a lightweight hand-written writer —
/// intentionally reflection-free (no <c>System.Text.Json</c> serializer) to honor the project's
/// zero-allocation / reflection-less constraints.
/// </summary>
public sealed class JsonProcessExporter : IProcessExporter
{
    public ExportFormat Format => ExportFormat.Json;
    public string FileExtension => ".json";

    public void Render(TextWriter writer, IReadOnlyList<ProcessInfo> snapshot)
    {
        writer.Write('{');
        writer.Write("\"generatedBy\":\"System Processes\",");
        writer.Write("\"exportedAt\":\"");
        writer.Write(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        writer.Write("\",\"processCount\":");
        writer.Write(snapshot.Count.ToString(CultureInfo.InvariantCulture));
        writer.Write(",\"processes\":[");

        for (int i = 0; i < snapshot.Count; i++)
        {
            if (i > 0) writer.Write(',');
            WriteNode(writer, snapshot[i]);
        }

        writer.Write(']');
        writer.Write('}');
    }

    private static void WriteNode(TextWriter writer, ProcessInfo node)
    {
        writer.Write('{');

        WriteNumber(writer, "pid", node.Pid);
        writer.Write(',');
        WriteString(writer, "name", node.Name);
        writer.Write(',');
        writer.Write("\"cpuPercentage\":");
        writer.Write(node.CpuPercentage.ToString("F2", CultureInfo.InvariantCulture));
        writer.Write(',');
        WriteNumber(writer, "workingSetBytes", node.MemoryBytes);
        writer.Write(',');
        WriteNumber(writer, "virtualMemoryBytes", node.VirtualMemoryBytes);
        writer.Write(',');
        WriteNumber(writer, "threadCount", node.ThreadCount);
        writer.Write(',');
        WriteNumber(writer, "handleCount", node.HandleCount);
        writer.Write(',');
        writer.Write(node.IsService ? "\"isService\":true" : "\"isService\":false");
        writer.Write(',');
        WriteNumber(writer, "parentPid", node.ParentPid);
        writer.Write(',');
        WriteStringOrNull(writer, "processPath", node.ProcessPath);
        writer.Write(',');
        WriteString(writer, "commandLine", node.Parameters);
        writer.Write(',');
        WriteNumber(writer, "createTimeTicks", node.CreateTime);

        var children = node.Children;
        writer.Write(",\"children\":[");
        for (int i = 0; i < children.Count; i++)
        {
            if (i > 0) writer.Write(',');
            WriteNode(writer, children[i]);
        }
        writer.Write(']');

        writer.Write('}');
    }

    private static void WriteString(TextWriter writer, string name, string value)
    {
        writer.Write('"');
        writer.Write(name);
        writer.Write("\":\"");
        WriteEscaped(writer, value);
        writer.Write('"');
    }

    private static void WriteStringOrNull(TextWriter writer, string name, string? value)
    {
        writer.Write('"');
        writer.Write(name);
        writer.Write("\":");
        if (string.IsNullOrEmpty(value))
        {
            writer.Write("null");
        }
        else
        {
            writer.Write('"');
            WriteEscaped(writer, value);
            writer.Write('"');
        }
    }

    private static void WriteNumber(TextWriter writer, string name, long value)
    {
        writer.Write('"');
        writer.Write(name);
        writer.Write("\":");
        writer.Write(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteEscaped(TextWriter writer, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': writer.Write("\\\""); break;
                case '\\': writer.Write("\\\\"); break;
                case '\b': writer.Write("\\b"); break;
                case '\f': writer.Write("\\f"); break;
                case '\n': writer.Write("\\n"); break;
                case '\r': writer.Write("\\r"); break;
                case '\t': writer.Write("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        writer.Write("\\u");
                        writer.Write(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        writer.Write(c);
                    }
                    break;
            }
        }
    }
}