using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Platform;
using AutoTrigger.Core.Storage;
using AutoTrigger.Core.Text;
using AutoTrigger.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace AutoTrigger
{
    internal sealed partial class App : Application
    {
        // Same stem as the names of the instance lock and of the command pipe.
        private const string TaskbarIdentity = "AutoTrigger.7f3c1d2e";

        private readonly SingleInstance _instance;
        private SettingsStore _store;
        private AppSettings _settings;
        private SettingsController _controller;
        private ProfilesPage _profilesPage;
        private WindowsProcessSystem _processes;
        private WindowsAudioHardware _audio;
        private WindowsPowerPlans _powerPlans;
        private WindowsKeyboard _keyboard;
        private WindowsScreenSaver _screenSaver;
        private HotkeyService _hotkeys;
        private CommandServer _commands;
        private readonly Command _startCommand;
        private readonly bool _restarted;
        private string _language;
        private ActionExecutor _executor;
        private EngineLoop _loop;
        private ForegroundWatcher _foreground;
        private TriggerEngine _engine;
        private AppIcons _icons;
        private DisplayService _displays;
        private DispatcherTimer _displayTimer;
        private TrayIcon _tray;
        private TrayMenu _menu;
        private MainWindow _window;
        private string _version;
        private string _exePath;
        private bool _exiting;
        private int _statePending;

        public App(SingleInstance instance, Command startCommand, bool restarted)
        {
            _instance = instance;
            _startCommand = startCommand ?? Command.Parse(null);
            _restarted = restarted;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            try
            {
                StartUp();
            }
            catch (Exception ex)
            {
                // Any startup failure ends in one clear message and a log entry instead of a crash dialog.
                FailStart(ex);
            }
        }

        private void StartUp()
        {
            string dataDir = SettingsStore.DefaultDirectory;
            StartLog(dataDir);
            UseOwnTaskbarIdentity();
            LoadResult loaded = LoadSettings(dataDir);
            UseLanguage(dataDir);

            Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
            Resources.MergedDictionaries.Add(new ControlsDictionary());
            RemoveListContextMenus();
            CutText.Register();

            CreateServices(dataDir);
            StatusPage status = CreateWindow();
            CreateEngine();
            ConnectStatusPage(status);
            CreateDisplayTimer();
            CreateTray();

            ApplyStartPause();
            _engine.Start(_settings.MonitoringPaused);
            _displays.Refresh();
            _hotkeys.Update(_settings.Profiles);
            _controller.Changed += () => _hotkeys.Update(_settings.Profiles);
            if (_startCommand.RunsProfile)
                _engine.RunProfileNamed(_startCommand.ProfileName, _startCommand.Event, "command line");
            else if (_startCommand.Kind == CommandKind.Launch)
                LaunchApp(_startCommand.AppId);

            if ((_restarted || !_settings.StartMinimized) && _startCommand.Kind == CommandKind.None)
                ShowWindow();
            if (_restarted)
                _window.ShowTab(Ui.MainWindow.SettingsTab);
            if (loaded.Message != null)
                MessageDialog.Show(_window, Texts.SettingsProblemTitle, loaded.Message.Text, Texts.Ok);

            _instance.Listen(() => Dispatcher.BeginInvoke(new Action(ShowWindow)));
            _commands = new CommandServer(CommandChannel.PipeName, HandleCommand, CommandPipe.CreateSecured);
            CreateUpdateCheck();
        }

        // Windows gives a window the icon and name of the shortcut that started its process, unless the process names
        // itself: started by the desktop shortcut of a game, AutoTrigger would wear that game's icon until it closes.
        private static void UseOwnTaskbarIdentity()
        {
            int result = NativeUi.SetCurrentProcessExplicitAppUserModelID(TaskbarIdentity);
            if (result != 0)
                Log.Warning("Taskbar identity not set: 0x" + result.ToString("X8", CultureInfo.InvariantCulture));
        }

        // WPF gives scroll bars and combo boxes a context menu that Windows 11 does not show.
        private static void RemoveListContextMenus()
        {
            var noMenu = new ContextMenuEventHandler((sender, e) => e.Handled = true);
            EventManager.RegisterClassHandler(typeof(ScrollBar), FrameworkElement.ContextMenuOpeningEvent, noMenu);
            EventManager.RegisterClassHandler(typeof(ComboBox), FrameworkElement.ContextMenuOpeningEvent, noMenu);
        }

        private void StartLog(string dataDir)
        {
            try
            {
                Log.Start(dataDir, LogLevel.Error);
            }
            catch (Exception)
            {
                // AutoTrigger still runs without a log.
            }
            _version = ReadVersion();
            _exePath = Assembly.GetEntryAssembly()?.Location ?? string.Empty;
        }

        private LoadResult LoadSettings(string dataDir)
        {
            _store = new SettingsStore(dataDir);
            LoadResult loaded = _store.Load();
            _settings = loaded.Settings;
            Log.Level = _settings.LogLevel;
            Log.Info($"AutoTrigger {_version} started ({CountText.Of(_settings.Applications.Count, "application")}, "
                + $"{CountText.Of(_settings.Profiles.Count, "profile")})");
            foreach (string warning in loaded.Warnings)
                Log.Warning(warning);
            foreach (ValidationIssue issue in SettingsValidator.Validate(_settings))
                Log.Warning(issue.Note.English);
            _store.RemoveOldCopies();
            return loaded;
        }

        // Read once at start: a new language applies after a restart, so no window ever mixes two languages.
        private void UseLanguage(string dataDir)
        {
            _language = Languages.Resolve(_settings.Language, Program.SystemCulture);
            var report = new List<string>();
            var problems = new List<string>();
            TextCatalog.Activate(TextCatalog.Load(_language, dataDir, report, problems));
            CultureInfo culture = Languages.UiCulture(_language, Program.SystemCulture);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            string source = string.IsNullOrEmpty(_settings.Language) ? "Windows display language " + Program.SystemCulture : "setting";
            Log.Info($"Language: {_language} ({source})");
            foreach (string line in report)
                Log.Info("Language: " + line);
            foreach (string line in problems)
                Log.Warning("Language: " + line);
        }

        private void RestartForLanguage()
        {
            try
            {
                string arguments;
                using (Process self = Process.GetCurrentProcess())
                    arguments = Program.RestartedArgument + " " + self.Id.ToString(CultureInfo.InvariantCulture);
                Process.Start(new ProcessStartInfo(_exePath, arguments) { UseShellExecute = false })?.Dispose();
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
            {
                Log.Error("AutoTrigger could not be restarted", ex);
                MessageDialog.Show(_window, Texts.AppName, Texts.RestartFailed(ex.Message), Texts.Ok);
                return;
            }
            Log.Info("AutoTrigger is restarting to use another language");
            ExitApp();
        }

        private void CreateServices(string dataDir)
        {
            _controller = new SettingsController(_settings, _store, Dispatcher);
            _icons = new AppIcons(Path.Combine(dataDir, "icons"), Dispatcher, _settings.Applications.Select(a => a.Path));
            _processes = new WindowsProcessSystem();
            var displayHardware = new WindowsDisplayHardware();
            _audio = new WindowsAudioHardware();
            _powerPlans = new WindowsPowerPlans();
            _keyboard = new WindowsKeyboard();
            KeyNames.LayoutName = WindowsKeyboard.LayoutName;
            _screenSaver = new WindowsScreenSaver();
            _executor = new ActionExecutor(_processes, displayHardware, _audio, _powerPlans, _keyboard, _screenSaver);
            _hotkeys = new HotkeyService(_keyboard,
                (profileId, kind, keys) => _engine?.RunProfile(profileId, kind, "keyboard shortcut " + KeyNames.Readable(keys)));
            HotkeyBox.CapturingChanged += capturing => _hotkeys?.Suspend(capturing);
            _displays = new DisplayService(_controller, displayHardware, _executor, Dispatcher);
        }

        private StatusPage CreateWindow()
        {
            var status = new StatusPage(_icons);
            _profilesPage = new ProfilesPage(_controller, _processes, _displays, _audio, _powerPlans, _hotkeys, () => _window);
            var pages = new FrameworkElement[]
            {
                new ApplicationsPage(_controller, _processes, _icons, () => _window),
                _profilesPage,
                new DisplaysPage(_controller, _displays, () => _window),
                new SettingsPage(_controller, IsStartWithWindows, SetStartWithWindows, OpenLog, ImportFromAutoActions, _language, RestartForLanguage),
            };
            _window = new MainWindow(status, pages, _version, _settings.WindowWidth, _settings.WindowHeight);
            MainWindow = _window;
            ThemeController.Apply(_settings.Theme);
            _window.Closing += OnWindowClosing;
            _window.SizeChanged += OnWindowSizeChanged;
            _window.IsVisibleChanged += OnWindowVisibleChanged;
            _window.StateChanged += (s, a) => UpdateDisplayTimer();
            _window.TabChanged += UpdateDisplayTimer;
            _window.AboutClicked += ShowAbout;
            return status;
        }

        private void CreateEngine()
        {
            _loop = new EngineLoop();
            _foreground = new ForegroundWatcher();
            _engine = new TriggerEngine(_controller.Copy(), _processes, _loop, _executor.ExecuteAsync, _foreground);
            _executor.DisplayEntries = () => _engine?.Settings?.Displays;
            _engine.StateChanged += OnEngineStateChanged;
            _controller.ConnectEngine(_engine.UpdateSettings);
        }

        private void ConnectStatusPage(StatusPage status)
        {
            _controller.Changed += () =>
            {
                status.SetApplications(_settings.Applications, _settings.Profiles);
                status.SetShortcuts(_settings.Profiles);
            };
            status.SetApplications(_settings.Applications, _settings.Profiles);
            status.SetShortcuts(_settings.Profiles);
            status.ShowDisplays(_displays.Monitors);
            status.LaunchRequested += app => Launch(app, "Launch button");
            status.ShortcutRequested += CreateDesktopShortcut;
            status.MonitoringChanged += on => SetMonitoringPaused(!on);
            status.HdrChanged += SetHdr;
            status.ProfileClicked += id =>
            {
                _window.ShowTab(Ui.MainWindow.ProfilesTab);
                _profilesPage.Select(id);
            };
            status.DisplaysClicked += () => _window.ShowTab(Ui.MainWindow.DisplaysTab);
            _displays.Changed += () => _window.Status.ShowHdr(_displays.Hdr, _displays.IsChanging);
            _displays.MonitorsChanged += () => status.ShowDisplays(_displays.Monitors);
        }

        private void CreateDisplayTimer()
        {
            _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _displayTimer.Tick += (s, a) => _displays.Refresh();
        }

        private void CreateTray()
        {
            _tray = new TrayIcon();
            _tray.OpenRequested += ShowWindow;
            _tray.MenuRequested += ShowMenu;
            _tray.ThemeChanged += () => ThemeController.Apply(_settings.Theme);
            _tray.DisplaysChanged += () => _displays.Refresh();
            _menu = new TrayMenu();
        }

        private void ShowWindow()
        {
            if (_exiting || _window == null)
                return;
            _window.Bring();
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (_exiting)
                return;
            e.Cancel = true;
            if (_settings.CloseToTray)
                _window.Hide();
            else
                Dispatcher.BeginInvoke(new Action(ExitApp));
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_window.IsLoaded || _window.WindowState != WindowState.Normal)
                return;
            double width = Math.Round(e.NewSize.Width);
            double height = Math.Round(e.NewSize.Height);
            if (width == _settings.WindowWidth && height == _settings.WindowHeight)
                return;
            _settings.WindowWidth = width;
            _settings.WindowHeight = height;
            _controller.SaveOnly();
        }

        private void OnWindowVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_window.IsVisible && _engine != null)
                _window.Status.ShowEngine(_engine.Snapshot);
            UpdateDisplayTimer();
        }

        private void UpdateDisplayTimer()
        {
            if (_displayTimer == null || _displays == null || _window == null)
                return;
            bool wanted = !_exiting && _window.IsVisible && _window.WindowState != WindowState.Minimized
                && (_window.CurrentTab == Ui.MainWindow.StatusTab || _window.CurrentTab == Ui.MainWindow.DisplaysTab);
            if (wanted == _displayTimer.IsEnabled)
                return;
            if (wanted)
            {
                if (_window.CurrentTab == Ui.MainWindow.StatusTab)
                    _displays.Refresh();
                _displayTimer.Start();
            }
            else
            {
                _displayTimer.Stop();
            }
        }

        private void FailStart(Exception ex)
        {
            _exiting = true;
            Log.Error("AutoTrigger could not start", ex);
            try
            {
                _displayTimer?.Stop();
                StopUpdateCheck();
                _commands?.Dispose();
                _hotkeys?.Dispose();
                _menu?.Dispose();
                _tray?.Dispose();
                _engine?.Dispose();
                _loop?.Dispose();
                _executor?.Dispose();
                _foreground?.Dispose();
                _icons?.Dispose();
                _window?.Close();
            }
            catch (Exception)
            {
                // Best effort: AutoTrigger is already closing because of an error.
            }
            Log.Stop(TimeSpan.FromSeconds(2));
            try
            {
                MessageBox.Show(Texts.StartupError(Log.FilePath), Texts.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception)
            {
                // If even a message box fails, the log still has the details.
            }
            Shutdown(1);
        }

        private void OnEngineStateChanged()
        {
            // Engine updates are coalesced: one window refresh per burst, at background priority.
            if (Interlocked.Exchange(ref _statePending, 1) == 1)
                return;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                Interlocked.Exchange(ref _statePending, 0);
                if (_engine == null || _exiting)
                    return;
                EngineSnapshot snapshot = _engine.Snapshot;
                _window.Status.ShowEngine(snapshot);
                if (_tray != null)
                    _tray.Paused = snapshot.Paused;
            }));
        }

        private void ShowMenu(NativeUi.POINT anchor)
        {
            if (_exiting || _menu == null || _menu.IsOpen)
                return;
            NativeUi.RECT? icon = null;
            if (_tray.TryGetIconRect(out NativeUi.RECT rect))
                icon = rect;
            else if (anchor.X == 0 && anchor.Y == 0)
                NativeUi.GetCursorPos(out anchor);

            TrayCommand command = _menu.Show(icon, anchor, TrayIcon.GetTaskbarEdge(), _engine.Snapshot.Paused, IsStartWithWindows());
            if (_exiting)
                return;
            switch (command)
            {
                case TrayCommand.Open:
                    ShowWindow();
                    break;
                case TrayCommand.Pause:
                    SetMonitoringPaused(!_engine.Snapshot.Paused);
                    break;
                case TrayCommand.StartWithWindows:
                    ToggleStartWithWindows();
                    break;
                case TrayCommand.Exit:
                    ExitApp();
                    break;
                default:
                    _tray.ReturnFocus();
                    break;
            }
        }

        private bool IsStartWithWindows()
        {
            try
            {
                return StartupRegistration.IsEnabled(_exePath);
            }
            catch (Exception ex) when (IsRegistryError(ex))
            {
                Log.Warning("Start with Windows cannot be read", ex);
                return false;
            }
        }

        private void ToggleStartWithWindows() => SetStartWithWindows(!IsStartWithWindows());

        private void SetStartWithWindows(bool enable)
        {
            try
            {
                StartupRegistration.SetEnabled(_exePath, enable);
                Log.Info(enable ? "Start with Windows turned on" : "Start with Windows turned off");
            }
            catch (Exception ex) when (IsRegistryError(ex))
            {
                Log.Error("Start with Windows could not be changed", ex);
                MessageDialog.Show(_window, Texts.StartWithWindows, Texts.StartupFailed(ex.Message), Texts.Ok);
            }
        }

        private static bool IsRegistryError(Exception ex) =>
            ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException;

        private void Launch(AppEntry app, string from, Action<bool> done = null)
        {
            switch (app.Launch)
            {
                case LaunchKind.Profile:
                    _engine.RunProfile(app.LaunchProfileId, EventKind.Started, from + " of " + app.Name, done);
                    break;
                case LaunchKind.Program:
                    LaunchProgram(app, done);
                    break;
                default:
                    Log.Warning($"{app.Name}: its Launch button runs nothing ({from})");
                    done?.Invoke(false);
                    break;
            }
        }

        private void CreateDesktopShortcut(AppEntry app)
        {
            DesktopShortcut shortcut = DesktopShortcut.For(app, _exePath);
            var writer = new Thread(() =>
            {
                Func<string> reason;
                try
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    string saved = desktop.Length == 0 ? null : WindowsShortcuts.Save(shortcut, desktop);
                    if (saved != null)
                    {
                        Log.Info($"{app.Name}: desktop shortcut {Path.GetFileName(saved)} saved");
                        return;
                    }
                    Log.Warning(desktop.Length == 0
                        ? $"{app.Name}: desktop shortcut not saved: this account has no Desktop folder"
                        : $"{app.Name}: desktop shortcut not saved: {DesktopShortcut.MaxCopies} other shortcuts already have its name");
                    reason = desktop.Length == 0 ? (Func<string>)(() => Texts.NoDesktopFolder) : () => Texts.ShortcutNamesTaken;
                }
                catch (Exception ex)
                {
                    // The shell's COM errors are not documented, and this thread must not end AutoTrigger.
                    Log.Error($"{app.Name}: desktop shortcut not saved", ex);
                    reason = () => ex.Message;
                }
                // Texts are read on the UI thread, where the language of the window is set.
                Dispatcher.BeginInvoke(new Action(() =>
                    MessageDialog.Show(_window, Texts.CreateDesktopShortcut, Texts.ShortcutFailed(reason()), Texts.Ok)));
            })
            { IsBackground = true, Name = "Desktop shortcut" };
            writer.SetApartmentState(ApartmentState.STA);
            writer.Start();
        }

        private void LaunchProgram(AppEntry app, Action<bool> done)
        {
            string path = ActionExecutor.ExpandPath(app.Path);
            string folder = ActionExecutor.ProgramFolder(path);
            Task.Run(() => _processes.Start(path, string.Empty, folder, false, false)).ContinueWith(t =>
            {
                done?.Invoke(!t.IsFaulted);
                if (!t.IsFaulted)
                {
                    Log.Info($"{app.Name} launched from AutoTrigger");
                    return;
                }
                string message = t.Exception.GetBaseException().Message;
                Log.Error($"{app.Name} cannot be launched: {message}");
                Dispatcher.BeginInvoke(new Action(() =>
                    MessageDialog.Show(_window, Texts.LaunchFailedTitle, Texts.LaunchFailed(app.Name, message), Texts.Ok)));
            }, TaskScheduler.Default);
        }

        private void SetHdr(bool on)
        {
            _displays.SetHdr(on, error =>
                MessageDialog.Show(_window, Texts.Hdr, Texts.HdrFailed(error), Texts.Ok));
            _window.Status.ShowHdr(_displays.Hdr, _displays.IsChanging);
        }

        private void ShowAbout()
        {
            DialogChoice choice = MessageDialog.Show(_window, Texts.About + " " + Texts.AppName,
                Texts.AboutText(_version), Texts.Close, Texts.OpenLicenses, Texts.GitHub);
            if (choice == DialogChoice.Secondary)
                OpenInNotepad(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NOTICE.txt"));
            else if (choice == DialogChoice.Extra)
                ShellOpen(Texts.RepositoryUrl);
        }

        private void OpenLog()
        {
            string path = Log.FilePath ?? Path.Combine(SettingsStore.DefaultDirectory, Log.FileName);
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, string.Empty);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warning($"{path} cannot be created", ex);
            }
            if (!ShellOpen(path, null, false))
                OpenInNotepad(path);
        }

        private static void OpenInNotepad(string path) => ShellOpen("notepad.exe", "\"" + path + "\"");

        private static bool ShellOpen(string target, string arguments = null, bool logFailure = true)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target, arguments ?? string.Empty) { UseShellExecute = true })?.Dispose();
                return true;
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
            {
                if (logFailure)
                    Log.Error($"{target} cannot be opened", ex);
                return false;
            }
        }

        private void ExitApp()
        {
            if (_exiting)
                return;
            _exiting = true;
            Log.Info("AutoTrigger is closing");
            try
            {
                _displayTimer?.Stop();
                StopUpdateCheck();
                _commands?.Dispose();
                _hotkeys?.Dispose();
                _menu?.Dispose();
                _window?.Close();
                _controller?.Flush();
                _tray?.Dispose();
                _engine?.Dispose();
                _screenSaver?.RestoreIfTurnedOff();
                _loop?.Dispose();
                _executor?.Dispose();
                _foreground?.Dispose();
                _processes?.Dispose();
                _icons?.Dispose();
                _store?.Dispose();
            }
            catch (Exception ex)
            {
                // Exit must complete even if one part fails to close.
                Log.Error("Error while closing", ex);
            }
            Log.Info("AutoTrigger closed");
            Log.Stop(TimeSpan.FromSeconds(2));
            Shutdown();
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            base.OnSessionEnding(e);
            Log.Info("Windows is signing out or shutting down");
            ExitApp();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // A failing click or redraw is logged and the window keeps working.
            Log.Error("Unexpected error in the window", e.Exception);
            e.Handled = true;
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error("Unexpected error; AutoTrigger has to close", e.ExceptionObject as Exception);
            Log.Stop(TimeSpan.FromSeconds(2));
            try
            {
                MessageBox.Show(Texts.FatalError(Log.FilePath), Texts.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception)
            {
                // The process is ending; if the message box fails, the log still has the details.
            }
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Log.Error("Unexpected error in a background task", e.Exception?.GetBaseException());
            e.SetObserved();
        }

        private static string ReadVersion()
        {
            var attribute = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return attribute?.InformationalVersion ?? "0.0.0";
        }
    }
}
