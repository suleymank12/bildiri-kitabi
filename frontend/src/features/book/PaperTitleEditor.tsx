import { CheckIcon, PencilSimpleIcon, XIcon } from '@phosphor-icons/react';
import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { ApiError, errorMessage } from '../../api/errors';
import type { Paper } from '../../api/types';
import { Button } from '../../components/ui';

export const PAPER_TITLE_MAX = 500;

/** What the page did with a title: saved, or not saved because the user backed out of a confirmation. */
export type TitleSaveResult = 'saved' | 'cancelled';

export interface PaperTitleEditorProps {
  paper: Paper;
  /** Saves the title; a rejected promise shows the error under the box and keeps the text. */
  onSave: (title: string) => Promise<TitleSaveResult>;
  disabled?: boolean;
}

/** The same rules as the server, so an obvious mistake is shown before a request. */
export function paperTitleError(title: string): string | undefined {
  const trimmed = title.replace(/\r\n|[\r\n]/g, ' ').trim();
  if (trimmed.length === 0) {
    return 'Bildiri başlığı boş olamaz.';
  }

  return trimmed.length > PAPER_TITLE_MAX ? `Bildiri başlığı en fazla ${PAPER_TITLE_MAX} karakter olabilir.` : undefined;
}

/**
 * A paper title with a "Başlığı düzenle" button that turns it into a text box in place. Enter saves, Esc backs out;
 * the focus goes back to the button either way.
 */
export function PaperTitleEditor({ paper, onSave, disabled = false }: PaperTitleEditorProps) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(paper.title);
  const [error, setError] = useState<string>();
  const [saving, setSaving] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const editButton = useRef<HTMLButtonElement>(null);
  const returnFocus = useRef(false);
  const errorId = useId();

  useEffect(() => {
    if (editing) {
      input.current?.focus();
      input.current?.select();
    } else if (returnFocus.current) {
      returnFocus.current = false;
      editButton.current?.focus();
    }
  }, [editing]);

  function start() {
    setDraft(paper.title);
    setError(undefined);
    setEditing(true);
  }

  function cancel() {
    returnFocus.current = true;
    setEditing(false);
    setError(undefined);
  }

  async function save() {
    const problem = paperTitleError(draft);
    if (problem) {
      setError(problem);
      input.current?.focus();
      return;
    }

    setSaving(true);
    setError(undefined);
    try {
      if ((await onSave(draft.trim())) === 'saved') {
        returnFocus.current = true;
        setEditing(false);
      } else {
        input.current?.focus();
      }
    } catch (caught) {
      // The page may already have turned the API error into its own message.
      setError(caught instanceof ApiError || !(caught instanceof Error) ? errorMessage(caught) : caught.message);
      input.current?.focus();
    } finally {
      setSaving(false);
    }
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter') {
      event.preventDefault();
      void save();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      cancel();
    }
  }

  if (!editing) {
    return (
      <div className="flex items-start gap-1">
        <span className="min-w-0 flex-1 py-1 font-serif text-[0.9375rem] leading-snug text-ink-muted">
          {paper.title}
        </span>
        <Button
          ref={editButton}
          variant="ghost"
          size="sm"
          className="w-11 shrink-0 px-0"
          icon={<PencilSimpleIcon size={16} aria-hidden="true" />}
          disabled={disabled}
          aria-label={`${paper.fileName} başlığını düzenle`}
          title="Başlığı düzenle"
          onClick={start}
        />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <input
        ref={input}
        value={draft}
        maxLength={PAPER_TITLE_MAX}
        readOnly={saving}
        aria-label={`${paper.fileName} başlığı`}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        className="field-control min-h-11 w-full px-3 font-serif text-base"
        onChange={(event) => {
          setDraft(event.target.value);
        }}
        onKeyDown={onKeyDown}
      />
      {error && (
        <p id={errorId} className="text-sm text-danger">
          {error}
        </p>
      )}
      <div className="flex flex-wrap justify-end gap-2">
        <Button
          variant="secondary"
          size="sm"
          icon={<XIcon size={16} aria-hidden="true" />}
          disabled={saving}
          onClick={cancel}
        >
          Vazgeç
        </Button>
        <Button
          variant="primary"
          size="sm"
          icon={<CheckIcon size={16} aria-hidden="true" />}
          loading={saving}
          onClick={() => void save()}
        >
          Kaydet
        </Button>
      </div>
    </div>
  );
}
