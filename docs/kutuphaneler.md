# Kullanılan kütüphaneler ve lisanslar

← [README](../README.md)

## Backend

| Kütüphane | Amaç | Lisans |
|---|---|---|
| ASP.NET Core, EF Core 10 (`Microsoft.EntityFrameworkCore.SqlServer`) | Web API, veri erişimi, migration | MIT |
| DocumentFormat.OpenXml | .docx okuma | MIT |
| QuestPDF | PDF dizgisi | QuestPDF Community License |
| UglyToad.PdfPig | Üretilen PDF'ten metin çıkarma (sızıntı taraması, testler) | Apache-2.0 |
| RabbitMQ.Client | RabbitMQ kuyruk sağlayıcısı | Apache-2.0 / MPL-2.0 |
| Scalar.AspNetCore | Development ortamında API referansı | MIT |
| xUnit v3, Shouldly | Test çatısı ve doğrulamalar | Apache-2.0, BSD-3-Clause |
| Testcontainers (MsSql, RabbitMq) | Testlerde geçici SQL Server ve RabbitMQ | MIT |

## Frontend

| Kütüphane | Amaç | Lisans |
|---|---|---|
| React 19, React Router | Arayüz ve yönlendirme | MIT |
| Vite, TypeScript | Derleme, tip denetimi | MIT, Apache-2.0 |
| Tailwind CSS v4 | Stil | MIT |
| TanStack Query, openapi-fetch, openapi-typescript | Sunucu durumu, OpenAPI şemasından tipli istemci | MIT |
| react-pdf, pdfjs-dist | PDF görüntüleyici | MIT, Apache-2.0 |
| Phosphor Icons | Simgeler | MIT |
| Vitest, Testing Library, MSW, jsdom | Birim ve bileşen testleri | MIT |
| Playwright, axe-core | E2E ve erişilebilirlik taraması | Apache-2.0, MPL-2.0 |
| ESLint, typescript-eslint, eslint-plugin-jsx-a11y, Prettier | Kod denetimi ve biçimlendirme | MIT |

## Yazı tipleri

| Yazı tipi | Kullanım | Lisans |
|---|---|---|
| Liberation Serif, Liberation Sans | PDF gövdesi ve başlıkları (gömülü) | SIL OFL 1.1 ([`OFL.txt`](../backend/src/BildiriKitabi.Infrastructure/Fonts/OFL.txt)) |
| DejaVu Serif, DejaVu Sans | PDF'te eksik karakterler için yedek | Bitstream Vera / Arev yazı tipi lisansı, serbest ([`DejaVu-LICENSE.txt`](../backend/src/BildiriKitabi.Infrastructure/Fonts/DejaVu-LICENSE.txt)) |
| Source Serif 4 | Arayüz başlıkları | SIL OFL 1.1 |
| IBM Plex Sans | Arayüz metni | SIL OFL 1.1 |

## Konteyner imajları

SQL Server 2022 (Developer sürümü; Microsoft lisansı, üretim dışı kullanım içindir), RabbitMQ 4 (MPL-2.0), nginx-unprivileged (BSD-2-Clause), .NET ASP.NET çalışma imajı (MIT).

## Notlar

- **QuestPDF lisansı:** Community lisansı yıllık brüt geliri 1 milyon USD'nin altındaki kuruluşlar, bireyler ve açık kaynak projeler için ücretsizdir; bu eşiğin üstündeki bir kuruluşta kullanım için Professional veya Enterprise lisans gerekir. Lisans gerektirmeyen bir alternatif PDFsharp/MigraDoc'tur (MIT); dizgi `QuestPdfBookRenderer` sınıfında toplandığından değiştirilebilir, ancak İçindekiler sayfa numaraları için bölüm numarası özelliğinin karşılığı yazılmalıdır.
- **ESLint 9:** ESLint 10 yayımlanmış olsa da erişilebilirlik kurallarını sağlayan `eslint-plugin-jsx-a11y` henüz ESLint 10'u desteklemediği için 9.x sürümünde kalındı.
- **FluentAssertions** 8. sürümle ticari lisansa geçtiği için kullanılmadı; yerine Shouldly tercih edildi.
