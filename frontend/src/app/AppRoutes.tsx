import { Navigate, Route, Routes } from 'react-router';
import { BookPage } from '../features/book/BookPage';
import { DeletedBooksPage } from '../features/deleted/DeletedBooksPage';
import { EditBookPage } from '../features/edit/EditBookPage';
import { LibraryPage } from '../features/library/LibraryPage';
import { Layout } from './Layout';
import { NotFoundPage } from './NotFoundPage';
import { paths } from './paths';

export function AppRoutes() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<LibraryPage />} />
        {/* The list used to live here; old links and bookmarks still work. */}
        <Route path="kitaplar" element={<Navigate to={paths.library} replace />} />
        <Route path="kitaplar/:uid" element={<BookPage />} />
        <Route path="kitaplar/:uid/duzenle" element={<EditBookPage />} />
        <Route path="silinenler" element={<DeletedBooksPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}
