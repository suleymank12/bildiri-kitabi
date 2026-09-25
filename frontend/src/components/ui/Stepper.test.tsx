import { render, screen } from '@testing-library/react';
import { Stepper } from './Stepper';

const steps = ['Dosyalar', 'Sıra ve kontrol', 'Oluşturma'];

describe('Stepper', () => {
  it('marks the current step for assistive technology', () => {
    render(<Stepper steps={steps} current={1} />);

    const current = screen.getByRole('listitem', { current: 'step' });
    expect(current).toHaveTextContent('2. adım: Sıra ve kontrol');
    expect(screen.getAllByRole('listitem')[0]).toHaveTextContent('(tamamlandı)');
  });

  it('shows the compact line for phones', () => {
    const { container } = render(<Stepper steps={steps} current={1} />);

    const compact = container.querySelector('[aria-hidden="true"] p');
    expect(compact?.textContent).toBe('Adım 2 / 3 · Sıra ve kontrol');
  });
});
