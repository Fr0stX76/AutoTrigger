using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
using AutoTrigger.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace AutoTrigger
{
    internal sealed partial class App : Application
    {
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

        public App(SingleInstance instance, Command startCommand)
        {
            _instance = instance;
            _startCommand = startCommand ?? Command.Parse(null);
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
                FailStart(ex);
            }
        }

        private void StartUp()
        {
            string dataDir = SettingsStore.DefaultDirectory;
            StartLog(dataDir);
            LoadResult loaded = LoadSettings(dataDir);

            Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
            Resources.MergedDictionaries.Add(new ControlsDictionary());
            RemoveListContextMenus();

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

            if (!_settings.StartMinimized && _startCommand.Kind == CommandKind.None)
                ShowWindow();
            if (!string.IsNullOrEmpty(loaded.Message))
                MessageDialog.Show(_window, Texts.SettingsProblemTitle, loaded.Message, Texts.Ok);

            _instance.Listen(() => Dispatcher.BeginInvoke(new Action(ShowWindow)));
            _commands = new CommandServer(CommandChannel.PipeName, HandleCommand, CommandPipe.CreateSecured);
        }

        private void ApplyStartPause()
        {
            bool? paused = PausedAfter(_startCommand.Kind, _settings.MonitoringPaused);
            if (paused == null || paused.Value == _settings.MonitoringPaused)
                return;
            _settings.MonitoringPaused = paused.Value;
            _controller.SaveOnly();
        }

        private static bool? PausedAfter(CommandKind kind, bool paused)
        {
            switch (kind)
            {
                case CommandKind.Pause: return true;
                case CommandKind.Resume: return false;
                case CommandKind.Toggle: return !paused;
                default: return null;
            }
        }

        private Task<string> HandleCommand(Command command)
        {
            const string closing = "closing";
            if (_exiting)
                return Task.FromResult(closing);
            if (command.RunsProfile)
            {
                var result = new TaskCompletionSource<string>();
                _engine.RunProfileNamed(command.ProfileName, command.Event, "command line",
                    found => result.TrySetResult(found ? CommandServer.Ok : "profile not found"));
                return result.Task;
            }
            return Dispatcher.InvokeAsync(() =>
            {
                if (_exiting)
                    return closing;
                bool? paused = PausedAfter(command.Kind, _settings.MonitoringPaused);
                if (paused != null && paused.Value != _settings.MonitoringPaused)
                    SetMonitoringPaused(paused.Value);
                return CommandServer.Ok;
            }).Task;
        }

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
            Log.Info($"AutoTrigger {_version} started ({_settings.Applications.Count} applications, {_settings.Profiles.Count} profiles)");
            foreach (string warning in loaded.Warnings)
                Log.Warning(warning);
            foreach (ValidationIssue issue in SettingsValidator.Validate(_settings))
                Log.Warning(issue.Message);
            return loaded;
        }

        private void CreateServices(string dataDir)
        {
            _controller = new SettingsController(_settings, _store, Dispatcher);
            _icons = new AppIcons(Path.Combine(dataDir, "icons"), Dispatcher);
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
                new SettingsPage(_controller, IsStartWithWindows, SetStartWithWindows, OpenLog, ImportFromAutoActions),
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
                status.SetApplications(_settings.Applications);
                status.SetShortcuts(_settings.Profiles);
            };
            status.SetApplications(_settings.Applications);
            status.SetShortcuts(_settings.Profiles);
            status.LaunchRequested += Launch;
            status.MonitoringChanged += on => SetMonitoringPaused(!on);
            status.HdrChanged += SetHdr;
            status.ProfileClicked += id =>
            {
                _window.ShowTab(Ui.MainWindow.ProfilesTab);
                _profilesPage.Select(id);
            };
            _displays.Changed += () => _window.Status.ShowHdr(_displays.Hdr, _displays.IsChanging);
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
            }
            Log.Stop(TimeSpan.FromSeconds(2));
            try
            {
                MessageBox.Show(Texts.Format(Texts.StartupError, Log.FilePath), Texts.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception)
            {
            }
            Shutdown(1);
        }

        private void OnEngineStateChanged()
        {
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

        private void SetMonitoringPaused(bool paused)
        {
            _settings.MonitoringPaused = paused;
            _controller.SaveOnly();
            _engine.SetPaused(paused);
        }

        private bool IsStartWithWindows()
        {
            try
            {
                return StartupRegistration.IsEnabled(_exePath);
            }
            catch (Exception ex)
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
            catch (Exception ex)
            {
                Log.Error("Start with Windows could not be changed", ex);
                MessageDialog.Show(_window, Texts.StartWithWindows, Texts.Format(Texts.StartupFailed, ex.Message), Texts.Ok);
            }
        }

        private void Launch(AppEntry app)
        {
            string path = ActionExecutor.ExpandPath(app.Path);
            string folder = ActionExecutor.ProgramFolder(path);
            Task.Run(() => _processes.Start(path, string.Empty, folder, false)).ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    Log.Info($"{app.Name} launched from AutoTrigger");
                    return;
                }
                string message = t.Exception?.GetBaseException().Message ?? "unknown error";
                Log.Error($"{app.Name} cannot be launched: {message}");
                Dispatcher.BeginInvoke(new Action(() =>
                    MessageDialog.Show(_window, Texts.LaunchFailedTitle, Texts.Format(Texts.LaunchFailed, app.Name, message), Texts.Ok)));
            }, TaskScheduler.Default);
        }

        private void SetHdr(bool on)
        {
            _displays.SetHdr(on, error =>
                MessageDialog.Show(_window, Texts.HdrFailedTitle, Texts.Format(Texts.HdrFailed, error), Texts.Ok));
            _window.Status.ShowHdr(_displays.Hdr, _displays.IsChanging);
        }

        private void ShowAbout()
        {
            DialogChoice choice = MessageDialog.Show(_window, Texts.About + " " + Texts.AppName,
                Texts.Format(Texts.AboutText, _version), Texts.Close, Texts.OpenLicenses, Texts.GitHub);
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
            catch (Exception ex)
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
            Log.Error("Unexpected error in the window", e.Exception);
            e.Handled = true;
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error("Unexpected error; AutoTrigger has to close", e.ExceptionObject as Exception);
            Log.Stop(TimeSpan.FromSeconds(2));
            try
            {
                MessageBox.Show(Texts.Format(Texts.FatalError, Log.FilePath), Texts.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception)
            {
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
