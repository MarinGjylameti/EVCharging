import { Brand } from "@/components/brand";
import { LoginForm } from "@/features/auth/login-form";

export default function Home() {
  return <main className="auth-page">
    <section className="brand-panel" aria-label="EVCharging">
      <Brand light/>
      <div className="hero-copy"><span className="eyebrow">NJË RRJET. NJË PAMJE E QARTË.</span><h2>Energjia që<br/>na çon <em>përpara.</em></h2><p>Një hapësirë për pikat e karikimit,<br/>njerëzit dhe çdo lidhje mes tyre.</p></div>
      <div className="network-art" aria-hidden="true">
        <div className="orbit orbit-one"/><div className="orbit orbit-two"/><div className="orbit orbit-three"/>
        <div className="network-core"><svg viewBox="0 0 24 24"><path d="m13.5 2-8 12h6l-1 8 8-12h-6l1-8Z" fill="currentColor"/></svg></div>
        <i className="network-node node-one"/><i className="network-node node-two"/><i className="network-node node-three"/>
      </div>
      <footer className="brand-footer"><span>EV CHARGING MANAGEMENT</span><span>Designed to keep you moving ↗</span></footer>
    </section>
    <section className="login-panel"><div className="mobile-brand"><Brand/></div><LoginForm/><footer className="login-footer">EVCharging <span>Menaxhim i karikimit elektrik</span></footer></section>
  </main>;
}
