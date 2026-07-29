using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using RevivalDPI.Engine;
using RevivalDPI.Themes;

namespace RevivalDPI
{
    /// <summary>
    /// Uygulama kabuğu: üst bar, sol navigasyon, altı sayfa ve ortak modal
    /// katman. Kabukta iş mantığı yoktur; her işlem
    /// <see cref="IOperationsHost"/> üzerinden motora devredilir.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IOperationsHost _host;
        private readonly DispatcherTimer _toastTimer;

        private StatusSnapshot _status;
        private bool _loaded;

        /// <summary>Modal onaylandığında çalışacak iş; modal kapanınca temizlenir.</summary>
        private Action _modalConfirm;

        /// <summary>Seçili ByeDPI yöntemi. true = Split Tunneling.</summary>
        private bool _dpiIsSplit = true;

        private EventHandler _themeChangedHandler;

        /// <summary>Son başlatılan yenilemenin sırası; geç dönen eskiler atılır.</summary>
        private int _refreshSeq;

        public MainWindow(IOperationsHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            InitializeComponent();

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.8) };
            _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };

            _host.BusyChanged += OnBusyChanged;
            _host.StatusChanged += OnStatusChanged;

            Loaded += OnLoaded;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        // =====================================================================
        // Yaşam döngüsü
        // =====================================================================

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_loaded) return;
            _loaded = true;

            // Başlık çubuğunun koyu görünümü ancak HWND oluştuktan sonra ayarlanır.
            ThemeManager.ApplyTitleBar(this);

            // ThemeChanged STATİK bir olay: abonelik bırakılmazsa lambda 'this'i
            // yakalar ve pencere kapandıktan sonra da kapalı bir görsel ağaca
            // karşı çalışmaya devam eder.
            _themeChangedHandler = (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                ThemeManager.ApplyTitleBar(this);
                RenderStatus();      // rozet renkleri token'lardan yeniden okunur
                UpdateDpiPage();     // seçim kartlarının çerçevesi kodda atanıyor
                MarkThemeOption();
            }));
            ThemeManager.ThemeChanged += _themeChangedHandler;

            Closed += OnClosed;

            var logo = LoadBrandLogo();
            if (logo != null)
            {
                ImgHeaderLogo.Source = logo;
                ImgAboutLogo.Source = logo;
            }

            TxtVersion.Text = "v" + _host.ProductVersion;
            MarkThemeOption();
            MarkLanguageOption();

            LoadSettingsIntoUi();
            UpdateFolderCount();

            // Zapret/GoodbyeDPI çalışma dosyaları %LOCALAPPDATA% altına
            // kopyalanmadan profil listeleri boş gelir.
            _host.PrepareResources();
            LoadPresets();

            UpdateDpiPage();
            UpdateZapretPage();
            UpdateClassicPage();
            BuildServiceRows(placeholderOnly: true);
            RenderStatus();

            // Servis sorguları ağır; UI thread'i bloklamadan çalışır ve bitene
            // kadar her şey "Kontrol ediliyor…" gösterir.
            await RefreshAsync();
        }

        /// <summary>
        /// Marka logosunu yükler.
        ///
        /// XAML'de <c>pack://</c> URI'si KULLANILMIYOR: bu projede
        /// <c>Resources\**</c> aynı dosyaları ayrıca <c>Content</c> olarak da
        /// işaretleyip <c>res\</c> altına kopyalıyor. WPF bu durumda önce
        /// exe'nin yanındaki <c>Resources\</c> klasörüne bakıp bulamıyor ve
        /// görüntüyü sessizce boş bırakıyor, Image yüksekliği verildiği için
        /// yer kaplıyor ama hiçbir şey çizilmiyor.
        ///
        /// Bu yüzden akış doğrudan okunuyor: önce gömülü kaynak, olmazsa
        /// diskteki kopya.
        /// </summary>
        private static ImageSource LoadBrandLogo()
        {
            try
            {
                var info = Application.GetResourceStream(
                    new Uri("Resources/revivaldpi-logo.png", UriKind.Relative));
                if (info?.Stream != null)
                {
                    using (var stream = info.Stream) return DecodeImage(stream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Logo (gömülü kaynak): " + ex.Message);
            }

            try
            {
                var path = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "res", "revivaldpi-logo.png");
                if (File.Exists(path))
                {
                    using (var stream = File.OpenRead(path)) return DecodeImage(stream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Logo (disk): " + ex.Message);
            }

            // Logo bulunamazsa metin başlık tek başına anlaşılır kalır.
            return null;
        }

        private static ImageSource DecodeImage(Stream stream)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            // OnLoad: akış kapandıktan sonra da geçerli kalsın.
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            // Motor süreç boyunca yaşıyor; abonelikler bırakılmazsa kapalı
            // pencereye olay gelmeye devam eder.
            if (_themeChangedHandler != null) ThemeManager.ThemeChanged -= _themeChangedHandler;
            _host.BusyChanged -= OnBusyChanged;
            _host.StatusChanged -= OnStatusChanged;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;

            if (ModalLayer.Visibility == Visibility.Visible) { CloseModal(); e.Handled = true; }
            else if (PopupSettings.IsOpen) { PopupSettings.IsOpen = false; e.Handled = true; }
        }

        private void OnBusyChanged(object sender, bool busy)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PanelBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
                SetActionsEnabled(!busy);
            }));
        }

        private void OnStatusChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(async () => await RefreshAsync()));
        }

        /// <summary>
        /// İşlem sürerken yalnızca işlem başlatan kontroller kilitlenir;
        /// navigasyon ve okuma açık kalır.
        /// </summary>
        private void SetActionsEnabled(bool enabled)
        {
            foreach (var b in new[]
            {
                BtnStdRoute, BtnAltRoute, BtnRunDpi, BtnRunZapret, BtnRunClassic,
                BtnZapretAuto, BtnDiscordRepair, BtnDiscordStart, BtnDiscordRemove,
                BtnPtbInstall, BtnPtbRemove, BtnPtbStart,
                BtnWebCordInstall, BtnWebCordStart, BtnWebCordRemove,
                BtnRemoveAll, BtnRevertDns, BtnUninstall, BtnDroverRemove,
                BtnCustomSetup, BtnGenerateConfig,
            })
            {
                if (b != null) b.IsEnabled = enabled;
            }

            // Bu ikisinin etkinligi normalde secili klasor sayisina bagli.
            // Islem bittiginde o kurali geri veriyoruz, aksi halde klasor
            // secilmemisken de tiklanabilir kalirlardi.
            if (enabled) UpdateFolderCount();
        }

        // =====================================================================
        // Navigasyon
        // =====================================================================

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            var tag = (sender as FrameworkElement)?.Tag?.ToString();

            // Sayfalar yeniden oluşturulmaz, yalnızca görünürlükleri değişir:
            // durum ve girdiler sekme değiştirince kaybolmaz.
            PageOverview.Visibility = tag == "0" ? Visibility.Visible : Visibility.Collapsed;
            PageDpi.Visibility = tag == "1" ? Visibility.Visible : Visibility.Collapsed;
            PagePackets.Visibility = tag == "2" ? Visibility.Visible : Visibility.Collapsed;
            PageClassic.Visibility = tag == "3" ? Visibility.Visible : Visibility.Collapsed;
            PageRecovery.Visibility = tag == "4" ? Visibility.Visible : Visibility.Collapsed;
            PageServices.Visibility = tag == "5" ? Visibility.Visible : Visibility.Collapsed;

            // Dosya kopyalama arka planda sürdüğü için profiller açılışta
            // henüz hazır olmayabilir; ilgili sayfaya gelindiğinde bir kez
            // daha denenir.
            if (tag == "2" || tag == "3") ReloadPresetsIfEmpty();
        }

        private void ReloadPresetsIfEmpty()
        {
            // YALNIZCA boş olanı yeniden yükle. Eskiden ikisi birden
            // yenileniyordu; GoodbyeDPI listesi hiçbir zaman boş kalmadığı için
            // Zapret dosyaları eksikken her sayfa geçişi kullanıcının seçtiği
            // DPI Classic profilini sessizce "Standart"a döndürüyordu.
            if (CmbZapretPreset.Items.Count == 0)
            {
                FillCombo(CmbZapretPreset, _host.ZapretPresetNames);
                UpdateZapretPage();
            }

            if (CmbClassicPreset.Items.Count == 0)
            {
                FillCombo(CmbClassicPreset, _host.GoodbyeDpiPresetNames);
                UpdateClassicPage();
            }
        }

        // =====================================================================
        // Tema
        // =====================================================================

        private void BtnSettings_Click(object sender, RoutedEventArgs e) =>
            PopupSettings.IsOpen = !PopupSettings.IsOpen;

        private void ThemeOption_Click(object sender, RoutedEventArgs e)
        {
            var tag = (sender as FrameworkElement)?.Tag?.ToString();
            if (Enum.TryParse(tag, out AppTheme mode)) ThemeManager.Apply(mode);
            PopupSettings.IsOpen = false;
            MarkThemeOption();
        }

        /// <summary>Seçili tema seçeneğini ✓ ve SemiBold ile işaretler.</summary>
        private void MarkThemeOption()
        {
            var map = new (Button Button, AppTheme Mode, string Label)[]
            {
                (BtnThemeSystem, AppTheme.System, "Sistem"),
                (BtnThemeLight, AppTheme.Light, "Açık"),
                (BtnThemeDark, AppTheme.Dark, "Koyu"),
            };

            foreach (var (button, mode, label) in map)
            {
                var selected = ThemeManager.Mode == mode;
                button.Content = (selected ? "✓  " : "     ") + label;
                button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        // =====================================================================
        // Durum
        // =====================================================================

        private async System.Threading.Tasks.Task RefreshAsync()
        {
            // Yenilemeler üst üste binebilir (her işlem bitişi bir tane tetikler)
            // ve ~1 sn süren 8 `sc query` farklı sırada tamamlanabilir. Sıra
            // numarası olmadan geç dönen ESKİ anlık görüntü yenisini ezer ve
            // kaldırılmış bir servis "Kurulu" görünmeye devam ederdi.
            var seq = ++_refreshSeq;
            try
            {
                var snapshot = await _host.RefreshStatusAsync();
                if (seq != _refreshSeq) return;   // daha yenisi başladı
                _status = snapshot;
                RenderStatus();
            }
            catch (Exception ex)
            {
                if (seq != _refreshSeq) return;
                ShowToast("Durum bilgisi alınamadı: " + ex.Message, success: false);
            }
        }

        private Brush DotBrush(ComponentState state)
        {
            switch (state)
            {
                case ComponentState.Installed: return (Brush)FindResource("Brush.Success");
                case ComponentState.Checking: return (Brush)FindResource("Brush.Warning");
                case ComponentState.Error: return (Brush)FindResource("Brush.DangerStrong");
                // "Kurulu değil" nötr gridir; her satırda kırmızı nokta bağırmaz.
                default: return (Brush)FindResource("Brush.Track");
            }
        }

        private static string StateText(ComponentState state)
        {
            switch (state)
            {
                case ComponentState.Installed: return "Kurulu";
                case ComponentState.Checking: return "Kontrol ediliyor…";
                case ComponentState.Error: return "Hata";
                default: return "Kurulu değil";
            }
        }

        private void FillBadge(Border badge, ComponentState state)
        {
            if (badge == null) return;

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = DotBrush(state),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            var text = new TextBlock
            {
                Text = StateText(state),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = state == ComponentState.Installed
                    ? (Brush)FindResource("Brush.Success")
                    : (Brush)FindResource("Brush.TextSecondary"),
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(dot);
            panel.Children.Add(text);
            badge.Child = panel;
        }

        private void RenderStatus()
        {
            if (!IsInitialized) return;

            var ready = _status != null;
            var services = _status?.Services;

            // ---- Genel Bakış özet satırı ----
            if (!ready)
            {
                TxtOverviewLine.Text = "Durum kontrol ediliyor…";
            }
            else
            {
                var running = services.Count(s => s.Value == ComponentState.Installed);
                var line = $"RevivalDPI hazır · {running} servis etkin";
                if (_host.AutoDnsChange) line += " · DNS ayarları uygulanacak";
                TxtOverviewLine.Text = line;
            }

            // ---- Durum kartı ----
            RenderOverviewStats(ready);

            // ---- DPI kartlarının rozetleri ----
            var dpi = _status?.DpiMethod;
            FillBadge(BadgeSplit, !ready ? ComponentState.Checking
                : dpi == "ByeDPI Split Tunneling" ? ComponentState.Installed : ComponentState.NotInstalled);
            FillBadge(BadgeDll, !ready ? ComponentState.Checking
                : dpi == "ByeDPI DLL" ? ComponentState.Installed : ComponentState.NotInstalled);

            // ---- Kurtarma ----
            FillBadge(BadgeDiscord, ready ? _status.Discord : ComponentState.Checking);
            FillBadge(BadgePtb, ready ? _status.DiscordPtb : ComponentState.Checking);
            FillBadge(BadgeWebCord, ready ? _status.WebCord : ComponentState.Checking);

            if (ready)
            {
                var discordInstalled = _status.Discord == ComponentState.Installed;
                BtnDiscordRepair.Content = discordInstalled ? "Onar" : "Kur";
                // Kurulu değilken başlatma ve kaldırma anlamsız.
                BtnDiscordStart.Visibility = discordInstalled ? Visibility.Visible : Visibility.Collapsed;
                BtnDiscordRemove.Visibility = discordInstalled ? Visibility.Visible : Visibility.Collapsed;

                var ptbInstalled = _status.DiscordPtb == ComponentState.Installed;
                // Duruma göre geçersiz işlemler gösterilmez.
                BtnPtbStart.Visibility = ptbInstalled ? Visibility.Visible : Visibility.Collapsed;
                BtnPtbRemove.Visibility = ptbInstalled ? Visibility.Visible : Visibility.Collapsed;
                BtnPtbInstall.Visibility = ptbInstalled ? Visibility.Collapsed : Visibility.Visible;
                if (BtnPtbStart.Visibility == Visibility.Visible) BtnPtbStart.Margin = new Thickness(0, 0, 8, 0);

                var wcInstalled = _status.WebCord == ComponentState.Installed;
                BtnWebCordInstall.Visibility = wcInstalled ? Visibility.Collapsed : Visibility.Visible;
                BtnWebCordStart.Visibility = wcInstalled ? Visibility.Visible : Visibility.Collapsed;
                BtnWebCordRemove.Visibility = wcInstalled ? Visibility.Visible : Visibility.Collapsed;
            }

            // Drover ayrı satırda: servis değil, dosya tabanlı.
            FillBadge(BadgeDrover, ready ? _status.Drover : ComponentState.Checking);
            BtnDroverRemove.Visibility = ready && _status.Drover == ComponentState.Installed
                ? Visibility.Visible : Visibility.Collapsed;

            RenderConflictWarning(ready);
            BuildServiceRows(placeholderOnly: !ready);
        }

        /// <summary>
        /// Ağ katmanına müdahale eden yazılımlar için uyarı satırı.
        /// Eski arayüz bunun için ekranı kaplayan bir katman kullanıyordu;
        /// bilgi korunuyor ama kullanıcının önünü kesmiyor.
        /// </summary>
        private void RenderConflictWarning(bool ready)
        {
            if (!ready || _status == null)
            {
                PanelConflict.Visibility = Visibility.Collapsed;
                return;
            }

            var names = new List<string>();
            if (_status.KasperskyDetected) names.Add("Kaspersky");
            if (_status.CloudflareWarpDetected) names.Add("Cloudflare WARP");

            if (names.Count == 0)
            {
                PanelConflict.Visibility = Visibility.Collapsed;
                return;
            }

            TxtConflict.Text =
                string.Join(" ve ", names) +
                " tespit edildi. Bu yazılımlar ağ katmanına müdahale ettiği için " +
                "kurulumlar başarısız olabilir veya bağlantınız kesilebilir. " +
                "Sorun yaşarsanız önce bunları kapatın.";
            PanelConflict.Visibility = Visibility.Visible;
        }

        private void RenderOverviewStats(bool ready)
        {
            GridOverviewStats.Children.Clear();

            var services = _status?.Services;
            int running = ready ? services.Count(s => s.Value == ComponentState.Installed) : 0;

            var cells = new (string Label, string Value, ComponentState State)[]
            {
                ("Etkin yönlendirme",
                    ready ? (_status.RoutingMethod ?? "Kurulmamış") : "Kontrol ediliyor…",
                    !ready ? ComponentState.Checking
                        : _status.RoutingMethod != null ? ComponentState.Installed : ComponentState.NotInstalled),

                ("DPI yöntemi",
                    ready ? (_status.DpiMethod ?? "Kurulu değil") : "Kontrol ediliyor…",
                    !ready ? ComponentState.Checking
                        : _status.DpiMethod != null ? ComponentState.Installed : ComponentState.NotInstalled),

                ("Çalışan servis",
                    ready ? running + " servis" : "Kontrol ediliyor…",
                    !ready ? ComponentState.Checking
                        : running > 0 ? ComponentState.Installed : ComponentState.NotInstalled),

                ("DNS / DoH",
                    _host.AutoDnsChange ? "Her kurulumda uygulanacak" : "Kapalı",
                    _host.AutoDnsChange ? ComponentState.Installed : ComponentState.NotInstalled),

                ("WinDivert bileşeni",
                    ready ? StateText(services[ServiceIds.WinDivert]) : "Kontrol ediliyor…",
                    ready ? services[ServiceIds.WinDivert] : ComponentState.Checking),

                ("Drover",
                    ready ? StateText(_status.Drover) : "Kontrol ediliyor…",
                    ready ? _status.Drover : ComponentState.Checking),
            };

            for (int i = 0; i < cells.Length; i++)
            {
                var (label, value, state) = cells[i];

                var stack = new StackPanel { Margin = new Thickness(0, i < 3 ? 0 : 16, 24, 0) };
                stack.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 4),
                    Foreground = (Brush)FindResource("Brush.TextSecondary"),
                });

                // StackPanel çocuklarına sonsuz genişlik verir, bu yüzden
                // TextTrimming devreye girmez ve uzun değerler hücre kenarında
                // kırpılır. Grid ile metin sütunu gerçekten sınırlanır.
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = DotBrush(state),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                Grid.SetColumn(dot, 0);
                row.Children.Add(dot);

                var valueText = new TextBlock
                {
                    Text = value,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = value,
                };
                Grid.SetColumn(valueText, 1);
                row.Children.Add(valueText);

                stack.Children.Add(row);

                Grid.SetColumn(stack, i % 3);
                Grid.SetRow(stack, i / 3);
                GridOverviewStats.Children.Add(stack);
            }
        }

        /// <summary>
        /// Servis tablosunu kurar. Satır yüksekliği sabittir (44), böylece
        /// durumlar yüklenince layout zıplamaz.
        /// </summary>
        private void BuildServiceRows(bool placeholderOnly)
        {
            ListServices.Items.Clear();

            for (int i = 0; i < ServiceIds.All.Length; i++)
            {
                var id = ServiceIds.All[i];
                var state = placeholderOnly || _status?.Services == null
                    ? ComponentState.Checking
                    : _status.Services[id];

                var grid = new Grid { Height = 44, Margin = new Thickness(16, 0, 16, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock
                {
                    Text = ServiceIds.DisplayName(id),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(name, 0);
                grid.Children.Add(name);

                var badge = new Border
                {
                    Style = (Style)FindResource("Badge"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                FillBadge(badge, state);
                Grid.SetColumn(badge, 1);
                grid.Children.Add(badge);

                var info = new TextBlock
                {
                    Text = "-",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("Brush.TextSecondary"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetColumn(info, 2);
                grid.Children.Add(info);

                if (state == ComponentState.Installed)
                {
                    var display = ServiceIds.DisplayName(id);
                    var remove = new Button
                    {
                        Style = (Style)FindResource("Button.DangerSmall"),
                        Content = "Kaldır",
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    remove.Click += (_, _) => ConfirmRemoveService(id, display);
                    Grid.SetColumn(remove, 3);
                    grid.Children.Add(remove);
                }

                var row = new Border
                {
                    Child = grid,
                    BorderBrush = (Brush)FindResource("Brush.Border"),
                    BorderThickness = new Thickness(0, 0, 0, i == ServiceIds.All.Length - 1 ? 0 : 1),
                };
                ListServices.Items.Add(row);
            }
        }

        // =====================================================================
        // Ayarlar <-> arayüz
        // =====================================================================

        private void LoadSettingsIntoUi()
        {
            TglBrowsers.IsChecked = _host.BrowserTunneling;
            TglWsClient.IsChecked = _host.InstallWireSockClient;
            TglDpiBrowsers.IsChecked = _host.ByeDpiBrowserTunneling;
            TglDns.IsChecked = _host.AutoDnsChange;
            TglPtbClean.IsChecked = _host.DiscordPtbCleanInstall;
            TglWcShortcut.IsChecked = _host.WebCordDesktopShortcut;
            TglBlacklist.IsChecked = _host.UseBlacklist;
            BtnEditBlacklist.IsEnabled = _host.UseBlacklist;
        }

        private void LoadPresets()
        {
            FillCombo(CmbZapretPreset, _host.ZapretPresetNames);
            FillCombo(CmbClassicPreset, _host.GoodbyeDpiPresetNames);
        }

        private static void FillCombo(ComboBox combo, System.Collections.Generic.IReadOnlyList<string> items)
        {
            combo.Items.Clear();
            foreach (var name in items) combo.Items.Add(name);
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private void TglBrowsers_Click(object sender, RoutedEventArgs e) =>
            _host.BrowserTunneling = TglBrowsers.IsChecked == true;

        private void TglWsClient_Click(object sender, RoutedEventArgs e) =>
            _host.InstallWireSockClient = TglWsClient.IsChecked == true;

        private void TglDpiBrowsers_Click(object sender, RoutedEventArgs e)
        {
            _host.ByeDpiBrowserTunneling = TglDpiBrowsers.IsChecked == true;
            UpdateDpiPage();
        }

        private void TglDns_Click(object sender, RoutedEventArgs e)
        {
            _host.AutoDnsChange = TglDns.IsChecked == true;
            RenderStatus();
        }

        private void TglPtbClean_Click(object sender, RoutedEventArgs e) =>
            _host.DiscordPtbCleanInstall = TglPtbClean.IsChecked == true;

        private void TglWcShortcut_Click(object sender, RoutedEventArgs e) =>
            _host.WebCordDesktopShortcut = TglWcShortcut.IsChecked == true;

        private void TglBlacklist_Click(object sender, RoutedEventArgs e)
        {
            var on = TglBlacklist.IsChecked == true;
            _host.UseBlacklist = on;
            // Blacklist düzenleme yalnızca blacklist etkinken kullanılabilir.
            BtnEditBlacklist.IsEnabled = on;
            UpdateClassicPage();
        }

        // =====================================================================
        // Genel Bakış
        // =====================================================================

        private void BtnAdvanced_Click(object sender, RoutedEventArgs e)
        {
            var open = PanelAdvanced.Visibility != Visibility.Visible;
            PanelAdvanced.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            BtnAdvanced.Content = (open ? "▾" : "▸") + " Gelişmiş seçenekler";
        }

        private void BtnStdRoute_Click(object sender, RoutedEventArgs e) => _host.RunStandardRouteSetup();
        private void BtnAltRoute_Click(object sender, RoutedEventArgs e) => _host.RunAlternativeRouteSetup();

        private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            _host.AddCustomFolder();
            UpdateFolderCount();
        }

        private void BtnClearFolders_Click(object sender, RoutedEventArgs e)
        {
            _host.ClearCustomFolders();
            UpdateFolderCount();
            ShowToast("Klasör listesi temizlendi");
        }

        private void UpdateFolderCount()
        {
            var count = _host.CustomFolders.Count;
            TxtFolderCount.Text = count == 0 ? "Seçili klasör yok" : $"{count} klasör seçili";
            // Klasör seçilmeden özel kurulumun anlamı yok.
            BtnCustomSetup.IsEnabled = count > 0;
            BtnGenerateConfig.IsEnabled = count > 0;
        }

        private void BtnCustomSetup_Click(object sender, RoutedEventArgs e) => _host.RunCustomRouteSetup();
        private void BtnGenerateConfig_Click(object sender, RoutedEventArgs e) => _host.GenerateConfigOnly();

        private void LanguageOption_Click(object sender, RoutedEventArgs e)
        {
            var code = (sender as FrameworkElement)?.Tag?.ToString();
            if (string.IsNullOrEmpty(code) || code == _host.CurrentLanguage)
            {
                PopupSettings.IsOpen = false;
                return;
            }

            PopupSettings.IsOpen = false;
            ShowConfirm(
                title: "Dil değiştirilsin mi?",
                body: "Arayüz metinleri uygulama açılışında yüklendiği için dil değişikliği RevivalDPI'yi yeniden başlatır. Süren bir işlem varsa önce tamamlanmasını bekleyin.",
                confirmLabel: "Değiştir ve Yeniden Başlat",
                onConfirm: () => _host.ChangeLanguageAndRestart(code));
        }

        /// <summary>Etkin dili ✓ ile işaretler.</summary>
        private void MarkLanguageOption()
        {
            var map = new (Button Button, string Code, string Label)[]
            {
                (BtnLangTR, "TR", "Türkçe"),
                (BtnLangEN, "EN", "English"),
                (BtnLangRU, "RU", "Русский"),
            };

            foreach (var (button, code, label) in map)
            {
                var selected = _host.CurrentLanguage == code;
                button.Content = (selected ? "✓  " : "     ") + label;
                button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        // =====================================================================
        // DPI Yönlendirme
        // =====================================================================

        private void PickSplit_Click(object sender, MouseButtonEventArgs e) { _dpiIsSplit = true; UpdateDpiPage(); }
        private void PickDll_Click(object sender, MouseButtonEventArgs e) { _dpiIsSplit = false; UpdateDpiPage(); }

        private void UpdateDpiPage()
        {
            if (!IsInitialized) return;

            // Seçili kartın çerçevesi accent; parlak yeşil çerçeve yok.
            CardSplit.BorderBrush = _dpiIsSplit
                ? (Brush)FindResource("Brush.AccentStrong")
                : (Brush)FindResource("Brush.Border");
            CardDll.BorderBrush = !_dpiIsSplit
                ? (Brush)FindResource("Brush.AccentStrong")
                : (Brush)FindResource("Brush.Border");

            TxtDpiMethodTitle.Text = _dpiIsSplit
                ? "ByeDPI Split Tunneling: Ayarlar"
                : "ByeDPI DLL: Ayarlar";

            // "Tarayıcıları da tünelle" yalnızca split tunneling'i etkiler.
            RowDpiBrowsers.Visibility = _dpiIsSplit ? Visibility.Visible : Visibility.Collapsed;

            TxtDpiSummary.Text = _dpiIsSplit
                ? "Split tunneling" + (TglDpiBrowsers.IsChecked == true ? " · tarayıcılar dâhil" : "") + " kurulacak"
                : "ByeDPI DLL kurulacak";
        }

        private void BtnRunDpi_Click(object sender, RoutedEventArgs e)
        {
            if (_dpiIsSplit) _host.RunByeDpiSplitSetup();
            else _host.RunByeDpiDllSetup();
        }

        // =====================================================================
        // Paket Kuralları (Zapret)
        // =====================================================================

        private void CmbZapretPreset_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;
            _host.SelectZapretPreset(CmbZapretPreset.SelectedIndex);
            TxtZapretParams.Text = _host.ZapretParameters;
            UpdateZapretPage();
        }

        private void ZapretScan_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            if (int.TryParse((sender as FrameworkElement)?.Tag?.ToString(), out var level))
                _host.ZapretScanLevel = level;
            UpdateZapretPage();
        }

        private void ZapretMode_Changed(object sender, RoutedEventArgs e) => UpdateZapretPage();

        private void TglZapretEdit_Click(object sender, RoutedEventArgs e)
        {
            // Kullanıcının değiştiremeyeceği alanı ekranda tutmuyoruz.
            PanelZapretParams.Visibility = TglZapretEdit.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtZapretParams_Changed(object sender, TextChangedEventArgs e)
        {
            if (!IsInitialized) return;
            _host.ZapretParameters = TxtZapretParams.Text;
        }

        private void UpdateZapretPage()
        {
            if (!IsInitialized) return;

            var isService = SegZapretService.IsChecked == true;
            var profile = CmbZapretPreset.SelectedItem?.ToString() ?? "-";
            var scan = SegScanQuick.IsChecked == true ? "Hızlı" : "Ayrıntılı";

            TxtZapretModeDesc.Text = isService
                ? "Servis: kalıcı olarak kurulur ve Windows ile birlikte başlar."
                : "Tek seferlik: yalnızca bu oturumda çalışır, yeniden başlatmada kaybolur.";

            TxtZapretSummary.Text =
                $"Zapret · {profile} profili · {scan} tarama · " +
                (isService ? "Servis olarak kurulacak" : "Tek seferlik çalıştırılacak");

            BtnRunZapret.Content = isService ? "Kurulumu Başlat" : "Çalıştır";
        }

        private void BtnRunZapret_Click(object sender, RoutedEventArgs e) =>
            _host.RunZapret(SegZapretService.IsChecked == true ? RunMode.Service : RunMode.Once);

        private void BtnZapretAuto_Click(object sender, RoutedEventArgs e) => _host.RunZapretAutoInstall();

        // =====================================================================
        // DPI Classic (GoodbyeDPI)
        // =====================================================================

        private void CmbClassicPreset_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;
            _host.SelectGoodbyeDpiPreset(CmbClassicPreset.SelectedIndex);
            TxtClassicParams.Text = _host.GoodbyeDpiParameters;
            UpdateClassicPage();
        }

        private void ClassicMode_Changed(object sender, RoutedEventArgs e) => UpdateClassicPage();

        private void TglClassicEdit_Click(object sender, RoutedEventArgs e)
        {
            PanelClassicParams.Visibility = TglClassicEdit.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtClassicParams_Changed(object sender, TextChangedEventArgs e)
        {
            if (!IsInitialized) return;
            _host.GoodbyeDpiParameters = TxtClassicParams.Text;
        }

        private void UpdateClassicPage()
        {
            if (!IsInitialized) return;

            var isService = SegClassicService.IsChecked == true;
            var preset = CmbClassicPreset.SelectedItem?.ToString() ?? "-";

            TxtClassicSummary.Text =
                $"GoodbyeDPI · {preset} ayarı" +
                (TglBlacklist.IsChecked == true ? " · blacklist etkin" : "") + " · " +
                (isService ? "Servis olarak kurulacak" : "Tek seferlik çalıştırılacak");

            BtnRunClassic.Content = isService ? "Kurulumu Başlat" : "Çalıştır";
        }

        private void BtnRunClassic_Click(object sender, RoutedEventArgs e) =>
            _host.RunGoodbyeDpi(SegClassicService.IsChecked == true ? RunMode.Service : RunMode.Once);

        private void BtnEditBlacklist_Click(object sender, RoutedEventArgs e)
        {
            ShowEditorModal(
                title: "Blacklisti Düzenle",
                body: "Her satıra bir alan adı yazın.",
                text: _host.BlacklistText,
                confirmLabel: "Kaydet",
                onConfirm: () =>
                {
                    _host.BlacklistText = ModalEditor.Text;
                    _host.SaveBlacklist();
                    ShowToast("Blacklist kaydedildi");
                });
        }

        // =====================================================================
        // Kurtarma
        // =====================================================================

        private void BtnDiscordRepair_Click(object sender, RoutedEventArgs e) => _host.RepairDiscord();
        private void BtnDiscordStart_Click(object sender, RoutedEventArgs e) => _host.StartDiscord();
        private void BtnWebCordStart_Click(object sender, RoutedEventArgs e) => _host.StartWebCord();

        private void BtnDiscordRemove_Click(object sender, RoutedEventArgs e) =>
            ShowConfirm(
                title: "Discord kaldırılsın mı?",
                body: "Bu işlem Discord istemcisini ve program dosyalarını kaldıracak, " +
                      "ayrıca %APPDATA%\\discord altındaki uygulama verilerini silecektir.",
                confirmLabel: "Discord'u Kaldır",
                onConfirm: () => _host.RemoveDiscord());

        private void BtnDroverRemove_Click(object sender, RoutedEventArgs e) =>
            ShowConfirm(
                title: "Drover kaldırılsın mı?",
                body: "Bu işlem Discord'u kapatacak ve Discord klasörüne kopyalanmış " +
                      "version.dll ile drover.ini dosyalarını silecektir.",
                confirmLabel: "Drover'ı Kaldır",
                onConfirm: () => _host.RemoveDrover());
        private void BtnPtbStart_Click(object sender, RoutedEventArgs e) => _host.StartDiscordPtb();
        private void BtnWebCordInstall_Click(object sender, RoutedEventArgs e) => _host.InstallWebCord();

        private void BtnPtbInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_host.DiscordPtbCleanInstall)
            {
                ShowConfirm(
                    title: "Temiz kurulum onayı",
                    body: "Temiz kurulum seçili. Bu işlem mevcut Discord PTB verilerini silip istemciyi yeniden kuracaktır. Devam etmek istiyor musunuz?",
                    confirmLabel: "Temiz Kurulumu Başlat",
                    onConfirm: () => _host.InstallDiscordPtb());
                return;
            }
            _host.InstallDiscordPtb();
        }

        private void BtnPtbRemove_Click(object sender, RoutedEventArgs e) =>
            ShowConfirm(
                title: "Discord PTB kaldırılsın mı?",
                body: "Bu işlem Discord PTB istemcisini ve ilgili program dosyalarını kaldıracaktır. Kullanıcı verileri korunur.",
                confirmLabel: "PTB'yi Kaldır",
                onConfirm: () => _host.RemoveDiscordPtb());

        private void BtnWebCordRemove_Click(object sender, RoutedEventArgs e) =>
            ShowConfirm(
                title: "WebCord kaldırılsın mı?",
                body: "Bu işlem WebCord istemcisini ve oluşturulmuş masaüstü kısayolunu kaldıracaktır.",
                confirmLabel: "WebCord'u Kaldır",
                onConfirm: () => _host.RemoveWebCord());

        // =====================================================================
        // Servisler
        // =====================================================================

        private void ConfirmRemoveService(string serviceId, string displayName) =>
            ShowConfirm(
                title: displayName + " servisi kaldırılsın mı?",
                body: $"Bu işlem {displayName} servisini durduracak, kaldıracak ve servisin mevcut yapılandırmasını silecektir. Devam etmek istiyor musunuz?",
                confirmLabel: "Servisi Kaldır",
                onConfirm: () => _host.RemoveService(serviceId));

        private void BtnRemoveAll_Click(object sender, RoutedEventArgs e) =>
            ShowConfirm(
                title: "Tüm servisler kaldırılsın mı?",
                body: "Bu işlem kurulu bütün RevivalDPI servislerini (WireSock, ByeDPI, Zapret ve diğerleri) durdurup kaldıracak ve yapılandırmalarını silecektir. Devam etmek istiyor musunuz?",
                confirmLabel: "Tümünü Kaldır",
                onConfirm: () => _host.RemoveAllServices());

        private void BtnRevertDns_Click(object sender, RoutedEventArgs e) =>
            ShowConfirm(
                title: "DNS ve DoH ayarları geri alınsın mı?",
                body: "Bu işlem RevivalDPI tarafından uygulanan DNS ve DNS-over-HTTPS yapılandırmasını kaldırıp Windows varsayılanlarına dönecektir.",
                confirmLabel: "Ayarları Geri Al",
                onConfirm: () => _host.ResetDnsAndDoh());

        private void BtnUninstall_Click(object sender, RoutedEventArgs e) =>
            ShowConfirm(
                title: "RevivalDPI kaldırılsın mı?",
                body: "Bu işlem RevivalDPI uygulamasını, kurulu tüm servisleri ve kayıtlı ayarları sistemden kalıcı olarak kaldıracaktır. Bu işlem geri alınamaz.",
                confirmLabel: "RevivalDPI'yi Kaldır",
                onConfirm: () => _host.UninstallApplication(),
                // Yanlışlıkla tıklamayı önlemek için ek onay kutusu.
                checkLabel: "RevivalDPI'yi kaldırmak istediğimi anlıyorum");

        // =====================================================================
        // Yardım / Hakkında
        // =====================================================================

        private void HelpOverview_Click(object sender, RoutedEventArgs e) =>
            ShowInfo("Genel Bakış hakkında",
                "Bu ekran sistemin güncel durumunu özetler: hangi yönlendirme etkin, hangi DPI " +
                "yöntemi kurulu, kaç servis çalışıyor ve DNS ayarları uygulanacak mı.\n\n" +
                "Standart kurulum WireSock tabanlı yönlendirmeyi kurar ve önerilen yöntemdir. " +
                "Alternatif kurulum, standart yöntem çalışmadığında denenecek ikinci yapılandırmadır.\n\n" +
                "Gelişmiş seçeneklerden tarayıcı trafiğini tünele dâhil edebilir veya yalnızca " +
                "seçtiğiniz klasörleri tünelleyen özel bir kurulum yapabilirsiniz. " +
                "Tüm kurulumlar yönetici yetkisi gerektirir.");

        private void HelpRecovery_Click(object sender, RoutedEventArgs e) =>
            ShowInfo("Kurtarma hakkında",
                "DPI engelleri Discord'un ses ve görüntü bağlantılarını bozabilir. Bu ekran, " +
                "Discord kurulumunu onarır veya alternatif istemciler kurar.\n\n" +
                "Discord PTB, Discord'un test sürümüdür ve normal sürümle yan yana çalışır. " +
                "WebCord ise açık kaynak bir Discord istemcisidir.\n\n" +
                "Temiz kurulum seçeneği açıkken mevcut veriler silinir ve onay istenir. " +
                "İşlemler yönetici yetkisi gerektirir.");

        private void HelpServices_Click(object sender, RoutedEventArgs e) =>
            ShowInfo("Servisler hakkında",
                "Kurulan her yöntem arkasında bir veya birkaç Windows servisi bırakır. " +
                "Bu tablo hangilerinin kurulu olduğunu gösterir ve tek tek kaldırmanızı sağlar.\n\n" +
                "Drover bir servis değildir; Discord klasörüne kopyalanan bir DLL ile çalışır.\n\n" +
                "Tehlikeli işlemler bölümü sistemi kurulum öncesi hâline döndürür. " +
                "Her biri onay ister ve geri alınamaz.");

        private void HelpDpi_Click(object sender, RoutedEventArgs e) =>
            ShowInfo("DPI Yönlendirme hakkında",
                "ByeDPI Split Tunneling, trafiği uygulama bazında ayırır; yalnızca seçilen bağlantılar tünellenir. " +
                "ByeDPI DLL ise bileşeni sistem geneli kullanım için kurar.\n\n" +
                "Kurulum yönetici yetkisi gerektirir. Kurulan yöntem Servisler ekranından kaldırılabilir.");

        private void HelpPackets_Click(object sender, RoutedEventArgs e) =>
            ShowInfo("Paket Kuralları hakkında",
                "Zapret, paket düzeyinde kurallar uygulayarak DPI engellerini aşar. Hazır profiller operatörünüze göre ayarlanmıştır.\n\n" +
                "Servis: kalıcı kurulur, Windows ile başlar. Tek seferlik: yalnızca bu oturumda çalışır, yeniden başlatınca kaybolur.\n\n" +
                "Kurulum yönetici yetkisi gerektirir.");

        private void HelpClassic_Click(object sender, RoutedEventArgs e) =>
            ShowInfo("DPI Classic hakkında",
                "GoodbyeDPI tabanlı klasik yöntemdir. Hazır ayarlar çoğu bağlantı için yeterlidir; " +
                "blacklist yalnızca listedeki alan adlarına uygulanır.\n\n" +
                "Servis: kalıcı kurulur. Tek seferlik: yeniden başlatınca kaybolur. Kurulum yönetici yetkisi gerektirir.");

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            PrepareModal();
            ModalCard.Width = 460;

            // Hakkında kendi yerleşimini kullanır; genel modal gövdesi gizlenir.
            ModalGeneric.Visibility = Visibility.Collapsed;
            AboutPanel.Visibility = Visibility.Visible;

            TxtAboutVersion.Text = "Sürüm " + _host.ProductVersion;
            BuildAboutInfo();

            ModalLayer.Visibility = Visibility.Visible;
            BtnAboutClose.Focus();
        }

        /// <summary>
        /// Hakkında penceresindeki sistem bilgisi tablosunu doldurur.
        /// Buradaki değerler hata bildirimlerinde istenen bilgilerdir; kullanıcı
        /// bunları tek yerden okuyabilsin diye toplandı.
        /// </summary>
        private void BuildAboutInfo()
        {
            GridAboutInfo.Children.Clear();
            GridAboutInfo.RowDefinitions.Clear();

            var rows = new (string Label, string Value)[]
            {
                ("Sürüm", _host.ProductVersion),
                ("Platform", RuntimeInformation.OSDescription.Trim()),
                ("Mimari", RuntimeInformation.OSArchitecture.ToString()),
                ("Çalışma zamanı", RuntimeInformation.FrameworkDescription.Trim()),
                ("Arayüz dili", LanguageDisplayName(_host.CurrentLanguage)),
                ("Veri klasörü", Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevivalDPI")),
            };

            for (int i = 0; i < rows.Length; i++)
            {
                var (label, value) = rows[i];
                GridAboutInfo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var labelBlock = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    Margin = new Thickness(0, i == 0 ? 0 : 5, 12, 0),
                    Foreground = (Brush)FindResource("Brush.TextSecondary"),
                };
                Grid.SetRow(labelBlock, i);
                Grid.SetColumn(labelBlock, 0);
                GridAboutInfo.Children.Add(labelBlock);

                var valueBlock = new TextBlock
                {
                    Text = value,
                    FontSize = 12,
                    Margin = new Thickness(0, i == 0 ? 0 : 5, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = value,
                };
                Grid.SetRow(valueBlock, i);
                Grid.SetColumn(valueBlock, 1);
                GridAboutInfo.Children.Add(valueBlock);
            }
        }

        private static string LanguageDisplayName(string code)
        {
            switch (code)
            {
                case "EN": return "English";
                case "RU": return "Русский";
                default: return "Türkçe";
            }
        }

        private void BtnAboutRepo_Click(object sender, RoutedEventArgs e) => _host.OpenRepository();
        private void BtnAboutLogs_Click(object sender, RoutedEventArgs e) => _host.OpenLogsFolder();

        // =====================================================================
        // Modal katman
        // =====================================================================

        private void PrepareModal()
        {
            ModalGeneric.Visibility = Visibility.Visible;
            AboutPanel.Visibility = Visibility.Collapsed;
            ModalEditor.Visibility = Visibility.Collapsed;
            ModalCheck.Visibility = Visibility.Collapsed;
            ModalCheck.IsChecked = false;
            BtnModalCancel.Visibility = Visibility.Visible;
            BtnModalConfirm.Visibility = Visibility.Visible;
            BtnModalConfirm.IsEnabled = true;
            ModalCard.Width = 420;
            _modalConfirm = null;
        }

        private void ShowConfirm(string title, string body, string confirmLabel, Action onConfirm,
                                 string checkLabel = null)
        {
            PrepareModal();
            TxtModalTitle.Text = title;
            TxtModalBody.Text = body;
            BtnModalConfirm.Content = confirmLabel;
            BtnModalConfirm.Style = (Style)FindResource("Button.DangerSolid");
            _modalConfirm = onConfirm;

            if (checkLabel != null)
            {
                ModalCheck.Content = checkLabel;
                ModalCheck.Visibility = Visibility.Visible;
                BtnModalConfirm.IsEnabled = false;
            }

            ModalLayer.Visibility = Visibility.Visible;
            // Enter'a yanlışlıkla basıldığında yıkıcı işlem başlamasın:
            // varsayılan focus Vazgeç'tedir.
            BtnModalCancel.Focus();
        }

        private void ShowEditorModal(string title, string body, string text, string confirmLabel, Action onConfirm)
        {
            PrepareModal();
            ModalCard.Width = 460;
            TxtModalTitle.Text = title;
            TxtModalBody.Text = body;
            ModalEditor.Text = text;
            ModalEditor.Visibility = Visibility.Visible;
            BtnModalConfirm.Content = confirmLabel;
            BtnModalConfirm.Style = (Style)FindResource("Button.Primary");
            _modalConfirm = onConfirm;

            ModalLayer.Visibility = Visibility.Visible;
            ModalEditor.Focus();
        }

        private void ShowInfo(string title, string body)
        {
            PrepareModal();
            ModalCard.Width = 440;
            TxtModalTitle.Text = title;
            TxtModalBody.Text = body;
            BtnModalCancel.Visibility = Visibility.Collapsed;
            BtnModalConfirm.Content = "Kapat";
            BtnModalConfirm.Style = (Style)FindResource("Button.Secondary");
            _modalConfirm = null;

            ModalLayer.Visibility = Visibility.Visible;
            BtnModalConfirm.Focus();
        }

        private void CloseModal()
        {
            ModalLayer.Visibility = Visibility.Collapsed;
            _modalConfirm = null;
        }

        private void ModalCheck_Changed(object sender, RoutedEventArgs e) =>
            BtnModalConfirm.IsEnabled = ModalCheck.IsChecked == true;

        private void BtnModalCancel_Click(object sender, RoutedEventArgs e) => CloseModal();

        private void BtnModalConfirm_Click(object sender, RoutedEventArgs e)
        {
            var action = _modalConfirm;
            CloseModal();
            action?.Invoke();
        }

        // =====================================================================
        // Toast
        // =====================================================================

        private void ShowToast(string message, bool success = true)
        {
            TxtToast.Text = message;
            ToastStripe.Background = success
                ? (Brush)FindResource("Brush.Success")
                : (Brush)FindResource("Brush.DangerStrong");
            Toast.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }
    }
}
