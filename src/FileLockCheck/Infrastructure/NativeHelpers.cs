using FileLockCheck.Infrastructure;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public class NativeHelpers
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static string? GetSelectedPathInExplorer()
    {
        object? shellApp = null;

        try
        {
            IntPtr foregroundHwnd = GetForegroundWindow();

            Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");

            if (shellAppType == null)
            {
                return null;
            }

            shellApp = Activator.CreateInstance(shellAppType);

            if(shellApp == null)
            {
                return null;
            }

            dynamic windows = ((dynamic)shellApp).Windows();

            foreach (dynamic window in windows)
            {
                try
                {
                    if (window.HWND == (long)foregroundHwnd)
                    {
                        dynamic selectedItems = window.Document.SelectedItems();
                        if (selectedItems.Count > 0)
                        {
                            return selectedItems.Item(0).Path;
                        }
                    }
                }
                finally
                {
                    if (window != null)
                    {
                        Marshal.ReleaseComObject(window);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetSelectedPathInExplorer: {ex.Message}");
        }
        finally
        {
            if (shellApp != null)
            {
                Marshal.ReleaseComObject(shellApp);
            }
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmRegisterResources(uint dwSessionHandle, uint nFiles, string[] rgsFilenames, uint nApplications, [In] ref RM_UNIQUE_PROCESS rgApplications, uint nServices, string[] rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, out uint lpdwRebootReasons);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, [Out] StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int MaxPathLength = 255;
    private const int LargeBatchSize = 5000;

    public static async Task<List<LockingProcess>> GetLockingProcessesAsync(IEnumerable<string> paths)
    {
        return await Task.Run(() => GetLockingProcesses(paths));
    }

    private static bool IsFileLocked(string filePath)
    {
        try
        {
            // Request exclusive Access (FileShare.None). 
            using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // Possible... this file isn't locked by another process.
                return false;
            }
        }
        catch (IOException)
        {
            // Not possible, file is locked by another process.
            return true; 
        }
        catch
        {
            // Access Violation
            return false;
        }
    }

    private static ImageSource? GetIconFromExe(string exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            return null;
        }

        try
        {
            using (Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
            {
                if (icon != null)
                {
                    var imageSource = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    imageSource.Freeze();

                    return imageSource;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public static List<LockingProcess> GetLockingProcesses(IEnumerable<string> paths)
    {
        Stopwatch swTotal = Stopwatch.StartNew();
        ConcurrentDictionary<int, ConcurrentBag<string>> processToFileMap = new ConcurrentDictionary<int, ConcurrentBag<string>>();
        ConcurrentBag<string> allFilesToCheck = new ConcurrentBag<string>();

        Debug.WriteLine($"\n[RM-LOG] === START ({DateTime.Now.ToLongTimeString()}) ===");

        List<string> filesToScan = new List<string>();
        foreach (string path in paths)
        {
            try
            {
                if (File.Exists(path) && path.Length <= MaxPathLength)
                {
                    filesToScan.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    filesToScan.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                                                  .Where(f => f.Length <= MaxPathLength));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing path '{path}': {ex.Message}");
            }
        }

        Debug.WriteLine($"[RM-LOG] Discovery: {filesToScan.Count} Files Found. Start Pre-Filter...");

        // Check in Parallel if File is locked at all
        Parallel.ForEach(filesToScan, file =>
        {
            if (IsFileLocked(file))
            {
                allFilesToCheck.Add(file);
            }
        });

        Debug.WriteLine($"[RM-LOG] Pre-Filter: {allFilesToCheck.Count} locked files for RM detected.");

        // Invoke the Restart Manager with the reduced list of locked files. We batch them to avoid hitting limits and to improve performance.
        if (allFilesToCheck.Count > 0)
        {
            Stopwatch swRM = Stopwatch.StartNew();
            string[][] chunks = allFilesToCheck.ToArray().Chunk(LargeBatchSize).ToArray();

            foreach (string[]? chunk in chunks)
            {
                ProcessLargeBatch(chunk, processToFileMap);
            }
            swRM.Stop();
            Debug.WriteLine($"[RM-LOG] Restart Manager Phase: {swRM.ElapsedMilliseconds}ms");
        }

        // Resolved the reduced list of processes to get details like ProcessName, MainWindowTitle, ExePath, Description, MemoryUsage,
        // StartTime and Icon. This is the most expensive part, so we do it at the end.
        Stopwatch swResolve = Stopwatch.StartNew();

        var result = ResolveProcesses(processToFileMap);

        swResolve.Stop();

        swTotal.Stop();
        Debug.WriteLine($"[RM-LOG] Resolve Runtime: {swResolve.ElapsedMilliseconds}ms");
        Debug.WriteLine($"[RM-LOG] Total elapsed: {swTotal.ElapsedMilliseconds}ms. {result.Count} Processes found.");

        return result;
    }

    private static void ProcessLargeBatch(string[] files, ConcurrentDictionary<int, ConcurrentBag<string>> map)
    {
        uint handle = 0;
        if (RmStartSession(out handle, 0, Guid.NewGuid().ToString()) != 0)
        {
            return;
        }

        try
        {
            RM_UNIQUE_PROCESS rgApp = new RM_UNIQUE_PROCESS();
            if (RmRegisterResources(handle, (uint)files.Length, files, 0, ref rgApp, 0, []) == 0)
            {
                uint pnProcInfoNeeded = 0, pnProcInfo = 100, lpdwRebootReasons = 0;
                RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[pnProcInfo];

                int res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, out lpdwRebootReasons);
                if (res == 234)
                {
                    processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;
                    res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, out lpdwRebootReasons);
                }

                if (res == 0 && pnProcInfo > 0)
                {
                    string exampleFile = Path.GetFileName(files[0]);
                    for (int i = 0; i < pnProcInfo; i++)
                    {
                        int pid = processInfo[i].Process.dwProcessId;
                        var list = map.GetOrAdd(pid, _ => new ConcurrentBag<string>());
                        if (list.Count < 3)
                        {
                            list.Add(exampleFile);
                        }
                    }
                }
            }
        }
        finally
        {
            RmEndSession(handle);
        }
    }

    private static List<LockingProcess> ResolveProcesses(ConcurrentDictionary<int, ConcurrentBag<string>> map)
    {
        ConcurrentBag<LockingProcess> result = new ConcurrentBag<LockingProcess>();

        Parallel.ForEach(map, kvp =>
        {
            int pid = kvp.Key;
            if (pid <= 4)
            {
                return; // Ignore System-PIDs
            }

            string procName = "Unbekannt";
            string exePath = "Zugriff verweigert";
            string title = string.Empty;
            string? description = null;
            string? memoryUsage = null;
            string? startTime = null;
            ImageSource? iconSource = null;

            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    StringBuilder sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        exePath = sb.ToString();
                        procName = Path.GetFileNameWithoutExtension(exePath);
                    }
                }
                finally { CloseHandle(hProcess); }
            }

            // Extract Metadata if we have a valid exe path
            if (exePath != "Zugriff verweigert" && File.Exists(exePath))
            {
                iconSource = GetIconFromExe(exePath);
                try
                {
                    FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exePath);
                    description = fvi.FileDescription;
                }
                catch { }
            }

            // Fallback for ProcessName, MainWindowTitle, MemoryUsage, StartTime
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    if (procName == "Unbekannt")
                    {
                        procName = p.ProcessName;
                    }

                    title = p.MainWindowTitle;

                    if (string.IsNullOrEmpty(description))
                    {
                        description = p.ProcessName;
                    }

                    // Memory (Working Set in MB)
                    double memoryMb = p.WorkingSet64 / (1024.0 * 1024.0);
                    memoryUsage = $"{memoryMb:F1} MB";

                    startTime = p.StartTime.ToString("g");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error retrieving process details for PID {pid}: {ex.Message}");
            }

            result.Add(new LockingProcess
            {
                ProcessId = pid,
                ProcessName = procName,
                ExePath = exePath,
                MainWindowTitle = title,
                Description = description ?? "Keine Beschreibung verfügbar",
                MemoryUsage = memoryUsage ?? "Unbekannt",
                StartTime = startTime ?? "Unbekannt",
                Icon = iconSource,
                LockedFiles = kvp.Value.Distinct().ToList()
            });
        });

        return result.OrderBy(p => p.ProcessName).ToList();
    }
}