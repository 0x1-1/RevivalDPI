<p align="center">
  <img src="https://github.com/0x1-1/RevivalDPI/blob/main/src/RevivalDPI/Resources/revivaldpi-logo.png?raw=true" alt="RevivalDPI" width="360">
</p>

<h1 align="center">RevivalDPI</h1>

<p align="center">
  Windows üzerindeki ağ yönlendirme, DPI yöntemleri, onarım işlemleri ve servis
  temizliğini tek yerden yöneten masaüstü yönetim aracı.
</p>

<p align="center">
  <a href="https://github.com/0x1-1/RevivalDPI/actions/workflows/ci.yml"><img src="https://github.com/0x1-1/RevivalDPI/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://github.com/0x1-1/RevivalDPI/actions/workflows/release.yml"><img src="https://github.com/0x1-1/RevivalDPI/actions/workflows/release.yml/badge.svg" alt="Release"></a>
  <a href="https://github.com/0x1-1/RevivalDPI/releases/latest"><img src="https://img.shields.io/github/v/release/0x1-1/RevivalDPI?label=s%C3%BCr%C3%BCm" alt="Sürüm"></a>
  <a href="https://github.com/0x1-1/RevivalDPI/releases"><img src="https://img.shields.io/github/downloads/0x1-1/RevivalDPI/total?label=indirme" alt="İndirme"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/lisans-MIT-blue" alt="Lisans"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D4" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-8.0%20WPF-512BD4" alt=".NET 8">
</p>

<p align="center">
  <a href=".github/README_EN.md">English</a> ·
  <a href=".github/README_RU.md">Русский</a>
</p>

---

## RevivalDPI nedir?

RevivalDPI, Windows'ta DPI (Deep Packet Inspection) engellerini aşmak için
kullanılan araçları tek bir arayüzden yöneten bir masaüstü uygulamasıdır.
WireSock, ByeDPI, Zapret ve GoodbyeDPI gibi bileşenleri kurar, yapılandırır,
durumlarını gösterir ve gerektiğinde temizler.

Amaç, her biri kendi komut satırı parametreleriyle gelen bu araçları elle
kurmak zorunda kalmamanızdır. Uygulama hazır operatör profilleri sunar, kurulum
adımlarını yürütür ve yaptığı değişiklikleri geri alabilmenizi sağlar.

> [!WARNING]
> RevivalDPI **yönetici yetkisiyle çalışır**, Windows servisleri kurar, çekirdek
> modu paket yakalama sürücüsü (WinDivert) yükler ve DNS yapılandırmanızı
> değiştirir. Ne yaptığını anlamadan çalıştırmayın. Yaptığı değişikliklerin
> tamamı **Servisler** ekranındaki geri alma işlemleriyle kaldırılabilir.

## Kurulum

[Releases sayfasından](https://github.com/0x1-1/RevivalDPI/releases/latest) iki
seçenek indirebilirsiniz:

| Dosya | Açıklama |
| --- | --- |
| `RevivalDPI-Setup-vX.Y.Z.exe` | Kurulum sihirbazı. Gerekli ön koşulları (VC++ redist, Windows Packet Filter) paketler; uygulama ilk kurulum işleminde bunları otomatik kurar. **Önerilen.** |
| `RevivalDPI-win-x64-vX.Y.Z.zip` | Taşınabilir sürüm. Kendi kendine yeter, ayrıca .NET kurulumu gerektirmez. |

### İndirdiğinizi doğrulayın

Her release, yayımlanan dosyaların SHA-256 özetlerini içeren bir
`SHA256SUMS.txt` dosyası içerir:

```powershell
Get-FileHash .\RevivalDPI-win-x64-v1.6.0.zip -Algorithm SHA256
```

Çıkan değeri `SHA256SUMS.txt` içindeki satırla karşılaştırın.

### Gereksinimler

- Windows 10 (1809 veya üzeri) ya da Windows 11, **x64**
- Yönetici yetkisi
- Taşınabilir sürüm için ayrıca .NET kurulumu **gerekmez**

> [!NOTE]
> Cloudflare WARP, Kaspersky ve diğer VPN/güvenlik yazılımları ağ katmanına
> müdahale ettiği için RevivalDPI ile çakışabilir. Sorun yaşarsanız önce bu
> yazılımları kapatmayı deneyin.

## Lisans

MIT, bkz. [LICENSE](LICENSE).

RevivalDPI; Zapret, GoodbyeDPI, WinDivert, WireSock, ByeDPI ve ProxiFyre gibi
üçüncü taraflara ait çalıştırılabilir dosya ve kütüphaneleri yeniden dağıtır.
Bunların telif hakkı ve lisansları kendi sahiplerine aittir.
