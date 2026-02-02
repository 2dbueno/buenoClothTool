using buenoClothTool.Extensions;
using buenoClothTool.Views;
using Material.Icons;
using System;
using System.IO;
using System.Threading;
using System.Windows;
using buenoClothTool.Properties;
using static buenoClothTool.Controls.CustomMessageBox;
using System.Net.Http;
using System.Windows.Threading;

namespace buenoClothTool
{
    public partial class App : Application
    {
        public static ISplashScreen splashScreen;
        private ManualResetEvent ResetSplashCreated;
        private Thread SplashThread;

        public static readonly HttpClient httpClient = new();
        private static readonly string pluginsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "buenoClothTool", "plugins");

        protected override void OnStartup(StartupEventArgs e)
        {
            ResetSplashCreated = new ManualResetEvent(false);

            SplashThread = new Thread(ShowSplash);
            SplashThread.SetApartmentState(ApartmentState.STA);
            SplashThread.Start();

            ResetSplashCreated.WaitOne();
            base.OnStartup(e);

            bool isDarkTheme = Settings.Default.IsDarkMode;
            ChangeTheme(isDarkTheme);
        }

        public App()
        {
            MaterialIconDataProvider.Instance = new CustomIconProvider();
            httpClient.DefaultRequestHeaders.Add("X-BuenoClothTool", "true");

            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;

            Show($"An error occurred: {ex.Message}", "Error", CustomMessageBoxButtons.OKOnly);

            var date = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            var path = Path.Combine(AppContext.BaseDirectory, $"error-{date}.log");
            File.WriteAllText(path, ex.ToString());
            Console.WriteLine("Unhandled exception: " + ex.ToString());

        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var date = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            var path = Path.Combine(AppContext.BaseDirectory, $"error-{date}.log");
            File.WriteAllText(path, e.Exception.ToString());

            e.Handled = true;
        }

        private void ShowSplash()
        {
            Views.SplashScreen animatedSplashScreenWindow = new();
            splashScreen = animatedSplashScreenWindow;

            animatedSplashScreenWindow.Show();

            ResetSplashCreated.Set();
            System.Windows.Threading.Dispatcher.Run();
        }

        public static void ChangeTheme(bool isDarkMode)
        {
            Uri uri = isDarkMode
                ? new Uri("Themes/Dark.xaml", UriKind.Relative)
                : new Uri("Themes/Light.xaml", UriKind.Relative);

            ResourceDictionary theme = new() { Source = uri };

            Application.Current.Resources.MergedDictionaries.Clear();

            Application.Current.Resources.MergedDictionaries.Add(theme);

            buenoClothTool.MainWindow.Instance?.UpdateAvalonDockTheme(isDarkMode);
        }
    }
}