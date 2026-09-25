/** A small rotating ring; decorative, the surrounding control carries the state. */
export function Spinner({ className = '' }: { className?: string }) {
  return (
    <span
      aria-hidden="true"
      className={`inline-block size-4 shrink-0 animate-spin rounded-full border-2 border-current border-r-transparent ${className}`}
    />
  );
}
