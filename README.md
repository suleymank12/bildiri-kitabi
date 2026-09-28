# Bildiri Kitabı

Proje 10 Word bildirisini (.docx) tek bir PDF e-kitapta birleştiren web uygulamasıdır. E-kitapta İçindekiler ve sayfa numaraları bulunur; e-posta adresleri ve telefon numaraları PDF'e taşınmaz. Kitap tarayıcıda görüntülenir ve indirilir.

Backend ASP.NET Core Web API (.NET 10), EF Core ve SQL Server; arayüz React ve TypeScript ile yazıldı.

## Kurulum

Depoyu klonlayın:

```sh
git clone https://github.com/suleymank12/bildiri-kitabi.git
cd bildiri-kitabi
```
### Docker olmadan (MSSQL bağlantısı)

**Gerekenler:** .NET 10 SDK, Node.js 22.12 veya üstü ve SQL Server. Visual Studio ile gelen SQL Server LocalDB yeterlidir.

1. **API'yi başlatın.** Depo klasöründe bir terminal açın ve `backend` klasörüne geçin:

```sh
   cd backend
```

   Veritabanı olarak LocalDB kullanıyorsanız doğrudan bir sonraki komuta geçin; API `(localdb)\MSSQLLocalDB` üzerindeki `BildiriKitabi` veritabanına kendisi bağlanır.

   Başka bir SQL Server kullanıyorsanız, API'ye hangi sunucuya bağlanacağını söyleyin. Bunun için aynı terminalde, API'yi başlatmadan önce aşağıdaki satırlardan size uyanı çalıştırın (PowerShell). Değer yalnızca bu terminal açık kaldığı sürece geçerlidir; hiçbir dosya değişmez.

   - Windows oturumunuzla bağlanıyorsanız (ör. SQL Server Express). `localhost\SQLEXPRESS` yerine SQL Server Management Studio'da bağlandığınız sunucu adını yazın:

```powershell
     $env:ConnectionStrings__Default = "Server=localhost\SQLEXPRESS;Database=BildiriKitabi;Trusted_Connection=True;TrustServerCertificate=True"
```

   - SQL Server kullanıcı adı ve parolasıyla bağlanıyorsanız. `sa` ve `<parola>` yerine kendi bilgilerinizi yazın:

```powershell
     $env:ConnectionStrings__Default = "Server=localhost;Database=BildiriKitabi;User Id=sa;Password=<parola>;TrustServerCertificate=True"
```

   Bağlandığınız hesabın veritabanı oluşturma yetkisi olmalıdır; `BildiriKitabi` veritabanını API kendisi oluşturur.

   Sonra API'yi başlatın:

```sh
   dotnet run --project src/BildiriKitabi.Api
```

   `Now listening on: http://localhost:5080` satırını bekleyin. API ilk açılışta veritabanını ve tabloları migration'larla kendisi kurar; elle bir komut çalıştırmanız gerekmez.

2. **Arayüzü başlatın.** Depo klasöründe ikinci bir terminal açın:

```sh
   cd frontend
   npm ci
   npm run dev
```

3. **Deneyin.** http://localhost:5173 adresini açın, "Yeni kitap" düğmesine basın, kitap adını yazın ve 10 .docx dosyasını seçin.

Bu modda kuyruk uygulamanın içinde çalışır, RabbitMQ gerekmez. Migration'ları API'yi başlatmadan uygulamak isterseniz `backend` klasöründe `dotnet tool install --global dotnet-ef` ve ardından `dotnet ef database update --project src/BildiriKitabi.Infrastructure --startup-project src/BildiriKitabi.Api` çalıştırın.


### Docker ile

**Gerekenler:** Docker Desktop (açık ve çalışır durumda olmalı). Bu yol SQL Server, RabbitMQ, API ve arayüzü tek komutla kurar; bilgisayarınızda .NET, Node.js veya SQL Server kurulu olması gerekmez.

1. **Parola dosyasını oluşturun.** Docker'daki SQL Server ve RabbitMQ'nun parolaları `bildiri-kitabi` klasöründeki `.env` dosyasında durur. Bu dosyayı bir kez oluşturmanız gerekir. `bildiri-kitabi` klasöründe bir terminal açın ve işletim sisteminize uyan komutu çalıştırın:

   - Windows (PowerShell):

```powershell
     powershell -ExecutionPolicy Bypass -File scripts\env-olustur.ps1
```

   - macOS / Linux:

```sh
     sh scripts/env-olustur.sh
```

   Üstteki komut (işletim sistemine göre hangisini seçtiyseniz), tahmin edilmesi zor parolalar üretir ve bunları `.env` dosyasına yazar. `.env` dosyası zaten varsa ona dokunmaz, içindeki parolalar değişmez.

2. **Uygulamayı başlatın.** Aynı terminalde:

```sh
   docker compose up --build -d
```

   İlk çalıştırma, gerekli dosyalar indirildiği için birkaç dakika sürer. Komut bittiğinde listede `Healthy` ve `Started` satırları görünür; `db-init` satırının `Exited` olması normaldir, veritabanını hazırlayıp kapanır.

3. **Deneyin.** http://localhost:8080 adresini açın ve kullanın.

Durdurmak için aynı klasörde `docker compose down` çalıştırın. `docker compose down -v` veritabanı ve yüklenen dosyalar dahil her şeyi siler.

## İşleme akışı

1. **Yükleme:** Kitaplarım'daki "Yeni kitap" düğmesi bir modal açar. Kitap adı ve 10 dosya tek istekle gönderilir. Dosya sayısı, türü, boyutu ve aynı dosyanın iki kez yüklenmesi kontrol edilir. Word olmayan bir dosya seçildiği anda işaretlenir. Hata varsa hiçbir kayıt oluşmaz.
2. **Başlık:** Her bildirinin başlığı sırasıyla şuralardan alınır:
   - Word'ün başlık stili
   - İlk paragraflardaki ortalı ve kalın paragraf
   - Dosya adı

   Son iki yolla bulunan başlıklar arayüzde "kontrol edin" uyarısıyla gösterilir. Kullanıcı her başlığı kalem düğmesiyle düzeltebilir. Başlık yalnızca yüklemede bulunur; kitap oluşturulurken kayıtlı başlık kullanılır.
3. **Sıra:** Bildiriler yükleme sırasıyla birleştirilir. Kullanıcı ikinci adımda sırayı Yukarı / Aşağı düğmeleriyle değiştirebilir. Sıra değiştiyse arayüz bunu belirtir.
4. **Oluşturma:** "Kitabı Oluştur" düğmesi kitabı kuyruğa ekler (`Queued`). Arka plan işleyici kitabı aldığında durum `Processing` olur.
5. **Üretim:** Bildiriler okunur, iletişim bilgileri temizlenir ve PDF QuestPDF ile oluşturulur. Oluşan PDF, iletişim bilgisi kalıp kalmadığına karşı bir kez daha taranır.
6. **Sonuç:** Kitap `Completed` olur. Bir hata olursa `Failed` olur ve hata mesajı kaydedilir.
7. **Düzenleme:** Kitaplarım'daki "Düzenle" ile kitap adı, sıra ve başlıklar değiştirilebilir. Oluşmuş bir kitapta değişiklik yapılırsa, onay alındıktan sonra PDF silinir, kitap `Uploaded` olur ve yeniden oluşturulur.
8. **Silme:** "Sil" oluşan kitabı Silinenler'e taşır. Oradan PDF'iyle birlikte geri alınabilir.

## Katmanlar ve API

- **Core:** kitap ve bildiri modeli, iletişim bilgisi temizliği, başlık tespiti.
- **Infrastructure:** EF Core, Word okuma, PDF üretimi, dosya saklama, kuyruk.
- **Api:** controller'lar ve arka plan işleyici.

| Uç | Açıklama |
|---|---|
| `POST /api/books` | Kitap adı (`name`) ve 10 dosya (`files`) ile kitap oluşturur. |
| `GET /api/books` | Kitapları listeler. |
| `GET /api/books/deleted` | Silinen kitapları listeler. |
| `GET /api/books/{uid}` | Kitabın durumunu, ilerlemesini ve bildirilerini döner. |
| `PUT /api/books/{uid}` | Kitap adını değiştirir. |
| `PUT /api/books/{uid}/paper-order` | Bildiri sırasını değiştirir. |
| `PUT /api/books/{uid}/papers/{paperUid}/title` | Bir bildirinin başlığını değiştirir. |
| `POST /api/books/{uid}/generate` | Kitap oluşturmayı başlatır. |
| `GET /api/books/{uid}/pdf` | PDF'i döner. `?download=true` ile indirilir. |
| `DELETE /api/books/{uid}` | Kitabı Silinenler'e taşır (pasife alır). |
| `POST /api/books/{uid}/restore` | Silinen kitabı geri alır. |

Adreslerde ve yanıtlarda yalnızca `uid` kullanılır; veritabanındaki sayısal `Id` dışarı çıkmaz. Hatalar Türkçe mesaj ve hata kodu ile döner. İstek ve yanıt modellerinin tamamı, Development ortamında `/openapi/v1.json` adresindeki OpenAPI belgesindedir.

## Veri modeli

Her tabloda iki kimlik vardır: `Id` (int, yalnızca veritabanı içinde) ve `Uid` (dışarıya açılan kimlik).

**Kitaplar**
- Kolonlar: `Id`, `Uid`, `Ad`, `Durum`, `Asama`, `IlerlemeYuzdesi`, `HataKodu`, `HataMesaji`, `PdfDepolamaAnahtari`, `SayfaSayisi`, `AktifMi`, `SilinmeZamani` ve tarih alanları.
- `Durum` şu değerlerden birini alır: `Uploaded`, `Queued`, `Processing`, `Completed`, `Failed`.

**Bildiriler**
- Kolonlar: `Id`, `Uid`, `KitapId`, `SiraNo`, `OrijinalDosyaAdi`, `DepolamaAnahtari`, `Sha256`, `Baslik`, `BaslikKaynagi` (kullanıcının düzelttiği başlıkta `Manual`), `BaslangicSayfasi`, `SilinenEpostaSayisi`, `SilinenTelefonSayisi`.
- `KitapId` yabancı anahtardır (silmede cascade).
- Silinen kitaplar (`AktifMi = 0`) tek bir EF Core global sorgu filtresiyle gizlenir.

**Veri bütünlüğü**
- Bir kitapta aynı sıra numarası ve aynı dosya iki kez bulunamaz.
- `Failed` durumundaki kitabın hata mesajı olmak zorundadır.
- `Completed` durumundaki kitabın PDF'i olmak zorundadır.

## Dosya saklama

- Dosyalar `wwwroot` dışında, yerel diskte saklanır. Yerelde `App_Data/storage`, Docker'da `/data/storage` klasörü kullanılır. Yüklenen Word dosyaları temizlenmemiş iletişim bilgisi içerdiği için `wwwroot` tercih edilmedi.
- Dosya yolları sistem tarafından oluşturulur:
  - `books/{kitapUid}/sources/{bildiriUid}.docx`
  - `books/{kitapUid}/output/book.pdf`

  Kullanıcının verdiği dosya adı yol olarak kullanılmaz.
- Dosya önce geçici bir dosyaya yazılır, sonra asıl yerine taşınır. Böylece yarım kalan yazma eski dosyayı bozmaz.
- PDF yalnızca API üzerinden ve kitap hazır olduğunda indirilebilir. Silinen kitabın dosyaları diskte kalır, böylece kitap geri alınabilir.

## İletişim bilgisi temizliği

Temizlik sunucuda yapılır. Kurallar:
- Her paragrafta yalnızca e-posta adresleri ve telefon numaraları silinir. Bunlarla birlikte "E-posta:", "Tel:" gibi etiketler ve aradaki ayırıcılar da silinir. Metnin geri kalanı ve biçimi değişmez.
- Telefon numarası sayılması için Türkiye numaralarında 10 hane gerekir. Yurt dışı numaralar `+` ile başlamalı ve 8–15 haneli olmalıdır. ORCID, ISBN, DOI, tarih ve tutar gibi numaralara dokunulmaz.
- PDF oluştuktan sonra metni bir kez daha taranır. İletişim bilgisi bulunursa PDF kullanıcıya verilmez ve kitap `Failed` olur.

Örnek: `E-posta: suleyman.krmn@example.org | Tel: 0500 000 00 01 | ORCID: 0000-0001-1000-0001` satırı PDF'te `ORCID: 0000-0001-1000-0001` olarak kalır.

Örnek testler:
- `Sample_contact_lines_are_cleaned_exactly`: Örnek dosyalardaki 13 iletişim satırının temizlenmiş hali, beklenen sonuçla birebir karşılaştırılır.
- `Non_contact_numbers_are_kept_verbatim`: ORCID, ISBN, DOI, tarih ve tutarlar değişmeden kalır.
- `Email_split_across_runs_is_removed_and_neighbouring_formatting_is_kept`: Word'ün iki parçaya böldüğü bir e-posta da silinir.
- `Generation_fails_with_contact_leak_code_when_the_rendered_pdf_still_contains_contact_values`: PDF'te iletişim bilgisi kalırsa kitap oluşturma başarısız sayılır.

**Testleri çalıştırmak:** `backend` klasöründe `dotnet test` komutunu çalıştırın. Testlerin bir kısmı case ile gönderilen 10 örnek bildiriyi kullanır; bu dosyalar şirkete ait olduğu için depoda yoktur. O testleri de çalıştırmak için 10 dosyayı `testdata/bildiriler/` klasörüne kopyalayın. Kopyalamazsanız bu testler nedeni yazılarak atlanır, diğerleri normal çalışır. SQL Server ve RabbitMQ kullanan testler için Docker Desktop açık olmalıdır.

## Masaüstü ve mobil tasarım kararları

- **Ana sayfa Kitaplarım sayfasıdır.** "Yeni kitap" masaüstünde ortada bir modal, telefonda tam ekran açılır. Yükleme sürerken kapanmaz, doldurulmuşsa kapatmadan önce onay ister.
- **Akış:** Üç adımdan oluşur: dosyalar, sıra ve kontrol, oluşturma. Sayfa yenilense de akış kaldığı yerden devam eder.
- **Düzenle ve Silinenler:** Her kitap satırında "Düzenle" ve "Sil" vardır. Düzenle sayfasında ad, sıra ve başlıklar değişir. Başlık yerinde düzenlenir. Silinen kitaplar Silinenler sayfasından geri alınabilir.
- **Dosya listesi:** Masaüstünde tablo, telefonda kart olarak gösterilir.
- **Bekleme ekranı:** Sunucudaki gerçek aşamayı ve yüzdeyi gösterir.
- **Hata ekranı:** Kullanıcının anlayabileceği bir mesaj gösterir. Kullanıcı "Tekrar dene" ya da "Sırayı düzenle" seçebilir.
- **Görüntüleyici:** Masaüstünde iki sayfa yan yana, yanında İçindekiler paneliyle açılır; üstteki araç çubuğundan tek sayfa görünümüne geçilebilir. Telefonda tek sayfa açılır; yakınlaştırma alttaki araç çubuğuyla veya çift dokunmayla yapılır.

## Kullanılan kütüphaneler

- **Open XML SDK** (MIT): Word dosyalarını okuma
- **QuestPDF** (Community License): PDF oluşturma ve İçindekiler sayfa numaraları
- **PdfPig** (Apache-2.0): Oluşan PDF'i tarama
- **EF Core** (MIT): Veritabanı erişimi ve migration'lar
- **RabbitMQ.Client** (Apache-2.0 / MPL-2.0): Docker kurulumundaki kuyruk
- **React, React Router, TanStack Query, Tailwind CSS** (MIT): Arayüz
- **react-pdf / pdf.js** (MIT / Apache-2.0): PDF görüntüleyici

## Bilinen eksikler

Case'te istenen maddelerin hepsi tamamlandı. Bilinçli olarak kapsam dışında bırakılanlar:

- Case'te beklenmediği için kullanıcı girişi, Word'deki görsellerin ve renklerin PDF'e taşınması ve canlı ortam kurulumu (HTTPS) yapılmadı.
- İletişim bilgisi temizliği Word içeriği için yapılır; kullanıcının kendi yazdığı kitap adına dokunulmaz.

## Yapay zekâ kullanımı

Projenin mimari planlaması ve geliştirme sürecinde Claude Code'u yardımcı araç olarak kullandım. Uygulamanın kapsamı, teknik mimarisi ve kullanılacak teknolojilere ilişkin kararları ihtiyaçları değerlendirerek kendim belirledim. Geliştirme sırasında yapay zekâ tarafından oluşturulan kodları her aşamada inceleyip onayladım; uygun bulmadığım noktalarda hatalı yaklaşımı belirterek nasıl düzeltilmesi gerektiğini açıkladım ve kodun bu doğrultuda yeniden düzenlenmesini sağladım. Son aşamada uygulamayı manuel olarak test ederek ortaya çıkan sonucu kontrol ettim.
