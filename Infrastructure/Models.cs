using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileLockCheck.Infrastructure;

public static class NativeHelpers
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// Holt den Pfad der aktuell markierten Datei aus dem aktiven Windows Explorer Fenster.
    /// </summary>
    public static string GetSelectedFileInExplorer()
    {
        IntPtr foregroundHwnd = GetForegroundWindow();

        // COM Late Binding, um externe Referenzen zu vermeiden
        Type shellAppType = Type.GetTypeFromProgID("Shell.Application");
        dynamic shellApp = Activator.CreateInstance(shellAppType);

        foreach (dynamic window in shellApp.Windows())
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
        return null;
    }

    // --- Restart Manager API Structs & Imports (Zum Finden gesperrter Dateien) ---
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

    /// <summary>
    /// Fragt Windows, welche Prozesse eine bestimmte Datei aktuell blockieren.
    /// </summary>
    public static List<LockingProcess> GetLockingProcesses(string path)
    {
        uint handle = 0;
        string key = Guid.NewGuid().ToString();
        List<LockingProcess> processes = new List<LockingProcess>();

        int res = RmStartSession(out handle, 0, key);
        if (res != 0) return processes;

        try
        {
            string[] resources = { path };
            RM_UNIQUE_PROCESS defaultProcess = new RM_UNIQUE_PROCESS();
            res = RmRegisterResources(handle, (uint)resources.Length, resources, 0, ref defaultProcess, 0, null);

            if (res != 0) return processes;

            uint pnProcInfoNeeded = 0, pnProcInfo = 0, lpdwRebootReasons = 0;
            res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, out lpdwRebootReasons);

            if (res == 234) // ERROR_MORE_DATA - Wir brauchen ein größeres Array
            {
                RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;
                res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, out lpdwRebootReasons);

                if (res == 0)
                {
                    for (int i = 0; i < pnProcInfo; i++)
                    {
                        try
                        {
                            var proc = Process.GetProcessById(processInfo[i].Process.dwProcessId);

                            // Standardwerte, falls Rechte fehlen
                            path = "Zugriff verweigert (Admin-Rechte benötigt)";
                            string desc = proc.ProcessName;
                            string startTime = "Unbekannt";
                            string memUsage = "Unbekannt";
                            System.Windows.Media.ImageSource icon = null;

                            try
                            {
                                path = proc.MainModule?.FileName;
                                desc = proc.MainModule?.FileVersionInfo?.FileDescription ?? proc.ProcessName;
                                startTime = proc.StartTime.ToString("g");
                                double ramMb = proc.WorkingSet64 / (1024.0 * 1024.0);
                                memUsage = $"{ramMb:F1} MB";

                                // Versuche das Icon der .exe zu extrahieren
                                if (!string.IsNullOrEmpty(path))
                                {
                                    using (var sysicon = System.Drawing.Icon.ExtractAssociatedIcon(path))
                                    {
                                        if (sysicon != null)
                                        {
                                            icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                                sysicon.Handle,
                                                System.Windows.Int32Rect.Empty,
                                                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                                            icon.Freeze(); // Wichtig für WPF, um den Thread-Zugriff abzusichern
                                        }
                                    }
                                }
                            }
                            catch { /* Fehler ignorieren, z.B. wenn es sich um Systemprozesse handelt */ }

                            processes.Add(new LockingProcess
                            {
                                ProcessName = proc.ProcessName,
                                ProcessId = proc.Id,
                                MainWindowTitle = proc.MainWindowTitle,
                                ExePath = path,
                                Description = desc,
                                MemoryUsage = memUsage,
                                StartTime = startTime,
                                Icon = icon
                            });
                        }
                        catch (ArgumentException) { /* Prozess wurde in der Zwischenzeit evtl. beendet */ }
                    }
                }
            }
        }
        finally
        {
            RmEndSession(handle);
        }
        return processes;
    }
}