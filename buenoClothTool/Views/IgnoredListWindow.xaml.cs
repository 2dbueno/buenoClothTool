using buenoClothTool.Helpers;
using System.Windows;
using System.Windows.Controls;

namespace buenoClothTool.Views
{
    public partial class IgnoredListWindow : Window
    {
        public IgnoredListWindow()
        {
            InitializeComponent();
            LoadList();
        }

        private void LoadList()
        {
            // Vincula a lista de ignorados do AddonManager ao ListBox
            IgnoredListBox.ItemsSource = null;
            IgnoredListBox.ItemsSource = MainWindow.AddonManager.IgnoredDuplicateGroups;
        }

        private void RemoveIgnored_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string hashToRemove)
            {
                var result = Controls.CustomMessageBox.Show(
                    "Are you sure you want to stop ignoring this group?\nIt will appear as a duplicate again in the next scan.",
                    "Remove from Whitelist",
                    Controls.CustomMessageBox.CustomMessageBoxButtons.YesNo,
                    Controls.CustomMessageBox.CustomMessageBoxIcon.Question);

                if (result == Controls.CustomMessageBox.CustomMessageBoxResult.Yes)
                {
                    // Remove da lista
                    MainWindow.AddonManager.IgnoredDuplicateGroups.Remove(hashToRemove);

                    // Salva para garantir que a mudança persista
                    SaveHelper.SetUnsavedChanges(true);

                    // Atualiza a UI (embora ObservableCollection geralmente atualize sozinha, é bom garantir)
                    LoadList();
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}