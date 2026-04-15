using FileLockCheck.Infrastructure;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FileLockCheck.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private string? _targetFilePath;
    private string? _statusMessage;
    private bool _hasLocks;

    public ObservableCollection<LockingProcess> LockingProcesses { get; } = new ObservableCollection<LockingProcess>();

    public string? TargetFilePath
    {
        get => _targetFilePath;
        set { _targetFilePath = value; OnPropertyChanged(); }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool HasLocks
    {
        get => _hasLocks;
        set { _hasLocks = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Analyzes the currently selected file in Explorer and checks for locks.
    /// </summary>
    public void AnalyzeCurrentSelection()
    {
        string? selectedFile = NativeHelpers.GetSelectedPathInExplorer();

        LockingProcesses.Clear();

        if (string.IsNullOrEmpty(selectedFile))
        {
            TargetFilePath = Properties.Resources.StatusNoFileSelected;
            StatusMessage = Properties.Resources.StatusPleaseSelectFile;
            HasLocks = false;
            return;
        }

        TargetFilePath = selectedFile;

        List<LockingProcess> processes = NativeHelpers.GetLockingProcesses([ selectedFile ]);

        if (processes.Count == 0)
        {
            StatusMessage = Properties.Resources.StatusNotLocked;
            HasLocks = false;
        }
        else
        {
            StatusMessage = string.Format(Properties.Resources.StatusLockedFormat, processes.Count);
            HasLocks = true;
            foreach (LockingProcess p in processes)
            {
                LockingProcesses.Add(p);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}