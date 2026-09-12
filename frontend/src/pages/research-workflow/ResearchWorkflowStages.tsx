import { Sparkle } from "@phosphor-icons/react";
import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { Panel } from "../../components/Panel";
import { TextInput } from "../../components/TextInput";
import {
  CandidateSourceCard,
  EvidenceCard,
  type CandidateSource,
  type EvidenceRecord,
} from "../../components/sources";
import type { CompanyResearchWorkflow } from "./types";
import { confidenceLabel, entityTypeLabel, formatDate, matchStrengthLabel, toCandidateSource, toEvidenceRecord, targetLabel } from "./formatters";
import styles from "../research-workspace.module.css";
import { IdentityChoiceList } from "./identity/IdentityChoiceList";
import { IdentityClarificationForm } from "./identity/IdentityClarificationForm";

export function IdentityStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  const { form, view, loading, error, groundingOverride, defaultGroundingMode } = workflow;
  if (view !== "identify" && view !== "matching") return null;

  return (
    <Panel title="Company identity" eyebrow="STEP 01 · IDENTIFY" className={styles.identityPanel}>
      <form onSubmit={workflow.handleIdentitySubmit}>
        <fieldset className={styles.fieldset} disabled={loading && view === "matching"}>
          <legend className={styles.visuallyHidden}>Company identity details</legend>
          <div className={styles.formGrid}>
            <TextInput label="Company name" id="research-name" name="name" autoComplete="organization" value={form.name} onChange={(event) => workflow.updateField("name", event.target.value)} placeholder="e.g. FPT Software" required />
            <TextInput label="Legal name" id="research-legal-name" name="legalName" autoComplete="organization" value={form.legalName} onChange={(event) => workflow.updateField("legalName", event.target.value)} placeholder="Optional registered name" />
            <TextInput label="Website" id="research-website" name="website" type="url" autoComplete="url" value={form.website} onChange={(event) => workflow.updateField("website", event.target.value)} placeholder="https://example.com" />
            <TextInput label="Country" id="research-country" name="country" autoComplete="country-name" value={form.country} onChange={(event) => workflow.updateField("country", event.target.value)} placeholder="e.g. Vietnam" />
            <TextInput label="Registration / tax ID" id="research-registration" name="registrationNumber" autoComplete="off" value={form.registrationNumber} onChange={(event) => workflow.updateField("registrationNumber", event.target.value)} placeholder="Optional identifier" />
            <TextInput label="Headquarters / address" id="research-headquarters" name="headquarters" autoComplete="street-address" value={form.headquarters} onChange={(event) => workflow.updateField("headquarters", event.target.value)} placeholder="Optional research hint" />
          </div>
          <div className="field">
            <label htmlFor="research-hint">Research hint</label>
            <textarea className={styles.textarea} id="research-hint" name="researchHint" value={form.researchHint} onChange={(event) => workflow.updateField("researchHint", event.target.value)} placeholder="What should RAVEN pay attention to?" maxLength={500} rows={3} />
            <p className="field__hint">Hints help discovery; they are not accepted profile facts until public evidence supports them.</p>
          </div>
          <fieldset className={styles.groundingFieldset} aria-describedby="grounding-help">
            <legend className={styles.visuallyHidden}>Identity resolution preferences</legend>
            <div className={styles.groundingHeading}>
              <span className={styles.groundingIcon} aria-hidden="true"><Sparkle size={18} weight="duotone" /></span>
              <div>
                <strong>Identity resolution</strong>
                <span className={styles.groundingDefault}>{groundingOverride === "default" ? `Workspace default · ${defaultGroundingMode}` : groundingOverride === "Always" ? "One-run override · On" : "One-run override · Off"}</span>
              </div>
            </div>
            <p className={styles.groundingHelp} id="grounding-help">Use model knowledge to describe a company identity before public-source discovery. It does not verify company-profile facts.</p>
            <div className={styles.groundingOptions}>
              <label className={styles.groundingOption}>
                <input name="groundingOverride" type="radio" value="default" checked={groundingOverride === "default"} onChange={() => workflow.setGroundingOverride("default")} />
                <span><strong>Use default</strong><small>Auto when needed</small></span>
              </label>
              <label className={styles.groundingOption}>
                <input name="groundingOverride" type="radio" value="Always" checked={groundingOverride === "Always"} onChange={() => workflow.setGroundingOverride("Always")} />
                <span><strong>On</strong><small>Use model assistance for this run</small></span>
              </label>
              <label className={styles.groundingOption}>
                <input name="groundingOverride" type="radio" value="Off" checked={groundingOverride === "Off"} onChange={() => workflow.setGroundingOverride("Off")} />
                <span><strong>Off</strong><small>Ask for a stronger identifier</small></span>
              </label>
            </div>
          </fieldset>
        </fieldset>
        {error && view === "matching" ? <p className="form-error" role="alert">{error}</p> : null}
        <div className="form-actions">
          <Button type="submit" loading={loading}>{view === "matching" ? "Checking for existing companies" : "Research public sources"}</Button>
          <Link className="button button--quiet" to="/companies">Cancel</Link>
        </div>
      </form>
    </Panel>
  );
}

export function MatchStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  const { view, matches, loading } = workflow;
  if (view !== "matching" || matches.length === 0) return null;

  return (
    <Panel title="Existing company found" eyebrow="STEP 02 · REVIEW IDENTITY" className={styles.matchPanel}>
      <p className={styles.panelIntro}>RAVEN found likely existing records. Reuse one to preserve research history, or create a separate company intentionally.</p>
      <div className={styles.matchList}>
        {matches.map((match) => (
          <article className={styles.matchCard} key={match.company.id}>
            <div>
              <p className="eyebrow">{matchStrengthLabel(match.matchStrength)}</p>
              <h3>{match.company.name}</h3>
              {match.company.legalName && <p>{match.company.legalName}</p>}
              <p className={styles.matchReason}>{match.matchReason}</p>
              <dl className={styles.matchMeta}>
                {match.company.website && <div><dt>Website</dt><dd>{match.company.website}</dd></div>}
                {match.company.country && <div><dt>Country</dt><dd>{match.company.country}</dd></div>}
                <div><dt>Last researched</dt><dd>{formatDate(match.company.lastResearchedAt)}</dd></div>
              </dl>
            </div>
            <Button type="button" onClick={() => void workflow.handleResearchExisting(match.company)} loading={loading}>Research existing company</Button>
          </article>
        ))}
      </div>
      <div className={styles.overrideBox}>
        <div><strong>Different company?</strong><p>Create a separate record while keeping this match available for reference.</p></div>
        <Button type="button" tone="secondary" onClick={() => void workflow.createAndDiscover()} loading={loading}>Create separate anyway</Button>
      </div>
    </Panel>
  );
}

/** Pre-search identity gate. No Company or ResearchRun exists at this stage. */
export function PreflightIdentityStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  const { view, preflightResponse, identityGuidance, selectedPreflightEntityId, loading, error } = workflow;
  if ((view !== "preflightIdentity" && view !== "guidedIdentity") || !preflightResponse) return null;

  const input = {
    name: workflow.form.name,
    legalName: workflow.form.legalName || undefined,
    website: workflow.form.website || undefined,
    country: workflow.form.country || undefined,
    registrationNumber: workflow.form.registrationNumber || undefined,
    headquarters: workflow.form.headquarters || undefined,
    researchHint: workflow.form.researchHint || undefined,
  };
  if (view === "guidedIdentity") {
    const guidance = identityGuidance ?? preflightResponse;
    return <Panel title="Refine your company search" eyebrow="GUIDED IDENTITY SEARCH" className={`${styles.identityResolutionPanel} ${styles.guidedIdentityPanel}`}>
      <p className={styles.panelIntro}>Tell RAVEN what distinguishes the organization you have in mind. This does not start public research; it updates the choices you already saw.</p>
      <IdentityClarificationForm input={input} requestedHints={guidance.requestedHints} guided loading={loading} error={error} message={guidance.message || "Add the details that best describe the missing company."} onChange={(field, value) => workflow.updateField(field as keyof typeof workflow.form, value)} onSubmit={(event) => { event.preventDefault(); void workflow.retryGuidedIdentity(); }} />
      <div className={styles.guidedIdentityBack}><Button type="button" tone="quiet" onClick={workflow.returnToIdentityChoices} disabled={loading}>Back to original choices</Button></div>
    </Panel>;
  }
  if (preflightResponse.status === "Ambiguous") {
    return <Panel title="Which organization do you mean?" eyebrow="COMPANY IDENTITY" className={styles.identityResolutionPanel}>
      <p className={styles.panelIntro}>{preflightResponse.message || "Several organizations could match."}</p>
      <IdentityChoiceList entities={preflightResponse.entities} ambiguityType={preflightResponse.ambiguityType} selectedEntityId={selectedPreflightEntityId} onSelect={workflow.setSelectedPreflightEntityId} disabled={loading} />
      <button className={styles.refinementChoice} type="button" aria-labelledby="identity-refinement-choice" onClick={() => void workflow.requestPreflightClarification()} disabled={loading}>
        <Sparkle size={22} weight="duotone" aria-hidden="true" />
        <span><strong id="identity-refinement-choice">Still can’t find it? Help me refine this search</strong><small>Get guided suggestions about the name, website, location, legal identity, or what the company does.</small></span>
      </button>
      {error ? <p className="form-error" role="alert">{error}</p> : null}
      <div className="form-actions"><Button type="button" onClick={() => void workflow.handlePreflightSelection()} loading={loading} disabled={!selectedPreflightEntityId}>Continue with selected organization</Button><Button type="button" tone="quiet" onClick={() => workflow.setView("identify")} disabled={loading}>Back to edit</Button></div>
    </Panel>;
  }
  return <Panel title="A little more information will help" eyebrow="COMPANY IDENTITY" className={styles.identityResolutionPanel}>
    <IdentityClarificationForm input={input} requestedHints={preflightResponse.requestedHints} loading={loading} error={error} message={preflightResponse.message} onChange={(field, value) => workflow.updateField(field as keyof typeof workflow.form, value)} onSubmit={(event) => { event.preventDefault(); void workflow.retryPreflightIdentity(); }} onManualExactName={() => void workflow.researchExactName()} />
  </Panel>;
}

export function IdentityResolutionStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  const { view, identityCandidates, form, selectedIdentityCandidateId, loading, isPaused, selectionError, alternateIdentityHint } = workflow;
  if (view !== "resolvingIdentity") return null;

  return (
    <Panel title="Resolve research target" eyebrow="STEP 03 · AI GROUNDING" className={styles.identityResolutionPanel}>
      <p className={styles.panelIntro}>RAVEN found {identityCandidates.length || "several"} public organizations related to <strong>{form.name || "this search"}</strong>. Parent groups, subsidiaries, affiliates, and same-name companies are shown together so you can choose the exact target before deeper discovery.</p>
      <div className={styles.identityCandidateList} role="radiogroup" aria-label="Possible research targets">
        {identityCandidates.map((candidate) => {
          const candidateLabelId = `identity-candidate-${candidate.id}`;
          return (
            <label className={`${styles.identityCandidateCard} ${selectedIdentityCandidateId === candidate.id ? styles.identityCandidateCardSelected : ""}`} htmlFor={candidateLabelId} key={candidate.id}>
              <input
                id={candidateLabelId}
                name="researchIdentityCandidate"
                type="radio"
                value={candidate.id}
                checked={selectedIdentityCandidateId === candidate.id}
                onChange={() => { workflow.setSelectedIdentityCandidateId(candidate.id); }}
              />
              <span className={styles.identityCandidateCopy}>
                <span className={styles.identityCandidateTopline}>
                  <strong>{candidate.displayName}</strong>
                  {candidate.recommended && <span className={styles.identityRecommended}>Recommended</span>}
                </span>
                <span className={styles.identityCandidateMeta}>{entityTypeLabel(candidate.entityType)} · {confidenceLabel(candidate.confidence)}</span>
                <span className={styles.identityCandidateDetails}>
                  {[candidate.legalName, candidate.country, candidate.officialDomain || candidate.website].filter(Boolean).join(" · ") || "Public identity details are still limited."}
                </span>
                {candidate.relationshipHint && <span className={styles.identityCandidateRelationship}>{candidate.relationshipHint}</span>}
                {candidate.rationale && <span className={styles.identityCandidateRationale}>{candidate.rationale}</span>}
              </span>
            </label>
          );
        })}
      </div>
      {selectionError ? <p className="form-error" role="alert">{selectionError}</p> : null}
      <div className={styles.alternateIdentityBox}>
        <div>
          <strong>Can’t find the intended organization?</strong>
          <p>Search again with a subsidiary, brand, legal name, or another identifying phrase.</p>
        </div>
        <div className={styles.alternateIdentityForm}>
          <TextInput
            id="alternate-identity"
            name="alternateIdentity"
            label="Another organization name"
            value={alternateIdentityHint}
            onChange={(event) => workflow.setAlternateIdentityHint(event.target.value)}
            placeholder="e.g. FPT IS or FPT Telecom"
            disabled={loading || isPaused}
          />
          <Button type="button" tone="secondary" onClick={() => void workflow.handleAlternateIdentitySearch()} loading={loading} disabled={!alternateIdentityHint.trim() || isPaused}>Search this name</Button>
        </div>
      </div>
      <div className="form-actions">
        <Button type="button" onClick={() => void workflow.handleSelectIdentityCandidate()} loading={loading} disabled={identityCandidates.length === 0 || isPaused}>Research selected company</Button>
        <Button type="button" tone="secondary" onClick={workflow.focusIdentityForm} disabled={loading}>Back to edit</Button>
        <Button type="button" tone="quiet" onClick={() => void workflow.handleContinueWithoutGrounding()} disabled={loading}>Continue without grounding</Button>
      </div>
    </Panel>
  );
}

export function CandidateReviewStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  const { view, candidates, run, selectedCount, loading, isPaused, selectionError } = workflow;
  if (view !== "reviewingSources" && view !== "acquiring") return null;

  return (
    <Panel title="Review source candidates" eyebrow="STEP 03 · SOURCE SELECTION" className={styles.sourcesPanel}>
      <div className={styles.sectionSummary}>
        <div><strong>{candidates.length} unique source{candidates.length === 1 ? "" : "s"}</strong><p>{run?.recommendedCandidates ?? 0} recommended by RAVEN · {selectedCount} selected</p></div>
        <span className={styles.selectionPill}>{selectedCount} selected</span>
      </div>
      {candidates.length > 0 ? (
        <div className={styles.candidateGrid}>
          {candidates.map((candidate) => (
            <CandidateSourceCard key={candidate.id} candidate={toCandidateSource(candidate)} disabled={loading || isPaused} onSelectionChange={(selected) => workflow.updateCandidateSelection(candidate.id, selected)} />
          ))}
        </div>
      ) : (
        <p className="empty-state">No public source candidates were found. Try another identity or research hint.</p>
      )}
      {selectionError ? <p className="form-error" role="alert">{selectionError}</p> : null}
      <div className="form-actions">
        <Button type="button" onClick={() => void workflow.handleAcquire()} loading={view === "acquiring"} disabled={candidates.length === 0 || isPaused}>Acquire {selectedCount} selected source{selectedCount === 1 ? "" : "s"}</Button>
      </div>
    </Panel>
  );
}

export function EvidenceReviewStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  const { view, sources, run, coverage, candidates, coverageGaps, strengtheningTargets, loading, isPaused } = workflow;
  if (view !== "reviewingEvidence") return null;

  const failedCandidates = candidates.filter((candidate) => candidate.acquisitionStatus === "Failed" || candidate.acquisitionStatus === "Unavailable" || candidate.acquisitionStatus === "DuplicateSkipped");
  return (
    <Panel title="Evidence ready" eyebrow="STEP 04 · REVIEW EVIDENCE" className={styles.evidencePanel}>
      <div className={styles.sectionSummary}>
        <div><strong>{sources.length} acquired source{sources.length === 1 ? "" : "s"}</strong><p>Review the public pages RAVEN preserved before generating a Company Profile.</p></div>
        <span className={styles.successPill}>{run?.documentsAdded ?? sources.length} documents added</span>
      </div>
      {coverage ? <section className={styles.coverageSummary} aria-label="Research coverage"><h3>Research coverage</h3><ul>{coverage.items.map((item) => <li key={item.target}><span>{targetLabel(item.target)}</span><strong>{item.level}</strong></li>)}</ul>{coverage.budgetExhausted ? <p>Research budget reached. Remaining gaps stay unknown.</p> : null}</section> : null}
      {sources.length > 0 ? <div className={styles.evidenceGrid}>{sources.map((source) => <EvidenceCard key={source.id} evidence={toEvidenceRecord(source)} />)}</div> : <p className="empty-state">No source documents were acquired. The selected sources may have been unavailable.</p>}
      {failedCandidates.length > 0 ? (
        <div className={styles.acquisitionIssues} role="status">
          <h3>Sources not added as evidence</h3>
          <ul>
            {failedCandidates.map((candidate) => (
              <li key={candidate.id}><strong>{candidate.title || candidate.domain}</strong> — {candidate.acquisitionStatus === "DuplicateSkipped" ? "duplicate content skipped" : candidate.acquisitionError || "source unavailable"}</li>
            ))}
          </ul>
        </div>
      ) : null}
      {coverageGaps.length > 0 ? <fieldset className={styles.coverageTargets}><legend>Areas to strengthen</legend>{coverageGaps.map((target) => <label key={target}><input type="checkbox" checked={strengtheningTargets.includes(target)} onChange={() => workflow.toggleStrengtheningTarget(target)} /> {targetLabel(target)}</label>)}</fieldset> : null}
      <div className={styles.profileNextStep}>
        <div><p className="eyebrow">NEXT · AI PROFILE</p><h3>Generate a grounded Company Profile</h3><p>Gemini profile generation will use these preserved documents and attach evidence references.</p></div>
        <div className="form-actions"><Button type="button" onClick={() => void workflow.handleStrengthenDossier()} loading={loading} disabled={strengtheningTargets.length === 0 || isPaused}>Strengthen dossier</Button><Button type="button" onClick={() => void workflow.handleGenerateProfile()} loading={loading} tone="secondary" disabled={isPaused}>Generate profile now</Button></div>
      </div>
    </Panel>
  );
}

export function ProfileReviewStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  const { view, profileCandidate, profileWarnings, loading } = workflow;
  if (view !== "reviewingProfile" || !profileCandidate) return null;

  return (
    <Panel title="Company Profile preview" eyebrow="STEP 05 · REVIEW PROFILE" className={styles.evidencePanel}>
      <p className="page-intro">This is a generated candidate, not accepted company truth. Confirm only after reviewing its evidence.</p>
      <dl className="definition-list"><div><dt>Summary</dt><dd>{profileCandidate.summary || "Not verified"}</dd></div><div><dt>Industry</dt><dd>{profileCandidate.primaryIndustry || "Not verified"}</dd></div><div><dt>Scale</dt><dd>{profileCandidate.employeeCountRange || profileCandidate.companySize || "Not verified"}</dd></div><div><dt>Evidence groups</dt><dd>{profileCandidate.evidence.length}</dd></div></dl>
      {profileCandidate.productsServices.length ? <section><h3>Products &amp; services</h3><ul>{profileCandidate.productsServices.map((item) => <li key={`${item.name}-${item.type}`}>{item.name}{item.description ? ` â€” ${item.description}` : ""}</li>)}</ul></section> : null}
      {profileWarnings.length ? <div className={styles.acquisitionIssues} role="status"><h3>Validation notes</h3><ul>{profileWarnings.map((warning) => <li key={warning}>{warning}</li>)}</ul></div> : null}
      <div className="form-actions"><Button type="button" onClick={() => void workflow.handleConfirmProfile()} loading={loading}>Confirm Profile</Button><Button type="button" tone="secondary" onClick={() => workflow.setView("reviewingEvidence")}>Back to Evidence</Button></div>
    </Panel>
  );
}

export function GeneratingProfileStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  if (workflow.view !== "generatingProfile") return null;
  return <Panel title="Building Company Profile" eyebrow="STEP 05 · GEMINI"><p aria-live="polite">Gemini is normalizing only acquired evidence. RAVEN will show a candidate for confirmation when it returns.</p></Panel>;
}

export function CompletionStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  if (workflow.view !== "completed" || !workflow.company) return null;
  return <Panel title="Company Profile confirmed" eyebrow="RESEARCH COMPLETE"><p>Your accepted dossier is now versioned and traceable to selected evidence.</p><Link className="button" to={`/companies/${workflow.company.id}`}>Open Company workspace</Link></Panel>;
}

export function FailureStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  if (workflow.view !== "failed" || !workflow.error) return null;
  return <Panel title="Research needs attention" eyebrow="RESEARCH FAILED" className={styles.failurePanel}><p className="form-error" role="alert">{workflow.error}</p><Button type="button" tone="secondary" onClick={workflow.resetAfterFailure}>Start over</Button></Panel>;
}

export type { CandidateSource, EvidenceRecord };
