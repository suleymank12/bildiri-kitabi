/**
 * The server's rules that the browser checks early. Each value mirrors one backend constant or default; change both
 * together. The server has the final say.
 */

/** Papers in a book. */
export const REQUIRED_PAPER_COUNT = 10; // Sunucu: BookOptions.DefaultRequiredPaperCount (Books:RequiredPaperCount)

/** Size of one .docx file. */
export const MAX_FILE_BYTES = 10 * 1024 * 1024; // Sunucu: UploadOptions.DefaultMaxFileSizeBytes (Upload:MaxFileSizeBytes)

/** Size of all files of one upload. */
export const MAX_TOTAL_BYTES = 60 * 1024 * 1024; // Sunucu: UploadOptions.DefaultMaxTotalSizeBytes (Upload:MaxTotalSizeBytes)

/** Book name length after trimming. */
export const BOOK_NAME_MIN = 3; // Sunucu: Book.NameMinLength
export const BOOK_NAME_MAX = 150; // Sunucu: Book.NameMaxLength

/** Paper title length. */
export const PAPER_TITLE_MAX = 500; // Sunucu: Paper.TitleMaxLength

/** The size limits in whole megabytes, for messages. */
export const MAX_FILE_MB = MAX_FILE_BYTES / (1024 * 1024);
export const MAX_TOTAL_MB = MAX_TOTAL_BYTES / (1024 * 1024);
