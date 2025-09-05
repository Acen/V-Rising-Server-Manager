using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using System.ComponentModel;
using System.Text.Json.Nodes;
using VRisingServerManager.RCON;
using ModernWpf.Controls;
using ModernWpf;

namespace VRisingServerManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainSettings VsmSettings = new();
        private static dWebhook DiscordSender = new();
        private static HttpClient HttpClient = new();
        private RemoteConClient RCONClient;
        private int _onboardingCurrentStep = 1;
        private string _selectedServerPath = "";
        private string _selectedSaveFilePath = "";
        private bool _isBrowsingPath = false;

        public MainWindow()
        {
            if (!File.Exists(Directory.GetCurrentDirectory() + @"\VSMSettings.json"))
                MainSettings.Save(VsmSettings);
            else
                VsmSettings = MainSettings.Load();

            DataContext = VsmSettings;

            if (VsmSettings.AppSettings.DarkMode == true)
                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
            else
                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;

            InitializeComponent();

            // Find onboarding overlay and buttons
            var onboardingOverlay = (Grid)FindName("OnboardingOverlay");
            var onboardingBrowsePathButton = (Button)FindName("OnboardingBrowsePathButton");
            var onboardingNextStep1Button = (Button)FindName("OnboardingNextStep1Button");
            var onboardingSelectSaveButton = (Button)FindName("OnboardingSelectSaveButton");
            var onboardingImportSelectedButton = (Button)FindName("OnboardingImportSelectedButton");
            var onboardingBackStep2Button = (Button)FindName("OnboardingBackStep2Button");
            var onboardingCompleteButton = (Button)FindName("OnboardingCompleteButton");
            var onboardingBackStep3Button = (Button)FindName("OnboardingBackStep3Button");

            LogToConsole($"[DEBUG] Wiring up onboarding event handlers...");
            LogToConsole($"[DEBUG] Current onboarding step: {_onboardingCurrentStep}");
            LogToConsole($"[DEBUG] onboardingBrowsePathButton found: {onboardingBrowsePathButton != null}");
            LogToConsole($"[DEBUG] onboardingNextStep1Button found: {onboardingNextStep1Button != null}");
            LogToConsole($"[DEBUG] onboardingSelectSaveButton found: {onboardingSelectSaveButton != null}");
            LogToConsole($"[DEBUG] onboardingImportSelectedButton found: {onboardingImportSelectedButton != null}");

            if (onboardingBrowsePathButton != null) {
                onboardingBrowsePathButton.Click += OnboardingBrowsePathButton_Click;
                LogToConsole($"[DEBUG] Wired OnboardingBrowsePathButton.Click to OnboardingBrowsePathButton_Click");
            }
            if (onboardingNextStep1Button != null) {
                onboardingNextStep1Button.Click += OnboardingNextStep1Button_Click;
                LogToConsole($"[DEBUG] Wired OnboardingNextStep1Button.Click to OnboardingNextStep1Button_Click");
            }
            if (onboardingSelectSaveButton != null) {
                onboardingSelectSaveButton.Click += OnboardingSelectSaveButton_Click;
                LogToConsole($"[DEBUG] Wired OnboardingSelectSaveButton.Click to OnboardingSelectSaveButton_Click");
            }
            if (onboardingImportSelectedButton != null) {
                onboardingImportSelectedButton.Click += OnboardingNextStep2Button_Click;
                LogToConsole($"[DEBUG] Wired OnboardingImportSelectedButton.Click to OnboardingNextStep2Button_Click");
            }
            if (onboardingBackStep2Button != null) onboardingBackStep2Button.Click += OnboardingBackStep2Button_Click;
            if (onboardingCompleteButton != null) onboardingCompleteButton.Click += OnboardingCompleteButton_Click;
            if (onboardingBackStep3Button != null) onboardingBackStep3Button.Click += OnboardingBackStep3Button_Click;

            // Initialize onboarding
            UpdateOnboardingStepIndicators();
            UpdateOnboardingStepVisibility();

            VsmSettings.AppSettings.PropertyChanged += AppSettings_PropertyChanged;
            VsmSettings.Servers.CollectionChanged += Servers_CollectionChanged; // MVVM method not working

            VsmSettings.AppSettings.Version = new AppSettings().Version;

            LogToConsole("V Rising Server Manager started." + ((VsmSettings.Servers.Count > 0) ? "\r" + VsmSettings.Servers.Count.ToString() + " servers loaded from config." : "\rNo servers found. Starting onboarding."));

            ScanForServers();

            // Show onboarding overlay if no servers exist
            if (onboardingOverlay != null)
            {
                onboardingOverlay.Visibility = VsmSettings.Servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            // Hide/show main UI elements based on onboarding state
            bool isOnboarding = VsmSettings.Servers.Count == 0;
            ServerTabControl.Visibility = isOnboarding ? Visibility.Collapsed : Visibility.Visible;
            var mainSeparator = (Separator)FindName("MainSeparator");
            if (mainSeparator != null) mainSeparator.Visibility = isOnboarding ? Visibility.Collapsed : Visibility.Visible;
            var editorsGroupBox = (GroupBox)FindName("EditorsGroupBox");
            if (editorsGroupBox != null) editorsGroupBox.Visibility = isOnboarding ? Visibility.Collapsed : Visibility.Visible;
            // Keep console always visible for debugging
            var consoleGrid = (Grid)FindName("ConsoleGrid");
            if (consoleGrid != null) consoleGrid.Visibility = Visibility.Visible;
            var statusDockPanel = (DockPanel)FindName("StatusDockPanel");
            if (statusDockPanel != null) statusDockPanel.Visibility = isOnboarding ? Visibility.Collapsed : Visibility.Visible;
        }
        // Onboarding button event handlers
        private void OnboardingImportSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            LogToConsole($"[DEBUG] OnboardingImportSelectedButton clicked - Current step: {_onboardingCurrentStep}");
            LogToConsole($"[DEBUG] _selectedSaveFilePath: {_selectedSaveFilePath}");
            if (string.IsNullOrEmpty(_selectedSaveFilePath))
            {
                LogToConsole($"[DEBUG] No save directory selected, showing warning message");
                MessageBox.Show("Please select a save directory first.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LogToConsole($"[DEBUG] Moving to step 3 for final import");
            _onboardingCurrentStep = 3;
            UpdateOnboardingStepIndicators();
            UpdateOnboardingStepVisibility();
        }

        private void OnboardingSelectSaveButton_Click(object sender, RoutedEventArgs e)
        {
            LogToConsole($"[DEBUG] OnboardingSelectSaveButton clicked - Current step: {_onboardingCurrentStep}");
            var button = (Button)sender;
            button.IsEnabled = false;
            LogToConsole($"[DEBUG] Save browse button disabled, opening folder dialog...");
            try
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select V Rising save directory (should contain ServerHostSettings.json and ServerGameSettings.json)"
                };
                // Try to set initial directory to common V Rising save locations
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string localLowPath = Path.Combine(userProfile, "AppData", "LocalLow", "Stunlock Studios", "VRising");
                string v4Path = null;
                string cloudSavesPath = Path.Combine(localLowPath, "CloudSaves");
                if (Directory.Exists(cloudSavesPath))
                {
                    string[] steamIdDirs = Directory.GetDirectories(cloudSavesPath);
                    if (steamIdDirs.Length > 0)
                    {
                        string steamIdPath = steamIdDirs[0];
                        v4Path = Path.Combine(steamIdPath, "v4");
                    }
                }
                if (v4Path == null || !Directory.Exists(v4Path))
                {
                    v4Path = Path.Combine(localLowPath, "Saves", "v4");
                }
                if (Directory.Exists(v4Path))
                {
                    dialog.InitialDirectory = v4Path;
                }
                LogToConsole($"[DEBUG] FolderBrowserDialog created, showing dialog...");
                var result = dialog.ShowDialog();
                LogToConsole($"[DEBUG] FolderBrowserDialog result: {result}");
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    _selectedSaveFilePath = dialog.SelectedPath;
                    var saveFileTextBox = (TextBox)FindName("OnboardingSaveFileTextBox");
                    if (saveFileTextBox != null)
                    {
                        saveFileTextBox.Text = dialog.SelectedPath;
                        LogToConsole($"[DEBUG] Save directory path set to: {dialog.SelectedPath}");
                    }
                    LogToConsole($"Selected save directory: {dialog.SelectedPath}");
                }
                else
                {
                    LogToConsole($"[DEBUG] FolderBrowserDialog cancelled by user");
                }
            }
            finally
            {
                button.IsEnabled = true;
                LogToConsole($"[DEBUG] Save browse button re-enabled");
            }
        }

        private void OnboardingNextStep2Button_Click(object sender, RoutedEventArgs e)
        {
            LogToConsole($"[DEBUG] OnboardingNextStep2Button clicked - Current step: {_onboardingCurrentStep}");
            LogToConsole($"[DEBUG] _selectedSaveFilePath: {_selectedSaveFilePath}");
            if (string.IsNullOrEmpty(_selectedSaveFilePath))
            {
                LogToConsole($"[DEBUG] No save directory selected, showing warning message");
                MessageBox.Show("Please select a save directory first.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LogToConsole($"[DEBUG] Moving to step 3");
            _onboardingCurrentStep = 3;
            UpdateOnboardingStepIndicators();
            UpdateOnboardingStepVisibility();
            UpdateOnboardingSetupSummary();
        }

        private void OnboardingChooseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select server storage location"
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Store chosen path in settings if needed
                LogToConsole($"Selected server storage location: {dialog.SelectedPath}");
            }
        }

        private void OnboardingSkipButton_Click(object sender, RoutedEventArgs e)
        {
            var onboardingOverlay = (Grid)FindName("OnboardingOverlay");
            if (onboardingOverlay != null)
            {
                onboardingOverlay.Visibility = Visibility.Collapsed;
                // Show main UI
                ServerTabControl.Visibility = Visibility.Visible;
                var mainSeparator = (Separator)FindName("MainSeparator");
                if (mainSeparator != null) mainSeparator.Visibility = Visibility.Visible;
                var editorsGroupBox = (GroupBox)FindName("EditorsGroupBox");
                if (editorsGroupBox != null) editorsGroupBox.Visibility = Visibility.Visible;
                var consoleGrid = (Grid)FindName("ConsoleGrid");
                if (consoleGrid != null) consoleGrid.Visibility = Visibility.Visible;
                var statusDockPanel = (DockPanel)FindName("StatusDockPanel");
                if (statusDockPanel != null) statusDockPanel.Visibility = Visibility.Visible;
            }
            LogToConsole("Onboarding skipped by user.");
        }

        // New 3-step onboarding methods
        private void UpdateOnboardingStepIndicators()
        {
            var step1Border = (Border)FindName("OnboardingStep1Border");
            var step2Border = (Border)FindName("OnboardingStep2Border");
            var step3Border = (Border)FindName("OnboardingStep3Border");

            if (step1Border != null && step2Border != null && step3Border != null)
            {
                // Reset all indicators
                step1Border.BorderBrush = System.Windows.Media.Brushes.LightGray;
                step1Border.Background = System.Windows.Media.Brushes.Transparent;
                step2Border.BorderBrush = System.Windows.Media.Brushes.LightGray;
                step2Border.Background = System.Windows.Media.Brushes.Transparent;
                step3Border.BorderBrush = System.Windows.Media.Brushes.LightGray;
                step3Border.Background = System.Windows.Media.Brushes.Transparent;

                // Update text colors
                var step1Text = (TextBlock)step1Border.Child;
                var step2Text = (TextBlock)step2Border.Child;
                var step3Text = (TextBlock)step3Border.Child;
                if (step1Text != null) step1Text.Foreground = System.Windows.Media.Brushes.Gray;
                if (step2Text != null) step2Text.Foreground = System.Windows.Media.Brushes.Gray;
                if (step3Text != null) step3Text.Foreground = System.Windows.Media.Brushes.Gray;

                // Highlight current step
                if (_onboardingCurrentStep >= 1 && step1Text != null)
                {
                    step1Border.BorderBrush = System.Windows.Media.Brushes.Blue;
                    step1Border.Background = System.Windows.Media.Brushes.LightBlue;
                    step1Text.Foreground = System.Windows.Media.Brushes.Black;
                }
                if (_onboardingCurrentStep >= 2 && step2Text != null)
                {
                    step2Border.BorderBrush = System.Windows.Media.Brushes.Blue;
                    step2Border.Background = System.Windows.Media.Brushes.LightBlue;
                    step2Text.Foreground = System.Windows.Media.Brushes.Black;
                }
                if (_onboardingCurrentStep >= 3 && step3Text != null)
                {
                    step3Border.BorderBrush = System.Windows.Media.Brushes.Blue;
                    step3Border.Background = System.Windows.Media.Brushes.LightBlue;
                    step3Text.Foreground = System.Windows.Media.Brushes.Black;
                }
            }
        }

        private void UpdateOnboardingStepVisibility()
        {
            LogToConsole($"[DEBUG] UpdateOnboardingStepVisibility called - Current step: {_onboardingCurrentStep}");
            var step1Panel = (StackPanel)FindName("OnboardingStep1Panel");
            var step2Panel = (StackPanel)FindName("OnboardingStep2Panel");
            var step3Panel = (StackPanel)FindName("OnboardingStep3Panel");

            if (step1Panel != null) {
                step1Panel.Visibility = _onboardingCurrentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
                LogToConsole($"[DEBUG] Step 1 panel visibility set to: {step1Panel.Visibility}");
            }
            if (step2Panel != null) {
                step2Panel.Visibility = _onboardingCurrentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
                LogToConsole($"[DEBUG] Step 2 panel visibility set to: {step2Panel.Visibility}");
            }
            if (step3Panel != null) {
                step3Panel.Visibility = _onboardingCurrentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
                LogToConsole($"[DEBUG] Step 3 panel visibility set to: {step3Panel.Visibility}");
            }
        }

        private void OnboardingBrowsePathButton_Click(object sender, RoutedEventArgs e)
        {
            LogToConsole($"[DEBUG] OnboardingBrowsePathButton clicked - Current step: {_onboardingCurrentStep}");
            var button = (Button)sender;
            button.IsEnabled = false;
            LogToConsole($"[DEBUG] Browse button disabled, opening folder dialog...");
            try
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select server storage location"
                };
                LogToConsole($"[DEBUG] FolderBrowserDialog created, showing dialog...");
                var result = dialog.ShowDialog();
                LogToConsole($"[DEBUG] FolderBrowserDialog result: {result}");
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    _selectedServerPath = dialog.SelectedPath;
                    var pathTextBox = (TextBox)FindName("OnboardingServerPathTextBox");
                    if (pathTextBox != null)
                    {
                        pathTextBox.Text = dialog.SelectedPath;
                        LogToConsole($"[DEBUG] Server path set to: {dialog.SelectedPath}");
                    }
                    LogToConsole($"Selected server storage location: {dialog.SelectedPath}");
                }
                else
                {
                    LogToConsole($"[DEBUG] FolderBrowserDialog cancelled by user");
                }
            }
            finally
            {
                button.IsEnabled = true;
                LogToConsole($"[DEBUG] Browse button re-enabled");
            }
        }

        private void OnboardingNextStep1Button_Click(object sender, RoutedEventArgs e)
        {
            LogToConsole($"[DEBUG] OnboardingNextStep1Button clicked - Current step: {_onboardingCurrentStep}");
            LogToConsole($"[DEBUG] _selectedServerPath: {_selectedServerPath}");
            if (string.IsNullOrEmpty(_selectedServerPath))
            {
                LogToConsole($"[DEBUG] No server path selected, showing warning message");
                MessageBox.Show("Please select a server storage location first.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LogToConsole($"[DEBUG] Moving to step 2");
            _onboardingCurrentStep = 2;
            UpdateOnboardingStepIndicators();
            UpdateOnboardingStepVisibility();
        }

        private void OnboardingSkipImportButton_Click(object sender, RoutedEventArgs e)
        {
            // Create a new server with default settings
            var newServer = new Server
            {
                Name = "My V Rising Server",
                Path = _selectedServerPath
            };
            VsmSettings.Servers.Add(newServer);
            
            // Move to step 3
            _onboardingCurrentStep = 3;
            UpdateOnboardingStepIndicators();
            UpdateOnboardingStepVisibility();
            UpdateOnboardingSetupSummary();
            MainSettings.Save(VsmSettings);
            LogToConsole("Created new server with default settings.");
        }

        private void OnboardingBackStep2Button_Click(object sender, RoutedEventArgs e)
        {
            _onboardingCurrentStep = 1;
            UpdateOnboardingStepIndicators();
            UpdateOnboardingStepVisibility();
        }

        private async void OnboardingCompleteButton_Click(object sender, RoutedEventArgs e)
        {
            LogToConsole($"[DEBUG] OnboardingCompleteButton clicked - Current step: {_onboardingCurrentStep}");
            LogToConsole($"[DEBUG] _selectedSaveFilePath: {_selectedSaveFilePath}");
            if (string.IsNullOrEmpty(_selectedSaveFilePath))
            {
                LogToConsole($"[DEBUG] No save directory selected, showing warning message");
                MessageBox.Show("Please select a save directory first.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LogToConsole($"[DEBUG] Executing direct import from save directory");
            try
            {
                // Find Steam VRising server installation
                string steamServerPath = FindSteamVRisingServerPath();
                if (string.IsNullOrEmpty(steamServerPath))
                {
                    LogToConsole($"[DEBUG] Steam VRising server not found, prompting user");
                    MessageBox.Show("Could not find VRisingDedicatedServer installation from Steam.\n\nPlease make sure you have installed 'V Rising Dedicated Server' from Steam.\n\nYou can install it from: steam://install/1829350", "Steam Installation Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                LogToConsole($"[DEBUG] Found Steam server at: {steamServerPath}");

                // Create new server with default settings
                Server newServer = new Server
                {
                    Name = GetSaveDisplayName(_selectedSaveFilePath),
                    Path = _selectedServerPath,
                    SteamPath = steamServerPath
                };

                // Set the world name to the save folder name
                newServer.LaunchSettings.WorldName = Path.GetFileName(_selectedSaveFilePath);

                // Setup server files from Steam installation
                SetupServerFiles(newServer.Path, steamServerPath);

                // Create target directory structure for saves
                string targetSavesPath = Path.Combine(newServer.Path, "SaveData", "Saves", "v4");
                Directory.CreateDirectory(targetSavesPath);
                string targetSavePath = Path.Combine(targetSavesPath, Path.GetFileName(_selectedSaveFilePath));

                // Copy the save directory
                CopyDirectory(_selectedSaveFilePath, targetSavePath);

                // Add server to settings
                VsmSettings.Servers.Add(newServer);

                LogToConsole($"[DEBUG] Import successful, completing onboarding");
                // Complete onboarding
                var onboardingOverlay = (Grid)FindName("OnboardingOverlay");
                if (onboardingOverlay != null)
                {
                    onboardingOverlay.Visibility = Visibility.Collapsed;
                    // Show main UI
                    var serverTabControl = (TabControl)FindName("ServerTabControl");
                    if (serverTabControl != null) serverTabControl.Visibility = Visibility.Visible;
                    var mainSeparator = (Separator)FindName("MainSeparator");
                    if (mainSeparator != null) mainSeparator.Visibility = Visibility.Visible;
                    var editorsGroupBox = (GroupBox)FindName("EditorsGroupBox");
                    if (editorsGroupBox != null) editorsGroupBox.Visibility = Visibility.Visible;
                    var consoleGrid = (Grid)FindName("ConsoleGrid");
                    if (consoleGrid != null) consoleGrid.Visibility = Visibility.Visible;
                    var statusDockPanel = (DockPanel)FindName("StatusDockPanel");
                    if (statusDockPanel != null) statusDockPanel.Visibility = Visibility.Visible;
                }
                LogToConsole("Onboarding completed successfully!");
                MainSettings.Save(VsmSettings);
                LogToConsole($"Imported server from save directory: {VsmSettings.Servers.Last().Name}");
            }
            catch (Exception ex)
            {
                LogToConsole($"[DEBUG] Import failed: {ex.Message}");
                MessageBox.Show($"Error importing server: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnboardingBackStep3Button_Click(object sender, RoutedEventArgs e)
        {
            _onboardingCurrentStep = 2;
            UpdateOnboardingStepIndicators();
            UpdateOnboardingStepVisibility();
        }

        private string GetSaveDisplayName(string savePath)
        {
            string dirName = Path.GetFileName(savePath);
            string hostSettingsPath = Path.Combine(savePath, "ServerHostSettings.json");
            if (File.Exists(hostSettingsPath))
            {
                try
                {
                    string content = File.ReadAllText(hostSettingsPath);
                    int nameIndex = content.IndexOf("\"Name\":");
                    if (nameIndex >= 0)
                    {
                        int startQuote = content.IndexOf('"', nameIndex + 7);
                        int endQuote = content.IndexOf('"', startQuote + 1);
                        if (startQuote >= 0 && endQuote > startQuote)
                        {
                            string serverName = content.Substring(startQuote + 1, endQuote - startQuote - 1);
                            return serverName;
                        }
                    }
                }
                catch { }
            }
            return $"V Rising Server ({dirName})";
        }

        private void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }
            foreach (string directory in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(targetDir, Path.GetFileName(directory));
                CopyDirectory(directory, targetSubDir);
            }
        }

        private string FindSteamVRisingServerPath()
        {
            try
            {
                // Try to find Steam installation path from registry
                string steamPath = GetSteamInstallPath();
                if (!string.IsNullOrEmpty(steamPath))
                {
                    // Check common Steam library locations
                    string[] libraryPaths = GetSteamLibraryPaths(steamPath);
                    
                    foreach (string libraryPath in libraryPaths)
                    {
                        string serverPath = Path.Combine(libraryPath, "steamapps", "common", "VRisingDedicatedServer");
                        if (Directory.Exists(serverPath) && File.Exists(Path.Combine(serverPath, "VRisingServer.exe")))
                        {
                            return serverPath;
                        }
                    }
                }

                // Fallback: Check default Steam installation path
                string defaultPath = @"C:\Program Files (x86)\Steam\steamapps\common\VRisingDedicatedServer";
                if (Directory.Exists(defaultPath) && File.Exists(Path.Combine(defaultPath, "VRisingServer.exe")))
                {
                    return defaultPath;
                }

                // Another fallback: Check current user's Steam path
                string userSteamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "VRisingDedicatedServer");
                if (Directory.Exists(userSteamPath) && File.Exists(Path.Combine(userSteamPath, "VRisingServer.exe")))
                {
                    return userSteamPath;
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"Error finding Steam VRising server: {ex.Message}");
            }

            return null;
        }

        private string GetSteamInstallPath()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        return key.GetValue("SteamPath") as string;
                    }
                }
            }
            catch { }

            return null;
        }

        private string[] GetSteamLibraryPaths(string steamPath)
        {
            var paths = new List<string> { steamPath };

            try
            {
                string configPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(configPath))
                {
                    string content = File.ReadAllText(configPath);
                    // Simple parsing for library paths
                    var matches = System.Text.RegularExpressions.Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"");
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        if (match.Groups.Count > 1)
                        {
                            paths.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
                        }
                    }
                }
            }
            catch { }

            return paths.ToArray();
        }

        private void SetupServerFiles(string serverPath, string steamServerPath)
        {
            try
            {
                LogToConsole("Setting up server directories (using Steam executable)...");

                // Create necessary directories for server data
                string[] directoriesToCreate = new[] {
                    "SaveData",
                    "SaveData/Settings",
                    "SaveData/Saves",
                    "SaveData/Saves/v4",
                    "Logs"
                };

                foreach (string dir in directoriesToCreate)
                {
                    Directory.CreateDirectory(Path.Combine(serverPath, dir));
                }

                LogToConsole("Server directories setup complete.");
            }
            catch (Exception ex)
            {
                LogToConsole($"Error setting up server directories: {ex.Message}");
                throw;
            }
        }

        private void UpdateOnboardingSetupSummary()
        {
            var summaryText = (TextBlock)FindName("OnboardingSetupSummaryText");
            if (summaryText != null && VsmSettings.Servers.Count > 0)
            {
                var server = VsmSettings.Servers.Last();
                summaryText.Text = $"Server '{server.Name}' is ready!\nLocation: {server.Path}";
            }
        }

        private void LogToConsole(string output)
        {
            Dispatcher.Invoke(new Action(() =>
            {
                var console = (RichTextBox)FindName("MainMenuConsole");
                if (console != null)
                {
                    console.AppendText("\r" + output);
                    console.ScrollToEnd();
                }
            }));
        }

        private void SendDiscordMessage(string message)
        {
            if (VsmSettings.WebhookSettings.Enabled == false || message == "")
                return;

            if (VsmSettings.WebhookSettings.URL == "")
            {
                LogToConsole("Discord webhook tried to send a message but URL is undefined.");
                return;
            }

            if (DiscordSender.WebHook == null)
            {
                DiscordSender.WebHook = VsmSettings.WebhookSettings.URL;
            }            

            DiscordSender.SendMessage(message);
        }

        /// <summary>
        /// Updates SteamCMD, used when the executable could not be found
        /// </summary>
        /// <returns><see cref="bool"/> true if succeeded</returns>
        private async Task<bool> UpdateSteamCMD()
        {            
            string workingDir = Directory.GetCurrentDirectory();
            LogToConsole("SteamCMD not found. Downloading...");
            byte[] fileBytes = await HttpClient.GetByteArrayAsync(@"https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip");
            await File.WriteAllBytesAsync(workingDir + @"\steamcmd.zip", fileBytes);
            if (File.Exists(workingDir + @"\SteamCMD\steamcmd.exe") == true)
            {
                File.Delete(workingDir + @"\SteamCMD\steamcmd.exe");
            }
            LogToConsole("Unzipping...");
            ZipFile.ExtractToDirectory(workingDir + @"\steamcmd.zip", workingDir + @"\SteamCMD");
            if (File.Exists(workingDir + @"\steamcmd.zip"))
            {
                File.Delete(workingDir + @"\steamcmd.zip");
            }

            LogToConsole("Fetching V Rising AppInfo.");
            await CheckForUpdate();

            return true;
        }

        private async Task<bool> UpdateGame(Server server)
        {
            server.Runtime.State = ServerRuntime.ServerState.Updating;

            if (server.Runtime.Process != null)
            {
                LogToConsole("Server is already running. Exiting.");
                return false;
            }

            if (!File.Exists(Directory.GetCurrentDirectory() + @"\SteamCMD\steamcmd.exe"))
            {
                bool sCMDSuccess = await UpdateSteamCMD();
                if (!sCMDSuccess == true)
                {
                    LogToConsole("Unable to download SteamCMD. Exiting update process.");
                    server.Runtime.State = ServerRuntime.ServerState.Stopped;
                    return false;
                }
            }

            string workingDir = Directory.GetCurrentDirectory();
            LogToConsole("Updating game server: " + server.Name);
            string[] installScript = { "force_install_dir \"" + server.Path + "\"", "login anonymous", (VsmSettings.AppSettings.VerifyUpdates) ? "app_update 1829350 validate" : "app_update 1829350", "quit" };
            if (File.Exists(server.Path + @"\steamcmd.txt"))
                File.Delete(server.Path + @"\steamcmd.txt");
            File.WriteAllLines(server.Path + @"\steamcmd.txt", installScript);
            string parameters = $@"+runscript ""{server.Path}\steamcmd.txt""";
            Process steamcmd = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = workingDir + @"\SteamCMD\steamcmd.exe",
                    Arguments = parameters,
                    CreateNoWindow = !VsmSettings.AppSettings.ShowSteamWindow
                }
            };
            steamcmd.Start();
            await steamcmd.WaitForExitAsync();
            LogToConsole("Update completed on server: " + server.Name);
            server.Runtime.State = ServerRuntime.ServerState.Stopped;
            return true;
        }

        private async Task<bool> StartServer(Server server)
        {
            if (server.Runtime.Process != null)
            {
                LogToConsole($"ERROR: {server.Name} is already running");
                return false;
            }

            // Use Steam executable path if available, otherwise fall back to server path
            string executablePath = !string.IsNullOrEmpty(server.SteamPath) 
                ? Path.Combine(server.SteamPath, "VRisingServer.exe")
                : server.Path + @"\VRisingServer.exe";

            if (File.Exists(executablePath))
            {
                LogToConsole("Starting server: " + server.Name + (server.Runtime.RestartAttempts > 0 ? $" Attempt {server.Runtime.RestartAttempts}/3." : ""));
                if (VsmSettings.WebhookSettings.Enabled == true && !string.IsNullOrEmpty(server.WebhookMessages.StartServer) && server.WebhookMessages.Enabled == true)
                    SendDiscordMessage(server.WebhookMessages.StartServer);
                string parameters = $@"-persistentDataPath ""{server.Path + @"\SaveData"}"" -serverName ""{server.LaunchSettings.DisplayName}"" -saveName ""{server.LaunchSettings.WorldName}"" -logFile ""{server.Path + @"\logs\VRisingServer.log"}""{(server.LaunchSettings.BindToIP ? $@" -address ""{server.LaunchSettings.BindingIP}""" : "")}";
                Process serverProcess = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        WindowStyle = ProcessWindowStyle.Minimized,
                        FileName = executablePath,
                        UseShellExecute = true,
                        Arguments = parameters
                    },
                    EnableRaisingEvents = true
                };
                serverProcess.Exited += new EventHandler((sender, e) => ServerProcessExited(sender, e, server));
                serverProcess.Start();
                server.Runtime.State = ServerRuntime.ServerState.Running;
                server.Runtime.UserStopped = false;
                server.Runtime.Process = serverProcess;                
                return true;
            }
            else
            {
                LogToConsole($"'VRisingServer.exe' not found at {executablePath}. Please make sure server is installed correctly.");
                return false;
            }
        }

        private async Task SendRconRestartMessage(Server server)
        {
            RCONClient = new()
            {
                UseUtf8 = true
            };

            RCONClient.OnLog += async message =>
            {
                if (message == "Authentication success.")
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    RCONClient.SendCommand("announcerestart 5", result =>
                    {
                        //Do nothing
                    });
                }

            };

            RCONClient.OnConnectionStateChange += state =>
            {
                if (state == RemoteConClient.ConnectionStateChange.Connected)
                {
                    RCONClient.Authenticate(server.RconServerSettings.Password);
                }
            };

            RCONClient.Connect(server.RconServerSettings.IPAddress, int.Parse(server.RconServerSettings.Port));
            await Task.Delay(TimeSpan.FromSeconds(3));
            RCONClient.Disconnect();
        }

        private void ScanForServers()
        {
            int foundServers = 0;

            Process[] serverProcesses = Process.GetProcessesByName("vrisingserver");
            foreach (Process process in serverProcesses)
            {
                foreach (Server server in VsmSettings.Servers)
                {
                    // Check both Steam path and server path for the executable
                    string steamExecutablePath = !string.IsNullOrEmpty(server.SteamPath) 
                        ? Path.Combine(server.SteamPath, "VRisingServer.exe")
                        : null;
                    string serverExecutablePath = server.Path + @"\VRisingServer.exe";
                    
                    if ((steamExecutablePath != null && process.MainModule?.FileName == steamExecutablePath) ||
                        process.MainModule?.FileName == serverExecutablePath)
                    {
                        server.Runtime.State = ServerRuntime.ServerState.Running;
                        process.EnableRaisingEvents = true;
                        process.Exited += new EventHandler((sender, e) => ServerProcessExited(sender, e, server));
                        server.Runtime.Process = process;
                        foundServers++;
                    }
                }
            }

            foreach (Server server in VsmSettings.Servers)
            {
                if (server.AutoStart == true && server.Runtime.State == ServerRuntime.ServerState.Stopped)
                {
                    StartServer(server);
                }
            }

            if (foundServers > 0)
            {
                LogToConsole($"Found {foundServers} servers that are running.");
            }
        }

        private async Task<bool> StopServer(Server server)
        {
            LogToConsole("Stopping server: " + server.Name);
            if (VsmSettings.WebhookSettings.Enabled == true && !string.IsNullOrEmpty(server.WebhookMessages.StopServer) && server.WebhookMessages.Enabled == true)
                SendDiscordMessage(server.WebhookMessages.StopServer);

            server.Runtime.UserStopped = true;

            bool success;
            bool close = server.Runtime.Process.CloseMainWindow();            

            if (close)
            {
                await server.Runtime.Process.WaitForExitAsync();
                server.Runtime.Process = null;
                success = true;
            }
            else
            {
                success = false;
            }
            return success;
        }

        private bool RemoveServer(Server server)
        {
            int serverIndex = VsmSettings.Servers.IndexOf(server);
            string workingDir = Directory.GetCurrentDirectory();
            string serverName = server.Name.Replace(" ", "_");

            if (MessageBox.Show($"Are you sure you want to remove the server {server.Name}?\nThis action is permanent and all files will be removed.", "Remove Server - Verification", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
            {
                return false;
            }

            if (serverIndex != -1)
            {
                if (MessageBox.Show($@"Create a backup of the SaveData?{Environment.NewLine}It will be saved to: {workingDir}\Backups\{serverName}_Bak.zip", "Remove Server - Backup", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    if (!Directory.Exists(workingDir + @"\Backups"))
                        Directory.CreateDirectory(workingDir + @"\Backups");

                    if (Directory.Exists(server.Path + @"\SaveData\"))
                    {
                        if (File.Exists(workingDir + @"\Backups\" + serverName + "_Bak.zip"))
                            File.Delete(workingDir + @"\Backups\" + serverName + "_Bak.zip");

                        ZipFile.CreateFromDirectory(server.Path + @"\SaveData\", workingDir + @"\Backups\" + serverName + "_Bak.zip");
                    }
                }
                VsmSettings.Servers.RemoveAt(serverIndex);
                if (Directory.Exists(server.Path))
                    Directory.Delete(server.Path, true);
                return true;
            }
            else
            {
                return false;
            }
        }

        private async Task<bool> CheckForUpdate()
        {
            bool foundUpdate = false;
            LogToConsole("Searching for updates...");
            string json = await HttpClient.GetStringAsync("https://api.steamcmd.net/v1/info/1829350");
            JsonNode jsonNode = JsonNode.Parse(json);

            var version = jsonNode!["data"]["1829350"]["depots"]["branches"]["public"]["timeupdated"]!.ToString();

            if (version != VsmSettings.AppSettings.LastUpdateTimeUNIX)
            {
                VsmSettings.AppSettings.LastUpdateTimeUNIX = version;
                foundUpdate = true;
            }

            if (VsmSettings.AppSettings.LastUpdateTimeUNIX == "")
            {
                VsmSettings.AppSettings.LastUpdateTimeUNIX = version;
                foundUpdate = true;
            }

            if (VsmSettings.AppSettings.LastUpdateTimeUNIX != "")
                VsmSettings.AppSettings.LastUpdateTime = "Last Update on Steam: " + DateTimeOffset.FromUnixTimeSeconds(long.Parse(VsmSettings.AppSettings.LastUpdateTimeUNIX)).DateTime.ToString();

            MainSettings.Save(VsmSettings);
            return foundUpdate;
        }

        private async void ReadLog(Server server)
        {
            using (FileStream fs = new FileStream(server.Path + @"\Logs\VRisingServer.log", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs))
            {
                string ipAddress = "";
                string steamID = "";
                int foundVariables = 0;

                while (foundVariables < 3 && server.Runtime.Process != null)
                {
                    string line = await sr.ReadLineAsync();
                    if (line != null)
                    {
                        if (line.Contains("SteamPlatformSystem - OnPolicyResponse - Public IP: "))
                        {
                            ipAddress = line.Split("SteamPlatformSystem - OnPolicyResponse - Public IP: ")[1];
                            foundVariables++;
                        }
                        if (line.Contains("SteamNetworking - Successfully logged in with the SteamGameServer API. SteamID: "))
                        {
                            steamID = line.Split("SteamNetworking - Successfully logged in with the SteamGameServer API. SteamID: ")[1];
                            foundVariables++;
                        }
                        if (line.Contains("Shutting down Asynchronous Streaming"))
                            foundVariables++;
                    }                    
                }

                if (foundVariables == 3 && VsmSettings.WebhookSettings.Enabled == true && server.WebhookMessages.Enabled == true)
                {
                    List<string> toSendList = new()
                    {
                        !string.IsNullOrEmpty(server.WebhookMessages.ServerReady) ? server.WebhookMessages.ServerReady : "",
                        (server.WebhookMessages.BroadcastIP == true) ? $"Public IP: {ipAddress}" : "",
                        (server.WebhookMessages.BroadcastSteamID == true) ? $"SteamID: {steamID}" : ""
                    };

                    if (!toSendList.All(x => string.IsNullOrEmpty(x)))
                    {
                        string toSend = string.Join("\r", toSendList);
                        SendDiscordMessage(toSend);
                    }
                }

                sr.Close();
                fs.Close();
            }
        }

        #region Events
        private async void ServerProcessExited(object sender, EventArgs e, Server server)
        {
            server.Runtime.State = ServerRuntime.ServerState.Stopped;

            switch (server.Runtime.Process.ExitCode)
            {
                case 1:
                    LogToConsole($"{server.Name} crashed.");
                    break;
                case -2147483645:
                    LogToConsole($"{server.Name} exited with code '-2147483645' which can occur when ports failed to open. Make sure no other server is using the same ports.");
                    break;
            }

            server.Runtime.Process = null;

            if (server.Runtime.RestartAttempts >= 3)
            {
                LogToConsole($"Server '{server.Name}' attempted to restart 3 times unsuccessfully. Disabling auto-restart.");
                if (VsmSettings.WebhookSettings.Enabled == true && !string.IsNullOrEmpty(server.WebhookMessages.AttemptStart3) && server.WebhookMessages.Enabled == true)
                    SendDiscordMessage(server.WebhookMessages.AttemptStart3);
                server.Runtime.RestartAttempts = 0;
                server.AutoRestart = false;
                return;
            }

            if (server.AutoRestart == true && server.Runtime.UserStopped == false)
            {
                server.Runtime.RestartAttempts++;
                if (VsmSettings.WebhookSettings.Enabled == true && !string.IsNullOrEmpty(server.WebhookMessages.ServerCrash) && server.WebhookMessages.Enabled == true)
                    SendDiscordMessage(server.WebhookMessages.ServerCrash);
                await StartServer(server);
            }
        }

        private void AppSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "DarkMode":
                    if (VsmSettings.AppSettings.DarkMode == true)
                    {
                        ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                    }
                    else
                    {
                        ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                    }
                    break;
            }
        }

        private void Servers_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            var serverTabControl = (TabControl)FindName("ServerTabControl");
            if (serverTabControl != null)
            {
                int serversLength = serverTabControl.Items.Count;
                if (serversLength > 0)
                {
                    serverTabControl.SelectedIndex = serversLength - 1;
                }
            }
        }
#endregion

        #region Buttons
        private async void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            Server server = ((Button)sender).DataContext as Server;
            MainSettings.Save(VsmSettings);

            bool started = await StartServer(server);
            await Task.Delay(3000);

            if (started == true && VsmSettings.WebhookSettings.Enabled)
                ReadLog(server);
        }

        private async void UpdateServerButton_Click(object sender, RoutedEventArgs e)
        {
            Server server = ((Button)sender).DataContext as Server;

            await UpdateGame(server);
        }

        private async void StopServerButton_Click(object sender, RoutedEventArgs e)
        {
            Server server = ((Button)sender).DataContext as Server;            

            bool success = await StopServer(server);
            if (success)
            {
                LogToConsole("Successfully stopped server: " + server.Name);
            }
            else
            {
                LogToConsole("Unable to stop server: " + server.Name);
            }   
        }

        private void RemoveServerButton_Click(object sender, RoutedEventArgs e)
        {
            Server server = ((Button)sender).DataContext as Server;

            if (server == null)
            {
                LogToConsole("ERROR: Unable to find selected server to delete");
                return;
            }
            bool success = RemoveServer(server);
            if (!success)
                LogToConsole("There was an error deleting the server or the action was aborted.");
            else
                MainSettings.Save(VsmSettings);
        }

        private void ServerSettingsEditorButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Application.Current.Windows.OfType<ServerSettingsEditor>().Any())
            {
                var serverTabControl = (TabControl)FindName("ServerTabControl");
                if (VsmSettings.AppSettings.AutoLoadEditor == true && serverTabControl != null && !(serverTabControl.SelectedIndex == -1))
                {
                    ServerSettingsEditor sSettingsEditor = new(VsmSettings.Servers, true, serverTabControl.SelectedIndex);
                    sSettingsEditor.Show();
                }
                else
                {
                    ServerSettingsEditor sSettingsEditor = new(VsmSettings.Servers);
                    sSettingsEditor.Show();
                }
            }
        }

        private void ManageAdminsButton_Click(object sender, RoutedEventArgs e)
        {
            Server server = ((Button)sender).DataContext as Server;

            if (!File.Exists(server.Path + @"\SaveData\Settings\adminlist.txt"))
            {
                LogToConsole("Unable to find adminlist.txt, please make sure the server is installed correctly.");
                return;
            }

            if (!Application.Current.Windows.OfType<AdminManager>().Any())
            {
                AdminManager aManager = new AdminManager(server);
                aManager.Show();
            }
        }

        private void ServerFolderButton_Click(object sender, RoutedEventArgs e)
        {
            Server server = ((Button)sender).DataContext as Server;

            if (Directory.Exists(server.Path))
                Process.Start("explorer.exe", server.Path);
            else
                LogToConsole("Unable to find server folder.");
        }

        private void GameSettingsEditor_Click(object sender, RoutedEventArgs e)
        {
            if (!Application.Current.Windows.OfType<GameSettingsEditor>().Any())
            {
                var serverTabControl = (TabControl)FindName("ServerTabControl");
                if (VsmSettings.AppSettings.AutoLoadEditor == true && serverTabControl != null && !(serverTabControl.SelectedIndex == -1))
                {
                    GameSettingsEditor gSettingsEditor = new(VsmSettings.Servers, true, serverTabControl.SelectedIndex);
                    gSettingsEditor.Show();
                }
                else
                {
                    GameSettingsEditor gSettingsEditor = new(VsmSettings.Servers);
                    gSettingsEditor.Show();
                }
            }
        }

        private void RconServerButton_Click(object sender, RoutedEventArgs e)
        {
            Server server = ((Button)sender).DataContext as Server;

            if (!Application.Current.Windows.OfType<RconConsole>().Any())
            {
                RconConsole rConsole = new(server);
                rConsole.Show();
            }
        }
        #endregion
    }
}