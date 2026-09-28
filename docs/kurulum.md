# Kurulum

← [README](../README.md)

<a id="hizli-baslangic"></a>

## Hızlı başlangıç (Docker)

Gereksinim: Docker Desktop veya Docker Engine ile Docker Compose v2. SQL Server konteyneri için en az 2 GB boş bellek gerekir.

1. Depoyu klonlayın ve klasörüne geçin:

   ```sh
   git clone https://github.com/suleymank12/bildiri-kitabi.git
   cd bildiri-kitabi
   ```

2. Ortam dosyasını oluşturun. Betik `.env.example`'ı temel alır ve üç parolayı kriptografik rastgele, SQL Server parola kurallarını sağlayan 24 karakterlik değerlerle doldurur; `.env` zaten varsa ona dokunmaz. Parolalar ekrana yazılmaz, `.env` dosyasında durur.

   Windows (PowerShell):

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\env-olustur.ps1
   ```

   `-ExecutionPolicy Bypass` yalnızca bu tek çalıştırmayı etkiler, sistem ayarını değiştirmez.

   macOS / Linux:

   ```sh
   sh scripts/env-olustur.sh
   ```

   Betik kullanmadan elle oluşturmak için:

   ```sh
   cp .env.example .env
   ```

   Windows PowerShell'de: `Copy-Item .env.example .env`

3. Yalnızca `.env`'i elle oluşturduysanız: `.env` dosyasını açın ve `<...>` ile gösterilen üç parolayı doldurun (`MSSQL_SA_PASSWORD`, `APP_DB_PASSWORD`, `RABBITMQ_PASSWORD`). SQL Server parolaları en az 8 karakter olmalı ve büyük harf, küçük harf, rakam, simge gruplarından en az üçünü içermelidir (örnek biçim: `Kitap-2026-Parola`). Parolalarda `'`, `"`, `;`, `$` ve ters tırnak kullanmayın.

4. Sistemi derleyip başlatın:

   ```sh
   docker compose up --build -d
   ```

   İlk çalıştırmada imajlar indirilir ve derlenir (birkaç dakika sürer). SQL Server hazır olunca `db-init` servisi veritabanını ve uygulama girişini oluşturup çıkar; API açılışta migration'ları uygular.

5. Durumu kontrol edin; `mssql` ve `rabbitmq` `healthy`, `api` ve `web` `running` olmalıdır:

   ```sh
   docker compose ps
   ```

   Sağlık raporu: http://localhost:8080/health (`"status":"Healthy"`, veritabanı ve RabbitMQ kontrolleriyle).

6. Uygulamayı açın: http://localhost:8080

7. RabbitMQ yönetim arayüzü: http://localhost:15672 — kullanıcı adı ve parola `.env` içindeki `RABBITMQ_USER` ve `RABBITMQ_PASSWORD`. Üretim kuyruğu `bildiri-kitabi.uretim`, ölü mektup kuyruğu `bildiri-kitabi.uretim.dlq` adıyla görünür.

Durdurma ve temizleme:

```sh
docker compose stop      # konteynerleri durdurur, veriler kalır
docker compose down      # konteynerleri ve ağı kaldırır, veriler (volume'lar) kalır
docker compose down -v   # veritabanı, kuyruk ve üretilen PDF'ler dahil her şeyi siler
```

Yeniden başlatma desteklenir: `stop` ya da `down` sonrasında `docker compose up -d` (veya `docker compose start`) sistemi veriler korunarak yeniden açar; önceki kitaplar ve PDF'leri yerinde kalır.

Notlar:

- Yalnızca web arayüzü (8080) ve RabbitMQ yönetim arayüzü (15672) yayımlanır, ikisi de yalnızca `127.0.0.1` üzerinde. API ve veritabanı dışarıya açılmaz; tarayıcı API'ye nginx üzerinden ulaşır.
- Bu kurulum yerel değerlendirme içindir ve TLS içermez (bkz. [Bilinen eksikler](sinirlamalar.md#bilinen-eksikler)).
- Apple Silicon (ARM): SQL Server imajı yalnızca `linux/amd64` olarak yayımlanır. Docker Desktop'ta "Use Rosetta for x86_64/amd64 emulation" seçeneği açıkken çalışır; diğer servisler ARM için yerel imaj kullanır.
- Uçtan uca testleri bu kuruluma karşı çalıştırmak için `.env` içinde `UPLOAD_RATE_LIMIT_PER_MINUTE=1000` ve `GENERATE_RATE_LIMIT_PER_MINUTE=1000` satırlarını açın, `docker compose up -d` ile API'yi yeniden oluşturun ve [Testler](testler.md) belgesindeki `npm run test:e2e:docker` komutunu kullanın.

<a id="docker-olmadan"></a>

## Docker olmadan çalıştırma

### Gereksinimler

- .NET SDK 10.0.100 veya üstü (`backend/global.json` ile sabitlenmiştir)
- Node.js 22.12 veya üstü (önerilen sürüm `.nvmrc` içinde: 24)
- SQL Server: LocalDB, SQL Server Express/Developer veya Docker'da çalışan bir SQL Server
- İsteğe bağlı: RabbitMQ 4 (varsayılan kuyruk sağlayıcısı süreç içi kuyruktur)

### MSSQL bağlantısı

API bağlantı dizesini `ConnectionStrings:Default` anahtarından okur. `appsettings.Development.json` içindeki varsayılan LocalDB'dir; başka bir sunucu için dize ortam değişkeniyle veya user-secrets ile verilir (parolalar `appsettings` dosyalarına yazılmaz).

| Sunucu | Örnek bağlantı dizesi |
|---|---|
| LocalDB (varsayılan) | `Server=(localdb)\MSSQLLocalDB;Database=BildiriKitabi;Trusted_Connection=True;TrustServerCertificate=True` |
| SQL Server Express | `Server=localhost\SQLEXPRESS;Database=BildiriKitabi;Trusted_Connection=True;TrustServerCertificate=True` |
| Docker'daki SQL Server | `Server=localhost,1433;Database=BildiriKitabi;User Id=sa;Password=<parola>;TrustServerCertificate=True` |

Docker'daki SQL Server için örnek bir konteyner:

```sh
docker run -d --name bildiri-sql -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=<parola>" -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

User-secrets ile (yalnızca Development ortamında okunur):

```sh
cd backend
dotnet user-secrets set "ConnectionStrings:Default" "<bağlantı dizesi>" --project src/BildiriKitabi.Api
```

Ortam değişkeniyle (her ortamda geçerlidir, user-secrets'ı da geçersiz kılar):

```sh
# bash
export ConnectionStrings__Default="<bağlantı dizesi>"
# PowerShell
$env:ConnectionStrings__Default = "<bağlantı dizesi>"
```

### Veritabanı şeması

- Development ortamında API açılışta bekleyen migration'ları uygular (`Database:ApplyMigrationsOnStartup`).
- EF Core araçlarıyla elle:

  ```sh
  dotnet tool install --global dotnet-ef
  cd backend
  dotnet ef database update --project src/BildiriKitabi.Infrastructure --startup-project src/BildiriKitabi.Api
  ```

- EF araçları olmadan: [`backend/database/schema.sql`](../backend/database/schema.sql) şemayı kuran tek migration'ı (`InitialCreate`) içeren, tekrar çalıştırılabilir (idempotent) bir betiktir; boş bir veritabanında SQL Server Management Studio, Azure Data Studio veya `sqlcmd -i` ile çalıştırılabilir.

### Backend ve frontend

```sh
# API: http://localhost:5080 (Development'ta API referansı: http://localhost:5080/scalar)
cd backend
dotnet run --project src/BildiriKitabi.Api

# Arayüz: http://localhost:5173 (/api ve /health istekleri 5080'deki API'ye yönlendirilir)
cd frontend
npm ci
npm run dev
```

### Kuyruk sağlayıcısı

`Queue:Provider` ayarı `InMemory` (varsayılan) veya `RabbitMq` olabilir. RabbitMQ için:

```sh
export Queue__Provider=RabbitMq
export RabbitMq__Host=localhost
export RabbitMq__Username=<kullanıcı>
export RabbitMq__Password=<parola>
```

RabbitMQ seçildiğinde kullanıcı adı ve parola zorunludur; `guest` hesabı reddedilir.
