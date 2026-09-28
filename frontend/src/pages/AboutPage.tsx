import { Binoculars, ChatCircleDots, FileText, ShieldCheck } from "@phosphor-icons/react";
import styles from "./about-help.module.css";

const capabilities = [
  { icon: Binoculars, title: "Research", copy: "Find and preserve public-source company evidence." },
  { icon: FileText, title: "Investigations", copy: "Keep deeper research as durable, reviewable material." },
  { icon: ShieldCheck, title: "Profiles & provenance", copy: "Accept company truth explicitly with source-backed version history." },
  { icon: ChatCircleDots, title: "Ask RAVEN & Briefings", copy: "Understand accepted and selected research context without silently changing the Profile." },
];

export function AboutPage() {
  return <div className={styles.page}>
    <section className={styles.hero} aria-labelledby="about-heading">
      <div className={styles.heroMark} aria-hidden="true"><img src="/raven-logo.svg" alt="" /></div>
      <div>
        <span className={styles.eyebrow}>About RAVEN</span>
        <h1 id="about-heading">Company intelligence you can inspect, not just generate.</h1>
        <p>RAVEN turns public-source research into reviewable, evidence-backed company knowledge.</p>
      </div>
      <ol className={styles.flow} aria-label="RAVEN knowledge flow">
        {['Public sources', 'Research', 'Human review', 'Accepted Profile'].map((step) => <li key={step}>{step}</li>)}
      </ol>
    </section>

    <section aria-labelledby="capabilities-heading">
      <span className={styles.eyebrow}>What RAVEN does</span>
      <h2 id="capabilities-heading">One workspace from discovery to trusted context</h2>
      <div className={styles.cardGrid}>
        {capabilities.map(({ icon: Icon, title, copy }) => <article className={styles.capabilityCard} key={title}>
          <Icon size={22} weight="duotone" aria-hidden="true" />
          <h3>{title}</h3><p>{copy}</p>
        </article>)}
      </div>
    </section>

    <section className={styles.trustSection} aria-labelledby="trust-heading">
      <div>
        <span className={styles.eyebrow}>Trust model</span>
        <h2 id="trust-heading">Research remains research until a person accepts it.</h2>
        <p>RAVEN keeps generated and imported material distinct from the accepted Company Profile.</p>
      </div>
      <ol className={`${styles.flow} ${styles.flowVertical}`} aria-label="Profile trust flow">
        {['Public sources', 'Research material', 'Human review', 'Accepted Profile'].map((step) => <li key={step}>{step}</li>)}
      </ol>
      <div className={styles.principles}>
        <article><strong>Evidence-backed</strong><span>Accepted values retain traceable evidence.</span></article>
        <article><strong>Versioned</strong><span>Important accepted and generated records preserve history.</span></article>
        <article><strong>Human-confirmed</strong><span>Research never silently becomes accepted Profile truth.</span></article>
      </div>
    </section>

    <section aria-labelledby="product-map-heading">
      <span className={styles.eyebrow}>Product map</span>
      <h2 id="product-map-heading">The same model across every surface</h2>
      <div className={styles.previewGrid}>
        <article className={styles.miniPreview}>
          <header><span>Company workspace</span><i>Profile ready</i></header>
          <nav aria-label="Illustrative company workspace navigation"><b>Overview</b><span>Sources</span><span>Investigations</span><span>Briefings</span></nav>
          <div><strong>Accepted company profile</strong><small>Source-backed fields and version history</small></div>
        </article>
        <article className={styles.miniPreview}>
          <header><span>Ask RAVEN</span><i>Profile grounded</i></header>
          <div className={styles.miniAnswer}><strong>Ask with citations and context</strong><small>Use selected research or current public information when needed.</small></div>
          <footer><span>Sources</span><span>Research context</span><span>Web</span></footer>
        </article>
      </div>
    </section>

    <footer className={styles.technology}>
      <strong>Built for reviewable public-source intelligence</strong>
      <div><span>React + TypeScript</span><span>ASP.NET Core</span><span>SQLite + EF Core</span><span>Brave Search</span><span>Crawl4AI</span><span>Exa</span><span>Gemini</span></div>
      <p>RAVEN focuses on public-source company intelligence, durable research, and human-confirmed company profiles.</p>
    </footer>
  </div>;
}
