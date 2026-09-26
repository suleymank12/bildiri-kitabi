import type { BookStatus, GenerationStage, TitleSource } from '../api/types';

export type Tone = 'neutral' | 'success' | 'warning' | 'danger' | 'accent';

const statusLabels: Record<BookStatus, string> = {
  Uploaded: 'Yüklendi',
  Queued: 'Kuyrukta',
  Processing: 'Hazırlanıyor',
  Completed: 'Hazır',
  Failed: 'Hata',
};

const statusTones: Record<BookStatus, Tone> = {
  Uploaded: 'neutral',
  Queued: 'accent',
  Processing: 'accent',
  Completed: 'success',
  Failed: 'danger',
};

const stageLabels: Record<GenerationStage, string> = {
  Reading: 'Belgeler okunuyor',
  Sanitizing: 'İletişim bilgileri temizleniyor',
  Composing: 'Sayfa düzeni hazırlanıyor',
  Rendering: 'PDF oluşturuluyor',
  Verifying: 'Son kontroller yapılıyor',
  Saving: 'Kaydediliyor',
};

export const QUEUED_LABEL = 'Sırada bekliyor';

/** The generation stages in the order the server runs them. */
export const GENERATION_STAGES: readonly GenerationStage[] = [
  'Reading',
  'Sanitizing',
  'Composing',
  'Rendering',
  'Verifying',
  'Saving',
];

// Only a title found by a fallback method needs a second look; one from the title style needs no note.
const titleSourceWarnings: Record<TitleSource, string | undefined> = {
  TitleStyle: undefined,
  FirstBoldParagraph: 'Başlık ilk kalın paragraftan alındı, kontrol edin.',
  FileName: 'Başlık bulunamadı, dosya adı kullanıldı. Kontrol edin.',
};

export function statusLabel(status: BookStatus): string {
  return statusLabels[status];
}

export function statusTone(status: BookStatus): Tone {
  return statusTones[status];
}

export function stageLabel(stage: GenerationStage): string {
  return stageLabels[stage];
}

/** The note asking the user to check a title found by a fallback method; undefined for the title style. */
export function titleSourceWarning(source: TitleSource): string | undefined {
  return titleSourceWarnings[source];
}

/** True while the server owns the book: it is waiting in the queue or being generated. */
export function isBusy(status: BookStatus): boolean {
  return status === 'Queued' || status === 'Processing';
}

/** True while the paper order may change and generation may start. */
export function isEditable(status: BookStatus): boolean {
  return status === 'Uploaded' || status === 'Failed';
}
