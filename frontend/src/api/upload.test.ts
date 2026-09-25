import { ApiError } from './errors';
import { uploadBook } from './upload';

type Listener = (event: ProgressEvent) => void;

/** Just enough of XMLHttpRequest to drive uploadBook without a network. */
class FakeXhr {
  static last: FakeXhr | undefined;
  method = '';
  url = '';
  body: FormData | undefined;
  status = 0;
  responseText = '';
  headers = new Map<string, string>();
  requestHeaders = new Map<string, string>();
  private listeners = new Map<string, Listener[]>();
  readonly upload = {
    listeners: [] as Listener[],
    addEventListener: (_type: string, listener: Listener) => {
      this.upload.listeners.push(listener);
    },
  };

  constructor() {
    FakeXhr.last = this;
  }

  open(method: string, url: string) {
    this.method = method;
    this.url = url;
  }

  setRequestHeader(name: string, value: string) {
    this.requestHeaders.set(name, value);
  }

  getResponseHeader(name: string) {
    return this.headers.get(name) ?? null;
  }

  addEventListener(type: string, listener: Listener) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  send(body: FormData) {
    this.body = body;
  }

  progress(loaded: number, total: number) {
    for (const listener of this.upload.listeners) {
      listener({ lengthComputable: true, loaded, total } as ProgressEvent);
    }
  }

  respond(status: number, body: unknown, headers: Record<string, string> = {}) {
    this.status = status;
    this.responseText = JSON.stringify(body);
    this.headers = new Map(Object.entries(headers));
    this.fire('load');
  }

  fire(type: string) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener({} as ProgressEvent);
    }
  }
}

function start(files = [new File(['a'], '01.docx'), new File(['b'], '02.docx')]) {
  vi.stubGlobal('XMLHttpRequest', FakeXhr);
  const progress: number[] = [];
  const promise = uploadBook('Örnek Kitap', files, (percent) => progress.push(percent));
  const xhr = FakeXhr.last!;
  return { promise, xhr, progress };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('uploadBook', () => {
  it('posts the name and the files in list order and reports progress', async () => {
    const { promise, xhr, progress } = start();

    expect(xhr.method).toBe('POST');
    expect(xhr.url).toBe('/api/books');
    expect(xhr.body?.get('name')).toBe('Örnek Kitap');
    expect(xhr.body?.getAll('files').map((file) => (file as File).name)).toEqual(['01.docx', '02.docx']);

    xhr.progress(25, 100);
    xhr.progress(75, 100);
    xhr.respond(201, { id: 'kitap-1' });

    await expect(promise).resolves.toEqual({ id: 'kitap-1' });
    expect(progress).toEqual([25, 75, 100]);
  });

  it('turns a problem response into an ApiError with the retry time', async () => {
    const { promise, xhr } = start();

    xhr.respond(
      429,
      { title: 'x', status: 429, detail: 'Çok fazla istek.', code: 'RATE_LIMITED' },
      { 'Retry-After': '30' },
    );

    const error = await promise.catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(429);
    expect((error as ApiError).code).toBe('RATE_LIMITED');
    expect((error as ApiError).retryAfterSeconds).toBe(30);
  });

  it('reports a network failure as status 0', async () => {
    const { promise, xhr } = start();

    xhr.fire('error');

    const error = await promise.catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).isNetworkError).toBe(true);
  });
});
