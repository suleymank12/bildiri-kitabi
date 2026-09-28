import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { isBusy } from '../lib/status';
import { api, unwrap } from './client';
import { ApiError } from './errors';
import type { BookDetail, BookPage } from './types';
import { uploadBook } from './upload';

export const BOOK_POLL_MS = 700;
export const LIST_POLL_MS = 3000;
export const LIST_PAGE_SIZE = 20;

export const bookKeys = {
  all: ['books'] as const,
  lists: () => [...bookKeys.all, 'list'] as const,
  list: (page: number) => [...bookKeys.lists(), page] as const,
  detail: (id: string) => [...bookKeys.all, 'detail', id] as const,
};

/** One book; polled every 700 ms while it is queued or being generated, not at all otherwise. */
export function useBook(id: string) {
  return useQuery({
    queryKey: bookKeys.detail(id),
    queryFn: ({ signal }) =>
      unwrap(api.GET('/api/books/{uid}', { params: { path: { uid: id } }, signal })) as Promise<BookDetail>,
    refetchInterval: (query) => (query.state.data && isBusy(query.state.data.status) ? BOOK_POLL_MS : false),
    // Polling pauses while the tab is hidden and resumes when it is visible again.
    refetchIntervalInBackground: false,
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 2,
  });
}

/** A page of books; refreshed every three seconds only while one of them is queued or being generated. */
export function useBookList(page: number) {
  return useQuery({
    queryKey: bookKeys.list(page),
    queryFn: ({ signal }) =>
      unwrap(
        api.GET('/api/books', { params: { query: { page, pageSize: LIST_PAGE_SIZE } }, signal }),
      ) as Promise<BookPage>,
    refetchInterval: (query) =>
      query.state.data?.items.some((book) => isBusy(book.status)) === true ? LIST_POLL_MS : false,
    placeholderData: (previous) => previous,
  });
}

/** Uploads a new book and reports upload progress (0–100). */
export function useCreateBook() {
  const queryClient = useQueryClient();
  const [progress, setProgress] = useState(0);
  const mutation = useMutation({
    mutationFn: ({ name, files }: { name: string; files: readonly File[] }) => {
      setProgress(0);
      return uploadBook(name, files, setProgress);
    },
    onSuccess: (book) => {
      queryClient.setQueryData(bookKeys.detail(book.id), book);
      void queryClient.invalidateQueries({ queryKey: bookKeys.lists() });
    },
  });
  return { ...mutation, progress };
}

/**
 * Saves a new paper order. The list changes at once (optimistic update) and is rolled back if the server
 * refuses; the caller shows the error.
 */
export function useReorderPapers(id: string) {
  const queryClient = useQueryClient();
  const key = bookKeys.detail(id);
  return useMutation({
    mutationFn: (paperIds: string[]) =>
      unwrap(
        api.PUT('/api/books/{uid}/paper-order', { params: { path: { uid: id } }, body: { paperIds } }),
      ) as Promise<BookDetail>,
    onMutate: async (paperIds) => {
      await queryClient.cancelQueries({ queryKey: key });
      const previous = queryClient.getQueryData<BookDetail>(key);
      if (previous) {
        const byId = new Map(previous.papers.map((paper) => [paper.id, paper]));
        const papers = paperIds.flatMap((paperId, index) => {
          const paper = byId.get(paperId);
          return paper ? [{ ...paper, order: index + 1 }] : [];
        });
        queryClient.setQueryData<BookDetail>(key, { ...previous, papers });
      }

      return { previous };
    },
    onError: (error, _paperIds, context) => {
      if (context?.previous) {
        queryClient.setQueryData(key, context.previous);
      }

      if (error instanceof ApiError && error.code === 'PAPER_ORDER_LOCKED') {
        void queryClient.invalidateQueries({ queryKey: key });
      }
    },
    onSuccess: (book) => {
      queryClient.setQueryData(key, book);
    },
  });
}

/** Starts generation (202) and refreshes the book so its page switches to the progress view. */
export function useStartGeneration(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/books/{uid}/generate', { params: { path: { uid: id } } })),
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: bookKeys.detail(id) });
      void queryClient.invalidateQueries({ queryKey: bookKeys.lists() });
    },
  });
}

export function useDeleteBook() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE('/api/books/{uid}', { params: { path: { uid: id } } })),
    onSuccess: (_result, id) => {
      queryClient.removeQueries({ queryKey: bookKeys.detail(id) });
      void queryClient.invalidateQueries({ queryKey: bookKeys.lists() });
    },
  });
}

export function pdfUrl(book: Pick<BookDetail, 'pdfUrl'>, download = false): string | undefined {
  if (book.pdfUrl == null) {
    return undefined;
  }

  return download ? `${book.pdfUrl}?download=true` : book.pdfUrl;
}
