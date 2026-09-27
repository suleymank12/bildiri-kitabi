# Bildiri Kitabı

Aynı etkinliğe ait 10 Word bildirisini (.docx) tek bir PDF e-kitapta birleştiren web uygulaması. Kitapta İçindekiler ve sayfa numaraları bulunur; e-posta adresleri ve telefon numaraları PDF'e taşınmaz. Kitap tarayıcıda görüntülenir ve indirilir.

Backend ASP.NET Core Web API (.NET 10), EF Core ve SQL Server; arayüz React ve TypeScript ile yazıldı.

## Kurulum

Depoyu klonlayın:

```sh
git clone https://github.com/suleymank12/bildiri-kitabi.git
cd bildiri-kitabi
```

### Docker olmadan (MSSQL bağlantısı)

Gereksinimler: .NET 10 SDK, Node.js 22.12+ ve SQL Server (LocalDB yeterli).

API, bağlantı dizesini `ConnectionStrings:Default` anahtarından okur. Varsayılan değer LocalDB'dir:

```text
Server=(localdb)\MSSQLLocalDB;Database=BildiriKitabi;Trusted_Connection=True;TrustServerCertificate=True
```

Başka bir sunucu kullanmak için bağlantı dizesini `ConnectionStrings__Default` ortam değişkeniyle verin. Veritabanı, API açılırken migration'larla kurulur. Migration'ları elle uygulamak isterseniz `backend` klasöründe şu komutu çalıştırın (EF araçları kurulu değilse önce `dotnet tool install --global dotnet-ef`):

```sh
dotnet ef database update --project src/BildiriKitabi.Infrastructure --startup-project src/BildiriKitabi.Api
```

Uygulamayı başlatmak için iki ayrı terminalde:

```sh
cd backend && dotnet run --project src/BildiriKitabi.Api   # API: http://localhost:5080
cd frontend && npm ci && npm run dev                       # arayüz: http://localhost:5173
```

http://localhost:5173 adresini açın ve 10 .docx dosyasını yükleyin. Bu modda kuyruk uygulamanın içinde çalışır, RabbitMQ gerekmez.

### Docker ile

Gereksinim: Docker Desktop. Bu yol SQL Server, RabbitMQ, API ve arayüzü tek komutla kurar.

1. `.env` dosyasını oluşturun. Betik güçlü, rastgele parolalar üretir:
   - Windows: `powershell -ExecutionPolicy Bypass -File scripts\env-olustur.ps1`
   - macOS / Linux: `sh scripts/env-olustur.sh`
2. Uygulamayı başlatın. İlk çalıştırma birkaç dakika sürer:
   ```sh
   docker compose up --build -d
   ```
3. http://localhost:8080 adresini açın ve 10 .docx dosyasını yükleyin.

Durdurmak için `docker compose down` kullanın. `docker compose down -v` veritabanı ve dosyalar dahil her şeyi siler.

## İşleme akışı

1. **Yükleme:** Kitap adı ve 10 dosya tek istekle gönderilir. Dosya sayısı, türü, boyutu ve aynı dosyanın iki kez yüklenmesi kontrol edilir. Hata varsa hiçbir kayıt oluşmaz.
2. **Başlık:** Her bildirinin başlığı sırasıyla şuralardan alınır:
   - Word'ün başlık stili
   - İlk paragraflardaki ortalı ve kalın paragraf
   - Dosya adı

   Son iki yolla bulunan başlıklar arayüzde "kontrol edin" uyarısıyla gösterilir.
3. **Sıra:** Bildiriler yükleme sırasıyla birleştirilir. Kullanıcı ikinci adımda sırayı Yukarı / Aşağı düğmeleriyle değiştirebilir. Sıra değiştiyse arayüz bunu belirtir.
4. **Oluşturma:** "Kitabı Oluştur" düğmesi kitabı kuyruğa ekler (`Queued`). Arka plan işleyici kitabı aldığında durum `Processing` olur.
5. **Üretim:** Bildiriler okunur, iletişim bilgileri temizlenir ve PDF QuestPDF ile oluşturulur. Oluşan PDF, iletişim bilgisi kalıp kalmadığına karşı bir kez daha taranır.
6. **Sonuç:** Kitap `Completed` olur. Bir hata olursa `Failed` olur ve hata mesajı kaydedilir.

## Katmanlar ve API

- **Core:** kitap ve bildiri modeli, iletişim bilgisi temizliği, başlık tespiti.
- **Infrastructure:** EF Core, Word okuma, PDF üretimi, dosya saklama, kuyruk.
- **Api:** controller'lar ve arka plan işleyici.

| Uç | Açıklama |
|---|---|
| `POST /api/books` | Kitap adı (`name`) ve 10 dosya (`files`) ile kitap oluşturur. |
| `GET /api/books` | Kitapları listeler. |
| `GET /api/books/{id}` | Kitabın durumunu, ilerlemesini ve bildirilerini döner. |
| `PUT /api/books/{id}/paper-order` | Bildiri sırasını değiştirir. |
| `POST /api/books/{id}/generate` | Kitap oluşturmayı başlatır. |
| `GET /api/books/{id}/pdf` | PDF'i döner. `?download=true` ile indirilir. |
| `DELETE /api/books/{id}` | Kitabı ve dosyalarını siler. |

Hatalar Türkçe mesaj ve hata kodu ile döner. İstek ve yanıt modellerinin tamamı, Development ortamında `/openapi/v1.json` adresindeki OpenAPI belgesindedir.

## Veri modeli

**Kitaplar**
- Kolonlar: `Id`, `Ad`, `Durum`, `Asama`, `IlerlemeYuzdesi`, `HataKodu`, `HataMesaji`, `PdfDepolamaAnahtari`, `SayfaSayisi` ve tarih alanları.
- `Durum` şu değerlerden birini alır: `Uploaded`, `Queued`, `Processing`, `Completed`, `Failed`.

**Bildiriler**
- Kolonlar: `Id`, `KitapId`, `SiraNo`, `OrijinalDosyaAdi`, `DepolamaAnahtari`, `Sha256`, `Baslik`, `BaslikKaynagi`, `BaslangicSayfasi`, `SilinenEpostaSayisi`, `SilinenTelefonSayisi`.
- `KitapId` yabancı anahtardır. Bir kitap silinince bildirileri de silinir.

**Veri bütünlüğü**
- Bir kitapta aynı sıra numarası ve aynı dosya iki kez bulunamaz.
- `Failed` durumundaki kitabın hata mesajı olmak zorundadır.
- `Completed` durumundaki kitabın PDF'i olmak zorundadır.

## Dosya saklama

- Dosyalar `wwwroot` dışında, yerel diskte saklanır. Yerelde `App_Data/storage`, Docker'da `/data/storage` klasörü kullanılır. Yüklenen Word dosyaları temizlenmemiş iletişim bilgisi içerdiği için `wwwroot` tercih edilmedi.
- Dosya yolları sistem tarafından oluşturulur:
  - `books/{kitapId}/sources/{bildiriId}.docx`
  - `books/{kitapId}/output/book.pdf`

  Kullanıcının verdiği dosya adı yol olarak kullanılmaz.
- Dosya önce geçici bir dosyaya yazılır, sonra asıl yerine taşınır. Böylece yarım kalan yazma eski dosyayı bozmaz.
- PDF yalnızca API üzerinden ve kitap hazır olduğunda indirilebilir. Kitap silinince dosyaları da silinir.

## İletişim bilgisi temizliği

Temizlik sunucuda yapılır. Kurallar:
- Her paragrafta yalnızca e-posta adresleri ve telefon numaraları silinir. Bunlarla birlikte "E-posta:", "Tel:" gibi etiketler ve aradaki ayırıcılar da silinir. Metnin geri kalanı ve biçimi değişmez.
- Telefon numarası sayılması için Türkiye numaralarında 10 hane gerekir. Yurt dışı numaralar `+` ile başlamalı ve 8–15 haneli olmalıdır. ORCID, ISBN, DOI, tarih ve tutar gibi numaralara dokunulmaz.
- PDF oluştuktan sonra metni bir kez daha taranır. İletişim bilgisi bulunursa PDF kullanıcıya verilmez ve kitap `Failed` olur.

Örnek: `E-posta: elif.kaya@example.org | Tel: 0500 000 00 01 | ORCID: 0000-0001-1000-0001` satırı PDF'te `ORCID: 0000-0001-1000-0001` olarak kalır.

Örnek testler:
- `Sample_contact_lines_are_cleaned_exactly`: Örnek dosyalardaki 13 iletişim satırının temizlenmiş hali, beklenen sonuçla birebir karşılaştırılır.
- `Non_contact_numbers_are_kept_verbatim`: ORCID, ISBN, DOI, tarih ve tutarlar değişmeden kalır.
- `Email_split_across_runs_is_removed_and_neighbouring_formatting_is_kept`: Word'ün iki parçaya böldüğü bir e-posta da silinir.
- `Generation_fails_with_contact_leak_code_when_the_rendered_pdf_still_contains_contact_values`: PDF'te iletişim bilgisi kalırsa kitap oluşturma başarısız sayılır.

Testleri çalıştırmak için `cd backend && dotnet test` yeterlidir. Örnek bildirilere dayanan testler için case ile gönderilen 10 dosyayı `testdata/bildiriler/` klasörüne kopyalayın. Dosyalar şirkete ait olduğu için depoda yoktur.

## Masaüstü ve mobil tasarım kararları

- **Akış:** Üç adımdan oluşur: dosyalar, sıra ve kontrol, oluşturma. Sayfa yenilense de akış kaldığı yerden devam eder.
- **Dosya listesi:** Masaüstünde tablo, telefonda kart olarak gösterilir.
- **Bekleme ekranı:** Sunucudaki gerçek aşamayı ve yüzdeyi gösterir.
- **Hata ekranı:** Anlaşılır bir mesaj gösterir. Kullanıcı "Tekrar dene" ya da "Sırayı düzenle" seçebilir.
- **Görüntüleyici:** Masaüstünde iki sayfa yan yana, yanında İçindekiler paneliyle açılır. Telefonda tek sayfa açılır; yakınlaştırma alttaki araç çubuğuyla veya çift dokunmayla yapılır.

## Kullanılan kütüphaneler

- **Open XML SDK** (MIT): Word dosyalarını okuma
- **QuestPDF** (Community License): PDF oluşturma ve İçindekiler sayfa numaraları. Bu lisans bireyler ve yıllık geliri 1 milyon USD'nin altındaki kuruluşlar için ücretsizdir.
- **PdfPig** (Apache-2.0): Oluşan PDF'i tarama
- **EF Core** (MIT): Veritabanı erişimi ve migration'lar
- **RabbitMQ.Client** (Apache-2.0 / MPL-2.0): Docker kurulumundaki kuyruk
- **React, React Router, TanStack Query, Tailwind CSS** (MIT): Arayüz
- **react-pdf / pdf.js** (MIT / Apache-2.0): PDF görüntüleyici

## Bilinen eksikler

- Kullanıcı girişi yok. Uygulamaya erişen herkes bütün kitapları görebilir ve silebilir.
- Word'deki görseller, liste numaraları ve yazı renkleri PDF'e taşınmaz.
- Kitap adına yazılan iletişim bilgisi temizlenmez. Temizlik kuralı yalnızca Word içeriği için geçerlidir.
- Yüklenip hiç oluşturulmayan kitapların dosyaları kendiliğinden silinmez. Kullanıcı bunları "Sil" düğmesiyle kaldırabilir.
- Docker kurulumu yerel kullanım içindir ve HTTPS içermez.

## Yapay zekâ kullanımı

Case'in analizini, mimariyi ve aşama planını Claude ile birlikte çıkardım. Kodun büyük bölümünü, her aşama için hazırladığım talimatlarla Claude Code yazdı. Her aşamanın sonunda kodu ve çıktıları inceledim, uygulamayı elle test ettim ve kapsam kararlarını verdim (ör. RabbitMQ ve Docker'ın eklenmesi, kitap adındaki iletişim bilgisinin temizlenmemesi). Sonuçları, PDF'i bağımsız olarak okuyan uçtan uca testler ve gerçek SQL Server ile RabbitMQ üzerinde çalışan entegrasyon testleriyle doğruladım.
