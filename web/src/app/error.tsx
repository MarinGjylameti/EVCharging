"use client";
export default function ErrorPage({ reset }: { reset: () => void }) {
  return <main className="simple-page"><h1>Shërbimi nuk është i disponueshëm</h1><p>Lidhja me serverin dështoi. Provo përsëri pas pak.</p><button className="button-primary" onClick={reset}>Provo përsëri</button><a href="/">Kthehu te hyrja</a></main>;
}
