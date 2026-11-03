# Metin2Search

Metin2 "Ticaret Camı" (oyuncu pazarı) ilanlarını yerel bir tarayıcıyla toplayan ve bunları hızlı, filtrelenebilir bir web sitesinde gösteren, tamamen yerelde çalışan bir pazar arama aracı.

> **English summary:** A local-first market search tool for the Metin2 player market. A Windows scanner (C# WPF + a native bridge) collects market listings into SQLite; a dependency-free Node.js site serves search, filters, price/listing history, comparison and FAKE-shop status. Unofficial, not affiliated with Gameforge/Webzen/YMIR. No game assets or data tables are included. See the warnings below before using it.

## Önemli uyarılar

- **Resmî bir proje değildir.** Gameforge, Webzen, YMIR veya Metin2 ile hiçbir bağlantısı yoktur. Metin2 adı ve oyun içeriği ilgili hak sahiplerine aittir.
- **Tarama bileşeni oyun istemcisine müdahale eder.** Tarayıcı, oyun sürecine bir köprü bağlar ve istemcinin kendi pazar arama işlevlerini çağırır. Bu, oyunun kullanım şartlarına aykırı olabilir ve **hesabınızın kapatılmasına** yol açabilir. Kullanırsanız tüm sorumluluk size aittir. Ana hesabınızla kullanmayın; yalnızca kaybetmeyi göze aldığınız bir hesapla deneyin.
- **Oyun içeriği dahil değildir.** Eşya simgeleri, eşya tabloları (`item_proto`, `item_names` vb.), harita/model dosyaları ve oyun logoları depoda yoktur. Siteyi çalıştırmak için bu tabloları kendi istemcinizden üretmeniz gerekir (bkz. [Oyun veri tabloları](#oyun-veri-tabloları)).
- **İstemci sürümüne bağlıdır.** Tarayıcının native köprüsü belirli bir istemci derlemesine göre yazılmıştır; oyun güncellenince çalışmayabilir.
- Yazılım "olduğu gibi" verilir, herhangi bir garanti vermez (bkz. [LICENSE](LICENSE)).
- Canlı bir hizmet olarak işletilmemektedir. Depo, kendi bilgisayarında denemek isteyenler içindir; destek garanti edilmez.

## Ne yapar

**Site (Node.js, bağımlılıksız)**
- Eşya adı, fiyat (Won/Yang), seviye, yükseltme, miktar, element, efsun türü ve değeri (Min ~ Max), kategoriye özel filtreler ve satıcı araması.
- Adet başı ve toplam fiyat sıralaması, sayfalama, aktif filtre şeridi, kayıtlı eşyalar.
- Eşya penceresi: temel özellikler, efsunlar, taşlar, element, SungMa puanı, sandık içerikleri.
- **Karşılaştırma:** aynı, daha iyi, daha düşük ve yalnızca birinde olan değerler ayrı gösterilir. **Benzer alternatifler:** aynı eşyanın daha ucuz ilanları (efsun türü oranıyla eşleştirilir).
- Fiyat ve eşya geçmişi, satıcı pazarı (envanter yerleşimiyle), FAKE/doğrulandı rozetleri.
- SEO sayfaları, çerez/gizlilik sayfaları, yönetim paneli (`/yonetim`, yalnızca yerel), bakım modu, ziyaretçi istatistikleri, günlük yedek ve arşiv.
- Mobil uyumlu arayüz, cam (glass) tema; solda katlanır filtre paneli (genel, kategori, efsun Min ~ Max kartları), üstte ince araç çubuğu (fiyat Won/Yang, miktar, satıcı, sıralama).

**Tarayıcı (Windows, C# WPF + native köprü)**
- Ticaret Camı'ndaki kategorileri sırayla tarar: tam tur, hızlı tur ve artımlı gözlem.
- Her ilanı yerel SQLite veritabanına yazar. Tam turda görünmeyen ilan kaldırılmış sayılır; artımlı turda görülmeyen ilan kaldırılmış sayılmaz.
- Satıcı pazarlarının alışverişe hazır olup olmadığını (FAKE) kontrol eder.
- İsteğe bağlı: karakter döngüsü, günlük arşiv, uzak bir siteye aktarım (`uplink`).

## Mimari

```
Oyun istemcisi ──(native köprü)──▶ Tarayıcı (local-scanner, C#)
                                        │  scanner-data/Sunucular/<sunucu>/market.sqlite
                                        ▼
                      Site (market-draft, Node.js) ──▶ http://127.0.0.1:4180
```

Site, tarayıcının yazdığı veritabanlarını `SOURCE_ROOT` klasöründen okur ve kendi okuma modellerini `DATA_ROOT/data` altında üretir. Tarayıcı ile site aynı bilgisayarda olabilir; uzak kurulum için `ingest-server.cjs` ve `config/uplink.example.json` kullanılır.

## Gereksinimler

- Windows 10/11 (tarayıcı için).
- **Node.js 22.5 veya üstü** (`node:sqlite` kullanılır). Başka bir paket gerekmez.
- **.NET 9 SDK** (tarayıcıyı derlemek için).
- **Visual Studio 2022+ Build Tools, "C++ ile masaüstü geliştirme" (x86)** (native köprüyü derlemek için).
- Metin2 istemcisi ve ilgili Gameforge hesabı.

## Hızlı başlangıç

### 1. Siteyi çalıştırmak

Oyun veri tablolarını `market-draft/data/definitions` içine koyduktan sonra (aşağıya bakın) **`Siteyi-Ac.cmd`** dosyasına çift tıklayın; site başlar ve tarayıcıda açılır. Komut satırından başlatmak için:

```powershell
powershell -ExecutionPolicy Bypass -File .\start-site.ps1
```

İlk çalıştırmada `start-site.ps1` statik dosyaları kendiliğinden derler (`market-draft/build-public-assets.cjs`). Site `http://127.0.0.1:4180/` adresinde açılır. Henüz tarama yapılmadıysa "bu sunucuya henüz veri gelmedi" görünür. Telefondan denemek için `start-site.ps1 -Telefon` (aynı ağdan erişim) kullanın.

### 2. Tarayıcıyı derlemek ve çalıştırmak

```powershell
cd local-scanner
powershell -ExecutionPolicy Bypass -File .\build.ps1      # .NET + native köprü
powershell -ExecutionPolicy Bypass -File .\test.ps1       # yerel kontroller
cd ..
.\Tarayiciyi-Ac.cmd
```

- `build.ps1`, `local-scanner/dist-cycle-v40/` klasörünü üretir (`Metin2Search.Local.exe`, sürümlü `MarketBridge.*.dll`, `MarketBridge.Host.exe`). EXE'yi köprü dosyalarından ayırmayın.
- `Tarayiciyi-Ac.cmd`, derleme yoksa önce `build.ps1`'i kendisi çalıştırır (ilk seferde birkaç dakika sürer; .NET 9 SDK ve Visual C++ x86 araçları gerekir). Derleme başarısız olursa nedenini pencerede yazar.
- Tarayıcı açılırken Windows bir **yönetici izni penceresi** gösterir (yükseltilmiş oyun istemcisine bağlanmak için). **Evet**'e basmadan tarayıcı penceresi görünmez; pencere görünmüyorsa görev çubuğunda yanıp sönen kalkan simgesine bakın.
- Oyuna elle girin, Ticaret haritasında olun ve Ticaret Camı'nı açık tutun. Başlat'a basınca tarama başlar; veriler `scanner-data/` altına yazılır.
- Karakter döngüsü, depo parolası ve diğer seçenekler tarayıcı penceresindedir. Depo parolası yalnızca Windows kullanıcı hesabınızla çözülebilen şifreli dosyada saklanır.

### Oyun veri tabloları

`market-draft/data/definitions/` ve `market-draft/assets/items/` klasörleri bilerek boştur; her birinde beklenen dosyaları anlatan bir `BENI-OKU.md` vardır. Bu içerik oyuna ait olduğu için depoya konmamıştır; kendi istemcinizden üretmeniz gerekir.

## Yapılandırma

Ortam değişkenleri (site):

| Değişken | Açıklama |
|---|---|
| `PORT`, `HOST` | Dinlenecek port ve adres (varsayılan 4180 / 127.0.0.1) |
| `SOURCE_ROOT` | Tarayıcı verisinin klasörü (`scanner-data/Sunucular`) |
| `DATA_ROOT` | Sitenin kendi verisini yazacağı kök |
| `SERVE_BUILD` | `1` ise birleştirilmiş/hash'li statik dosyalar sunulur |
| `READ_WORKERS` | Okuma işçisi sayısı |
| `SYNC_INTERVAL_MS` | Kaynak veritabanı eşitleme aralığı |
| `SITE_ORIGIN` | Sitemap ve SEO bağlantıları için kök adres |
| `IMPORT_PAUSED`, `ARCHIVE_PAUSED`, `BACKUP_PAUSED` | İlgili arka plan işlerini durdurur |

Gizli dosyalar `config/` altındadır ve **depoya konmaz**. Şablonlar: `local-archive.example.json`, `r2.example.json`, `uplink.example.json`. Bunları `.example` olmadan kopyalayıp doldurun.

## Testler

```powershell
cd market-draft
node check-similar.cjs        # tek kontrol
# tüm kontroller: market-draft içindeki check-*.cjs dosyalarını tek tek çalıştırın
cd ..\local-scanner
powershell -File .\test.ps1   # tarayıcı kendi kendine sınama (C#)
```

Bazı kontroller gerçek veriye veya çalışan siteye (`127.0.0.1:4180`) ihtiyaç duyar.

## Dizin yapısı

```
market-draft/        Site, API, okuma modelleri, arşiv/yedek, testler (check-*.cjs)
local-scanner/       Tarayıcı (C# WPF), testler (kendi kendine sınama), native köprü
  native/            C++ köprü ve istemciye gömülen Python 2.7 betikleri
  tools/             Yardımcı PowerShell betikleri
config/              Yapılandırma şablonları (gizli dosyalar buraya konur, depoya girmez)
start-site.ps1       Siteyi başlatır
start-scanner.ps1    Tarayıcıyı başlatır (Tarayiciyi-Ac.cmd bunu çağırır)
```

## Bilinen sınırlar

- Veritabanında yalnızca **istenen fiyat** vardır. Bir ilanın satıldığı bilinmez, yalnızca "artık görünmüyor" bilinir.
- Tarama aralığına göre listeler gecikmeli olabilir; artımlı tur görmediği ilanı kaldırmaz.
- FAKE bilgisi oyunun pazar için verdiği yanıta dayanır; belirsiz yanıt kesin FAKE kanıtı sayılmaz.
- Native köprü, belirli istemci derlemesine bağlıdır.

## Lisans

[MIT](LICENSE). Oyun içeriği, marka ve diğer üçüncü taraf materyali bu lisansın kapsamında değildir.
