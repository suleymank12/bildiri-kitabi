import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { TOUCH_TOOLTIP_MS, Tooltip } from './Tooltip';

function Example({ content = 'Açıklama' }: { content?: string }) {
  return (
    <>
      <button type="button">Önceki</button>
      <Tooltip content={content}>
        {(trigger) => (
          <button type="button" {...trigger}>
            Düğme
          </button>
        )}
      </Tooltip>
      <p>Başka bir yer</p>
    </>
  );
}

describe('Tooltip', () => {
  it('describes the trigger through aria-describedby, also while closed', () => {
    render(<Example />);

    const button = screen.getByRole('button', { name: 'Düğme' });
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    expect(button).toHaveAccessibleDescription('Açıklama');
    const tooltip = document.getElementById(button.getAttribute('aria-describedby')!);
    expect(tooltip).toHaveAttribute('role', 'tooltip');
  });

  it('opens while the mouse is over the trigger and closes as soon as it leaves', async () => {
    const user = userEvent.setup();
    render(<Example />);
    const button = screen.getByRole('button', { name: 'Düğme' });

    await user.hover(button);
    expect(screen.getByRole('tooltip')).toHaveTextContent('Açıklama');

    await user.unhover(button);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });

  it('ignores the pointer itself', async () => {
    const user = userEvent.setup();
    render(<Example />);

    await user.hover(screen.getByRole('button', { name: 'Düğme' }));
    expect(screen.getByRole('tooltip')).toHaveClass('pointer-events-none');
  });

  it('opens on keyboard focus and closes on blur', async () => {
    const user = userEvent.setup();
    render(<Example />);

    await user.tab();
    await user.tab();
    expect(screen.getByRole('button', { name: 'Düğme' })).toHaveFocus();
    expect(screen.getByRole('tooltip')).toBeVisible();

    await user.tab({ shift: true });
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });

  it('closes on Esc and keeps the focus on the trigger', async () => {
    const user = userEvent.setup();
    render(<Example />);
    const button = screen.getByRole('button', { name: 'Düğme' });
    act(() => {
      button.focus();
    });
    expect(screen.getByRole('tooltip')).toBeInTheDocument();

    await user.keyboard('{Escape}');

    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    expect(button).toHaveFocus();
  });

  it('uses up the Esc that closes it, so a modal dialog around it does not start to close', async () => {
    const user = userEvent.setup();
    render(<Example />);
    const button = screen.getByRole('button', { name: 'Düğme' });

    // Opened by the mouse, with the focus elsewhere: the key reaches the document.
    await user.hover(button);
    const fromDocument = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    act(() => {
      document.body.dispatchEvent(fromDocument);
    });
    expect(fromDocument.defaultPrevented).toBe(true);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();

    // Opened by focus: the key reaches the trigger first.
    act(() => {
      button.focus();
    });
    const onTrigger = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    act(() => {
      button.dispatchEvent(onTrigger);
    });
    expect(onTrigger.defaultPrevented).toBe(true);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();

    // Without an open bubble, Esc is left alone.
    const idle = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    act(() => {
      button.dispatchEvent(idle);
    });
    expect(idle.defaultPrevented).toBe(false);
  });

  it('opens on a tap and closes after a few seconds', () => {
    vi.useFakeTimers();
    try {
      render(<Example />);
      const button = screen.getByRole('button', { name: 'Düğme' });

      fireEvent.pointerDown(button, { pointerType: 'touch' });
      fireEvent.focus(button);
      expect(screen.getByRole('tooltip')).toBeInTheDocument();

      act(() => {
        vi.advanceTimersByTime(TOUCH_TOOLTIP_MS);
      });
      expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it('closes a tapped tooltip on a tap elsewhere', () => {
    render(<Example />);
    fireEvent.pointerDown(screen.getByRole('button', { name: 'Düğme' }), { pointerType: 'touch' });
    expect(screen.getByRole('tooltip')).toBeInTheDocument();

    fireEvent.pointerDown(screen.getByText('Başka bir yer'), { pointerType: 'touch' });

    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });

  it('does not open for a touch pointer entering the trigger', () => {
    render(<Example />);

    fireEvent.pointerEnter(screen.getByRole('button', { name: 'Düğme' }), { pointerType: 'touch' });

    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });

  it('has no tooltip and no description without content, and keeps the trigger mounted', async () => {
    const user = userEvent.setup();
    function Toggle() {
      const [content, setContent] = useState<string>();
      return (
        <Tooltip content={content}>
          {(trigger) => (
            <button
              type="button"
              {...trigger}
              onClick={() => {
                setContent('Artık açıklamalı');
              }}
            >
              Düğme
            </button>
          )}
        </Tooltip>
      );
    }
    render(<Toggle />);
    const button = screen.getByRole('button', { name: 'Düğme' });

    await user.hover(button);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    expect(button).not.toHaveAttribute('aria-describedby');

    await user.click(button);
    expect(screen.getByRole('button', { name: 'Düğme' })).toBe(button);
    expect(button).toHaveFocus();
    expect(button).toHaveAccessibleDescription('Artık açıklamalı');
  });
});
