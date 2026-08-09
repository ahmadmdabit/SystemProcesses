using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

using Serilog;

using SystemProcesses.Desktop.Helpers;
using SystemProcesses.Desktop.ViewModels;

namespace SystemProcesses.Desktop.Services;

/// <summary>
/// Service for exiting processes gracefully or forcefully.
/// </summary>
/// <remarks>
/// <para>
/// Consolidates all process exition logic (graceful and force) into a single service.
/// Handles both individual processes and process trees with comprehensive error handling.
/// </para>
/// <para>
/// Exition strategies:
/// - Graceful: Sends CloseMainWindow signal, waits 3 seconds, prompts for force stop if needed
/// - Force: Immediately exits process with Stop()
/// </para>
/// <para>
/// Tree exition uses bottom-up approach (children first) to avoid orphaned processes.
/// All operations include comprehensive error handling and logging.
/// </para>
/// </remarks>
public class RuntimeUnitExitor
{
    private readonly ILiteDialogService dialogService;
    private readonly ConcurrentDictionary<int, ProcessItemViewModel> viewModelCache;

    /// <summary>
    /// Initializes a new instance of the ProcessExitor service.
    /// </summary>
    /// <param name="dialogService">Service for showing dialogs to the user.</param>
    /// <param name="viewModelCache">Cache of process ViewModels for tree traversal.</param>
    public RuntimeUnitExitor(
        ILiteDialogService dialogService,
        ConcurrentDictionary<int, ProcessItemViewModel> viewModelCache)
    {
        this.dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        this.viewModelCache = viewModelCache ?? throw new ArgumentNullException(nameof(viewModelCache));
    }

    /// <summary>
    /// Gracefully exits a single process.
    /// </summary>
    /// <param name="pid">The process ID to exit.</param>
    /// <param name="name">The process name for display.</param>
    /// <remarks>
    /// <para>
    /// Sends CloseMainWindow signal and waits up to 3 seconds for graceful exit.
    /// If the process doesn't respond, prompts the user to force stop it.
    /// </para>
    /// <para>
    /// Error handling:
    /// - Access denied: Logged as warning, user prompted to force stop
    /// - Process exited: Logged as warning, operation continues
    /// - No window: Logged as warning, user prompted to force stop
    /// </para>
    /// </remarks>
    public async Task GracefullyExitAsync(int pid, string name)
    {
        if (await dialogService.ShowAsync(new LiteDialogRequest(
                title: "Graceful End",
                message: $"Send close request to '{name}' (PID: {pid})?",
                buttons: LiteDialogButton.YesNo,
                image: LiteDialogImage.Question
            )) != LiteDialogResult.Yes)
        {
            return;
        }

        await Task.Run(async () =>
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Refresh();

                if (process.CloseMainWindow())
                {
                    if (!process.WaitForExit(AppConstants.GracefulShutdownTimeoutMs))
                    {
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            if (await dialogService.ShowAsync(new LiteDialogRequest(
                                    title: "Process Unresponsive",
                                    message: $"Process '{name}' did not close within 3 seconds.\nForce stop it?",
                                    buttons: LiteDialogButton.YesNo,
                                    image: LiteDialogImage.Warning
                                )) == LiteDialogResult.Yes)
                            {
                                process.Kill();
                            }
                        });
                    }
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        if (await dialogService.ShowAsync(new LiteDialogRequest(
                                title: "No Window Found",
                                message: $"Could not send close request (No Window or Unresponsive).\nForce stop '{name}'?",
                                buttons: LiteDialogButton.YesNo,
                                image: LiteDialogImage.Warning
                            )) == LiteDialogResult.Yes)
                        {
                            process.Kill();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to gracefully exit process {ProcessName} (PID {Pid})", name, pid);
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                    await dialogService.ShowAsync(new LiteDialogRequest(
                        title: "Error",
                        message: $"Error: {ex.Message}",
                        buttons: LiteDialogButton.OK,
                        image: LiteDialogImage.Error
                    )));
            }
        });
    }

    /// <summary>
    /// Gracefully exits a process and all its children.
    /// </summary>
    /// <param name="rootPid">The root process ID to exit.</param>
    /// <param name="rootName">The root process name for display.</param>
    /// <remarks>
    /// <para>
    /// Attempts to close the entire process tree gracefully. Waits up to 3 seconds per attempt
    /// for processes to exit. If any processes remain after 3 attempts, prompts the user to
    /// force stop the entire tree.
    /// </para>
    /// <para>Uses bottom-up exition (children first) to avoid orphaned processes.</para>
    /// <para>
    /// Error handling:
    /// - Access denied: Logged as warning, continues with other processes
    /// - Process exited: Logged as warning, continues
    /// - Incomplete shutdown: User prompted to force stop
    /// </para>
    /// </remarks>
    public async Task GracefullyExitTreeAsync(int rootPid, string rootName)
    {
        if (await dialogService.ShowAsync(new LiteDialogRequest(
                title: "Graceful End Tree",
                message: $"Send close request to '{rootName}' (PID: {rootPid}) and all children?",
                buttons: LiteDialogButton.YesNo,
                image: LiteDialogImage.Question
            )) != LiteDialogResult.Yes)
        {
            return;
        }

        // 1. Collect all PIDs in the tree (Bottom-Up approach preferred for closing)
        var pidsToClose = new List<int>();
        void CollectPids(ProcessItemViewModel vm)
        {
            foreach (var child in vm.Children) CollectPids(child);
            pidsToClose.Add(vm.Pid);
        }

        // Use cache to find the current tree structure
        if (viewModelCache.TryGetValue(rootPid, out var rootVm))
        {
            CollectPids(rootVm);
        }
        else
        {
            pidsToClose.Add(rootPid);
        }

        // 2. Send Close Requests asynchronously
        await Task.Run(() =>
        {
            foreach (var pid in pidsToClose)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    p.Refresh();
                    p.CloseMainWindow();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to send close request to process {Pid}", pid);
                }
            }
        });

        var remaining = new HashSet<int>(pidsToClose);
        var remainingTries = AppConstants.GracefulTreeShutdownMaxAttempts;
        var delay = AppConstants.GracefulTreeShutdownDelayMs;
        var tryNumber = 0;

        while (true)
        {
            ++tryNumber;

            // 3. Verify
            var closedPids = new HashSet<int>();
            foreach (var pid in remaining)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (p == null)
                    {
                        closedPids.Add(pid);
                    }
                    else
                    {
                        p.Refresh();
                        if (p.HasExited)
                        {
                            closedPids.Add(pid);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to verify process exit status for PID {Pid}", pid);
                }
            }

            foreach (var pid in closedPids)
            {
                remaining.Remove(pid);
            }

            if (remaining.Count == 0)
            {
                break;
            }
            else
            {
                if (--remainingTries == 0)
                    break;

                remaining.Clear();
            }

            // 4. Wait for processes to exit
            await Task.Delay(delay * tryNumber);
        }

        if (remaining.Count > 0)
        {
            if (await dialogService.ShowAsync(new LiteDialogRequest(
                    title: "Incomplete Shutdown",
                    message: $"{remaining.Count} processes in the tree are still running.\nForce stop the entire tree?",
                    buttons: LiteDialogButton.YesNo,
                    image: LiteDialogImage.Warning
                )) == LiteDialogResult.Yes)
            {
                try
                {
                    ForceExitTree(rootPid);
                    await dialogService.ShowAsync(new LiteDialogRequest(
                        title: "Success",
                        message: "Tree force exitd.",
                        buttons: LiteDialogButton.OK,
                        image: LiteDialogImage.Success
                    ));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to force exit process tree for PID {Pid}", rootPid);
                    await dialogService.ShowAsync(new LiteDialogRequest(
                        title: "Error",
                        message: $"Error exiting tree: {ex.Message}",
                        buttons: LiteDialogButton.OK,
                        image: LiteDialogImage.Error
                    ));
                }
            }
        }
        else
        {
            await dialogService.ShowAsync(new LiteDialogRequest(
                title: "Success",
                message: "All processes in tree closed successfully.",
                buttons: LiteDialogButton.OK,
                image: LiteDialogImage.Success
            ));
        }
    }

    /// <summary>
    /// Force exits a single process immediately.
    /// </summary>
    /// <param name="pid">The process ID to exit.</param>
    /// <remarks>
    /// <para>
    /// Sends a SIGKILL signal to the process, exiting it immediately without
    /// allowing cleanup. Use GracefullyExitAsync for graceful shutdown.
    /// </para>
    /// <para>
    /// Error handling:
    /// - Access denied: Logged as warning, error shown to user
    /// - Process exited: Logged as warning, error shown to user
    /// </para>
    /// </remarks>
    public async Task ForceExitAsync(int pid, string name)
    {
        if (await dialogService.ShowAsync(new LiteDialogRequest(
                title: "End Process",
                message: $"End process '{name}' (PID: {pid})?",
                buttons: LiteDialogButton.YesNo,
                image: LiteDialogImage.Warning
            )) == LiteDialogResult.Yes)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
                await dialogService.ShowAsync(new LiteDialogRequest(
                    title: "Success",
                    message: "Process exitd successfully.",
                    buttons: LiteDialogButton.OK,
                    image: LiteDialogImage.Success
                ));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to force exit process {ProcessName} (PID {Pid})", name, pid);
                await dialogService.ShowAsync(new LiteDialogRequest(
                    title: "Error",
                    message: $"Failed to end process: {ex.Message}",
                    buttons: LiteDialogButton.OK,
                    image: LiteDialogImage.Error
                ));
            }
        }
    }

    /// <summary>
    /// Force exits a process and all its children.
    /// </summary>
    /// <param name="rootPid">The root process ID to exit.</param>
    /// <remarks>
    /// <para>
    /// Recursively exits the entire process tree. Children are exitd first
    /// (bottom-up approach) to avoid orphaned processes.
    /// </para>
    /// <para>
    /// Error handling:
    /// - Access denied: Logged as warning, continues with other processes
    /// - Process exited: Logged as warning, continues
    /// </para>
    /// </remarks>
    public async Task ForceExitTreeAsync(int rootPid, string rootName)
    {
        if (await dialogService.ShowAsync(new LiteDialogRequest(
                title: "End Process Tree",
                message: $"Are you sure you want to end process tree for '{rootName}' (PID: {rootPid}) and all its children?",
                buttons: LiteDialogButton.YesNo,
                image: LiteDialogImage.Warning
            )) == LiteDialogResult.Yes)
        {
            try
            {
                ForceExitTree(rootPid);
                await dialogService.ShowAsync(new LiteDialogRequest(
                    title: "Success",
                    message: "Process tree exitd successfully.",
                    buttons: LiteDialogButton.OK,
                    image: LiteDialogImage.Success
                ));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to force exit process tree for PID {Pid}", rootPid);
                await dialogService.ShowAsync(new LiteDialogRequest(
                    title: "Error",
                    message: $"Failed to end process tree: {ex.Message}",
                    buttons: LiteDialogButton.OK,
                    image: LiteDialogImage.Error
                ));
            }
        }
    }

    /// <summary>
    /// Recursively exits a process and all its children (internal helper).
    /// </summary>
    /// <param name="pid">The process ID to exit.</param>
    /// <remarks>
    /// Uses bottom-up exition (children first) to avoid orphaned processes.
    /// Logs warnings for access denied or already exited processes but continues.
    /// </remarks>
    private void ForceExitTree(int pid)
    {
        if (!viewModelCache.TryGetValue(pid, out var vm))
            return;

        var processInfo = vm.ProcessInfo;

        // Stop children first
        foreach (var child in processInfo.Children)
        {
            ForceExitTree(child.Pid);
        }

        // Then stop the process itself
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Refresh();
            process.Kill();
        }
        catch (Exception ex)
        {
            // Process may have already exited
            Log.Warning(ex, "Failed to exit process {Pid}", pid);
        }
    }
}
