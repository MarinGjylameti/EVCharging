export function Brand({ light = false }: { light?: boolean }) {
  return <span className={`brand ${light ? "brand-light" : ""}`}><span className="brand-icon" aria-hidden="true">
    <svg viewBox="0 0 24 24" fill="none"><path d="m13.5 2-8 12h6l-1 8 8-12h-6l1-8Z" fill="currentColor"/></svg>
  </span><span>EV<span className="brand-weight">Charging</span><small>CONNECTED ENERGY</small></span></span>;
}
