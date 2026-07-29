using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace RevivalDPI.Themes
{
    /// <summary>Kullanıcının seçebileceği tema modları.</summary>
    public enum AppTheme
    {
        /// <summary>Windows'un açık/koyu tercihini takip eder (varsayılan).</summary>
        System,
        Light,
        Dark
    }

    /// <summary>
    /// Uygulama genelinde tema yönetimi.
    ///
    /// Çalışma biçimi: <c>Themes/Light.xaml</c> ve <c>Themes/Dark.xaml</c> aynı
    /// anahtar kümesini tanımlar. Tema değiştiğinde
    /// <see cref="Application.Resources"/> içindeki tema sözlüğü tek seferde
    /// diğeriyle değiştirilir. Kontroller renkleri <c>DynamicResource</c> ile
    /// tükettiği için değişim anında ve eksiksiz yayılır, pencere yeniden
    /// oluşturulmaz, flash olmaz, hiçbir kontrol eski temada kalmaz.
    /// </summary>
    public static class ThemeManager
    {
        private const string RegKeyPath = @"Software\RevivalDPI\Theme";
        private const string RegValueMode = "ThemeMode";
        // Eski kod bu DWORD'u okuyor; iki değer de yazılarak tutarlılık korunur.
        private const string RegValueIsDark = "IsDarkMode";

        private const string PersonalizeKey =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightTheme = "AppsUseLightTheme";

        private static readonly Uri LightUri =
            new Uri("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);
        private static readonly Uri DarkUri =
            new Uri("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute);

        private static ResourceDictionary _current;
        private static bool _initialized;

        /// <summary>Seçili mod (Sistem / Açık / Koyu).</summary>
        public static AppTheme Mode { get; private set; } = AppTheme.System;

        /// <summary>Modun çözümlenmiş hâli: şu anda koyu tema mı etkin?</summary>
        public static bool IsDark { get; private set; }

        /// <summary>Etkin tema değiştiğinde tetiklenir.</summary>
        public static event EventHandler ThemeChanged;

        /// <summary>
        /// Uygulama başlangıcında bir kez çağrılır. Kayıtlı tercihi yükler,
        /// uygular ve Windows tema değişikliklerini dinlemeye başlar.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            Application.Current.Exit += (_, _) =>
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

            Apply(LoadMode(), persist: false);
        }

        /// <summary>Modu değiştirir, uygular ve tercihi kalıcı olarak saklar.</summary>
        public static void Apply(AppTheme mode) => Apply(mode, persist: true);

        private static void Apply(AppTheme mode, bool persist)
        {
            Mode = mode;
            var dark = mode == AppTheme.Dark || (mode == AppTheme.System && IsSystemDark());

            // Mod değişmiş ama çözümlenmiş tema aynıysa (ör. Sistem koyu iken
            // Koyu seçildi) sözlüğü yeniden yüklemeye gerek yok; yalnızca
            // tercihi sakla.
            var needsSwap = _current == null || dark != IsDark;
            IsDark = dark;

            if (needsSwap) SwapDictionary(dark);
            if (persist) SaveMode(mode, dark);

            ApplyTitleBarToAllWindows(dark);

            if (needsSwap) ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static void SwapDictionary(bool dark)
        {
            var app = Application.Current;
            if (app == null) return;

            var next = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
            var merged = app.Resources.MergedDictionaries;

            if (_current != null && merged.Contains(_current))
            {
                // Aynı konumda değiştir: sıralama korunur, dolayısıyla
                // MaterialDesign sözlüklerine göre önceliğimiz sabit kalır.
                merged[merged.IndexOf(_current)] = next;
            }
            else
            {
                // Sona eklenir; sonradan eklenen sözlük çakışan anahtarlarda kazanır.
                merged.Add(next);
            }

            _current = next;
        }

        /// <summary>
        /// Yeni açılan pencereler için başlık çubuğunu geçerli temaya uydurur.
        /// Pencere <c>Loaded</c> olduktan sonra çağrılmalıdır (HWND gerekir).
        /// </summary>
        public static void ApplyTitleBar(Window window) => ApplyTitleBar(window, IsDark);

        private static void ApplyTitleBarToAllWindows(bool dark)
        {
            var app = Application.Current;
            if (app == null) return;
            foreach (Window w in app.Windows) ApplyTitleBar(w, dark);
        }

        private static void ApplyTitleBar(Window window, bool dark)
        {
            if (window == null) return;
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return; // pencere henüz oluşturulmadı

                // DWMWA_USE_IMMERSIVE_DARK_MODE pencere düğmelerinin (simge
                // durumu / kapat) glif rengini belirler. Windows 10 20H1
                // öncesinde aynı anlam 19'daydı.
                var value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref value, sizeof(int));

                // DİKKAT: bu özniteliği 0 yapmak, koyu bir Windows temasında
                // başlık çubuğunu AÇIK yapmaz, yalnızca "zorlamayı bırak"
                // demektir ve çubuk sistem temasında kalır. Uygulama teması
                // sistemden bağımsız seçilebildiği için çubuk rengini açıkça
                // yazmamız gerekir (Windows 11 22000+). Eski sürümlerde bu
                // çağrılar sessizce başarısız olur ve yukarıdaki davranış
                // geçerli kalır.
                if (TryGetColor("Color.Surface", out var caption))
                {
                    var captionRef = ToColorRef(caption);
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionRef, sizeof(int));
                }

                if (TryGetColor("Color.TextPrimary", out var text))
                {
                    var textRef = ToColorRef(text);
                    DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textRef, sizeof(int));
                }

                if (TryGetColor("Color.Border", out var border))
                {
                    var borderRef = ToColorRef(border);
                    DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderRef, sizeof(int));
                }

                // Öznitelikler pencere oluşturulduktan SONRA değiştiğinde DWM
                // çerçeveyi kendiliğinden yeniden çizmeyebilir.
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch
            {
                // Başlık çubuğu rengi kozmetiktir; başarısızlığı uygulamayı
                // durdurmamalı (ör. desteklemeyen Windows sürümü).
            }
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            if (Mode != AppTheme.System) return;

            // Bu olay UI dışı bir thread'den gelebilir.
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                Apply(AppTheme.System, persist: false)));
        }

        private static bool IsSystemDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    // Anahtar yoksa (ör. Windows Server) açık tema varsayılır.
                    if (key?.GetValue(AppsUseLightTheme) is int light) return light == 0;
                }
            }
            catch
            {
                // Erişilemiyorsa açık temaya düş.
            }
            return false;
        }

        private static AppTheme LoadMode()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegKeyPath))
                {
                    if (key == null) return AppTheme.System;

                    if (key.GetValue(RegValueMode) is string s &&
                        Enum.TryParse(s, ignoreCase: true, out AppTheme parsed))
                    {
                        return parsed;
                    }

                    // ThemeMode yoksa eski IsDarkMode DWORD'undan devral.
                    if (key.GetValue(RegValueIsDark) is int legacy)
                        return legacy != 0 ? AppTheme.Dark : AppTheme.Light;
                }
            }
            catch
            {
                // Okunamıyorsa varsayılana düş.
            }
            return AppTheme.System;
        }

        private static void SaveMode(AppTheme mode, bool dark)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegKeyPath))
                {
                    if (key == null) return;
                    key.SetValue(RegValueMode, mode.ToString(), RegistryValueKind.String);
                    key.SetValue(RegValueIsDark, dark ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch
            {
                // Tercih saklanamazsa uygulama yine çalışır; sonraki açılışta
                // varsayılan moda döner.
            }
        }

        /// <summary>
        /// Light.xaml ve Dark.xaml'in anahtar kümelerinin birebir aynı olduğunu
        /// doğrular. Eksik bir anahtar, çalışma anında sessizce karışık temaya
        /// yol açar, bu yüzden hata ayıklama derlemelerinde başlangıçta
        /// çağrılır ve eksikleri döndürür.
        /// </summary>
        public static IReadOnlyList<string> FindMismatchedKeys()
        {
            var light = new ResourceDictionary { Source = LightUri };
            var dark = new ResourceDictionary { Source = DarkUri };

            var lightKeys = light.Keys.Cast<object>().Select(k => k.ToString()).ToHashSet();
            var darkKeys = dark.Keys.Cast<object>().Select(k => k.ToString()).ToHashSet();

            var missing = new List<string>();
            missing.AddRange(lightKeys.Except(darkKeys).Select(k => $"Dark.xaml eksik: {k}"));
            missing.AddRange(darkKeys.Except(lightKeys).Select(k => $"Light.xaml eksik: {k}"));
            missing.Sort();
            return missing;
        }

        /// <summary>Etkin tema sözlüğünden bir renk token'ı okur.</summary>
        private static bool TryGetColor(string key, out System.Windows.Media.Color color)
        {
            color = default;
            var value = Application.Current?.TryFindResource(key);
            if (value is System.Windows.Media.Color c) { color = c; return true; }
            return false;
        }

        /// <summary>WPF rengini Win32 COLORREF'e (0x00BBGGRR) çevirir.</summary>
        private static int ToColorRef(System.Windows.Media.Color c) =>
            c.R | (c.G << 8) | (c.B << 16);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);
    }
}
