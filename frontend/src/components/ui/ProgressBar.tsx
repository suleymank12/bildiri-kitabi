export interface ProgressBarProps {
  /** 0–100; leave out while the amount of work left is unknown (indeterminate). */
  value?: number | undefined;
  label: string;
  /** Read by screen readers instead of the bare percentage, for example the current stage. */
  valueText?: string | undefined;
}

export function ProgressBar({ value, label, valueText }: ProgressBarProps) {
  const clamped = value === undefined ? undefined : Math.max(0, Math.min(100, Math.round(value)));
  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={clamped}
      aria-valuetext={valueText}
      className="relative h-2 w-full overflow-hidden rounded-full bg-line"
    >
      {clamped === undefined ? (
        <div className="progress-indeterminate absolute inset-y-0 w-1/3 rounded-full bg-accent" />
      ) : (
        <div
          className="h-full rounded-full bg-accent transition-[width] duration-300"
          style={{ width: `${clamped}%` }}
        />
      )}
    </div>
  );
}
