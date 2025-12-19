using System;
using System.Windows;

using Serilog;

namespace SystemProcesses.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 1. Configure Serilog
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Warning() // PERFORMANCE: Only log Warnings and Errors. Ignore Info/Debug to save IO.
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()  // Critical for debugging your async code
            .Enrich.WithProcessId();
#if DEBUG
        loggerConfiguration.WriteTo.Debug();
#endif
        loggerConfiguration.WriteTo.Async(a => a.File(
                "logs/SystemProcesses-.log",
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [Thread:{ThreadId}] {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7
            ));

        Log.Logger = loggerConfiguration.CreateLogger();

        // 2. Global Exception Handling (Catch crashes)
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "Application crashed (Domain)");
            Log.CloseAndFlush();
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log.Fatal(args.Exception, "Application crashed (Dispatcher)");
            Log.CloseAndFlush();
            // Optional: args.Handled = true;
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application Exiting");
        Log.CloseAndFlush(); // CRITICAL: Ensures all async logs are written to disk
        base.OnExit(e);
    }
}
