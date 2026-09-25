/** Placeholder block while data loads; hidden from assistive technology (the page announces loading itself). */
export function Skeleton({ className = '' }: { className?: string }) {
  return <div aria-hidden="true" className={`animate-pulse rounded-(--radius-sm) bg-line/70 ${className}`} />;
}
