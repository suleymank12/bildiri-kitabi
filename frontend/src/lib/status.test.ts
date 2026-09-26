import type { BookStatus, GenerationStage, TitleSource } from '../api/types';
import { isBusy, isEditable, stageLabel, statusLabel, statusTone, titleSourceWarning } from './status';

describe('status labels', () => {
  it.each<[BookStatus, string, string]>([
    ['Uploaded', 'Yüklendi', 'neutral'],
    ['Queued', 'Kuyrukta', 'accent'],
    ['Processing', 'Hazırlanıyor', 'accent'],
    ['Completed', 'Hazır', 'success'],
    ['Failed', 'Hata', 'danger'],
  ])('%s → %s', (status, label, tone) => {
    expect(statusLabel(status)).toBe(label);
    expect(statusTone(status)).toBe(tone);
  });

  it.each<[GenerationStage, string]>([
    ['Reading', 'Belgeler okunuyor'],
    ['Sanitizing', 'İletişim bilgileri temizleniyor'],
    ['Composing', 'Sayfa düzeni hazırlanıyor'],
    ['Rendering', 'PDF oluşturuluyor'],
    ['Verifying', 'Son kontroller yapılıyor'],
    ['Saving', 'Kaydediliyor'],
  ])('stage %s → %s', (stage, label) => {
    expect(stageLabel(stage)).toBe(label);
  });

  it.each<[TitleSource, string | undefined]>([
    ['TitleStyle', undefined],
    ['FirstBoldParagraph', 'Başlık ilk kalın paragraftan alındı, kontrol edin.'],
    ['FileName', 'Başlık bulunamadı, dosya adı kullanıldı. Kontrol edin.'],
  ])('title source %s → %s', (source, warning) => {
    expect(titleSourceWarning(source)).toBe(warning);
  });

  it('knows which states are busy and which can be edited', () => {
    expect((['Uploaded', 'Queued', 'Processing', 'Completed', 'Failed'] as const).filter(isBusy)).toEqual([
      'Queued',
      'Processing',
    ]);
    expect((['Uploaded', 'Queued', 'Processing', 'Completed', 'Failed'] as const).filter(isEditable)).toEqual(
      ['Uploaded', 'Failed'],
    );
  });
});
