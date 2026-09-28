# Word → PDF işleme

← [README](../README.md)

## Okunan öğeler

`OpenXmlDocxReader`, belge gövdesini sırayla dolaşır ve kütüphaneden bağımsız bir belge modeli üretir: paragraflar (hizalama, önce/sonra boşluk, satır aralığı, girintiler, `keepNext`), run'lar (kalın, italik, altı çizili, punto, serif/sans, üst/alt simge, köprü), sekme ve satır sonları, açık sayfa sonları (`w:br w:type="page"` ve `w:pageBreakBefore`), basit tablolar (yatay hücre birleştirme dahil), dipnotlar (bildirinin sonunda numaralı), metin kutuları, içerik denetimleri (`w:sdt`) ve izlenen değişikliklerde eklenen metin. Silinen metin (`w:del`) ve gizli metin (`w:vanish`) atlanır. Tanınmayan öğeler üretimi durdurmaz.

## Stil çözümleme

Biçim katman katman hesaplanır: belge varsayılanları → paragraf stili (`basedOn` zinciriyle) → karakter stili → doğrudan run biçimi. İki ayrıntı önemlidir:

- Başlık stili stil kimliğiyle değil, çözümlenen stil adıyla tanınır. Örnek bildirilerde stil kimliği yerelleştirilmiştir (`KonuBal`), adı ise `Title`'dır.
- `b` ve `i` gibi aç/kapa özellikleri değerleriyle okunur. Örnek bildirilerde kalın olmayan run'larda `<w:b w:val="0"/>` bulunur; yalnızca öğenin varlığına bakmak her şeyi kalın yapardı.

## Başlık tespiti

Sırasıyla: (1) çözümlenen stil adı `Title` veya `heading 1` olan ilk boş olmayan paragraf; (2) ilk beş paragraf içinde ortalı ve tamamı kalın ilk paragraf; (3) dosya adı (uzantı ve baştaki `01_` gibi sıra öneki atılır, `_` boşluğa çevrilir). Başlık metni olduğu gibi kullanılır; büyük/küçük harf dönüştürmesi yapılmaz. Kaynağı kaydedilir; başlık yedek yöntemle (kalın ilk paragraf veya dosya adı) bulunduysa arayüz o bildirinin altında kontrol edilmesini isteyen bir uyarı gösterir. Belge özelliklerindeki (`docProps/core.xml`) başlık kullanılmaz: örnek dosyalarda küçük harfe çevrilmiş ve bozuk karakterler içerir.

Başlık yalnızca yüklemede bir kez bulunur ve `Bildiriler.Baslik` kolonuna kaydedilir; kitap oluşturulurken yeniden tespit edilmez, kayıtlı başlık kullanılır. Kullanıcı başlığı değiştirebilir (`PUT /api/books/{uid}/papers/{paperUid}/title`; boş olamaz, en fazla 500 karakter, e-posta veya telefon içeremez, PDF yazı tipinde olmayan karakter içeremez). Bu durumda kaynak `Manual` olur ve yeni başlık İçindekiler'de, üst bilgide ve bildirinin ilk sayfasında görünür: ilk sayfada, yüklemede başlığın alındığı paragrafın metni (hizası, boşlukları ve ilk parçasının biçimi korunarak) yeni başlıkla değiştirilir. Başlık dosya adından geldiyse belgede değiştirilecek paragraf yoktur; yeni başlık İçindekiler'de ve üst bilgide görünür.

## Kitap düzeni ve sayfa numaraları

- A4 sayfa, 2,5 cm kenar boşluğu; gövde yazı tipi kaynaktaki puntoyla Liberation Serif, serif olmayan yazı tipleri Liberation Sans'a eşlenir. Heceleme kapalıdır.
- Sayfa 1 kapaktır (kitap adı, "Bildiri Kitabı", bildiri sayısı, `tr-TR` biçiminde oluşturulma tarihi); numarası basılmaz.
- İçindekiler 2. sayfadan başlar, gerekirse birden çok sayfaya taşar. Her satırda solda sıra numarası, ortada başlık, sağda başlığın satırlarının dikey ortasına hizalı başlangıç sayfası bulunur (tek satırlık başlıkta o satırla aynı hizada; sıra numarası ilk satırla hizalıdır); girdiler arasında ince açık gri bir çizgi vardır. Satır tıklanabilir bir iç bağlantıdır.
- Her bildiri yeni sayfada başlar ve QuestPDF'te adlandırılmış bir bölüm (section) olarak dizilir. İçindekiler'deki numara, dizgi motorunun o bölümün ilk sayfası için verdiği numaradır (`BeginPageNumberOfSection`); elle hesap veya tahmin yoktur, bu yüzden İçindekiler'in kendisi uzasa bile numaralar doğru kalır.
- Basılı sayfa numarası fiziksel sayfa sırasıdır (kapak 1 sayılır). Böylece İçindekiler'deki numara PDF görüntüleyicinin sayfa kutusundaki numarayla aynıdır; "sayfa 11" yazan başlık görüntüleyicide 11. sayfadadır. Roma rakamlı ön sayfalar bu eşleşmeyi bozacağı için seçilmedi.
- İçerik sayfalarının üst bilgisi basılı kitaplardaki gibidir: çift numaralı (sol) sayfalarda sola hizalı kitap adı, tek numaralı (sağ) sayfalarda sağa hizalı bildiri başlığı. Metin hiçbir zaman kesilmez ve üç nokta kullanılmaz: tek satıra sığana kadar 9 → 8,5 → 8 → 7,5 → 7 pt küçültülür, 7 pt'de de sığmazsa iki satıra kırılır. Genişlik QuestPDF'in kendi ölçümüyle (gömülü yazı tiplerinin gerçek glif genişlikleri, yedek yazı tipleri dahil) bulunur. Alt bilgide ortada sayfa numarası vardır.
- İçindekiler'de sayfa numarasının dikey merkezi, girdinin başlık satırlarının dikey merkeziyle aynıdır (testte en fazla 1 pt fark). Numara başlıktan sonra çizildiği için PDF'ten metin kopyalanırken veya aranırken her girdide önce başlık, sonra sayfa numarası gelir (pdf.js gibi çizim sırasını izleyen okuyucularda; testte de bu sırayla okunur).
- PDF üst verisi (başlık, oluşturan) uygulama tarafından yazılır; kaynak belgelerin üst verisi (başlık, yazar, son değiştiren) PDF'e taşınmaz.

## Yazı tipleri ve desteklenmeyen karakterler

Yazı tipleri uygulamaya gömülüdür, sistem yazı tipleri kullanılmaz; bu yüzden çıktı Windows'ta ve Linux konteynerinde aynıdır. Zincir: Liberation Serif/Sans (Times New Roman ve Arial ile metrik uyumlu, Türkçe karakterler tam) → eksik karakterler için DejaVu Serif/Sans (ör. `≤`, `≥`, `±`, `µ`, Yunan harfleri). Hiçbir yazı tipinde bulunmayan bir karakter sessizce boş kutu olarak basılmaz: yükleme sırasında ilgili dosya (`FILE_UNSUPPORTED_CHARACTER`) veya kitap adı (`BOOK_NAME_UNSUPPORTED_CHARACTER`) karakter ve konumuyla reddedilir. Denetim, yazı tiplerinin `cmap` tablolarından okunan karakter kapsamıyla yapılır.
