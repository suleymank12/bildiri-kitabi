export const paths = {
  library: '/',
  deleted: '/silinenler',
  book: (uid: string) => `/kitaplar/${uid}`,
  editBook: (uid: string) => `/kitaplar/${uid}/duzenle`,
};
