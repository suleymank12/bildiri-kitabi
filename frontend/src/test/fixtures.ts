import type { BookDetail, BookSummary, Paper } from '../api/types';

export function paper(index: number, overrides: Partial<Paper> = {}): Paper {
  const number = String(index).padStart(2, '0');
  return {
    uid: `00000000-0000-0000-0000-0000000000${number}`,
    order: index,
    uploadOrder: index,
    fileName: `${number}_Bildiri.docx`,
    title: `BİLDİRİ ${number} BAŞLIĞI`,
    titleSource: 'TitleStyle',
    startPage: null,
    endPage: null,
    removedEmailCount: 0,
    removedPhoneCount: 0,
    sizeBytes: 26_010,
    ...overrides,
  };
}

export function bookDetail(overrides: Partial<BookDetail> = {}): BookDetail {
  return {
    uid: 'b0000000-0000-0000-0000-000000000001',
    name: 'Örnek Bilim Kongresi 2026',
    status: 'Uploaded',
    stage: null,
    progressPercent: 0,
    error: null,
    createdAt: '2026-09-29T07:00:00Z',
    processingStartedAt: null,
    processingFinishedAt: null,
    pageCount: null,
    pdfSizeBytes: null,
    pdfUrl: null,
    papers: [1, 2, 3].map((i) => paper(i)),
    ...overrides,
  };
}

export function bookSummary(overrides: Partial<BookSummary> = {}): BookSummary {
  return {
    uid: 'b0000000-0000-0000-0000-000000000001',
    name: 'Örnek Bilim Kongresi 2026',
    status: 'Completed',
    stage: null,
    progressPercent: 100,
    paperCount: 10,
    pageCount: 22,
    error: null,
    createdAt: '2026-09-29T07:00:00Z',
    processingFinishedAt: '2026-09-29T07:01:00Z',
    pdfUrl: '/api/books/b0000000-0000-0000-0000-000000000001/pdf',
    ...overrides,
  };
}

/** `count` distinct books, newest first ("Kitap 1" … "Kitap n"). */
export function manyBooks(count: number): BookSummary[] {
  return Array.from({ length: count }, (_, index) =>
    bookSummary({ uid: `kitap-${String(index + 1)}`, name: `Kitap ${String(index + 1)}` }),
  );
}

/** The page of `items` a list request (`?page=&pageSize=`) asks for, as the API answers it: empty past the end. */
export function pageOf<T>(items: readonly T[], requestUrl: string) {
  const query = new URL(requestUrl).searchParams;
  const page = Number(query.get('page') ?? '1');
  const pageSize = Number(query.get('pageSize') ?? '20');
  return {
    items: items.slice((page - 1) * pageSize, page * pageSize),
    page,
    pageSize,
    totalCount: items.length,
  };
}

/** The ZIP signature every real .docx starts with. */
const ZIP_SIGNATURE = new Uint8Array([0x50, 0x4b, 0x03, 0x04]);

/** A small "Word" file with its own content (distinct hashes unless the same seed is used). */
export function docx(name: string, seed: string = name): File {
  return new File([ZIP_SIGNATURE, `içerik ${seed}`], name, {
    type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  });
}
