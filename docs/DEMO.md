# RAVEN Day-6 customer / mentor demonstration

## Purpose

Tell one coherent story: a researcher turns an ambiguous company name into a trustworthy, evidence-backed dossier, then keeps that workspace clean and usable. This is a product demonstration, not a tour of every button.

**Duration:** 10–12 minutes.
**Audience:** customer, mentor, or evaluator.
**Presenter posture:** show what RAVEN knows, what it does not know, and why the researcher remains in control.

## Before the meeting

- Start the API and frontend. Use fixture-backed data for lifecycle, merge, and enrichment unless a controlled live-provider run has been prepared.
- Keep one well-supported company dossier and one intentionally sparse/disposable company available. Keep a known duplicate pair available for workspace review.
- If live Gemini is enabled, confirm it is a controlled smoke: label it as live and do not expose provider credentials or raw responses. The rest of the demo works with fixtures.
- Do not demo permanent deletion against a record the audience may want later. It is deliberate and irreversible.

## Presenter flow

### 1. Establish the problem — 45 seconds

> “Company research normally starts with a name, scattered sources, and a lot of uncertainty. RAVEN gives us a durable dossier, but it will not turn a guess into a fact.”

Open **Company Research**. Point out that it begins with identity resolution, before Search or Crawl. Explain that this prevents expensive research on the wrong company.

### 2. Resolve an ambiguous company — 2 minutes

Enter `FPT`, country `Vietnam`.

> “FPT is a corporate-family shorthand, so RAVEN presents the known parent first and asks us to choose an entity. It has not started public-source research yet.”

Select the intended organization. Show that duplicate matching occurs before a new research run starts. Then enter `FPT Software` to show a direct resolution. Optionally enter `Viettel` to show another family ambiguity, or `Sun Property` to show the minimum useful clarification request.

Use **Still can’t find the right organization?** once. Minimize and reopen the guidance, then choose **Edit search details**. Emphasize that the original form stays the single source of input; the guidance does not invent a second search path.

### 3. Research with evidence, not confidence theatre — 2 minutes

Continue with the resolved company. Review the complementary recommended roots, select the relevant ones, and acquire them.

> “These are research roots, not five arbitrary documents. One approved official root can yield bounded same-domain evidence; unsupported fields remain unknown.”

Show source classification and coverage. Generate a profile candidate, point out its evidence relationships, and explicitly distinguish it from the accepted profile. Confirm only after review.

### 4. Show that the dossier is safely reusable — 2 minutes

Open the accepted dossier and briefly show Overview, Sources, Changes, and Monitoring.

Open **Improve profile**. Show the targeted enrichment dialog and explain that it produces a patch candidate limited to its selected research targets; unrelated accepted fields remain intact. Confirm a prepared patch and show the appended immutable version in **Changes**.

Then open **Monitor company** from the Company List.

> “This goes directly to Monitoring, not just the generic profile page. Monitoring creates work for review; it never auto-edits the accepted dossier.”

For the sparse company, choose **Improve profile** or **Monitor company**.

> “RAVEN explains why that action is unavailable: there is no accepted profile yet. It offers the honest next step—refresh research or review the workspace—rather than an empty screen.”

### 5. Keep the workspace clean without losing knowledge — 2 minutes

From Company List, choose **Review workspace** and open a duplicate merge preview.

> “A merge is explicit and transactional. RAVEN preserves research runs, evidence, profile versions, monitoring, and saved investigations. It also carries missing stable identity values from the richer record into the retained company.”

Confirm the prepared merge. The application opens the canonical dossier immediately. Show that the richer/latest accepted profile is still visible and that profile history remains available. Do not claim that a merge proves two companies are the same—the user made that decision.

Optionally show archive/restore for the disposable record. Mention permanent deletion only as an explicit, confirmed administrative action.

### 6. Close on boundaries and next step — 1 minute

Open the Ask RAVEN dock.

> “The UI knows the company context and offers Quick and Deep modes. The conversation backend is intentionally pending its published contract, so RAVEN does not fabricate an answer today.”

Close with: “The value is not merely a generated company description. It is a reviewable, versioned company knowledge record whose claims can be traced back to preserved public evidence.”

## Recovery paths to demonstrate only if asked

- Cancel an in-progress research run. RAVEN returns to the blank research form and clears the saved run, ready for a new search.
- Reopen the application with a stale saved run. A missing, cancelled, failed, completed, or legacy-unrestorable run is discarded rather than leaving the user on a misleading “company not found” state.
- If a provider fails, show the source-level failure and bounded fallback behavior. Never pretend the source was acquired.

## Demo truthfulness rules

- Label fixture-backed screens separately from controlled live-provider behavior.
- Identity hints and model topology are navigation aids, not accepted profile evidence.
- A saved investigation and a generated candidate are not accepted Company Profile truth.
- Do not expose secrets, full prompts, raw provider responses, or hidden reasoning.
