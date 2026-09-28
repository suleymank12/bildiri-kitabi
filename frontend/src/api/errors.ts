import { BOOK_NAME_MAX, BOOK_NAME_MIN, MAX_FILE_MB, MAX_TOTAL_MB, REQUIRED_PAPER_COUNT } from '../lib/limits';
import type { Problem, ProblemItem } from './types';

export const NETWORK_MESSAGE = 'Sunucuya ulaşılamıyor. Bağlantınızı kontrol edip tekrar deneyin.';
export const GENERIC_MESSAGE = 'Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.';

/** A failed API call: an HTTP error with its ProblemDetails, or a network failure (status 0). */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: Problem | undefined;
  readonly retryAfterSeconds: number | undefined;

  constructor(status: number, problem?: Problem, retryAfterSeconds?: number) {
    super(problem?.detail ?? (status === 0 ? NETWORK_MESSAGE : `HTTP ${status}`));
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
    this.retryAfterSeconds = retryAfterSeconds;
  }

  get code(): string | undefined {
    return this.problem?.code;
  }

  get isNetworkError(): boolean {
    return this.status === 0;
  }
}

/**
 * User-facing Turkish text for every code the API returns. `null` means the server's own text is the better
 * message because it names a file or a character.
 */
export const errorMessages = {
  VALIDATION_FAILED: 'Yüklemede bazı sorunlar bulundu. Ayrıntılar ilgili alanlarda gösteriliyor.',
  REQUEST_INVALID: 'İstek geçersiz. Sayfayı yenileyip tekrar deneyin.',
  REQUEST_TOO_LARGE: 'Seçilen dosyaların toplam boyutu izin verilen sınırı aşıyor.',
  UNSUPPORTED_MEDIA_TYPE: 'İstek biçimi desteklenmiyor. Sayfayı yenileyip tekrar deneyin.',
  NOT_FOUND: 'İstenen kaynak bulunamadı.',
  METHOD_NOT_ALLOWED: 'Bu işlem desteklenmiyor.',
  BOOK_NOT_FOUND: 'Kitap bulunamadı; silinmiş olabilir.',
  DELETED_BOOK_NOT_FOUND: 'Kitap silinenler arasında bulunamadı; zaten geri alınmış olabilir.',
  PAPER_NOT_FOUND: 'Bildiri bu kitapta bulunamadı. Sayfayı yenileyip tekrar deneyin.',
  EDIT_CONFLICT: 'Kitap bu sırada başka bir işlemle değişti. Güncel hali yüklendi, lütfen tekrar deneyin.',
  GENERATION_ALREADY_IN_PROGRESS: 'Bu kitap zaten kuyrukta veya hazırlanıyor.',
  ALREADY_COMPLETED: 'Bu kitabın PDF’i zaten oluşturuldu.',
  BOOK_NOT_COMPLETED: 'Kitabın PDF’i henüz hazır değil.',
  PDF_NOT_FOUND: 'Kitabın PDF dosyası bulunamadı.',
  PAPER_ORDER_LOCKED: 'Kitap hazırlanmaya başladığı için sıra artık değiştirilemez.',
  PAPER_TITLE_INVALID: null,
  PAPER_TITLE_CONTACT_INFO: 'Başlıkta e-posta adresi veya telefon numarası bulunamaz.',
  PAPER_TITLE_UNSUPPORTED_CHARACTER: null,
  PAPER_ORDER_INVALID: 'Sıralama geçersiz. Sayfayı yenileyip tekrar deneyin.',
  RATE_LIMITED: 'Kısa sürede çok fazla istek gönderildi. Lütfen biraz bekleyip tekrar deneyin.',
  INTERNAL_ERROR: GENERIC_MESSAGE,
  BOOK_NAME_INVALID: `Kitap adı ${BOOK_NAME_MIN}–${BOOK_NAME_MAX} karakter olmalı ve satır sonu içermemelidir.`,
  BOOK_NAME_UNSUPPORTED_CHARACTER: null,
  PAPER_COUNT_INVALID: `Tam olarak ${REQUIRED_PAPER_COUNT} bildiri dosyası yüklenmelidir.`,
  TOTAL_SIZE_TOO_LARGE: `Dosyaların toplam boyutu ${MAX_TOTAL_MB} MB sınırını aşıyor.`,
  FILE_EXTENSION_INVALID: 'Yalnızca .docx uzantılı Word belgeleri yüklenebilir.',
  FILE_TOO_LARGE: `Dosya ${MAX_FILE_MB} MB sınırını aşıyor.`,
  FILE_EMPTY: 'Dosya boş.',
  FILE_NOT_DOCX: 'Geçerli bir Word (.docx) belgesi değil.',
  FILE_UNSAFE_ARCHIVE: 'Dosyanın iç yapısı güvenli değil. Belgeyi Word’de açıp yeniden kaydedin.',
  FILE_NO_CONTENT: 'Belgede metin bulunamadı.',
  FILE_DUPLICATE: null,
  FILE_UNSUPPORTED_CHARACTER: null,
  INVALID_DOCUMENT: null,
  CONTACT_LEAK_DETECTED:
    'PDF’te temizlenemeyen iletişim bilgisi bulundu; güvenlik nedeniyle PDF yayımlanmadı.',
  RENDER_FAILED: 'PDF dizgisi oluşturulamadı.',
  UNSUPPORTED_CHARACTER: null,
  GENERATION_TIMEOUT: 'Kitap oluşturma süre sınırını aştı. Lütfen tekrar deneyin.',
} as const satisfies Record<string, string | null>;

export type ErrorCode = keyof typeof errorMessages;

function isKnownCode(code: string): code is ErrorCode {
  return Object.hasOwn(errorMessages, code);
}

/** Turkish text for a code; the server text when the code is unknown or the server's text is more specific. */
export function messageForCode(code: string | undefined, serverText: string | undefined): string {
  if (code !== undefined && isKnownCode(code)) {
    return errorMessages[code] ?? serverText ?? GENERIC_MESSAGE;
  }

  return serverText ?? GENERIC_MESSAGE;
}

export function rateLimitMessage(retryAfterSeconds: number | undefined): string {
  if (retryAfterSeconds === undefined || retryAfterSeconds <= 0) {
    return errorMessages.RATE_LIMITED;
  }

  return `Kısa sürede çok fazla istek gönderildi. ${retryAfterSeconds} saniye sonra tekrar deneyin.`;
}

/** The one message to show for any error thrown by the API layer. */
export function errorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return GENERIC_MESSAGE;
  }

  if (error.isNetworkError) {
    return NETWORK_MESSAGE;
  }

  if (error.status === 429) {
    return rateLimitMessage(error.retryAfterSeconds);
  }

  return messageForCode(error.code, error.problem?.detail);
}

export interface UploadErrors {
  /** Message for the book name field. */
  name?: string;
  /** Message per uploaded file name. */
  files: Map<string, string>;
  /** Messages that belong to no single field or file. */
  general: string[];
}

function itemMessage(item: ProblemItem): string {
  return messageForCode(item.code, item.message);
}

/** Spreads an upload error over the form: name field, file rows and a general alert. */
export function mapUploadErrors(error: unknown): UploadErrors {
  const result: UploadErrors = { files: new Map(), general: [] };
  const items = error instanceof ApiError ? (error.problem?.errors ?? []) : [];

  for (const item of items) {
    const message = itemMessage(item);
    if (item.field === 'name') {
      result.name ??= message;
    } else if (item.fileName != null) {
      if (!result.files.has(item.fileName)) {
        result.files.set(item.fileName, message);
      }
    } else {
      result.general.push(message);
    }
  }

  if (items.length === 0) {
    result.general.push(errorMessage(error));
  }

  return result;
}

/** Seconds from a Retry-After header (delta-seconds or HTTP date). */
export function parseRetryAfter(value: string | null, now: Date = new Date()): number | undefined {
  if (value === null || value.trim() === '') {
    return undefined;
  }

  const seconds = Number(value);
  if (Number.isFinite(seconds)) {
    return Math.max(0, Math.ceil(seconds));
  }

  const date = Date.parse(value);
  return Number.isNaN(date) ? undefined : Math.max(0, Math.ceil((date - now.getTime()) / 1000));
}

/** Reads a ProblemDetails body if there is one. */
export function toProblem(body: unknown): Problem | undefined {
  if (typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string') {
    return body as Problem;
  }

  return undefined;
}
