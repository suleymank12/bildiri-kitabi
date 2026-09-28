import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { isBusy } from '../lib/status';
import { api, unwrap } from './client';
import { ApiError } from './errors';
import type { BookDetail, BookPage, DeletedBookPage } from './types';
import { uploadBook } from './upload';

export const BOOK_POLL_MS = 700;
export const LIST_POLL_MS = 3000;
export const LIST_PAGE_SIZE = 20;

export const bookKeys = {
  all: ['books'] as const,
  lists: () => [...bookKeys.all, 'list'] as const,
  list: (page: number) => [...bookKeys.lists(), page] as const,
  deletedLists: () => [...bookKeys.all, 'deleted'] as const,
  deleted: (page: number) => [...bookKeys.deletedLists(), page] as const,
  detail: (uid: string) => [...bookKeys.all, 'detail', uid] as const,
};

/** One book; polled every 700 ms while it is queued or being generated, not at all otherwise. */
export function useBook(uid: string) {
  return useQuery({
    queryKey: bookKeys.detail(uid),
    queryFn: ({ signal }) =>
      unwrap(api.GET('/api/books/{uid}', { params: { path: { uid } }, signal })) as Promise<BookDetail>,
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

/** A page of deleted books, most recently deleted first. */
export function useDeletedBooks(page: number) {
  return useQuery({
    queryKey: bookKeys.deleted(page),
    queryFn: ({ signal }) =>
      unwrap(
        api.GET('/api/books/deleted', { params: { query: { page, pageSize: LIST_PAGE_SIZE } }, signal }),
      ) as Promise<DeletedBookPage>,
    placeholderData: (previous) => previous,
  });
}

/** Brings a deleted book back to Kitaplarım. */
export function useRestoreBook() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (uid: string) => unwrap(api.POST('/api/books/{uid}/restore', { params: { path: { uid } } })),
    onSuccess: (_result, uid) => {
      // The row leaves the list at once; the refetch below confirms it.
      queryClient.setQueriesData<DeletedBookPage>(
        { queryKey: bookKeys.deletedLists() },
        (page) =>
          page && {
            ...page,
            items: page.items.filter((book) => book.uid !== uid),
            totalCount: page.totalCount - 1,
          },
      );
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: bookKeys.deletedLists() });
      void queryClient.invalidateQueries({ queryKey: bookKeys.lists() });
    },
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
      queryClient.setQueryData(bookKeys.detail(book.uid), book);
      void queryClient.invalidateQueries({ queryKey: bookKeys.lists() });
    },
  });
  return { ...mutation, progress };
}

/**
 * Saves a new paper order. The list changes at once (optimistic update) and is rolled back if the server
 * refuses; the caller shows the error.
 */
export function useReorderPapers(uid: string) {
  const queryClient = useQueryClient();
  const key = bookKeys.detail(uid);
  return useMutation({
    mutationFn: (paperUids: string[]) =>
      unwrap(
        api.PUT('/api/books/{uid}/paper-order', { params: { path: { uid } }, body: { paperUids } }),
      ) as Promise<BookDetail>,
    onMutate: async (paperUids) => {
      await queryClient.cancelQueries({ queryKey: key });
      const previous = queryClient.getQueryData<BookDetail>(key);
      if (previous) {
        const byUid = new Map(previous.papers.map((paper) => [paper.uid, paper]));
        const papers = paperUids.flatMap((paperUid, index) => {
          const paper = byUid.get(paperUid);
          return paper ? [{ ...paper, order: index + 1 }] : [];
        });
        queryClient.setQueryData<BookDetail>(key, { ...previous, papers });
      }

      return { previous };
    },
    onError: (error, _paperUids, context) => {
      if (context?.previous) {
        queryClient.setQueryData(key, context.previous);
      }

      refreshAfterConflict(queryClient, uid, error);
    },
    onSuccess: (book) => {
      queryClient.setQueryData(key, book);
      void queryClient.invalidateQueries({ queryKey: bookKeys.lists() });
    },
  });
}

/**
 * After a 409 the book changed on the server (another request edited it, or generation started): the fresh copy is
 * loaded so the page shows what is really there.
 */
function refreshAfterConflict(queryClient: ReturnType<typeof useQueryClient>, uid: string, error: unknown) {
  if (error instanceof ApiError && error.status === 409) {
    void queryClient.invalidateQueries({ queryKey: bookKeys.detail(uid) });
  }
}

/** Renames the book; a completed book goes back to "Uploaded" on the server. */
export function useRenameBook(uid: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (name: string) =>
      unwrap(
        api.PUT('/api/books/{uid}', { params: { path: { uid } }, body: { name } }),
      ) as Promise<BookDetail>,
    onSuccess: (book) => {
      queryClient.setQueryData(bookKeys.detail(uid), book);
      void queryClient.invalidateQueries({ queryKey: bookKeys.lists() });
    },
    onError: (error) => {
      refreshAfterConflict(queryClient, uid, error);
    },
  });
}

/** Replaces the detected title of one paper; a completed book goes back to "Uploaded" on the server. */
export function useSetPaperTitle(uid: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ paperUid, title }: { paperUid: string; title: string }) =>
      unwrap(
        api.PUT('/api/books/{uid}/papers/{paperUid}/title', {
          params: { path: { uid, paperUid } },
          body: { title },
        }),
      ) as Promise<BookDetail>,
    onSuccess: (book) => {
      queryClient.setQueryData(bookKeys.detail(uid), book);
      void queryClient.invalidateQueries({ queryKey: bookKeys.lists() });
    },
    onError: (error) => {
      refreshAfterConflict(queryClient, uid, error);
    },
  });
}

/** Starts generation (202) and refreshes the book so its page switches to the progress view. */
export function useStartGeneration(uid: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/books/{uid}/generate', { params: { path: { uid } } })),
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: bookKeys.detail(uid) });
      void queryClient.invalidateQueries({ queryKey: bookKeys.lists() });
    },
  });
}

/** Moves the book to "Silinenler"; it can be restored from there. */
export function useDeleteBook() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (uid: string) => unwrap(api.DELETE('/api/books/{uid}', { params: { path: { uid } } })),
    onSuccess: (_result, uid) => {
      queryClient.removeQueries({ queryKey: bookKeys.detail(uid) });
      void queryClient.invalidateQueries({ queryKey: bookKeys.lists() });
      void queryClient.invalidateQueries({ queryKey: bookKeys.deletedLists() });
    },
  });
}

export function pdfUrl(book: Pick<BookDetail, 'pdfUrl'>, download = false): string | undefined {
  if (book.pdfUrl == null) {
    return undefined;
  }

  return download ? `${book.pdfUrl}?download=true` : book.pdfUrl;
}
