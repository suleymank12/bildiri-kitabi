/**
 * Pages settling one by one onto a book, drawn in the accent and line colours. Animated with transform and
 * opacity only (see `.page-settle` in theme.css); static when the user prefers reduced motion.
 */
export function BookIllustration() {
  return (
    <svg
      viewBox="0 0 160 120"
      width="160"
      height="120"
      aria-hidden="true"
      focusable="false"
      className="shrink-0 text-accent"
    >
      {/* Book: cover, spine and the page block. */}
      <rect
        x="22"
        y="78"
        width="116"
        height="22"
        rx="3"
        fill="var(--color-accent-soft)"
        stroke="currentColor"
        strokeWidth="1.5"
      />
      <line x1="30" y1="86" x2="130" y2="86" stroke="var(--color-line-strong)" strokeWidth="1" />
      <line x1="30" y1="92" x2="130" y2="92" stroke="var(--color-line-strong)" strokeWidth="1" />

      {[0, 1, 2].map((index) => (
        <g
          key={index}
          className="page-settle"
          style={{ animationDelay: `${String(index * 0.6)}s` }}
          transform={`translate(0 ${String(-index * 7)})`}
        >
          <g>
            <rect
              x="34"
              y="44"
              width="92"
              height="32"
              rx="2"
              fill="var(--color-surface)"
              stroke="currentColor"
              strokeWidth="1.25"
            />
            <line
              x1="42"
              y1="53"
              x2="100"
              y2="53"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
            />
            <line
              x1="42"
              y1="60"
              x2="118"
              y2="60"
              stroke="var(--color-line-strong)"
              strokeWidth="1.5"
              strokeLinecap="round"
            />
            <line
              x1="42"
              y1="67"
              x2="110"
              y2="67"
              stroke="var(--color-line-strong)"
              strokeWidth="1.5"
              strokeLinecap="round"
            />
          </g>
        </g>
      ))}
    </svg>
  );
}
