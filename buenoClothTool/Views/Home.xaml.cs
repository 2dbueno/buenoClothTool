using buenoClothTool.Constants;
using buenoClothTool.Helpers;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using static buenoClothTool.Controls.CustomMessageBox;

namespace buenoClothTool.Views
{
    /// <summary>
    /// Interaction logic for Home.xaml
    /// </summary>
    public partial class Home : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private ObservableCollection<RecentProject> _recentlyOpened;
        public ObservableCollection<RecentProject> RecentlyOpened
        {
            get => _recentlyOpened;
            set
            {
                _recentlyOpened = value;
                OnPropertyChanged(nameof(RecentlyOpened));
                OnPropertyChanged(nameof(ShowNoRecentProjects));
            }
        }

        public bool ShowNoRecentProjects => RecentlyOpened == null || RecentlyOpened.Count == 0;

        private readonly List<string> didYouKnowStrings = [
            "You can open any existing addon and it will load all properties such as heels or hats.",
            "You can export an existing project when you are not finished and later import it to continue working on it.",
            "There is switch to enable dark theme in the settings.",
            "There is 'live texture' feature in 3d preview? It allows you to see how your texture looks on the model in real time, even after changes.",
            "You can click SHIFT + DEL to instantly delete a selected drawable, without popup.",
            "You can click CTRL + DEL to instantly replace a selected drawable with reserved drawable.",
            "You can reserve your drawables and later change it to real model.",
            "You can hover over warning icon to see what is wrong with your drawable or texture.",
        ];

        public string RandomDidYouKnow => didYouKnowStrings[new Random().Next(0, didYouKnowStrings.Count)];

        public Home()
        {
            InitializeComponent();
            DataContext = this;

            LoadRecentProjects();

        }

        private void LoadRecentProjects()
        {
            var recentProjects = PersistentSettingsHelper.Instance.RecentlyOpenedProjects;
            var validProjects = recentProjects.Where(p => File.Exists(p.FilePath)).ToList();
            
            if (validProjects.Count != recentProjects.Count)
            {
                PersistentSettingsHelper.Instance.RecentlyOpenedProjects = validProjects;
            }
            
            RecentlyOpened = new ObservableCollection<RecentProject>(validProjects);
        }

        private async void CreateNew_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mainProjectsFolder = PersistentSettingsHelper.Instance.MainProjectsFolder;
                if (string.IsNullOrEmpty(mainProjectsFolder))
                {
                    Show("Please configure the main projects folder in settings first.", 
                         "Configuration Required", 
                         CustomMessageBoxButtons.OKOnly, 
                         CustomMessageBoxIcon.Warning);
                    return;
                }

                if (!Directory.Exists(mainProjectsFolder))
                {
                    Show($"Main projects folder does not exist: {mainProjectsFolder}\n\nPlease update it in settings.", 
                         "Folder Not Found", 
                         CustomMessageBoxButtons.OKOnly, 
                         CustomMessageBoxIcon.Warning);
                    return;
                }

                bool nameAccepted = false;
                string projectName = string.Empty;

                while (!nameAccepted)
                {
                    var (result, textBoxValue) = Show("Choose a name for your project", 
                                                       "Project Name", 
                                                       CustomMessageBoxButtons.OKCancel, 
                                                       CustomMessageBoxIcon.None, 
                                                       true);

                    if (result != CustomMessageBoxResult.OK || string.IsNullOrWhiteSpace(textBoxValue))
                    {
                        return;
                    }

                    projectName = textBoxValue.Trim();

                    if (projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        Show("Project name contains invalid characters. Please choose a different name.", 
                             "Invalid Name", 
                             CustomMessageBoxButtons.OKOnly, 
                             CustomMessageBoxIcon.Warning);
                        continue;
                    }

                    var projectFolder = Path.Combine(mainProjectsFolder, projectName);
                    if (Directory.Exists(projectFolder))
                    {
                        var autoSavePath = Path.Combine(projectFolder, "autosave.json");
                        if (File.Exists(autoSavePath))
                        {
                            var openExisting = Show(
                                $"A project with the name '{projectName}' already exists.\n\nDo you want to open it instead?",
                                "Project Exists",
                                CustomMessageBoxButtons.YesNo,
                                CustomMessageBoxIcon.Question);

                            if (openExisting == CustomMessageBoxResult.Yes)
                            {
                                await SaveHelper.LoadSaveFileAsync(autoSavePath);
                                LoadRecentProjects();
                                MainWindow.NavigationHelper.Navigate("Project");
                                return;
                            }
                            else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            var useFolder = Show(
                                $"A folder named '{projectName}' already exists but contains no save file.\n\nDo you want to create a new project in this folder?",
                                "Folder Exists",
                                CustomMessageBoxButtons.YesNo,
                                CustomMessageBoxIcon.Question);

                            if (useFolder == CustomMessageBoxResult.Yes)
                            {
                                nameAccepted = true;
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }
                    else
                    {
                        nameAccepted = true;
                    }
                }

                var finalProjectFolder = Path.Combine(mainProjectsFolder, projectName);
                Directory.CreateDirectory(finalProjectFolder);

                var assetsFolder = Path.Combine(finalProjectFolder, GlobalConstants.ASSETS_FOLDER_NAME);
                Directory.CreateDirectory(assetsFolder);

                MainWindow.AddonManager.ProjectName = projectName;
                MainWindow.AddonManager.CreateAddon();

                var newProjectAutoSavePath = Path.Combine(finalProjectFolder, "autosave.json");
                PersistentSettingsHelper.Instance.AddRecentProject(
                    newProjectAutoSavePath,
                    projectName,
                    drawableCount: 0,
                    addonCount: 1
                );
                
                LoadRecentProjects();

                LogHelper.Log($"Created new project: {projectName} at {finalProjectFolder}");
                MainWindow.NavigationHelper.Navigate("Project");
            }
            catch (Exception ex)
            {
                LogHelper.Log($"Failed to create new project: {ex.Message}", Views.LogType.Error);
                Show($"Failed to create new project: {ex.Message}", 
                     "Error", 
                     CustomMessageBoxButtons.OKOnly, 
                     CustomMessageBoxIcon.Error);
            }
        }

        private async void OpenAddon_Click(object sender, RoutedEventArgs e)
        {
            var success = await MainWindow.Instance.OpenAddonAsync(true);
            if (success)
            {
                MainWindow.NavigationHelper.Navigate("Project");
            }
        }

        private async void ImportProject_Click(object sender, RoutedEventArgs e)
        {
            var success = await MainWindow.Instance.ImportProjectAsync(true);
            if (success)
            {
                MainWindow.NavigationHelper.Navigate("Project");
            }
        }

        private async void OpenSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new()
                {
                    Title = "Open Save File",
                    Filter = "Save files (*.json)|*.json|All files (*.*)|*.*",
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    if (!SaveHelper.CheckUnsavedChangesMessage())
                    {
                        return;
                    }

                    await SaveHelper.LoadSaveFileAsync(openFileDialog.FileName);
                    LoadRecentProjects();
                    MainWindow.NavigationHelper.Navigate("Project");
                }
            }
            catch (Exception ex)
            {
                Show($"Failed to load save: {ex.Message}", 
                     "Error", 
                     CustomMessageBoxButtons.OKOnly, 
                     CustomMessageBoxIcon.Error);
            }
        }

        private async void RecentProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string filePath)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        Show("This save file no longer exists.", 
                             "File Not Found", 
                             CustomMessageBoxButtons.OKOnly, 
                             CustomMessageBoxIcon.Warning);
                        
                        var recentProjects = PersistentSettingsHelper.Instance.RecentlyOpenedProjects;
                        recentProjects.RemoveAll(p => p.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                        PersistentSettingsHelper.Instance.RecentlyOpenedProjects = recentProjects;
                        LoadRecentProjects();
                        return;
                    }

                    if (!SaveHelper.CheckUnsavedChangesMessage())
                    {
                        return;
                    }

                    await SaveHelper.LoadSaveFileAsync(filePath);
                    LoadRecentProjects();
                    MainWindow.NavigationHelper.Navigate("Project");
                }
                catch (Exception ex)
                {
                    Show($"Failed to load save: {ex.Message}", 
                         "Error", 
                         CustomMessageBoxButtons.OKOnly, 
                         CustomMessageBoxIcon.Error);
                }
            }
        }

        private void RemoveRecentProject_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            
            if (sender is Button button && button.Tag is string filePath)
            {
                try
                {
                    var project = PersistentSettingsHelper.Instance.RecentlyOpenedProjects
                        .FirstOrDefault(p => p.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                    
                    var projectName = project?.ProjectName ?? "";
                    
                    var recentProjects = PersistentSettingsHelper.Instance.RecentlyOpenedProjects;
                    recentProjects.RemoveAll(p => p.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                    PersistentSettingsHelper.Instance.RecentlyOpenedProjects = recentProjects;
                    
                    LoadRecentProjects();
                    
                    LogHelper.Log($"Removed project from recent list: {projectName}");
                }
                catch (Exception ex)
                {
                    LogHelper.Log($"Failed to remove recent project: {ex.Message}", Views.LogType.Error);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.MainWindow.Close();
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ToolInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
    }
}
