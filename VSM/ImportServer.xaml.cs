using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VRisingServerManager
{
    public partial class ImportServer : Window
    {
        private MainSettings _mainSettings;
        private string _selectedSavePath = "";
        private Dictionary<string, string> _availableSaves = new();

        public ImportServer(MainSettings mainSettings)
        {
            InitializeComponent();
            _mainSettings = mainSettings;
            DataContext = new Server();

            // Auto-detect local saves path
            DetectLocalSavesPath();
        }

        private void DetectLocalSavesPath()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localLowPath = Path.Combine(userProfile, "AppData", "LocalLow", "Stunlock Studios", "VRising");

            // Try CloudSaves first
            string cloudSavesPath = Path.Combine(localLowPath, "CloudSaves");
            if (Directory.Exists(cloudSavesPath))
            {
                string[] steamIdDirs = Directory.GetDirectories(cloudSavesPath);
                if (steamIdDirs.Length > 0)
                {
                    // Use the first SteamID directory found
                    string steamIdPath = steamIdDirs[0];
                    string v4Path = Path.Combine(steamIdPath, "v4");
                    if (Directory.Exists(v4Path))
                    {
                        LocalSavesPathTextBox.Text = v4Path;
                        LoadAvailableSaves(v4Path);
                        return;
                    }
                }
            }

            // Try direct Saves path
            string savesPath = Path.Combine(localLowPath, "Saves", "v4");
            if (Directory.Exists(savesPath))
            {
                LocalSavesPathTextBox.Text = savesPath;
                LoadAvailableSaves(savesPath);
                return;
            }

            // If neither found, show message
            LocalSavesPathTextBox.Text = "Not found - click Browse to locate";
        }

        private void LoadAvailableSaves(string savesPath)
        {
            _availableSaves.Clear();
            AvailableSavesComboBox.Items.Clear();

            if (!Directory.Exists(savesPath))
                return;

            string[] saveDirs = Directory.GetDirectories(savesPath);
            foreach (string saveDir in saveDirs)
            {
                string dirName = Path.GetFileName(saveDir);
                string displayName = GetSaveDisplayName(saveDir);

                _availableSaves[displayName] = saveDir;
                AvailableSavesComboBox.Items.Add(displayName);
            }

            if (AvailableSavesComboBox.Items.Count > 0)
            {
                AvailableSavesComboBox.SelectedIndex = 0;
            }
        }

        private string GetSaveDisplayName(string savePath)
        {
            string dirName = Path.GetFileName(savePath);

            // Try to read ServerHostSettings.json for server name
            string hostSettingsPath = Path.Combine(savePath, "ServerHostSettings.json");
            if (File.Exists(hostSettingsPath))
            {
                try
                {
                    string content = File.ReadAllText(hostSettingsPath);
                    // Simple JSON parsing to extract name
                    int nameIndex = content.IndexOf("\"Name\":");
                    if (nameIndex >= 0)
                    {
                        int startQuote = content.IndexOf("\"", nameIndex + 7);
                        int endQuote = content.IndexOf("\"", startQuote + 1);
                        if (startQuote >= 0 && endQuote > startQuote)
                        {
                            string serverName = content.Substring(startQuote + 1, endQuote - startQuote - 1);
                            return $"{serverName} ({dirName})";
                        }
                    }
                }
                catch { }
            }

            return dirName;
        }

        private void ServerPathButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select server installation directory"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ((Server)DataContext).Path = dialog.SelectedPath;
            }
        }

        private void LocalSavesPathButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select local V Rising saves directory (should contain v4 folder)"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = dialog.SelectedPath;

                // Check if this is the v4 directory
                if (Path.GetFileName(selectedPath).ToLower() == "v4")
                {
                    LocalSavesPathTextBox.Text = selectedPath;
                    LoadAvailableSaves(selectedPath);
                }
                else
                {
                    // Check if v4 exists in the selected directory
                    string v4Path = Path.Combine(selectedPath, "v4");
                    if (Directory.Exists(v4Path))
                    {
                        LocalSavesPathTextBox.Text = v4Path;
                        LoadAvailableSaves(v4Path);
                    }
                    else
                    {
                        MessageBox.Show("Could not find v4 directory in the selected path. Please select the directory containing the v4 folder.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void AvailableSavesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AvailableSavesComboBox.SelectedItem != null)
            {
                string selectedDisplayName = AvailableSavesComboBox.SelectedItem.ToString()!;
                if (_availableSaves.TryGetValue(selectedDisplayName, out _selectedSavePath))
                {
                    UpdateSaveInfo(_selectedSavePath);
                    ImportButton.IsEnabled = true;
                }
            }
            else
            {
                SaveInfoTextBlock.Text = "No save selected";
                ImportButton.IsEnabled = false;
            }
        }

        private void UpdateSaveInfo(string savePath)
        {
            try
            {
                var files = Directory.GetFiles(savePath);
                var directories = Directory.GetDirectories(savePath);

                string info = $"Files: {files.Length}, Directories: {directories.Length}";

                // Try to get more details from ServerHostSettings.json
                string hostSettingsPath = Path.Combine(savePath, "ServerHostSettings.json");
                if (File.Exists(hostSettingsPath))
                {
                    try
                    {
                        string content = File.ReadAllText(hostSettingsPath);
                        // Extract basic info
                        if (content.Contains("\"Name\":"))
                        {
                            info += " - Contains server configuration";
                        }
                    }
                    catch { }
                }

                SaveInfoTextBlock.Text = info;
            }
            catch
            {
                SaveInfoTextBlock.Text = "Unable to read save information";
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(((Server)DataContext).Name))
            {
                MessageBox.Show("Please enter a server name.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(((Server)DataContext).Path))
            {
                MessageBox.Show("Please select a server installation path.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_selectedSavePath))
            {
                MessageBox.Show("Please select a local save to import.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Create the server
                Server newServer = (Server)DataContext;
                newServer.LaunchSettings.WorldName = Path.GetFileName(_selectedSavePath);

                // Copy the save files
                string targetSavesPath = Path.Combine(newServer.Path, "SaveData", "Saves", "v4");
                Directory.CreateDirectory(targetSavesPath);

                string targetSavePath = Path.Combine(targetSavesPath, Path.GetFileName(_selectedSavePath));

                // Copy the entire save directory
                CopyDirectory(_selectedSavePath, targetSavePath);

                // Add to main settings
                _mainSettings.Servers.Add(newServer);

                MessageBox.Show($"Server '{newServer.Name}' has been imported successfully!\n\nThe local save has been copied to the server directory.", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing server: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}