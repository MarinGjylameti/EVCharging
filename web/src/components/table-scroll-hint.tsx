export function TableScrollHint({sq}: {sq: boolean}) {
  return <p className="table-scroll-hint">{sq ? "↔ Rrëshqit tabelën për të parë të gjitha kolonat." : "↔ Swipe the table to see all columns."}</p>;
}
