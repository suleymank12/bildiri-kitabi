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

/** A small file with its own content (distinct hashes unless the same seed is used). */
export function docx(name: string, seed: string = name): File {
  return new File([`içerik ${seed}`], name, {
    type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  });
}
