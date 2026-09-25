import { ApiError, parseRetryAfter, toProblem } from './errors';
import type { BookDetail } from './types';

/**
 * Uploads the book with XMLHttpRequest, the one request that needs upload progress (fetch does not report it).
 * Files are sent in the given order, which becomes the paper order.
 */
export function uploadBook(
  name: string,
  files: readonly File[],
  onProgress: (percent: number) => void,
): Promise<BookDetail> {
  const form = new FormData();
  form.append('name', name);
  for (const file of files) {
    form.append('files', file, file.name);
  }

  return new Promise((resolve, reject) => {
    const request = new XMLHttpRequest();
    request.open('POST', '/api/books');
    request.setRequestHeader('Accept', 'application/json');

    request.upload.addEventListener('progress', (event) => {
      if (event.lengthComputable && event.total > 0) {
        onProgress(Math.min(100, Math.round((event.loaded / event.total) * 100)));
      }
    });

    request.addEventListener('load', () => {
      const body = parseJson(request.responseText);
      if (request.status === 201 && body !== undefined) {
        onProgress(100);
        resolve(body as BookDetail);
        return;
      }

      reject(
        new ApiError(
          request.status,
          toProblem(body),
          parseRetryAfter(request.getResponseHeader('Retry-After')),
        ),
      );
    });
    request.addEventListener('error', () => {
      reject(new ApiError(0));
    });
    request.addEventListener('abort', () => {
      reject(new ApiError(0));
    });

    request.send(form);
  });
}

function parseJson(text: string): unknown {
  try {
    return text ? (JSON.parse(text) as unknown) : undefined;
  } catch {
    return undefined;
  }
}
