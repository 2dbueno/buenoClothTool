using buenoClothTool.Extensions;
using buenoClothTool.Properties;
using buenoClothTool.Views;
using Material.Icons;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using static buenoClothTool.Controls.CustomMessageBox;

namespace buenoClothTool
{
    public partial class App : Application
    {
        public static ISplashScreen splashScreen;
        private ManualResetEvent ResetSplashCreated;
        private Thread SplashThread;

        public static readonly HttpClient httpClient = new();

        // Define pasta de logs organizada
        private static readonly string LogsDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");

        protected override void OnStartup(StartupEventArgs e)
        {
            // Cria diretório de logs se não existir
            Directory.CreateDirectory(LogsDirectory);

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

            // Hook up error handlers
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            LogFatalError(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogFatalError(e.Exception, "DispatcherUnhandledException");
            e.Handled = true; // Tenta impedir o crash total se possível, mas o log já foi salvo
        }

        private static void LogFatalError(Exception ex, string source)
        {
            if (ex == null) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== CRASH REPORT ===");
                sb.AppendLine($"Date: {DateTime.Now}");
                sb.AppendLine($"Source: {source}");
                sb.AppendLine($"Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
                sb.AppendLine($"OS: {Environment.OSVersion}");
                sb.AppendLine("--------------------");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"Stack Trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    sb.AppendLine("--------------------");
                    sb.AppendLine($"Inner Exception: {ex.InnerException.Message}");
                    sb.AppendLine(ex.InnerException.StackTrace);
                }

                // Salva na pasta Logs com data/hora
                string fileName = $"crash_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
                string fullPath = Path.Combine(LogsDirectory, fileName);

                File.WriteAllText(fullPath, sb.ToString());

                // Avisa o usuário de forma amigável
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Show($"A critical error occurred.\nA log file has been created at:\n{fileName}\n\nPlease send this file to the developer.",
                         "Critical Error",
                         CustomMessageBoxButtons.OKOnly,
                         CustomMessageBoxIcon.Error);
                });
            }
            catch (Exception logEx)
            {
                // Se falhar ao logar (ex: disco cheio), tenta apenas mostrar na tela ou console
                System.Diagnostics.Debug.WriteLine($"Failed to log error: {logEx.Message}");
            }
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