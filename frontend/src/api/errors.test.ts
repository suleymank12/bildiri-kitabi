import {
  ApiError,
  GENERIC_MESSAGE,
  NETWORK_MESSAGE,
  errorMessage,
  errorMessages,
  mapUploadErrors,
  messageForCode,
  parseRetryAfter,
  toProblem,
} from './errors';
import type { Problem } from './types';

const problem = (code: string, detail: string, errors?: Problem['errors']): Problem => ({
  type: null,
  title: 'Başlık',
  status: 400,
  detail,
  code,
  traceId: '00-abc',
  errors: errors ?? null,
});

describe('errorMessages', () => {
  const codes = Object.keys(errorMessages) as (keyof typeof errorMessages)[];

  it.each(codes)('%s has a Turkish message or uses the server text', (code) => {
    const message = messageForCode(code, 'Sunucunun Türkçe açıklaması.');
    const own = errorMessages[code];

    expect(message).toBe(own ?? 'Sunucunun Türkçe açıklaması.');
    expect(message).not.toMatch(/[!]/);
    expect(message.length).toBeGreaterThan(5);
  });

  it('covers every code the backend returns', () => {
    expect(codes).toEqual(
      expect.arrayContaining([
        'BOOK_NAME_INVALID',
        'BOOK_NAME_UNSUPPORTED_CHARACTER',
        'PAPER_COUNT_INVALID',
        'FILE_EXTENSION_INVALID',
        'FILE_TOO_LARGE',
        'FILE_EMPTY',
        'FILE_NOT_DOCX',
        'FILE_UNSAFE_ARCHIVE',
        'FILE_NO_CONTENT',
        'FILE_DUPLICATE',
        'FILE_UNSUPPORTED_CHARACTER',
        'TOTAL_SIZE_TOO_LARGE',
        'VALIDATION_FAILED',
        'BOOK_NOT_FOUND',
        'GENERATION_ALREADY_IN_PROGRESS',
        'ALREADY_COMPLETED',
        'BOOK_NOT_COMPLETED',
        'PAPER_ORDER_LOCKED',
        'PAPER_ORDER_INVALID',
        'RATE_LIMITED',
        'INTERNAL_ERROR',
        'UNSUPPORTED_CHARACTER',
        'GENERATION_TIMEOUT',
        'CONTACT_LEAK_DETECTED',
      ]),
    );
  });
});

describe('errorMessage', () => {
  it('uses the Turkish text of a known code', () => {
    expect(errorMessage(new ApiError(409, problem('PAPER_ORDER_LOCKED', 'detail')))).toBe(
      'Kitap hazırlanmaya başladığı için sıra artık değiştirilemez.',
    );
  });

  it('falls back to the server detail for an unknown code', () => {
    expect(errorMessage(new ApiError(400, problem('SOMETHING_NEW', 'Sunucunun açıklaması.')))).toBe(
      'Sunucunun açıklaması.',
    );
  });

  it('explains a network failure', () => {
    expect(errorMessage(new ApiError(0))).toBe(NETWORK_MESSAGE);
    expect(NETWORK_MESSAGE).toBe('Sunucuya ulaşılamıyor. Bağlantınızı kontrol edip tekrar deneyin.');
  });

  it('tells how long to wait after a 429', () => {
    expect(errorMessage(new ApiError(429, problem('RATE_LIMITED', 'x'), 42))).toBe(
      'Kısa sürede çok fazla istek gönderildi. 42 saniye sonra tekrar deneyin.',
    );
    expect(errorMessage(new ApiError(429, problem('RATE_LIMITED', 'x')))).toBe(errorMessages.RATE_LIMITED);
  });

  it('has a generic message for anything else', () => {
    expect(errorMessage(new Error('boom'))).toBe(GENERIC_MESSAGE);
  });
});

describe('mapUploadErrors', () => {
  it('spreads the errors over the name field, the file rows and the general alert', () => {
    const error = new ApiError(
      400,
      problem('VALIDATION_FAILED', 'Yüklemede 4 sorun bulundu.', [
        {
          code: 'BOOK_NAME_INVALID',
          message: 'Kitap adı 3–150 karakter olmalıdır.',
          field: 'name',
          fileName: null,
        },
        {
          code: 'PAPER_COUNT_INVALID',
          message: 'Tam olarak 10 bildiri dosyası yüklenmelidir; 9 dosya seçildi.',
          field: 'files',
          fileName: null,
        },
        {
          code: 'FILE_NOT_DOCX',
          message: '03.docx geçerli bir Word (.docx) belgesi değil.',
          field: null,
          fileName: '03.docx',
        },
        {
          code: 'FILE_DUPLICATE',
          message: 'kopya.docx ile 01.docx aynı içeriğe sahip.',
          field: null,
          fileName: 'kopya.docx',
        },
      ]),
    );

    const mapped = mapUploadErrors(error);

    expect(mapped.name).toBe(errorMessages.BOOK_NAME_INVALID);
    expect(mapped.files.get('03.docx')).toBe('Geçerli bir Word (.docx) belgesi değil.');
    expect(mapped.files.get('kopya.docx')).toBe('kopya.docx ile 01.docx aynı içeriğe sahip.');
    expect(mapped.general).toEqual([errorMessages.PAPER_COUNT_INVALID]);
  });

  it('keeps the server text when it names a character', () => {
    const error = new ApiError(
      400,
      problem('BOOK_NAME_UNSUPPORTED_CHARACTER', 'x', [
        {
          code: 'BOOK_NAME_UNSUPPORTED_CHARACTER',
          message: "Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın.",
          field: 'name',
          fileName: null,
        },
      ]),
    );

    expect(mapUploadErrors(error).name).toBe(
      "Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın.",
    );
  });

  it('puts a network error or a problem without items in the general alert', () => {
    expect(mapUploadErrors(new ApiError(0)).general).toEqual([NETWORK_MESSAGE]);
    expect(
      mapUploadErrors(new ApiError(413, { ...problem('REQUEST_TOO_LARGE', 'x'), status: 413 })).general,
    ).toEqual([errorMessages.REQUEST_TOO_LARGE]);
  });
});

describe('parseRetryAfter', () => {
  it('reads seconds and HTTP dates', () => {
    const now = new Date('2026-09-29T10:00:00Z');
    expect(parseRetryAfter('30', now)).toBe(30);
    expect(parseRetryAfter('Tue, 29 Sep 2026 10:01:00 GMT', now)).toBe(60);
    expect(parseRetryAfter(null, now)).toBeUndefined();
    expect(parseRetryAfter('yarın', now)).toBeUndefined();
  });
});

describe('toProblem', () => {
  it('recognises a ProblemDetails body by its code', () => {
    expect(toProblem(problem('X', 'y'))?.code).toBe('X');
    expect(toProblem({ title: 'no code' })).toBeUndefined();
    expect(toProblem('text')).toBeUndefined();
  });
});
