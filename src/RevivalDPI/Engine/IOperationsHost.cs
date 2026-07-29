using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RevivalDPI.Engine
{
    /// <summary>Bir servisin veya bileşenin arayüzde gösterilen durumu.</summary>
    public enum ComponentState
    {
        /// <summary>Sorgu henüz tamamlanmadı. Açılışta varsayılan durum budur:
        /// asıl sonuç gelmeden "Kurulu değil" göstermek yanlış bilgi olur.</summary>
        Checking,
        Installed,
        NotInstalled,
        Error
    }

    /// <summary>Servis durumlarının tek seferde alınmış görüntüsü.</summary>
    public sealed class StatusSnapshot
    {
        /// <summary>Servis adı -> durum. Anahtarlar <see cref="ServiceIds"/> içindedir.</summary>
        public IReadOnlyDictionary<string, ComponentState> Services { get; set; }

        public ComponentState Discord { get; set; }
        public ComponentState DiscordPtb { get; set; }
        public ComponentState WebCord { get; set; }
        public ComponentState Drover { get; set; }

        /// <summary>Kurulu yönlendirme yöntemi; yoksa <c>null</c>.</summary>
        public string RoutingMethod { get; set; }

        /// <summary>Kurulu DPI yöntemi; yoksa <c>null</c>.</summary>
        public string DpiMethod { get; set; }

        public bool KasperskyDetected { get; set; }
        public bool CloudflareWarpDetected { get; set; }
    }

    /// <summary>Motorun tanıdığı servis adları (Windows servis adlarıyla birebir).</summary>
    public static class ServiceIds
    {
        public const string WireSock = "wiresock-client-service";
        public const string ByeDpi = "ByeDPI";
        public const string ProxiFyre = "ProxiFyreService";
        public const string WinWs1 = "winws1";
        public const string WinWs2 = "winws2";
        public const string Zapret = "zapret";
        public const string GoodbyeDpi = "GoodbyeDPI";
        public const string WinDivert = "WinDivert";

        /// <summary>Servisler ekranındaki gösterim sırası.</summary>
        public static readonly string[] All =
        {
            WireSock, ByeDpi, ProxiFyre, WinWs1, WinWs2, Zapret, GoodbyeDpi, WinDivert
        };

        /// <summary>Servis adı -> arayüzde gösterilen ad.</summary>
        public static string DisplayName(string id)
        {
            switch (id)
            {
                case WireSock: return "WireSock";
                case ByeDpi: return "ByeDPI";
                case ProxiFyre: return "ProxiFyre";
                case WinWs1: return "WinWS 1";
                case WinWs2: return "WinWS 2";
                case Zapret: return "Zapret";
                case GoodbyeDpi: return "GoodbyeDPI";
                case WinDivert: return "WinDivert";
                default: return id;
            }
        }
    }

    /// <summary>Zapret / GoodbyeDPI çalışma biçimi.</summary>
    public enum RunMode
    {
        /// <summary>Kalıcı kurulur, Windows ile başlar.</summary>
        Service,
        /// <summary>Yalnızca bu oturumda çalışır.</summary>
        Once
    }

    /// <summary>
    /// Arayüzün iş mantığına eriştiği tek nokta.
    ///
    /// Uygulayan taraf <c>LegacyEngineWindow</c>'dur: kurulum ve servis
    /// işlemlerinin tamamı orada durur ve durumunu kendi (gösterilmeyen)
    /// kontrollerinde tutar. Arayüz bu arayüzün ötesindeki hiçbir şeye
    /// dokunmamalıdır.
    ///
    /// İşlemler <c>async void</c> tabanlı eski olay işleyicilerine dayandığı
    /// için <c>Task</c> döndürmezler; ilerleme <see cref="BusyChanged"/> ile,
    /// sonuç <see cref="StatusChanged"/> ile bildirilir.
    /// </summary>
    public interface IOperationsHost
    {
        // ---- Durum ---------------------------------------------------------
        bool IsBusy { get; }

        /// <summary>Uzun süren bir işlem başladığında/bittiğinde tetiklenir.</summary>
        event EventHandler<bool> BusyChanged;

        /// <summary>Servis veya bileşen durumları değiştiğinde tetiklenir.</summary>
        event EventHandler StatusChanged;

        Task<StatusSnapshot> RefreshStatusAsync(bool force = false);

        // ---- Ayarlar (kalıcı) ----------------------------------------------
        bool BrowserTunneling { get; set; }
        bool InstallWireSockClient { get; set; }
        bool AutoDnsChange { get; set; }
        bool ByeDpiBrowserTunneling { get; set; }
        bool DiscordPtbCleanInstall { get; set; }
        bool WebCordDesktopShortcut { get; set; }
        bool UseBlacklist { get; set; }

        // ---- Genel Bakış / yönlendirme -------------------------------------
        IReadOnlyList<string> CustomFolders { get; }
        void AddCustomFolder();
        void ClearCustomFolders();
        void RunStandardRouteSetup();
        void RunAlternativeRouteSetup();

        // ---- DPI Yönlendirme -----------------------------------------------
        void RunByeDpiSplitSetup();
        void RunByeDpiDllSetup();

        // ---- Paket Kuralları (Zapret) --------------------------------------

        /// <summary>
        /// Zapret / GoodbyeDPI çalışma dosyalarını %LOCALAPPDATA% altına
        /// kopyalar ve profilleri yeniden okur.
        ///
        /// Eski arayüz bunu sekme değişiminde yapıyordu; yeni arayüzde ilgili
        /// sayfaya ilk gidildiğinde çağrılmalıdır. Çağrılmazsa profil listesi
        /// boş kalır.
        /// </summary>
        void PrepareResources();

        IReadOnlyList<string> ZapretPresetNames { get; }
        /// <summary>Seçili profilin parametreleri; kullanıcı düzenlerse set edilir.</summary>
        string ZapretParameters { get; set; }
        void SelectZapretPreset(int index);
        /// <summary>0 = hızlı, 1 = standart, 2 = ayrıntılı.</summary>
        int ZapretScanLevel { get; set; }
        void RunZapretAutoInstall();
        void RunZapret(RunMode mode);

        // ---- DPI Classic (GoodbyeDPI) --------------------------------------
        IReadOnlyList<string> GoodbyeDpiPresetNames { get; }
        string GoodbyeDpiParameters { get; set; }
        void SelectGoodbyeDpiPreset(int index);
        string BlacklistText { get; set; }
        void SaveBlacklist();
        void RunGoodbyeDpi(RunMode mode);

        // ---- Kurtarma -------------------------------------------------------
        void RepairDiscord();
        void StartDiscord();
        void RemoveDiscord();
        void InstallDiscordPtb();
        void RemoveDiscordPtb();
        void StartDiscordPtb();
        void InstallWebCord();
        void StartWebCord();
        void RemoveWebCord();

        // ---- Servisler ------------------------------------------------------
        void RemoveService(string serviceId);

        /// <summary>
        /// Drover'ı kaldırır. Drover bir Windows servisi değildir; Discord
        /// klasörüne bırakılan dosyalarla çalışır, bu yüzden ayrı bir işlemdir.
        /// </summary>
        void RemoveDrover();

        void RemoveAllServices();
        void ResetDnsAndDoh();
        void UninstallApplication();

        // ---- Genel -----------------------------------------------------------
        string ProductVersion { get; }
        void OpenLogsFolder();
        void OpenRepository();

        /// <summary>Etkin arayüz dili: TR / EN / RU / ES.</summary>
        string CurrentLanguage { get; }

        /// <summary>
        /// Dili değiştirir ve uygulamayı yeniden başlatır.
        /// Metinler açılışta yüklendiği için yeniden başlatma zorunludur.
        /// </summary>
        void ChangeLanguageAndRestart(string code);

        // ---- Genel Bakış (gelişmiş) -----------------------------------------

        /// <summary>Seçilen klasör listesiyle özel yönlendirme kurulumu yapar.</summary>
        void RunCustomRouteSetup();

        /// <summary>Kurulum yapmadan yalnızca yapılandırma dosyasını üretir.</summary>
        void GenerateConfigOnly();
    }
}
