import type { BookStatus, GenerationStage, TitleSource } from '../api/types';
import {
  isBusy,
  isEditable,
  stageLabel,
  statusLabel,
  statusTone,
  titleSourceLabel,
  titleSourceTone,
} from './status';

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
    ['Reading', 'Bildiriler okunuyor'],
    ['Sanitizing', 'İletişim bilgileri temizleniyor'],
    ['Composing', 'İçindekiler hazırlanıyor'],
    ['Rendering', 'Sayfalar dizgiye giriyor'],
    ['Verifying', 'PDF denetleniyor'],
    ['Saving', 'PDF kaydediliyor'],
  ])('stage %s → %s', (stage, label) => {
    expect(stageLabel(stage)).toBe(label);
  });

  it.each<[TitleSource, string, string]>([
    ['TitleStyle', 'Başlık stilinden', 'neutral'],
    ['FirstBoldParagraph', 'Kalın ilk paragraftan', 'neutral'],
    ['FileName', 'Dosya adından', 'warning'],
  ])('title source %s → %s', (source, label, tone) => {
    expect(titleSourceLabel(source)).toBe(label);
    expect(titleSourceTone(source)).toBe(tone);
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
