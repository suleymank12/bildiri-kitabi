import { UploadSimpleIcon } from '@phosphor-icons/react';
import { useEffect, useId, useRef, useState } from 'react';
import { Button } from '../../components/ui';
import { DOCX_ACCEPT } from '../../lib/files';

export interface FileDropZoneProps {
  onFiles: (files: File[]) => void;
  disabled?: boolean;
}

/** Drop area plus a "Dosya seç" button; the button (and the hidden input behind it) keeps it keyboard accessible. */
export function FileDropZone({ onFiles, disabled = false }: FileDropZoneProps) {
  const input = useRef<HTMLInputElement>(null);
  const zone = useRef<HTMLDivElement>(null);
  const [dragging, setDragging] = useState(false);
  const hintId = useId();

  // Dropping is a pointer-only shortcut; the "Dosya seç" button is the accessible way to add files.
  useEffect(() => {
    const element = zone.current;
    if (!element) {
      return;
    }

    const onDragEnter = (event: DragEvent) => {
      event.preventDefault();
      if (!disabled) {
        setDragging(true);
      }
    };
    const onDragOver = (event: DragEvent) => {
      event.preventDefault();
    };
    const onDragLeave = (event: DragEvent) => {
      if (!element.contains(event.relatedTarget as Node | null)) {
        setDragging(false);
      }
    };
    const onDrop = (event: DragEvent) => {
      event.preventDefault();
      setDragging(false);
      const files = event.dataTransfer?.files;
      if (!disabled && files && files.length > 0) {
        onFiles(Array.from(files));
      }
    };

    element.addEventListener('dragenter', onDragEnter);
    element.addEventListener('dragover', onDragOver);
    element.addEventListener('dragleave', onDragLeave);
    element.addEventListener('drop', onDrop);
    return () => {
      element.removeEventListener('dragenter', onDragEnter);
      element.removeEventListener('dragover', onDragOver);
      element.removeEventListener('dragleave', onDragLeave);
      element.removeEventListener('drop', onDrop);
    };
  }, [disabled, onFiles]);

  return (
    <div
      ref={zone}
      className={
        'flex flex-col items-center gap-3 rounded-(--radius) border border-dashed px-6 py-8 text-center transition-colors duration-150 ' +
        (dragging ? 'border-accent bg-accent-soft' : 'border-ink-subtle bg-surface-muted')
      }
    >
      <UploadSimpleIcon size={28} aria-hidden="true" className="text-ink-subtle" />
      <div className="flex flex-col gap-1">
        <p className="font-medium text-ink">Dosyaları buraya sürükleyin</p>
        <p id={hintId} className="text-sm text-ink-muted">
          Yalnızca .docx; dosya başına en fazla 10 MB.
        </p>
      </div>
      <Button
        variant="secondary"
        disabled={disabled}
        aria-describedby={hintId}
        onClick={() => input.current?.click()}
      >
        Dosya seç
      </Button>
      <input
        ref={input}
        type="file"
        multiple
        accept={DOCX_ACCEPT}
        aria-label="Bildiri dosyaları"
        tabIndex={-1}
        className="sr-only"
        disabled={disabled}
        onChange={(event) => {
          const files = Array.from(event.currentTarget.files ?? []);
          // Allow choosing the same files again after removing them.
          event.currentTarget.value = '';
          if (files.length > 0) {
            onFiles(files);
          }
        }}
      />
    </div>
  );
}
