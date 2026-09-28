/*
 * Desktop (md and up) column layouts of the two tables. The heading row and every data row use the same
 * constant, so a heading can never drift away from its values. Phones get cards instead; nothing here applies
 * below md.
 */

/** Selected files: order, file name, size, status, remove. */
export const FILE_TABLE_COLUMNS = 'md:grid-cols-[2.5rem_minmax(0,1fr)_6rem_8rem_7rem] md:gap-x-6';

/** "Kitaplarım": book, status, paper count, page count, created, edit and delete. */
export const LIBRARY_TABLE_COLUMNS = 'md:grid-cols-[minmax(0,1fr)_7rem_5rem_5rem_10rem_11.5rem] md:gap-x-6';

/** Short numbers of a fixed shape (counts, sizes): centred under their heading, heading included. */
export const NUMERIC_COLUMN = 'md:text-center';
