import { useEffect, useRef } from "react";
import { Link, useLocation } from "react-router-dom";
import { CaretDown, ChatCircleDots, FileText, MagnifyingGlass, Microphone, Question, ShieldCheck, Sparkle, SpeakerHigh } from "@phosphor-icons/react";
import styles from "./about-help.module.css";

const topics = [
  ["research", "Research a company", MagnifyingGlass],
  ["review", "Review a company", ShieldCheck],
  ["ask-raven", "Ask RAVEN", ChatCircleDots],
  ["investigate", "Investigate deeper", Sparkle],
  ["briefings", "Create a Briefing", FileText],
  ["profile", "Keep a Profile current", ShieldCheck],
  ["voice", "Voice & speech", SpeakerHigh],
  ["faq", "FAQ", Question],
] as const;

function GuideFlow({ steps }: { steps: Array<[string, string]> }) {
  return <ol className={styles.guideFlow}>{steps.map(([title, copy]) => <li key={title}><strong>{title}</strong><span>{copy}</span></li>)}</ol>;
}

export function HelpPage() {
  const location = useLocation();
  const pageRef = useRef<HTMLDivElement>(null);
  const topicsRef = useRef<HTMLElement>(null);
  useEffect(() => {
    const topics = topicsRef.current;
    if (!topics) return;
    const updateOffset = () => pageRef.current?.style.setProperty("--help-topic-height", `${topics.getBoundingClientRect().height}px`);
    updateOffset();
    const observer = new ResizeObserver(updateOffset);
    observer.observe(topics);
    return () => observer.disconnect();
  }, []);
  return <div className={styles.page} ref={pageRef}>
    <header className={styles.helpHeader}>
      <span className={styles.eyebrow}>Help</span>
      <h1>What do you want to do?</h1>
      <p>Follow the product from public research to reviewed company knowledge, or jump directly to a task.</p>
    </header>
    <nav className={styles.topicNav} aria-label="Help topics" ref={topicsRef}>
      {topics.map(([id, label, Icon]) => <a href={`#${id}`} key={id}><Icon size={16} weight="duotone" aria-hidden="true" /><span>{label}</span></a>)}
    </nav>

    <section className={styles.gettingStarted} aria-labelledby="getting-started-heading">
      <span className={styles.eyebrow}>New to RAVEN?</span>
      <h2 id="getting-started-heading">A reliable first workflow</h2>
      <GuideFlow steps={[
        ["Research a company", "Choose the target and confirm its identity."],
        ["Review sources and identity", "Inspect the public evidence RAVEN found."],
        ["Confirm a Company Profile", "Explicitly accept supported company information."],
        ["Ask grounded questions", "Use the accepted Profile and its evidence."],
        ["Investigate when needed", "Save deeper work as Investigations or Briefings."],
      ]} />
    </section>

    <section id="research" className={styles.guideSection}>
      <span className={styles.eyebrow}>Research a company</span><h2>From target to accepted Profile</h2>
      <GuideFlow steps={[["Research Company", "Choose the target and confirm identity."], ["Review evidence", "Inspect public sources and unsupported gaps."], ["Confirm Profile", "Accept only the supported company information."]]} />
    </section>

    <section id="review" className={styles.guideSection}>
      <span className={styles.eyebrow}>Review a company</span><h2>Keep trust decisions explicit</h2>
      <p>Sources, Investigations, Briefings, changes, and monitoring remain visible beside the accepted Profile. Marking research reviewed does not delete its history or make it accepted truth.</p>
    </section>

    <section id="ask-raven" className={styles.guideSection}>
      <span className={styles.eyebrow}>Ask RAVEN</span><h2>Grounded answers with deliberate expansion</h2>
      <div className={styles.chatGuide}>
        <header><img src="/raven-logo.svg" alt="" /><strong>Ask RAVEN</strong><span>Profile grounded · 35 sources</span></header>
        <blockquote>“What changed recently?”</blockquote>
        <div><span className={styles.chatGuideAction}>Sources <CaretDown size={11} weight="bold" aria-hidden="true" /></span><span className={styles.chatGuideAction}>Search latest</span><span className={styles.chatGuideAction}>Research further</span></div>
        <footer><span>＋ Research context</span><span>Web search</span><span>Deep Research</span></footer>
      </div>
      <dl className={styles.definitionGrid}>
        <div><dt>Sources</dt><dd>Shows evidence supporting the answer.</dd></div>
        <div><dt>Research context</dt><dd>Adds completed Investigations or Briefings without converting them into Profile truth.</dd></div>
        <div><dt>Web search</dt><dd>Allows freshness-sensitive turns to use public Web evidence.</dd></div>
        <div><dt>Search latest</dt><dd>Prepares an editable Web follow-up and waits for confirmation.</dd></div>
        <div><dt>Research further</dt><dd>Prepares deeper research and waits for confirmation.</dd></div>
        <div><dt>Recent chats</dt><dd>Restores company-scoped conversations persisted by RAVEN.</dd></div>
      </dl>
    </section>

    <section id="investigate" className={styles.guideSection}>
      <span className={styles.eyebrow}>Investigate deeper</span><h2>Preserve work worth reviewing</h2>
      <p>An Investigation is durable research material. It may contain unresolved claims, contradictions, external AI material, and source leads; it is not automatically accepted Profile truth.</p>
    </section>

    <section id="briefings" className={styles.guideSection}>
      <span className={styles.eyebrow}>Investigations & Briefings</span><h2>Build a versioned synthesis</h2>
      <GuideFlow steps={[["Question", "Define what needs deeper research."], ["Investigation", "Keep the resulting material for review."], ["Review / Analyze", "Select relevant work and inspect gaps."], ["Add to Briefing", "Synthesize selected research by theme."], ["Briefing v1 → v2 → v3", "Each update creates another immutable version."]]} />
    </section>

    <section id="profile" className={styles.guideSection}>
      <span className={styles.eyebrow}>Keep a Profile current</span><h2>New research still requires confirmation</h2>
      <GuideFlow steps={[["New research", "Monitoring or refreshed research finds a potential change."], ["Workspace Review", "Inspect evidence, contradictions, and scope."], ["User confirmation", "Accept supported changes explicitly."], ["New Profile version", "Preserve the prior version and its provenance."]]} />
    </section>

    <section id="voice" className={styles.guideSection} aria-labelledby="voice-heading">
      <Microphone size={24} weight="duotone" aria-hidden="true" /><span className={styles.eyebrow}>Voice & speech</span><h2 id="voice-heading">Optional input and playback</h2>
      <p>Dictate with browser recognition or Gemini Transcribe Live when configured. Read answers with browser speech synthesis or Gemini TTS when configured. Dictation remains editable and never auto-sends; read aloud never starts automatically.</p>
      <Link to="/settings" state={location.state}>Open Voice & speech settings</Link>
    </section>

    <section id="faq" className={styles.faq} aria-labelledby="faq-heading">
      <span className={styles.eyebrow}>FAQ</span><h2 id="faq-heading">Common questions</h2>
      {[
        ["What does Profile grounded mean?", "The answer starts from the accepted Company Profile and its source-backed evidence."],
        ["What is the difference between a Profile, Investigation and Briefing?", "A Profile is accepted company truth. An Investigation is durable research material. A Briefing is a versioned synthesis of selected research."],
        ["Why doesn't research automatically update the Company Profile?", "RAVEN keeps research separate until a person reviews evidence and explicitly confirms a change."],
        ["When should I use Web Search vs Deep Research?", "Use Web Search for a current follow-up. Use Deep Research for a question that needs broader, durable investigation."],
        ["What happens when I update a Briefing?", "RAVEN creates a new immutable version and preserves the earlier versions."],
        ["Can I delete a Chat?", "Yes. Delete it from Recent chats; its attached Investigations and Briefings are not deleted."],
        ["Why can research continue after I leave a page?", "Deep Research and Briefing generation are server-owned background jobs. Normal Ask RAVEN answers remain request-bound."],
        ["What is the difference between Browser speech and Gemini speech?", "Browser speech uses browser or system capabilities. Gemini speech uses configured Gemini services and their quota."],
        ["Where are speech and device preferences stored?", "They are stored locally in this browser, not in company intelligence."],
        ["Where can I check provider health?", "Open System Status for current provider availability and rate-limit information."],
      ].map(([question, answer]) => <details key={question}><summary>{question}</summary><p>{answer}</p></details>)}
    </section>

    <aside className={styles.troubleshooting} aria-label="Troubleshooting links">
      <div><strong>Something not working?</strong><span>Check service health or review your provider and speech settings.</span></div>
      <Link to="/status" state={location.state}>System Status</Link><Link to="/settings" state={location.state}>Settings</Link>
    </aside>
  </div>;
}
