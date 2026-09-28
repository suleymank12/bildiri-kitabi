import type { Paper } from '../../api/types';
import { Alert } from '../../components/ui';
import { BreakableFileName } from '../../lib/fileName';
import { titleEditButtonId } from './PaperTitleEditor';

/**
 * At the top of the paper list: the papers whose title was not found, so their file name is used. One line per paper
 * in a list of ten is easy to miss, and a wrong file (or an empty one) often looks exactly like that. Only `FileName`
 * counts; a title from the first bold paragraph is usually right and keeps its own note on its row. The warning
 * does not block generation.
 */
export function MissingTitlesAlert({ papers }: { papers: readonly Paper[] }) {
  const missing = papers.filter((paper) => paper.titleSource === 'FileName');
  if (missing.length === 0) {
    return null;
  }

  function focusRow(paper: Paper) {
    const button = document.getElementById(titleEditButtonId(paper.uid));
    button?.scrollIntoView({ block: 'center' });
    button?.focus();
  }

  return (
    <Alert tone="warning" title="Başlığı bulunamayan bildiriler var">
      <p>{missing.length} bildiride başlık bulunamadı ve dosya adı kullanıldı:</p>
      <ul className="my-1 flex flex-col">
        {missing.map((paper) => (
          <li key={paper.uid}>
            <a
              href={`#${titleEditButtonId(paper.uid)}`}
              className="inline-flex min-h-11 items-center font-medium text-ink underline wrap-break-word hover:text-accent"
              onClick={(event) => {
                event.preventDefault();
                focusRow(paper);
              }}
            >
              <BreakableFileName name={paper.fileName} />
            </a>
          </li>
        ))}
      </ul>
      <p>Doğru dosyayı yüklediğinizi kontrol edin veya başlığı kalem düğmesiyle düzeltin.</p>
    </Alert>
  );
}
