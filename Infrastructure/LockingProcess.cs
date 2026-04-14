namespace FileLockCheck.Infrastructure;

/// <summary>
/// All information about a process that is locking the file. This class is used to display detailed information 
/// about the locking processes in the UI, allowing users to understand which processes are causing the lock 
/// and potentially take action to close them if necessary.
/// </summary>
public class LockingProcess
{
    /// <summary>
    /// Process Name of the locking process. This is the name of the executable without the extension, 
    /// e.g., "notepad" for "notepad.exe". This can help users quickly identify the process, especially 
    /// if they are familiar with common applications.
    /// </summary>
    public string? ProcessName { get; set; }

    /// <summary>
    /// Process ID of the locking process. This can be used to attempt to close the 
    /// process or to display more information about it.
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// Main Window Title of the locking process. This can provide additional context 
    /// about what the process is doing, especially if multiple instances of the same 
    /// process are running.
    /// </summary>
    public string? MainWindowTitle { get; set; }

    /// <summary>
    /// Path to the executable of the locking process. This can be useful for advanced users 
    /// who want to investigate the process further or for debugging purposes.
    /// </summary>
    public string? ExePath { get; set; }

    /// <summary>
    /// Description of the locking process. This can be a more user-friendly name or description of the 
    /// process, which can help users understand what the process is without needing to look it up.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Current Memory Usage of the locking process. This can provide insight into how resource-intensive 
    /// the process is, which might help users decide whether to close it or not. It can also indicate 
    /// if the process is behaving abnormally.
    /// </summary>
    public string? MemoryUsage { get; set; }

    /// <summary>
    /// The Start Time of the locking process. This can help users understand how long the process has been running, 
    /// which might be relevant if the process is stuck or has been running for an unusually long time.
    /// </summary>
    public string? StartTime { get; set; }

    /// <summary>
    /// Icon of the locking process. This can provide a visual cue to users, making it easier to identify the 
    /// process at a glance, especially if they are familiar with the application's icon.
    /// </summary>
    public System.Windows.Media.ImageSource? Icon { get; set; }
}