using FileLockCheck.Configuration;
using FileLockCheck.Infrastructure;
using FileLockCheck.ViewModels;
using System;
using System.Drawing; // Benötigt für SystemIcons
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace FileLockCheck;

public partial class MainWindow : Window
{
    // --- Windows API Imports for Hotkeys ---
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HOTKEY_ID = 9000;
    private const int MOD_NOREPEAT = 0x4000; // Prevents spamming when the key is held down
    private const int WM_HOTKEY = 0x0312;

    private IntPtr _windowHandle;
    private HwndSource _source;
    private MainViewModel _viewModel;

    private WinForms.NotifyIcon _notifyIcon;
    private AppConfig _config;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        this.DataContext = _viewModel;

        // Load configuration from %APPDATA%
        _config = AppConfig.Load();

        // Apply saved language
        if (!string.IsNullOrEmpty(_config.Language))
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(_config.Language);
            Thread.CurrentThread.CurrentCulture = new CultureInfo(_config.Language);
        }

        // Start the application hidden
        this.Visibility = Visibility.Hidden;

        SetupTrayIcon();
        UpdateHotkeyUI();

        new WindowInteropHelper(this).EnsureHandle();
    }

    private void SetupTrayIcon()
    {
        if (_notifyIcon == null)
        {
            _notifyIcon = new WinForms.NotifyIcon();
            _notifyIcon.Icon = SystemIcons.Shield; // Standard Windows Shield Icon
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        SetupTrayMenu();
    }

    private void SetupTrayMenu()
    {
        // Create context menu
        var contextMenu = new WinForms.ContextMenuStrip();

        var showItem = new WinForms.ToolStripMenuItem(Properties.Resources.TrayShowItem);
        showItem.Click += (s, e) => ShowMainWindow();

        var langItem = new WinForms.ToolStripMenuItem(Properties.Resources.TrayLanguageItem);
        var langEn = new WinForms.ToolStripMenuItem("English");
        langEn.Click += (s, e) => SwitchLanguage("en-US");
        var langDe = new WinForms.ToolStripMenuItem("Deutsch");
        langDe.Click += (s, e) => SwitchLanguage("de-DE");

        langItem.DropDownItems.Add(langEn);
        langItem.DropDownItems.Add(langDe);

        var exitItem = new WinForms.ToolStripMenuItem(Properties.Resources.TrayExitItem);
        exitItem.Click += (s, e) =>
        {
            _notifyIcon.Visible = false;
            Application.Current.Shutdown();
        };

        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(langItem);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void SwitchLanguage(string cultureCode)
    {
        _config.Language = cultureCode;
        _config.Save();

        Thread.CurrentThread.CurrentUICulture = new CultureInfo(cultureCode);
        Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureCode);

        SetupTrayMenu();
        UpdateHotkeyUI();

        MessageBox.Show(Properties.Resources.RestartRequiredMessage, Properties.Resources.RestartRequiredTitle, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowMainWindow()
    {
        this.Show();
        this.WindowState = WindowState.Normal;
        this.Activate();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_windowHandle);
        _source.AddHook(HwndHook);

        RegisterCurrentHotkey();
    }

    private void RegisterCurrentHotkey()
    {
        // Unregister old shortcut (if any)
        UnregisterHotKey(_windowHandle, HOTKEY_ID);

        // Translate WPF modifiers to Native Windows modifiers
        int nativeModifiers = MOD_NOREPEAT;
        if (_config.Modifiers.HasFlag(ModifierKeys.Alt)) nativeModifiers |= 0x0001;
        if (_config.Modifiers.HasFlag(ModifierKeys.Control)) nativeModifiers |= 0x0002;
        if (_config.Modifiers.HasFlag(ModifierKeys.Shift)) nativeModifiers |= 0x0004;
        if (_config.Modifiers.HasFlag(ModifierKeys.Windows)) nativeModifiers |= 0x0008;

        int virtualKey = KeyInterop.VirtualKeyFromKey(_config.HotKey);

        RegisterHotKey(_windowHandle, HOTKEY_ID, nativeModifiers, virtualKey);
    }

    private void UpdateHotkeyUI()
    {
        string shortcutText = $"{_config.Modifiers} + {_config.HotKey}".Replace(", ", " + ");
        HotkeyTextBox.Text = shortcutText;

        if (_notifyIcon != null)
        {
            _notifyIcon.Text = string.Format(Properties.Resources.TrayIconTextFormat, shortcutText);
        }
    }

    // Catches keys in the UI to define a new shortcut
    private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Ignore key presses if only modifiers (without actual key) are pressed
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
            e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
            e.Key == Key.LeftShift || e.Key == Key.RightShift ||
            e.Key == Key.LWin || e.Key == Key.RWin ||
            e.Key == Key.System || e.Key == Key.Capital || e.Key == Key.Tab)
        {
            return;
        }

        // Get the currently pressed modifiers
        var modifiers = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Save and apply (avoid empty modifiers)
        if (modifiers == ModifierKeys.None) return;

        _config.Modifiers = modifiers;
        _config.HotKey = key;
        _config.Save();

        UpdateHotkeyUI();
        RegisterCurrentHotkey();

        // Remove focus so the field is no longer selected
        Keyboard.ClearFocus();
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            _viewModel.AnalyzeCurrentSelection();
            ShowMainWindow();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // The "X" only hides the window. To exit, the tray icon must be used.
        e.Cancel = true;
        this.Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Clean up when the application actually exits
        if (_source != null)
        {
            _source.RemoveHook(HwndHook);
            _source = null;
        }
        UnregisterHotKey(_windowHandle, HOTKEY_ID);
        if (_notifyIcon != null) _notifyIcon.Dispose();

        base.OnClosed(e);
    }

    private void KillProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is LockingProcess process)
        {
            var result = MessageBox.Show(
                string.Format(Properties.Resources.KillConfirmMessage, process.ProcessName),
                Properties.Resources.KillConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.GetProcessById(process.ProcessId).Kill();

                    // Re-evaluate the currently selected file to refresh the locking processes list
                    _viewModel.AnalyzeCurrentSelection();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, Properties.Resources.KillErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}