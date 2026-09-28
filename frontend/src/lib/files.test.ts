import {
  MAX_FILE_BYTES,
  MAX_TOTAL_BYTES,
  basicIssue,
  bookNameError,
  compareFileNames,
  isSortedByName,
  describeIssue,
  describeSelectionProblem,
  fileIssues,
  isSelectionReady,
  selectionProblems,
  checkFileContent,
  type CheckedFile,
} from './files';

const file = (name: string, size = 1000, hash?: string): CheckedFile => ({ name, size, hash });
const ten = (): CheckedFile[] =>
  Array.from({ length: 10 }, (_, i) =>
    file(`${String(i + 1).padStart(2, '0')}_Bildiri.docx`, 1000, `hash-${String(i)}`),
  );

describe('basicIssue', () => {
  it.each(['a.docx', 'A.DOCX', 'Bildiri.Docx', 'çalışma.docx'])('accepts %s', (name) => {
    expect(basicIssue(file(name))).toBeUndefined();
  });

  it.each(['a.doc', 'a.docm', 'a.dotx', 'a.pdf', 'a', 'a.docx.pdf'])(
    'rejects the extension of %s',
    (name) => {
      expect(basicIssue(file(name))).toEqual({ kind: 'extension' });
    },
  );

  it('rejects an empty file', () => {
    expect(basicIssue(file('a.docx', 0))).toEqual({ kind: 'empty' });
  });

  it('accepts exactly 10 MB and rejects one byte more', () => {
    expect(basicIssue(file('a.docx', MAX_FILE_BYTES))).toBeUndefined();
    expect(basicIssue(file('a.docx', MAX_FILE_BYTES + 1))).toEqual({ kind: 'tooLarge' });
  });
});

describe('fileIssues', () => {
  it('marks the later copy of the same content as a duplicate of the first one, whatever the names', () => {
    const issues = fileIssues([
      file('01.docx', 10, 'x'),
      file('02.docx', 10, 'y'),
      file('kopya.docx', 10, 'x'),
    ]);

    expect(issues).toEqual([undefined, undefined, { kind: 'duplicate', of: '01.docx' }]);
    expect(describeIssue({ kind: 'duplicate', of: '01.docx' })).toBe('01.docx ile aynı içeriğe sahip.');
  });

  it('reports the extension before anything else and does not use unhashed files for duplicates', () => {
    expect(fileIssues([file('a.pdf', 10, 'x'), file('b.docx', 10, 'x'), file('c.docx', 10)])).toEqual([
      { kind: 'extension' },
      undefined,
      undefined,
    ]);
  });
});

describe('selection', () => {
  it('needs exactly ten files', () => {
    expect(selectionProblems(ten())).toEqual([]);
    expect(selectionProblems(ten().slice(0, 9))).toEqual([{ kind: 'count', selected: 9 }]);
    expect(selectionProblems([...ten(), file('11.docx')])).toEqual([{ kind: 'count', selected: 11 }]);
    expect(describeSelectionProblem({ kind: 'count', selected: 7 })).toBe(
      'Tam olarak 10 bildiri gerekiyor; 3 dosya daha seçin.',
    );
    expect(describeSelectionProblem({ kind: 'count', selected: 12 })).toBe(
      'Tam olarak 10 bildiri gerekiyor; 2 dosyayı kaldırın.',
    );
  });

  it('limits the total size to 60 MB', () => {
    const files = ten().map((f) => ({ ...f, size: MAX_TOTAL_BYTES / 10 }));
    expect(selectionProblems(files)).toEqual([]);

    files[0] = { ...files[0]!, size: MAX_TOTAL_BYTES / 10 + 1 };
    expect(selectionProblems(files)).toEqual([{ kind: 'totalTooLarge', totalBytes: MAX_TOTAL_BYTES + 1 }]);
  });

  it('is ready with ten hashed files without problems', () => {
    expect(isSelectionReady(ten())).toBe(true);
    expect(isSelectionReady(ten().slice(0, 9))).toBe(false);

    const unhashed = ten();
    unhashed[3] = { ...unhashed[3]!, hash: undefined };
    expect(isSelectionReady(unhashed)).toBe(false);

    const duplicate = ten();
    duplicate[9] = { ...duplicate[9]!, hash: 'hash-0' };
    expect(isSelectionReady(duplicate)).toBe(false);
  });
});

describe('bookNameError', () => {
  it.each(['Örnek Bilim Kongresi 2026', '  abc  ', 'a'.repeat(150)])('accepts %s', (name) => {
    expect(bookNameError(name)).toBeUndefined();
  });

  it.each([
    ['', 'Kitap adını yazın.'],
    ['ab', 'Kitap adı 3–150 karakter olmalıdır.'],
    ['a'.repeat(151), 'Kitap adı 3–150 karakter olmalıdır.'],
    ['Satır\nsonu', 'Kitap adı satır sonu veya kontrol karakteri içeremez.'],
  ])('rejects %j', (name, message) => {
    expect(bookNameError(name)).toBe(message);
  });
});

describe('checkFileContent', () => {
  const zip = (text: string) => new Uint8Array([0x50, 0x4b, 0x03, 0x04, ...new TextEncoder().encode(text)]);

  it('hashes the content, not the name', async () => {
    const a = await checkFileContent(new File([zip('aynı içerik')], 'a.docx'));
    const b = await checkFileContent(new File([zip('aynı içerik')], 'b.docx'));
    const c = await checkFileContent(new File([zip('başka içerik')], 'a.docx'));

    expect(a.hash).toMatch(/^[0-9a-f]{64}$/);
    expect(a.hash).toBe(b.hash);
    expect(a.hash).not.toBe(c.hash);
  });

  it('recognises a ZIP package by its first four bytes', async () => {
    expect((await checkFileContent(new File([zip('word/document.xml')], 'a.docx'))).zip).toBe(true);
    expect((await checkFileContent(new File(['düz metin'], 'not.docx'))).zip).toBe(false);
    expect((await checkFileContent(new File(['%PDF-1.7'], 'makale.docx'))).zip).toBe(false);
    expect((await checkFileContent(new File(['PK'], 'kisa.docx'))).zip).toBe(false);
  });
});

describe('notDocx', () => {
  it('marks a file whose content is not a ZIP, before any duplicate check', () => {
    expect(
      fileIssues([
        { name: 'a.docx', size: 10, hash: 'x', zip: false },
        { name: 'b.docx', size: 10, hash: 'x', zip: false },
        { name: 'c.docx', size: 10, hash: 'y', zip: true },
      ]),
    ).toEqual([{ kind: 'notDocx' }, { kind: 'notDocx' }, undefined]);
    expect(describeIssue({ kind: 'notDocx' })).toBe('Geçerli bir Word (.docx) dosyası değil.');
    expect(isSelectionReady(Array.from({ length: 10 }, (_, i) => ({ name: `${i}.docx`, size: 1, hash: `h${i}`, zip: i !== 3 })))).toBe(false);
  });
});

describe('compareFileNames', () => {
  it('orders numbers naturally and Turkish letters correctly', () => {
    expect(['10_a.docx', '2_a.docx', '01_a.docx'].sort(compareFileNames)).toEqual([
      '01_a.docx',
      '2_a.docx',
      '10_a.docx',
    ]);
    expect(['Şule.docx', 'Sema.docx', 'Zeynep.docx'].sort(compareFileNames)).toEqual([
      'Sema.docx',
      'Şule.docx',
      'Zeynep.docx',
    ]);
  });
});

describe('isSortedByName', () => {
  it('recognises a list in natural name order', () => {
    expect(isSortedByName(['01_a.docx', '2_b.docx', '10_c.docx'])).toBe(true);
    expect(isSortedByName(['10_c.docx', '2_b.docx'])).toBe(false);
    expect(isSortedByName([])).toBe(true);
  });
});
