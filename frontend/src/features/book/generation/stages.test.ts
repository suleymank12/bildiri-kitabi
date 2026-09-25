import { currentStepLabel, stageSteps } from './stages';

const states = (book: Parameters<typeof stageSteps>[0]) => stageSteps(book).map((step) => step.state);

describe('stageSteps', () => {
  it('lists the queue and every server stage with Turkish labels', () => {
    expect(stageSteps({ status: 'Queued', stage: null }).map((step) => step.label)).toEqual([
      'Sırada bekliyor',
      'Belgeler okunuyor',
      'İletişim bilgileri temizleniyor',
      'Sayfa düzeni hazırlanıyor',
      'PDF oluşturuluyor',
      'Son kontroller yapılıyor',
      'Kaydediliyor',
    ]);
  });

  it('marks the queue as current while the book waits', () => {
    expect(states({ status: 'Queued', stage: null })).toEqual([
      'current',
      'pending',
      'pending',
      'pending',
      'pending',
      'pending',
      'pending',
    ]);
  });

  it('starts with reading when a claimed job has not reported yet', () => {
    expect(states({ status: 'Processing', stage: null })).toEqual([
      'done',
      'current',
      'pending',
      'pending',
      'pending',
      'pending',
      'pending',
    ]);
  });

  it.each([
    ['Reading', 1],
    ['Sanitizing', 2],
    ['Composing', 3],
    ['Rendering', 4],
    ['Verifying', 5],
    ['Saving', 6],
  ] as const)('processing %s: earlier steps done, later pending', (stage, index) => {
    const result = states({ status: 'Processing', stage });
    expect(result[index]).toBe('current');
    expect(result.slice(0, index).every((state) => state === 'done')).toBe(true);
    expect(result.slice(index + 1).every((state) => state === 'pending')).toBe(true);
  });

  it('marks everything done when the book is ready', () => {
    expect(states({ status: 'Completed', stage: null }).every((state) => state === 'done')).toBe(true);
  });

  it('marks the stage where a failed book stopped', () => {
    expect(states({ status: 'Failed', stage: 'Rendering' })).toEqual([
      'done',
      'done',
      'done',
      'done',
      'failed',
      'pending',
      'pending',
    ]);
  });

  it('names the running step', () => {
    expect(currentStepLabel({ status: 'Processing', stage: 'Verifying' })).toBe('Son kontroller yapılıyor');
    expect(currentStepLabel({ status: 'Completed', stage: null })).toBeUndefined();
  });
});
