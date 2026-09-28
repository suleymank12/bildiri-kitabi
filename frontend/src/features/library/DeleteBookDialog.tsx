import { errorMessage } from '../../api/errors';
import { useDeleteBook } from '../../api/hooks';
import { useAnnounce } from '../../app/Announcer';
import { Button, Dialog } from '../../components/ui';

const DELETE_EXPLANATION = 'Kitap Silinenler’e taşınacak. Oradan geri alabilirsiniz.';

export interface DeleteBookDialogProps {
  /** The book to delete; the dialog is open while it is set. */
  book: { uid: string; name: string } | undefined;
  onClose: () => void;
  onDeleted?: () => void;
}

/** "Kitabı sil" confirmation, shared by Kitaplarım and the edit page. Deleting moves the book to Silinenler. */
export function DeleteBookDialog({ book, onClose, onDeleted }: DeleteBookDialogProps) {
  const announce = useAnnounce();
  const deleteBook = useDeleteBook();

  function confirm() {
    if (!book) {
      return;
    }

    deleteBook.mutate(book.uid, {
      onSuccess: () => {
        announce(`“${book.name}” Silinenler’e taşındı.`);
        onClose();
        onDeleted?.();
      },
    });
  }

  return (
    <Dialog
      open={book !== undefined}
      title="Kitabı sil"
      onClose={() => {
        if (!deleteBook.isPending) {
          deleteBook.reset();
          onClose();
        }
      }}
      actions={
        <>
          <Button
            variant="secondary"
            disabled={deleteBook.isPending}
            onClick={() => {
              deleteBook.reset();
              onClose();
            }}
          >
            Vazgeç
          </Button>
          <Button variant="danger" loading={deleteBook.isPending} onClick={confirm}>
            Sil
          </Button>
        </>
      }
    >
      <p>
        “{book?.name}” silinecek. {DELETE_EXPLANATION}
      </p>
      {deleteBook.isError && <p className="mt-3 text-sm text-danger">{errorMessage(deleteBook.error)}</p>}
    </Dialog>
  );
}
