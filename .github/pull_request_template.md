<!--
Başlığı Conventional Commits biçiminde yazın; sürüm artışı buradan hesaplanır.
Örnek: fix(tema): ComboBox açılır listesi koyu temada güncellenmiyordu
-->

## Ne değişti?

<!-- Bir iki cümleyle özetleyin. -->

## Neden?

<!-- Hangi sorunu çözüyor? Varsa issue bağlayın: Closes #123 -->

## Değişiklik türü

- [ ] `fix`: hata düzeltmesi
- [ ] `feat`: yeni özellik
- [ ] `ui` / `style`: arayüz değişikliği
- [ ] `perf`: performans
- [ ] `docs`: dokümantasyon
- [ ] `refactor` / `chore` / `build` / `ci`: bakım
- [ ] Geriye dönük uyumsuz değişiklik (`!` veya `BREAKING CHANGE`)

## Doğrulama

- [ ] `dotnet build src\RevivalDPI\RevivalDPI.csproj -c Release -warnaserror` geçiyor
- [ ] Açık, koyu ve sistem temasında test edildi
- [ ] Çalışma anında tema geçişinde karışık tema oluşmuyor (dialog, tooltip,
      ComboBox açılır listesi, başlık çubuğu dâhil)
- [ ] Pencere 880×620'ye küçültüldüğünde içerik kesilmiyor
- [ ] 125 ve 150% ölçeklendirmede metinler kesilmiyor
- [ ] Uzun işlem sırasında arayüz donmuyor

<!-- Kurulum/servis akışına dokunduysanız: -->

- [ ] Değişen akış sanal makinede uçtan uca çalıştırıldı
- [ ] Yıkıcı işlemler onay dialogu gösteriyor, varsayılan focus `Vazgeç`'te

<!-- Kullanıcıya görünen metin eklediyseniz: -->

- [ ] Yeni anahtarlar `tr`, `en`, `ru` dosyalarının üçüne de eklendi

## Ekran görüntüsü

<!-- Arayüz değişikliklerinde açık ve koyu tema için önce/sonra ekleyin. -->

## Notlar

<!-- Gözden geçirenin bilmesi gereken başka bir şey var mı? Bilinen eksikler? -->
