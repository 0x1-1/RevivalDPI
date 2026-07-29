# Değişiklik Günlüğü

Sürüm geçmişinin tamamı
[Releases sayfasındadır](https://github.com/0x1-1/RevivalDPI/releases). Her
release'in notları, o sürüme giren commit'lerden otomatik üretilir.

Bu dosya elle tutulmaz; sürüm bilgisinin tek kaynağı git etiketleridir.

## Sürüm numarası nasıl belirlenir?

`main` dalına yapılan her push yeni bir sürüm yayımlar. Artış, o push'taki
commit mesajlarından hesaplanır:

| Commit ön eki | Artış | Örnek |
| --- | --- | --- |
| `feat!:` veya gövdede `BREAKING CHANGE:` | major | 1.5.5 sonrası 2.0.0 |
| `feat:` | minor | 1.5.5 sonrası 1.6.0 |
| diğer her şey | patch | 1.5.5 sonrası 1.5.6 |

Yayımlanan sürümün kaynağı son `v*` etiketidir ve derleme sırasında MSBuild
ile kurulum betiğine parametre olarak geçirilir. Bu sayede yayın süreci depoya
commit atmaz.

`Directory.Build.props` içindeki değer yalnızca yerel derlemelerin ve ilk
yayının başlangıç noktasıdır; yayımlanan sürümü belirlemez.

Bir push'un sürüm yayımlamasını istemiyorsanız commit mesajınıza
`[skip release]` ekleyin.
