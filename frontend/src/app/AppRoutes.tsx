import { Route, Routes } from 'react-router';
import { BookPage } from '../features/book/BookPage';
import { NewBookPage } from '../features/new-book/NewBookPage';
import { Layout } from './Layout';
import { NotFoundPage } from './NotFoundPage';

export function AppRoutes() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<NewBookPage />} />
        <Route path="kitaplar/:id" element={<BookPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}
