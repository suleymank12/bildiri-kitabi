import type { components } from './schema';

type Schemas = components['schemas'];

export type BookDetail = Schemas['BookDetailDto'];
export type BookSummary = Schemas['BookSummaryDto'];
export type Paper = Schemas['PaperDto'];
export type BookStatus = Schemas['BookStatus'];
// .NET puts `null` into the enum because the property that uses it is nullable.
export type GenerationStage = NonNullable<Schemas['GenerationStage']>;
export type TitleSource = Schemas['TitleSource'];
export type Problem = Schemas['ProblemResponse'];
export type ProblemItem = Schemas['ApiError'];
export type BookPage = Schemas['PagedResultOfBookSummaryDto'];
