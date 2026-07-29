using System;
using System.Windows;
using System.Windows.Threading;
using System.Threading;
using System.Diagnostics;

namespace RevivalDPI
{
    public partial class App : Application
    {
        private static Mutex _mutex = null;
        private static bool _mutexOwned;
        private const string MutexName = "RevivalDPISingleInstanceMutex";
        
        public static bool IsSingleInstanceRejected { get; private set; } = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Load language first before using LanguageManager
            LoadLanguage();
            
            // Check if another instance is already running
            if (!CheckSingleInstance())
            {
                IsSingleInstanceRejected = true; // Single instance reddedildiğini işaretle
                MessageBox.Show(
                    GetLocalizedText("messages", "single_instance_message", "RevivalDPI zaten çalışıyor. Pencereyi göremiyorsanız Görev Yöneticisi kullanarak RevivalDPI.exe'yi sonlandırın."),
                    GetLocalizedText("messages", "single_instance_title", "Uygulama Zaten Çalışıyor"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return;
            }
            
            // Set up global exception handling
            Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            
            // Check if running as administrator
            if (!IsRunningAsAdministrator())
            {
                MessageBox.Show(
                    GetLocalizedText("messages", "admin_required_message", "Bu uygulama yönetici izinleri gerektirir. Lütfen yönetici olarak çalıştırın."),
                    GetLocalizedText("messages", "admin_required_title", "Yönetici İzinleri Gerekli"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            // Tema, ana pencere oluşturulmadan önce kurulmalı: aksi hâlde pencere
            // önce varsayılan renklerle çizilir ve tema uygulanırken göz kırpma
            // (flash) oluşur.
            Themes.ThemeManager.Initialize();

#if DEBUG
            // Light.xaml ve Dark.xaml'de anahtar kümeleri ayrışırsa tema geçişi
            // sessizce bozulur (eksik anahtarda kontrol eski renginde kalır).
            // Bu yüzden hata ayıklama derlemesinde başlangıçta doğrulanır.
            var mismatched = Themes.ThemeManager.FindMismatchedKeys();
            if (mismatched.Count > 0)
            {
                Debug.WriteLine("TEMA ANAHTARI UYUŞMAZLIĞI:");
                foreach (var key in mismatched) Debug.WriteLine("  " + key);
                Debug.Fail("Light.xaml ve Dark.xaml aynı anahtarları tanımlamalı. Ayrıntı: Output penceresi.");
            }
#endif

            StartUi();
        }

        /// <summary>
        /// Motoru ve arayüzü ayağa kaldırır.
        ///
        /// <see cref="Legacy.LegacyEngineWindow"/> tüm kurulum/servis iş
        /// mantığını taşır ve durumunu kendi XAML'indeki kontrollerde tutar.
        /// Bu yüzden bir <c>Window</c> olarak oluşturulur ama HİÇBİR ZAMAN
        /// gösterilmez: görünür arayüz yeni kabuktur, motor yalnızca ona
        /// hizmet eder.
        /// </summary>
        private void StartUi()
        {
            Legacy.LegacyEngineWindow.Headless = true;
            var engine = new Legacy.LegacyEngineWindow();
            engine.InitializeHeadless();

            var shell = new MainWindow(engine);
            MainWindow = shell;

            // WPF bir Window'u Application.Windows'a Show() ile değil YAPICISINDA
            // ekler. Motor penceresi hiç gösterilmese de listede kalır, bu yüzden
            // varsayılan OnLastWindowClose modunda sayaç asla sıfıra inmez:
            // kullanıcı kabuğu kapatır, süreç arka planda yaşamaya devam eder ve
            // tek-örnek mutex'i tuttuğu için uygulama bir daha açılmaz.
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            shell.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                string.Format(GetLocalizedText("messages", "unexpected_error_message", "Beklenmeyen bir hata oluştu:\n{0}"), e.Exception.Message),
                GetLocalizedText("messages", "unexpected_error_title", "Hata"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                string.Format(GetLocalizedText("messages", "critical_error_message", "Kritik bir hata oluştu:\n{0}"), e.ExceptionObject),
                GetLocalizedText("messages", "critical_error_title", "Kritik Hata"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private bool CheckSingleInstance()
        {
            try
            {
                _mutex = new Mutex(true, MutexName, out bool createdNew);
                // Sahiplik yalnızca mutex'i BİZ oluşturduysak bizdedir. İkinci
                // örnekte de nesne atanır ama sahibi değiliz; bunu bilmeden
                // ReleaseMutex çağırmak ApplicationException fırlatır.
                _mutexOwned = createdNew;
                return createdNew;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Tek instance kontrolü sırasında hata: {ex.Message}");
                return false;
            }
        }

        private bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private void LoadLanguage()
        {
            try
            {
                // Try to load language from registry first
                string language = "TR"; // Default language
                
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\RevivalDPI"))
                {
                    if (key != null)
                    {
                        var regLanguage = key.GetValue("Language") as string;
                        Debug.WriteLine($"Registry'den dil okundu: {regLanguage}");
                        if (!string.IsNullOrEmpty(regLanguage) && (regLanguage == "TR" || regLanguage == "EN" || regLanguage == "RU"))
                        {
                            language = regLanguage;
                            Debug.WriteLine($"Dil ayarlandı: {language}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Registry anahtarı bulunamadı, varsayılan dil kullanılıyor");
                    }
                }
                
                // Load the language file
                Debug.WriteLine($"LanguageManager.LoadLanguage çağrılıyor: {language}");
                bool loadResult = LanguageManager.LoadLanguage(language);
                Debug.WriteLine($"Dil yükleme sonucu: {loadResult}");
            }
            catch (Exception ex)
            {
                // If language loading fails, continue with default (TR)
                Debug.WriteLine($"Dil yüklenirken hata: {ex.Message}");
            }
        }

        private string GetLocalizedText(string category, string key, string fallbackText)
        {
            try
            {
                var text = LanguageManager.GetText(category, key);
                Debug.WriteLine($"GetLocalizedText: {category}.{key} -> {text}");
                // If the text is the same as the key, it means the translation wasn't found
                if (text == $"{category}.{key}")
                {
                    Debug.WriteLine($"Çeviri bulunamadı, fallback kullanılıyor: {fallbackText}");
                    return fallbackText;
                }
                return text;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetLocalizedText hatası: {ex.Message}");
                return fallbackText;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Mutex'i temizle. ReleaseMutex YALNIZCA sahibiysek çağrılabilir:
            // aksi hâlde "Object synchronization method was called from an
            // unsynchronized block of code" fırlar ve ikinci örnek, uyarı
            // penceresine tamam denince çökerdi.
            if (_mutex != null)
            {
                try
                {
                    if (_mutexOwned) _mutex.ReleaseMutex();
                }
                catch (ApplicationException ex)
                {
                    Debug.WriteLine($"Mutex serbest bırakılamadı: {ex.Message}");
                }
                _mutex.Dispose();
                _mutex = null;
            }
            
            base.OnExit(e);
        }
    }
} 