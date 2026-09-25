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
  Reading: 'Bildiriler okunuyor',
  Sanitizing: 'İletişim bilgileri temizleniyor',
  Composing: 'İçindekiler hazırlanıyor',
  Rendering: 'Sayfalar dizgiye giriyor',
  Verifying: 'PDF denetleniyor',
  Saving: 'PDF kaydediliyor',
};

const titleSourceLabels: Record<TitleSource, string> = {
  TitleStyle: 'Başlık stilinden',
  FirstBoldParagraph: 'Kalın ilk paragraftan',
  FileName: 'Dosya adından',
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

export function titleSourceLabel(source: TitleSource): string {
  return titleSourceLabels[source];
}

/** A title taken from the file name is a guess; it is shown in the warning colour. */
export function titleSourceTone(source: TitleSource): Tone {
  return source === 'FileName' ? 'warning' : 'neutral';
}

/** True while the server owns the book: it is waiting in the queue or being generated. */
export function isBusy(status: BookStatus): boolean {
  return status === 'Queued' || status === 'Processing';
}

/** True while the paper order may change and generation may start. */
export function isEditable(status: BookStatus): boolean {
  return status === 'Uploaded' || status === 'Failed';
}
