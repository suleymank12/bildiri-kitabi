# Güvenlik notları ve sınırlamalar

← [README](../README.md)

<a id="guvenlik"></a>

## Güvenlik notları

- **Dosya doğrulaması:** uzantı `.docx` olmalı; içerik ZIP imzası taşımalı, OpenXML ile açılabilmeli ve ana parça türü Word belgesi olmalı (`.docm`, `.dotx` ve uzantısı değiştirilmiş PDF/ZIP reddedilir). Dosya başına 10 MB, toplam 60 MB sınırı uygulamada denetlenir; yükleme ucunun ve nginx'in istek boyutu sınırları da buna göre ayarlıdır. Boş veya metinsiz belge ve aynı içeriğin iki kez yüklenmesi reddedilir.
- **ZIP bombası koruması:** arşivdeki girdi sayısı ve toplam açılmış boyut sınırlıdır; mutlak veya `..` içeren girdi yolları reddedilir. Bildirilen boyuttan uzun akışlar da sınırda kesilir.
- **Dosya adları:** kullanıcının dosya adı hiçbir zaman yol olarak kullanılmaz; gösterim için yalnızca son parça, kontrol karakterleri atılarak tutulur.
- **Konteynerler:** API `chiseled-extra` imajında (kabuk ve paket yöneticisi yok) root olmayan `app` kullanıcısıyla, nginx `nginx-unprivileged` imajında root olmayan kullanıcıyla çalışır. Yalnızca 8080 ve 15672 portları, yalnızca `127.0.0.1` üzerinde yayımlanır.
- **Gizli bilgiler:** parolalar yalnızca git dışındaki `.env` dosyasındadır; depoda yalnızca yer tutucular içeren `.env.example` bulunur. API veritabanına `sa` ile değil, yalnızca bu veritabanının sahibi olan `bildiri_app` girişiyle bağlanır. RabbitMQ'da `guest` hesabı kullanılmaz, uygulama bu hesabı reddeder.
- **Rate limiting:** yükleme ve üretim uçlarında istemci başına sınır; `X-Forwarded-For` yalnızca tanımlı vekil ağından geldiğinde dikkate alınır.
- **HTTP başlıkları:** nginx arayüz için `Content-Security-Policy` (`default-src 'self'`, satır içi betik yok, `object-src 'none'`, `frame-ancestors 'none'`), `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY` gönderir. API yanıtları `default-src 'none'` CSP'si ve aynı başlıkları taşır. CORS yalnızca yapılandırılmış kaynaklara izin verir.
- **Kişisel veri:** loglara dosya içeriği, iletişim bilgisi veya kitap adı yazılmaz; loglar kitap kimliği ve hata kodu gibi teknik bilgilerle sınırlıdır. Kaynak belgelerin üst verisi PDF'e taşınmaz.
- **Hata yanıtları:** istemciye yığın izi veya iç ayrıntı verilmez; beklenmeyen hatalar `INTERNAL_ERROR` koduyla genel bir mesaj döner.

<a id="bilinen-eksikler"></a>

## Bilinen eksikler ve sınırlamalar

Case'in zorunlu maddelerinin tamamı karşılanmıştır (bkz. [Kabul kriterleri](../README.md#kabul-kriterleri)). Aşağıdakiler, case'te beklenmeyen veya bilinçli olarak kapsam dışında bırakılan konulardır; gerçek bir üretim ortamında ele alınması gerekenler ayrıca belirtilmiştir.

- **Kimlik doğrulama yok:** kullanıcı kaydı ve girişi kapsam dışıdır; uygulamaya erişebilen herkes tüm kitapları görebilir ve silebilir.
- **Word biçim sadakati sınırlıdır.** Desteklenmeyenler: görseller, çizimler ve grafikler (metin kutularının metni hariç atlanır); liste numaraları ve madde işaretleri (numaralandırma tanımları okunmaz, yalnızca metin basılır); asılı girinti (negatif ilk satır girintisi); dikey hücre birleştirme ve tablo kenarlık/gölgelendirme ayrıntıları; yazı rengi, vurgulama ve üstü çizili metin; sayfa yönü, sütunlar ve kaynak kenar boşlukları (kitap kendi A4 düzenini kullanır); son notlar ve yorumlar. Yazı tipi aileleri serif ve sans olmak üzere ikiye eşlenir. `keepNext` mümkün olduğunca uygulanır, bir sayfadan uzun bloklarda uygulanamaz.
- **Kaynak üst ve alt bilgileri kullanılmaz:** kitabın kendi üst bilgisi (kitap adı ve bildiri başlığı) ve sayfa numarası vardır.
- **PDF yer imleri (outline) yok:** gezinme İçindekiler bağlantıları ve arayüzdeki İçindekiler paneliyle yapılır.
- **Zaman aşımı:** süre aşıldığında kitap hemen `Failed` olur, ancak QuestPDF dizgisi iptal edilemediğinden başlamış olan CPU işi arka planda bitene kadar sürer ve sonucu atılır.
- **TLS yok:** Docker kurulumu yerel değerlendirme içindir ve düz HTTP ile `127.0.0.1` üzerinde çalışır; SQL Server bağlantısı şifrelidir ancak konteynerin kendi imzalı sertifikasına güvenir. Dışa açık bir kurulumda önüne TLS sonlandıran bir vekil ve güvenilir sertifika gerekir.
- **Tek örnek varsayımı:** dosya deposu yerel disk veya tek bir volume'dur, rate limiting sayaçları süreç içindedir; birden çok API örneği için paylaşılan depolama ve dağıtık sınırlama gerekir.
- **SQL Server ARM:** SQL Server imajı yalnızca amd64'tür; Apple Silicon'da Rosetta öykünmesiyle çalışır ve daha yavaştır.
- **Erişilebilir PDF:** üretilen PDF etiketli (tagged PDF) veya PDF/A uyumlu değildir.
