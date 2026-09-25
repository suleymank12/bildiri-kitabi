import workerSrc from 'pdfjs-dist/build/pdf.worker.min.mjs?url';
import { pdfjs } from 'react-pdf';
import 'react-pdf/dist/Page/AnnotationLayer.css';
import 'react-pdf/dist/Page/TextLayer.css';

// The worker and pdf.js' data files come from the local package (copied to /pdfjs by vite.config.ts), never a CDN.
pdfjs.GlobalWorkerOptions.workerSrc = workerSrc;

/** Document options; one constant object so react-pdf does not reload the document on every render. */
export const pdfOptions = {
  cMapUrl: '/pdfjs/cmaps/',
  cMapPacked: true,
  standardFontDataUrl: '/pdfjs/standard_fonts/',
  wasmUrl: '/pdfjs/wasm/',
  iccUrl: '/pdfjs/iccs/',
};

/** A4 portrait, used until the first page reports its real size. */
export const A4 = { width: 595.28, height: 841.89 };
