# İletişim bilgisi temizliği

← [README](../README.md)

## Yaklaşım

`ContactInfoSanitizer` (Core) her paragrafta çalışır ve yalnızca e-posta ve telefon **değerlerini** ve bu değerlere bağlı **etiketleri ve ayırıcıları** siler; paragrafın geri kalanına ve biçimine (kalın, italik) dokunmaz.

- Run metinleri birleştirilerek aranır; iki run'a bölünmüş bir e-posta da bulunur ve silme her run'a ayrı uygulanır.
- **E-posta:** standart adresler (Unicode harfler, `+`, alt alan adları, büyük harf) ve gizlenmiş biçimler (`ad [at] alan.edu.tr`, `ad(at)alan.edu.tr`, `ad [at] alan [dot] edu [dot] tr`). `mailto:` ve `tel:` köprülerinin hedefi de düşürülür.
- **Telefon:** rakamlar ve `boşluk . - ( )` ayırıcılarından oluşan, `+` ile başlayabilen adaylar; iki yanında başka rakam grubu olmamalıdır. Aday rakamlara indirgenir ve doğrulanır: Türkiye numarası için baştaki `+90`, `90` veya `0` atıldıktan sonra tam 10 hane ve ilk hane 2, 3, 4, 5 veya 8 (sabit hat, mobil, 850); uluslararası numara için `+` ile başlayan 8–15 hane.
- **İstisnalar:** `ORCID`, `ISBN`, `ISSN`, `DOI` etiketinden sonra gelen diziler, 16 haneli dörtlü gruplar, tarihler (`25.09.2026`), yıl aralıkları (`2019-2023`), ondalık ve binlik ayraçlı sayılar, IBAN parçaları korunur.
- **Etiket ve ayırıcı temizliği:** silinen değerin hemen yanındaki etiketler (`E-posta`, `Email`, `Mail`, `Tel`, `Tel.No`, `Telefon`, `GSM`, `Cep`, `Cep telefonu`, `Mobile`, `Phone`, `Faks`, `İrtibat`, `İletişim`, `Contact` vb.; büyük/küçük harf duyarsız, `tr-TR` kültürüyle) ve artık ayırıcılar (`|`, `/`, `;`, `,`, ` - `, `–`, `—`) silinir, çift boşluklar tekleştirilir. Silinen bölgeden uzak ayırıcılara (ör. bir DOI içindeki `/`) dokunulmaz.
- Temizlik sonunda tamamen boşalan paragraf kaldırılır; kaynakta zaten boş olan paragraflar (ör. sayfa sonu paragrafı) korunur.

Etiket ve ayırıcıların da silinmesi bu projenin yorumudur. Yalnızca değer silinseydi PDF'te `E-posta:  | Tel:  | ORCID: …` gibi anlamsız kalıntılar ya da yalnızca "Cep:" yazan satırlar kalırdı. Kural "iletişim bilgisi görünmemeli, diğer bilgiler aynen korunmalı" olduğundan, değere ait olan etiket de iletişim bilgisinin parçası sayıldı; iletişimle ilgisi olmayan her şey (ORCID, kurum adı, metin) aynen kalır.

## Test verisinde önce ve sonra

Aşağıdaki değerler örnek bildirilerdeki kurgusal verilerdir (`example.org` adresleri ve `0500 000 …` numaraları). Örnek bildiriler şirkete ait olduğu için depoda yoktur; testler onları `testdata/bildiriler/` klasöründe arar, bulamazsa ilgili testler nedeni yazılarak atlanır.

| Bildiri | Kaynaktaki satır | PDF'teki sonuç |
|---|---|---|
| 01 | `E-posta: elif.kaya@example.org \| Tel: 0500 000 00 01 \| ORCID: 0000-0001-1000-0001` | `ORCID: 0000-0001-1000-0001` |
| 02 (1) | `Email: mert.demir@example.org \| Telefon: +90 (500) 000 00 02 \| ORCID: 0000-0001-1000-0002` | `ORCID: 0000-0001-1000-0002` |
| 02 (2) | `E-posta derya.akin@example.org / GSM 0 (500) 000 00 12` | *(paragraf kaldırılır)* |
| 03 | `İletişim: selin.arslan@example.org - 0500-000-00-03 - ORCID 0000-0001-1000-0003` | `ORCID 0000-0001-1000-0003` |
| 04 (1) | `kerem.celik@example.org \| Cep: +90 500 000 00 04` | *(paragraf kaldırılır)* |
| 04 (2) | `zeynep.sen@example.org \| 0500 000 10 04` | *(paragraf kaldırılır)* |
| 05 | `Eposta: burcu.ozturk@example.org; Telefon: (0500) 000 00 05` | *(paragraf kaldırılır)* |
| 06 | `E-mail onur.sahin@example.org \| Mobile +90-500-000-00-06` | *(paragraf kaldırılır)* |
| 07 (1) | `nazli.er@example.org / Tel.No: 0 500 000 00 07` | *(paragraf kaldırılır)* |
| 07 (2) | `emre.topal@example.org / GSM: +90 (500) 000-10-07` | *(paragraf kaldırılır)* |
| 08 | `Mail: ipek.yalcin@example.org \| İrtibat: 0500.000.00.08` | *(paragraf kaldırılır)* |
| 09 | `tolga.gunes@example.org \| Telefon 0500 000 00 09` | *(paragraf kaldırılır)* |
| 10 | `E-posta: asli.cetin@example.org \| Cep telefonu: +90 500 000 00 10` | *(paragraf kaldırılır)* |

Toplam 13 e-posta ve 13 telefon silinir, 3 ORCID korunur; bu sayılar kitap sayfasında bildiri bazında gösterilir.

## Kitap adındaki iletişim bilgisi

Kitap adı kullanıcının kendi girdisidir ve kapakta, üst bilgide ve PDF üst verisinde aynen kullanılır; içinde bir e-posta veya telefon olsa bile reddedilmez ve değiştirilmez. Temizlik kuralı Word içeriği içindir. Sızıntı taraması bu değerleri (telefon rakamlarına, e-posta küçük harfe indirgenerek) izinli sayar; bildirilerden gelen başka bir değer yine sızıntıdır.

## Üretim sonrası sızıntı taraması

Temizliğe ek bir güvence olarak, üretilen PDF'in metni PdfPig ile çıkarılır ve aynı e-posta ve telefon desenleriyle taranır. Eşleşme varsa PDF yayımlanmaz ve kitap `Failed` (`CONTACT_LEAK_DETECTED`) olur. Kaynak belgelerin üst verisinin (ör. `lastModifiedBy` alanındaki numara) PDF'e taşınmadığı ayrıca test edilir.

## Örnek testler

Test dosyaları: [`ContactInfoSanitizerTests.cs`](../backend/tests/BildiriKitabi.UnitTests/Sanitization/ContactInfoSanitizerTests.cs), [`BookEndToEndTests.cs`](../backend/tests/BildiriKitabi.IntegrationTests/Books/BookEndToEndTests.cs), [`RendererAndLeakScannerTests.cs`](../backend/tests/BildiriKitabi.IntegrationTests/Pdf/RendererAndLeakScannerTests.cs), [`BookNameContactTests.cs`](../backend/tests/BildiriKitabi.IntegrationTests/Pdf/BookNameContactTests.cs).

- `Sample_contact_lines_are_cleaned_exactly` — yukarıdaki 13 satır gerçek .docx dosyalarından okunur ve beklenen sonuçla birebir karşılaştırılır.
- `All_sample_papers_lose_thirteen_emails_and_thirteen_phones_and_keep_three_orcids`
- `Phone_numbers_are_removed` — `0312 555 12 34`, `+90 312 555 12 34`, `0850 222 00 00`, `+44 20 7946 0958`, `+1 (202) 555-0147` vb.
- `Email_addresses_are_removed` — `ad.soyad+kongre@mail.univ.edu.tr`, büyük harfli, Türkçe karakterli ve gizlenmiş biçimler.
- `Non_contact_numbers_are_kept_verbatim` — ORCID, ISBN, DOI, yıl aralığı, tarih, yüzde, binlik ayraçlı tutar, `p<0.05`, IBAN.
- `Email_split_across_runs_is_removed_and_neighbouring_formatting_is_kept`
- `Book_contains_no_email_addresses_or_turkish_phone_numbers` — örnek kitabın PDF metni üzerinde.
- `Generation_fails_with_contact_leak_code_when_the_rendered_pdf_still_contains_contact_values`
