import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { basicIssue, checkFileContent, compareFileNames, fileIssues, type FileIssue } from '../../lib/files';

export interface SelectedFile {
  id: string;
  file: File;
  /** SHA-256 once computed; files with a size or extension problem are not read. */
  hash?: string | undefined;
  /** False when the content is not a ZIP package; undefined until read. */
  zip?: boolean | undefined;
  issue?: FileIssue | undefined;
  /** True while the content is being read (hash and ZIP signature). */
  checking: boolean;
}

interface Entry {
  id: string;
  file: File;
  hash?: string | undefined;
  zip?: boolean | undefined;
  hashing: boolean;
}

let nextId = 0;

/**
 * The list of chosen files, in book order. New files go to the end; each valid file is read once: its hash catches
 * a second copy of the same content and its first bytes catch a file that is not a Word (ZIP) package.
 */
export function useFileSelection() {
  const [entries, setEntries] = useState<Entry[]>([]);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const add = useCallback((files: Iterable<File>) => {
    const added: Entry[] = Array.from(files, (file) => ({
      id: `file-${String(++nextId)}`,
      file,
      hashing: basicIssue({ name: file.name, size: file.size }) === undefined,
    }));
    setEntries((current) => [...current, ...added]);

    for (const entry of added.filter((e) => e.hashing)) {
      void checkFileContent(entry.file)
        .catch(() => undefined)
        .then((content) => {
          if (mounted.current) {
            setEntries((current) =>
              current.map((e) =>
                e.id === entry.id
                  ? { ...e, hash: content?.hash ?? `unreadable-${entry.id}`, zip: content?.zip, hashing: false }
                  : e,
              ),
            );
          }
        });
    }
  }, []);

  const remove = useCallback((id: string) => {
    setEntries((current) => current.filter((entry) => entry.id !== id));
  }, []);

  const sortByName = useCallback(() => {
    setEntries((current) => [...current].sort((a, b) => compareFileNames(a.file.name, b.file.name)));
  }, []);

  const files = useMemo<SelectedFile[]>(() => {
    const issues = fileIssues(
      entries.map((entry) => ({ name: entry.file.name, size: entry.file.size, hash: entry.hash, zip: entry.zip })),
    );
    return entries.map((entry, index) => ({
      id: entry.id,
      file: entry.file,
      hash: entry.hash,
      zip: entry.zip,
      issue: issues[index],
      checking: entry.hashing,
    }));
  }, [entries]);

  return { files, add, remove, sortByName };
}
