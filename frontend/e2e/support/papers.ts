import { existsSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

/** The sample papers sent with the case; they belong to the company and are not in git. */
export const papersDirectory = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../../testdata/bildiriler',
);

export const missingPapersMessage =
  'Örnek bildiriler bulunamadı: case ile gönderilen 10 .docx dosyasını testdata/bildiriler/ klasörüne kopyalayın.';

/** The sample papers in file name order; empty when they have not been copied. */
export function listPaperFiles(): string[] {
  if (!existsSync(papersDirectory)) {
    return [];
  }

  return readdirSync(papersDirectory)
    .filter((name) => name.endsWith('.docx'))
    .sort()
    .map((name) => join(papersDirectory, name));
}
