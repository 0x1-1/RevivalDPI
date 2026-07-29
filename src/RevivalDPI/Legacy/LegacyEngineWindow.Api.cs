using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RevivalDPI.Engine;

namespace RevivalDPI.Legacy
{
    /// <summary>
    /// <see cref="LegacyEngineWindow"/> için arayüz cephesi.
    ///
    /// Bu dosya, motor kodunun bulunduğu <c>LegacyEngineWindow.xaml.cs</c> ile
    /// AYNI partial sınıftır. Bu sayede oradaki <c>private</c> üyelere ve
    /// <c>x:Name</c> alanlarına, o 15 bin satırlık dosyaya dokunmadan
    /// erişilebilir. Yeni arayüz motora yalnızca buradan konuşur.
    ///
    /// Kural: buraya iş mantığı YAZILMAZ. Bu dosyanın işi, mevcut işleyicileri
    /// çağırmak ve durumu arayüzün anlayacağı biçimde döndürmektir.
    /// </summary>
    public partial class LegacyEngineWindow : IOperationsHost
    {
        /// <summary>
        /// Pencerenin gösterilmeden, yalnızca motor olarak çalıştığını belirtir.
        /// <c>new LegacyEngineWindow()</c> çağrılmadan ÖNCE ayarlanmalıdır:
        /// yapıcı içindeki bazı yollar buna bakar.
        /// </summary>
        internal static bool Headless;

        private int _busyDepth;

        public bool IsBusy => _busyDepth > 0;

        public event EventHandler<bool> BusyChanged;
        public event EventHandler StatusChanged;

        /// <summary>
        /// <c>ShowLoading</c> headless modda buraya düşer; eski kodun tek
        /// ilerleme göstergesi bu çağrıdır.
        ///
        /// Sayaçla tutulur çünkü eski kodda <c>ShowLoading</c> çağrıları İÇ İÇE
        /// geçer: örneğin WinDivert kaldırma önce GoodbyeDPI'yi kaldırır ve o
        /// alt işlemin <c>ShowLoading(false)</c>'ı, dış işlem hâlâ sürerken
        /// "bitti" sinyali verirdi. Sonuç: ilerleme paneli erken kapanır ve
        /// yıkıcı butonlar işlem sürerken yeniden etkinleşirdi.
        /// </summary>
        private void RaiseBusyChanged(bool busy)
        {
            var wasBusy = _busyDepth > 0;
            _busyDepth = busy ? _busyDepth + 1 : Math.Max(0, _busyDepth - 1);
            var isBusy = _busyDepth > 0;

            if (wasBusy == isBusy) return;
            BusyChanged?.Invoke(this, isBusy);

            // İşlem bittiğinde durumlar değişmiş olabilir.
            if (!isBusy) StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Gösterilmeyen pencerede <c>Loaded</c> olayı hiç tetiklenmez; oradaki
        /// başlangıç işlerini burada elle çalıştırıyoruz.
        /// </summary>
        internal void InitializeHeadless()
        {
            try
            {
                MainWindow_Loaded(this, new RoutedEventArgs());
                RestorePreferences();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeHeadless: {ex.Message}");
            }
        }

        // =====================================================================
        // Durum
        // =====================================================================

        public async Task<StatusSnapshot> RefreshStatusAsync(bool force = false)
        {
            if (force) await ForceRefreshAllServicesAsync();
            else await CheckAllServicesAsync();

            // Cache'i yazan her yol lock alıyor (Clear + 8 ekleme). Kilitsiz
            // okumak, açılışta motorun kendi taraması ile çakışıp ya hepsini
            // "Kontrol ediliyor" bırakır ya da sözlüğü bozar.
            Dictionary<string, bool> cached;
            lock (_serviceStatusCache)
            {
                cached = new Dictionary<string, bool>(_serviceStatusCache);
            }

            var services = new Dictionary<string, ComponentState>();
            foreach (var id in ServiceIds.All)
            {
                services[id] = cached.TryGetValue(id, out var installed)
                    ? (installed ? ComponentState.Installed : ComponentState.NotInstalled)
                    : ComponentState.Checking;
            }

            var snapshot = new StatusSnapshot
            {
                Services = services,
                Discord = FileState(DiscordUpdateExePath()),
                DiscordPtb = FileState(DiscordPtbUpdateExePath()),
                WebCord = FileState(WebCordExePath()),
                Drover = DroverState(),
                KasperskyDetected = _isKasperskyDetected || _isKasperskyVpnDetected,
                CloudflareWarpDetected = _isCloudflareWarpDetected,
            };

            snapshot.RoutingMethod =
                services[ServiceIds.WireSock] == ComponentState.Installed ? "WireSock" : null;

            if (services[ServiceIds.ByeDpi] == ComponentState.Installed)
            {
                // ProxiFyre da varsa split tunneling, yoksa DLL kurulumu.
                snapshot.DpiMethod = services[ServiceIds.ProxiFyre] == ComponentState.Installed
                    ? "ByeDPI Split Tunneling"
                    : "ByeDPI DLL";
            }
            else if (services[ServiceIds.Zapret] == ComponentState.Installed) snapshot.DpiMethod = "Zapret";
            else if (services[ServiceIds.GoodbyeDpi] == ComponentState.Installed) snapshot.DpiMethod = "GoodbyeDPI";

            return snapshot;
        }

        private static ComponentState FileState(string path) =>
            File.Exists(path) ? ComponentState.Installed : ComponentState.NotInstalled;

        private static string LocalAppData(params string[] parts)
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }

        private static string DiscordUpdateExePath() => LocalAppData("Discord", "Update.exe");
        private static string DiscordPtbUpdateExePath() => LocalAppData("DiscordPTB", "Update.exe");
        private static string WebCordExePath() => LocalAppData("RevivalDPI", "WebCord", "webcord.exe");

        /// <summary>
        /// Drover bir Windows servisi değil; Discord klasörüne bırakılan
        /// <c>version.dll</c> dosyasıyla anlaşılır.
        /// </summary>
        private static ComponentState DroverState()
        {
            try
            {
                var discordRoot = LocalAppData("Discord");
                if (!Directory.Exists(discordRoot)) return ComponentState.NotInstalled;

                foreach (var dir in Directory.GetDirectories(discordRoot, "app-*"))
                {
                    if (File.Exists(Path.Combine(dir, "version.dll"))) return ComponentState.Installed;
                }
            }
            catch
            {
                return ComponentState.Error;
            }
            return ComponentState.NotInstalled;
        }

        // =====================================================================
        // Ayarlar
        //
        // Kalıcılık eski CheckBox'ların Checked/Unchecked işleyicilerinde;
        // IsChecked atamak o işleyicileri tetikler ve ayar registry'ye yazılır.
        // Bu yüzden burada ayrıca kayıt yapılmıyor.
        // =====================================================================

        private static bool Get(ToggleButton box) => box?.IsChecked == true;

        private static void Set(ToggleButton box, bool value)
        {
            if (box != null) box.IsChecked = value;
        }

        /// <summary>
        /// Kendi Checked/Unchecked işleyicisi OLMAYAN toggle'lar için kalıcılık.
        ///
        /// chkAutoDNSChange, chkByeDPIBrowserTunneling ve chkGoodbyeDPIUseBlacklist
        /// kendilerini registry'ye yazar; aşağıdaki dördünün ise hiçbir işleyicisi
        /// yok. Bunlar yalnızca kurulum sırasında okunuyordu, yani kullanıcı
        /// açıp uygulamayı kapattığında ayar sessizce kayboluyordu.
        /// </summary>
        private static void SavePref(string name, bool value)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(REG_KEY_PATH))
                {
                    key?.SetValue(name, value ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ayar yazılamadı ({name}): {ex.Message}");
            }
        }

        private static bool LoadPref(string name, bool fallback)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REG_KEY_PATH))
                {
                    if (key?.GetValue(name) is int stored) return stored != 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ayar okunamadı ({name}): {ex.Message}");
            }
            return fallback;
        }

        private const string PrefBrowserTunneling = "BrowserTunneling";
        private const string PrefWireSockClient = "InstallWireSockClient";
        private const string PrefPtbCleanInstall = "DiscordPtbCleanInstall";
        private const string PrefWebCordShortcut = "WebCordDesktopShortcut";

        /// <summary>Kalıcı tercihleri motorun kontrollerine geri yükler.</summary>
        private void RestorePreferences()
        {
            Set(chkBrowserTunneling, LoadPref(PrefBrowserTunneling, false));
            Set(chkWireSockRepeater, LoadPref(PrefWireSockClient, false));
            Set(chkDiscordUninstallStandard, LoadPref(PrefPtbCleanInstall, false));
            Set(chkWebCordShortcut, LoadPref(PrefWebCordShortcut, true));
        }

        public bool BrowserTunneling
        {
            get => Get(chkBrowserTunneling);
            set { Set(chkBrowserTunneling, value); SavePref(PrefBrowserTunneling, value); }
        }

        public bool InstallWireSockClient
        {
            get => Get(chkWireSockRepeater);
            set { Set(chkWireSockRepeater, value); SavePref(PrefWireSockClient, value); }
        }

        public bool AutoDnsChange
        {
            get => Get(chkAutoDNSChange);
            set => Set(chkAutoDNSChange, value);
        }

        public bool ByeDpiBrowserTunneling
        {
            get => Get(chkByeDPIBrowserTunneling);
            set => Set(chkByeDPIBrowserTunneling, value);
        }

        public bool DiscordPtbCleanInstall
        {
            get => Get(chkDiscordUninstallStandard);
            set { Set(chkDiscordUninstallStandard, value); SavePref(PrefPtbCleanInstall, value); }
        }

        public bool WebCordDesktopShortcut
        {
            get => Get(chkWebCordShortcut);
            set { Set(chkWebCordShortcut, value); SavePref(PrefWebCordShortcut, value); }
        }

        public bool UseBlacklist
        {
            get => Get(chkGoodbyeDPIUseBlacklist);
            set => Set(chkGoodbyeDPIUseBlacklist, value);
        }

        // =====================================================================
        // Genel Bakış / yönlendirme
        // =====================================================================

        public IReadOnlyList<string> CustomFolders => _folders;

        public void AddCustomFolder() => BtnAddFolder_Click(this, new RoutedEventArgs());
        public void ClearCustomFolders() => BtnClearFolders_Click(this, new RoutedEventArgs());

        public void RunStandardRouteSetup() => BtnStart_Click(this, new RoutedEventArgs());
        public void RunAlternativeRouteSetup() => BtnAlternativeSetup_Click(this, new RoutedEventArgs());
        public void RunCustomRouteSetup() => BtnCustomSetup_Click(this, new RoutedEventArgs());
        // Açık arayüz implementasyonu: motorda aynı adlı bir private metot var.
        void IOperationsHost.GenerateConfigOnly() => BtnGenerateConfig_Click(this, new RoutedEventArgs());

        public string CurrentLanguage => _currentLanguage;

        public void ChangeLanguageAndRestart(string code)
        {
            // SetLanguage kalıcılığı ve LanguageManager yüklemesini yapar;
            // metinler açılışta okunduğu için yeniden başlatma gerekir.
            SetLanguage(code);
            RestartApplication();
        }

        // =====================================================================
        // DPI Yönlendirme
        // =====================================================================

        public void RunByeDpiSplitSetup() => BtnByeDPISetup_Click(this, new RoutedEventArgs());
        public void RunByeDpiDllSetup() => BtnByeDPIDLLSetup_Click(this, new RoutedEventArgs());

        // =====================================================================
        // Paket Kuralları (Zapret)
        // =====================================================================

        public void PrepareResources()
        {
            // Bu iki metot async void: dosya kopyalama arka planda sürer ve
            // bitiminde preset'leri kendisi yeniden yükler.
            try { CheckAndCopyZapretFilesIfNeeded(); }
            catch (Exception ex) { Debug.WriteLine($"PrepareResources/Zapret: {ex.Message}"); }

            try { CheckAndCopyGoodbyeDPIFilesIfNeeded(); }
            catch (Exception ex) { Debug.WriteLine($"PrepareResources/GoodbyeDPI: {ex.Message}"); }
        }

        public IReadOnlyList<string> ZapretPresetNames
        {
            get
            {
                // Zapret dosyaları henüz kopyalanmadıysa eski kod ComboBox'a
                // bir yer tutucu metin ekler ama _zapretPresets boş kalır.
                // Yapısal sinyal budur, Türkçe yer tutucu metnine BAKMIYORUZ,
                // yoksa dil değişince kırılırdı.
                if (_zapretPresets.Count == 0) return Array.Empty<string>();

                return cmbZapretPresets.Items.Cast<object>()
                    .Take(_zapretPresets.Count)
                    .Select(i => i?.ToString() ?? string.Empty)
                    .ToList();
            }
        }

        public string ZapretParameters
        {
            get => txtZapretParams?.Text ?? string.Empty;
            set { if (txtZapretParams != null) txtZapretParams.Text = value; }
        }

        public void SelectZapretPreset(int index)
        {
            if (cmbZapretPresets == null) return;
            if (index < 0 || index >= cmbZapretPresets.Items.Count) return;
            // SelectionChanged işleyicisi parametreleri txtZapretParams'a kopyalar.
            cmbZapretPresets.SelectedIndex = index;
        }

        public int ZapretScanLevel
        {
            get => cmbScanLevel?.SelectedIndex ?? 0;
            set { if (cmbScanLevel != null) cmbScanLevel.SelectedIndex = value; }
        }

        public void RunZapretAutoInstall() => BtnZapretAutoInstall_Click(this, new RoutedEventArgs());

        public void RunZapret(RunMode mode)
        {
            if (mode == RunMode.Service) BtnZapretCustomService_Click(this, new RoutedEventArgs());
            else BtnZapretCustomBatch_Click(this, new RoutedEventArgs());
        }

        // =====================================================================
        // DPI Classic (GoodbyeDPI)
        // =====================================================================

        public IReadOnlyList<string> GoodbyeDpiPresetNames =>
            cmbGoodbyeDPIPresets.Items.Cast<object>()
                .Select(i => (i as ComboBoxItem)?.Content?.ToString() ?? i?.ToString() ?? string.Empty)
                .ToList();

        public string GoodbyeDpiParameters
        {
            get => txtGoodbyeDPIParams?.Text ?? string.Empty;
            set { if (txtGoodbyeDPIParams != null) txtGoodbyeDPIParams.Text = value; }
        }

        public void SelectGoodbyeDpiPreset(int index)
        {
            if (cmbGoodbyeDPIPresets == null) return;
            if (index < 0 || index >= cmbGoodbyeDPIPresets.Items.Count) return;
            cmbGoodbyeDPIPresets.SelectedIndex = index;
        }

        public string BlacklistText
        {
            get
            {
                // Editör alanı boşsa diskten okumayı tetikle.
                if (string.IsNullOrEmpty(txtGoodbyeDPIBlacklist?.Text)) LoadGoodbyeDPIBlacklist();
                return txtGoodbyeDPIBlacklist?.Text ?? string.Empty;
            }
            set { if (txtGoodbyeDPIBlacklist != null) txtGoodbyeDPIBlacklist.Text = value; }
        }

        public void SaveBlacklist() => BtnGoodbyeDPISaveBlacklist_Click(this, new RoutedEventArgs());

        public void RunGoodbyeDpi(RunMode mode)
        {
            if (mode == RunMode.Service) BtnGoodbyeDPIService_Click(this, new RoutedEventArgs());
            else BtnGoodbyeDPIBatch_Click(this, new RoutedEventArgs());
        }

        // =====================================================================
        // Kurtarma
        // =====================================================================

        public void RepairDiscord() => BtnDiscordRepair_Click(this, new RoutedEventArgs());
        public void StartDiscord() => BtnDiscordStart_Click(this, new RoutedEventArgs());
        public void RemoveDiscord() => BtnDiscordRemove_Click(this, new RoutedEventArgs());
        public void StartWebCord() => BtnWebCordAction_Click(this, new RoutedEventArgs());
        public void InstallDiscordPtb() => BtnDiscordPTBInstall_Click(this, new RoutedEventArgs());
        public void RemoveDiscordPtb() => BtnDiscordPTBRemove_Click(this, new RoutedEventArgs());
        public void StartDiscordPtb() => BtnDiscordPTBStart_Click(this, new RoutedEventArgs());
        public void InstallWebCord() => BtnWebCordInstall_Click(this, new RoutedEventArgs());
        public void RemoveWebCord() => BtnWebCordRemove_Click(this, new RoutedEventArgs());

        // =====================================================================
        // Servisler
        // =====================================================================

        // Açık arayüz implementasyonu: motorda zaten aynı imzalı bir
        // `private async Task RemoveService(string)` var. Açık implementasyon
        // farklı bir ad alanında yaşadığı için ikisi çakışmaz ve gövde
        // içinden motorunkine erişilebilir.
        async void IOperationsHost.RemoveService(string serviceId)
        {
            try
            {
                // WinDivert'i GoodbyeDPI ve zapret'ten önce kaldırmak sürücüyü
                // kullanımdayken silmeye çalışmak demektir; eski işleyici bu
                // sırayı zaten yönetiyor.
                if (serviceId == ServiceIds.WinDivert)
                {
                    BtnWinDivertRemove_Click(this, new RoutedEventArgs());
                    return;
                }

                await RemoveService(serviceName: serviceId);

                // Zapret ve WinWS servisleri, servis silinse bile ayakta kalan
                // winws.exe süreçleri bırakır. Toplu kaldırma akışı bunu zaten
                // yapıyor; satır bazında kaldırmada yapılmazsa kullanıcı
                // "kaldırdım" sanırken trafik filtrelenmeye devam eder.
                if (serviceId == ServiceIds.Zapret ||
                    serviceId == ServiceIds.WinWs1 ||
                    serviceId == ServiceIds.WinWs2)
                {
                    await StopWinwsProcesses(GetZapretLogPath());
                }

                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RemoveService({serviceId}): {ex.Message}");
            }
        }

        public void RemoveDrover() => BtnDroverRemove_Click(this, new RoutedEventArgs());
        public void RemoveAllServices() => BtnRemoveAllServices_Click(this, new RoutedEventArgs());
        public void ResetDnsAndDoh() => BtnResetDNSDoH_Click(this, new RoutedEventArgs());
        public void UninstallApplication() => BtnUninstallRevivalDPI_Click(this, new RoutedEventArgs());

        // =====================================================================
        // Genel
        // =====================================================================

        /// <summary>
        /// Arayüzde gösterilen sürüm. AssemblyVersion dört parçalıdır
        /// ("1.5.5.0"); kullanıcıya ve release etiketlerine karşılık gelen
        /// biçim üç parçalı olduğu için sondaki gereksiz ".0" atılır.
        /// </summary>
        public string ProductVersion
        {
            get
            {
                var raw = VersionHelper.GetAssemblyVersion();
                var parts = raw.Split('.');
                return parts.Length >= 3 ? string.Join(".", parts.Take(3)) : raw;
            }
        }

        public void OpenLogsFolder()
        {
            try
            {
                Process.Start("explorer.exe", GetAppDataLogsDirectory());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OpenLogsFolder: {ex.Message}");
            }
        }

        public void OpenRepository()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/0x1-1/RevivalDPI",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OpenRepository: {ex.Message}");
            }
        }
    }
}
