using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SystemProcesses.Desktop.Models;
using SystemProcesses.Desktop.Services;

namespace SystemProcesses.Desktop.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IProcessService _processService;
        private readonly DispatcherTimer _refreshTimer;
        private List<ProcessInfo> _allProcesses = new List<ProcessInfo>();

        private string _searchText = string.Empty;
        private bool _isTreeIsolated;
        private ProcessItemViewModel? _selectedProcess;
        private bool _isPaused;
        private int _refreshInterval = 2000; // Default 2 seconds

        public ObservableCollection<ProcessItemViewModel> Processes { get; }
        public ObservableCollection<string> RefreshIntervals { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        public bool IsTreeIsolated
        {
            get => _isTreeIsolated;
            set
            {
                if (_isTreeIsolated != value)
                {
                    _isTreeIsolated = value;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        public ProcessItemViewModel? SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                if (_selectedProcess != value)
                {
                    _selectedProcess = value;
                    OnPropertyChanged();
                    ((RelayCommand)EndProcessCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)EndProcessTreeCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)ShowProcessDetailsCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)OpenProcessDirectoryCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (_isPaused != value)
                {
                    _isPaused = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PauseResumeText));

                    if (_isPaused)
                        _refreshTimer.Stop();
                    else
                        _refreshTimer.Start();
                }
            }
        }

        public string PauseResumeText => IsPaused ? "Resume" : "Pause";

        public string SelectedRefreshInterval
        {
            get => $"{_refreshInterval / 1000}s";
            set
            {
                var seconds = int.Parse(value.TrimEnd('s'));
                var newInterval = seconds * 1000;

                if (_refreshInterval != newInterval)
                {
                    _refreshInterval = newInterval;
                    OnPropertyChanged();
                    _refreshTimer.Interval = TimeSpan.FromMilliseconds(_refreshInterval);
                }
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand EndProcessCommand { get; }
        public ICommand EndProcessTreeCommand { get; }
        public ICommand ShowProcessDetailsCommand { get; }
        public ICommand OpenProcessDirectoryCommand { get; }
        public ICommand TogglePauseCommand { get; }

        public MainViewModel() : this(new ProcessService())
        {
        }

        public MainViewModel(IProcessService processService)
        {
            _processService = processService;
            Processes = new ObservableCollection<ProcessItemViewModel>();
            RefreshIntervals = new ObservableCollection<string> { "1s", "2s", "5s", "10s" };

            RefreshCommand = new RelayCommand(async _ => await RefreshProcessesAsync());
            EndProcessCommand = new RelayCommand(EndProcess, _ => SelectedProcess != null);
            EndProcessTreeCommand = new RelayCommand(EndProcessTree, _ => SelectedProcess != null);
            ShowProcessDetailsCommand = new RelayCommand(ShowProcessDetails, _ => SelectedProcess != null);
            OpenProcessDirectoryCommand = new RelayCommand(OpenProcessDirectory, _ => SelectedProcess != null);
            TogglePauseCommand = new RelayCommand(_ => IsPaused = !IsPaused);

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_refreshInterval)
            };
            _refreshTimer.Tick += async (s, e) =>
            {
                _refreshTimer.Stop();
                await RefreshProcessesAsync();
            };
            _refreshTimer.Start();

            // Initial load
            Task.Run(async () => await RefreshProcessesAsync());
        }

        private bool isRefreshingProcesses;
        private async Task RefreshProcessesAsync()
        {
            if (isRefreshingProcesses) return;
            isRefreshingProcesses = true;
            try
            {
                _allProcesses = await _processService.GetProcessTreeAsync();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing processes: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (!IsPaused)
                {
                    _refreshTimer.Start();
                }
                isRefreshingProcesses = false;
            }
        }

        private void ApplyFilters()
        {
            var filtered = _allProcesses;

            // Apply tree isolation filter
            if (IsTreeIsolated && SelectedProcess != null)
            {
                filtered = GetProcessAndDescendants(SelectedProcess.Pid, _allProcesses);
            }

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                filtered = FilterBySearch(filtered, SearchText);
            }

            // Update the UI collection
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateProcessCollection(filtered);
            });
        }

        private List<ProcessInfo> GetProcessAndDescendants(int pid, List<ProcessInfo> processes)
        {
            var result = new List<ProcessInfo>();
            var target = FindProcessById(pid, processes);

            if (target != null)
            {
                result.Add(target);
            }

            return result;
        }

        private ProcessInfo? FindProcessById(int pid, List<ProcessInfo> processes)
        {
            foreach (var process in processes)
            {
                if (process.Pid == pid)
                    return process;

                var found = FindProcessById(pid, process.Children);
                if (found != null)
                    return found;
            }

            return null;
        }

        private List<ProcessInfo> FilterBySearch(List<ProcessInfo> processes, string searchText)
        {
            var result = new List<ProcessInfo>();

            foreach (var process in processes)
            {
                var matchesSearch = process.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase);
                var filteredChildren = FilterBySearch(process.Children, searchText);

                if (matchesSearch || filteredChildren.Count > 0)
                {
                    var clone = CloneProcess(process);
                    clone.Children = filteredChildren;
                    result.Add(clone);
                }
            }

            return result;
        }

        private ProcessInfo CloneProcess(ProcessInfo source)
        {
            return new ProcessInfo
            {
                Pid = source.Pid,
                Name = source.Name,
                CpuPercentage = source.CpuPercentage,
                MemoryBytes = source.MemoryBytes,
                VirtualMemoryBytes = source.VirtualMemoryBytes,
                Parameters = source.Parameters,
                IsService = source.IsService,
                ParentPid = source.ParentPid,
                Icon = source.Icon
            };
        }

        private void UpdateProcessCollection(List<ProcessInfo> processes)
        {
            // Clear and rebuild (simple approach - could be optimized with differential updates)
            Processes.Clear();

            foreach (var process in processes)
            {
                Processes.Add(new ProcessItemViewModel(process));
            }
        }

        private void EndProcess(object? parameter)
        {
            if (SelectedProcess == null)
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to end process '{SelectedProcess.Name}' (PID: {SelectedProcess.Pid})?",
                "End Process",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var process = Process.GetProcessById(SelectedProcess.Pid);
                    process.Kill();
                    MessageBox.Show("Process terminated successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to end process: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void EndProcessTree(object? parameter)
        {
            if (SelectedProcess == null)
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to end process tree for '{SelectedProcess.Name}' (PID: {SelectedProcess.Pid}) and all its children?",
                "End Process Tree",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    KillProcessAndChildren(SelectedProcess.Pid);
                    MessageBox.Show("Process tree terminated successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to end process tree: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void KillProcessAndChildren(int pid)
        {
            var processInfo = FindProcessById(pid, _allProcesses);
            if (processInfo == null)
                return;

            // Kill children first
            foreach (var child in processInfo.Children)
            {
                KillProcessAndChildren(child.Pid);
            }

            // Then kill the process itself
            try
            {
                var process = Process.GetProcessById(pid);
                process.Kill();
            }
            catch
            {
                // Process may have already exited
            }
        }

        private void ShowProcessDetails(object? parameter)
        {
            if (SelectedProcess == null)
                return;

            try
            {
                var process = Process.GetProcessById(SelectedProcess.Pid);
                var details = new StringBuilder();

                details.AppendLine($"Process Name: {SelectedProcess.Name}");
                details.AppendLine($"Process ID: {SelectedProcess.Pid}");
                details.AppendLine($"CPU Usage: {SelectedProcess.CpuPercentage:F2}%");
                details.AppendLine($"Memory: {FormatBytes(SelectedProcess.MemoryBytes)}");
                details.AppendLine($"Virtual Memory: {FormatBytes(SelectedProcess.VirtualMemoryBytes)}");
                details.AppendLine($"Is Service: {(SelectedProcess.IsService ? "Yes" : "No")}");

                try
                {
                    details.AppendLine($"Start Time: {process.StartTime}");
                    details.AppendLine($"Total Processor Time: {process.TotalProcessorTime}");
                    details.AppendLine($"Threads: {process.Threads.Count}");
                    details.AppendLine($"Handles: {process.HandleCount}");

                    if (process.MainModule != null)
                    {
                        details.AppendLine($"File Path: {process.MainModule.FileName}");
                    }
                }
                catch
                {
                    details.AppendLine("(Some details unavailable - Access Denied)");
                }

                if (!string.IsNullOrWhiteSpace(SelectedProcess.Parameters))
                {
                    details.AppendLine($"\nCommand Line:");
                    details.AppendLine(SelectedProcess.Parameters);
                }

                MessageBox.Show(details.ToString(), "Process Details",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to get process details: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenProcessDirectory(object? parameter)
        {
            if (SelectedProcess == null)
                return;

            try
            {
                var process = Process.GetProcessById(SelectedProcess.Pid);
                var fileName = process.MainModule?.FileName;

                if (!string.IsNullOrEmpty(fileName))
                {
                    var directory = Path.GetDirectoryName(fileName);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    {
                        Process.Start("explorer.exe", directory);
                    }
                    else
                    {
                        MessageBox.Show("Process directory not found.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Cannot access process file path.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open process directory: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824)
                return $"{bytes / 1_073_741_824.0:F2} GB";
            if (bytes >= 1_048_576)
                return $"{bytes / 1_048_576.0:F2} MB";
            return $"{bytes / 1024.0:F2} KB";
        }

        public void Dispose()
        {
            _refreshTimer?.Stop();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
