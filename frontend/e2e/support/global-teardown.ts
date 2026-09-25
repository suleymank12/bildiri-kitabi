import { rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

/**
 * Removes the uploaded and generated files of the run from the OS temp folder. The compiled API next to them
 * is still in use until Playwright stops the server; it is rebuilt at the start of every run.
 */
export default function globalTeardown(): void {
  const work = join(tmpdir(), 'bildiri-kitabi-e2e');
  for (const folder of ['storage', 'uploads']) {
    rmSync(join(work, folder), { recursive: true, force: true });
  }
}
