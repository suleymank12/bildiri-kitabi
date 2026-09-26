/**
 * Client-side checks that mirror the server's upload rules. They only warn early; the server has the final say.
 */

export const REQUIRED_PAPER_COUNT = 10;
export const MAX_FILE_BYTES = 10 * 1024 * 1024;
export const MAX_TOTAL_BYTES = 60 * 1024 * 1024;
export const BOOK_NAME_MIN = 3;
export const BOOK_NAME_MAX = 150;

export const DOCX_ACCEPT = '.docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document';

export type FileIssue =
  { kind: 'extension' } | { kind: 'empty' } | { kind: 'tooLarge' } | { kind: 'duplicate'; of: string };

export interface CheckedFile {
  name: string;
  size: number;
  /** SHA-256 of the content (hex); undefined until computed or when the file is not hashed. */
  hash?: string | undefined;
}

export type SelectionProblem =
  { kind: 'count'; selected: number } | { kind: 'totalTooLarge'; totalBytes: number };

export function hasDocxExtension(name: string): boolean {
  return name.toLocaleLowerCase('en-US').endsWith('.docx');
}

/** Problems visible from the name and size alone. */
export function basicIssue(file: Pick<CheckedFile, 'name' | 'size'>): FileIssue | undefined {
  if (!hasDocxExtension(file.name)) {
    return { kind: 'extension' };
  }

  if (file.size <= 0) {
    return { kind: 'empty' };
  }

  if (file.size > MAX_FILE_BYTES) {
    return { kind: 'tooLarge' };
  }

  return undefined;
}

/** Every problem of every file, in list order; a later copy of the same content is the duplicate. */
export function fileIssues(files: readonly CheckedFile[]): (FileIssue | undefined)[] {
  const firstByHash = new Map<string, string>();
  return files.map((file) => {
    const basic = basicIssue(file);
    if (basic) {
      return basic;
    }

    if (file.hash === undefined) {
      return undefined;
    }

    const first = firstByHash.get(file.hash);
    if (first !== undefined) {
      return { kind: 'duplicate', of: first };
    }

    firstByHash.set(file.hash, file.name);
    return undefined;
  });
}

/** Problems of the selection as a whole. */
export function selectionProblems(files: readonly CheckedFile[]): SelectionProblem[] {
  const problems: SelectionProblem[] = [];
  if (files.length !== REQUIRED_PAPER_COUNT) {
    problems.push({ kind: 'count', selected: files.length });
  }

  const totalBytes = files.reduce((sum, file) => sum + file.size, 0);
  if (totalBytes > MAX_TOTAL_BYTES) {
    problems.push({ kind: 'totalTooLarge', totalBytes });
  }

  return problems;
}

/** Ready to upload: the right number of files, none with a problem, all hashed. */
export function isSelectionReady(files: readonly CheckedFile[]): boolean {
  return (
    selectionProblems(files).length === 0 &&
    files.every((file) => file.hash !== undefined) &&
    fileIssues(files).every((issue) => issue === undefined)
  );
}

export function describeIssue(issue: FileIssue): string {
  switch (issue.kind) {
    case 'extension':
      return 'Yalnızca .docx uzantılı Word belgeleri yüklenebilir.';
    case 'empty':
      return 'Dosya boş.';
    case 'tooLarge':
      return 'Dosya 10 MB sınırını aşıyor.';
    case 'duplicate':
      return `${issue.of} ile aynı içeriğe sahip.`;
  }
}

export function describeSelectionProblem(problem: SelectionProblem): string {
  switch (problem.kind) {
    case 'count':
      return problem.selected < REQUIRED_PAPER_COUNT
        ? `Tam olarak ${REQUIRED_PAPER_COUNT} bildiri gerekiyor; ${REQUIRED_PAPER_COUNT - problem.selected} dosya daha seçin.`
        : `Tam olarak ${REQUIRED_PAPER_COUNT} bildiri gerekiyor; ${problem.selected - REQUIRED_PAPER_COUNT} dosyayı kaldırın.`;
    case 'totalTooLarge':
      return 'Dosyaların toplam boyutu 60 MB sınırını aşıyor.';
  }
}

export function bookNameError(name: string): string | undefined {
  const trimmed = name.trim();
  if (trimmed.length === 0) {
    return 'Kitap adını yazın.';
  }

  if (trimmed.length < BOOK_NAME_MIN || trimmed.length > BOOK_NAME_MAX) {
    return `Kitap adı ${BOOK_NAME_MIN}–${BOOK_NAME_MAX} karakter olmalıdır.`;
  }

  // Control characters (line breaks, tabs) are not allowed by the server either.
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f-\u009f]/.test(trimmed)) {
    return 'Kitap adı satır sonu veya kontrol karakteri içeremez.';
  }

  return undefined;
}

/** SHA-256 of a file's content as lower-case hex (Web Crypto). */
export async function sha256(file: Blob): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', await file.arrayBuffer());
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('');
}

const fileNameCollator = new Intl.Collator('tr', { numeric: true, sensitivity: 'base' });

/** Order by file name the way a Turkish reader expects: natural numbers ("2_…" before "10_…"), Turkish letters. */
export function compareFileNames(a: string, b: string): number {
  return fileNameCollator.compare(a, b);
}

/** True when the names are already in {@link compareFileNames} order. */
export function isSortedByName(names: readonly string[]): boolean {
  return names.every((name, i) => i === 0 || compareFileNames(names[i - 1] ?? '', name) <= 0);
}
