# RAVEN customer / mentor demonstration

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

### 6. Return to Ask RAVEN — 1 minute

Leave the Company Workspace for Companies, then reopen the same Company. Open Ask RAVEN and show that the previous conversation restores. Use **Recent chats** to reopen an earlier one, then **New chat** to show that it starts empty without deleting history.

Ask a profile-grounded question and open **Show sources**. In the compact **+** menu, choose **Research context**, attach a completed Investigation or Briefing, and point out that this is unaccepted research material alongside the accepted Profile. Turn on **Web search** for a freshness-sensitive question; the explicit permission does not require a provider call on every turn. Use one quiet action beneath the answer to show a sensible next step.

If browser recognition is available and permissions are reliable, dictate one question, edit the resulting text, and send it manually. Voice is optional polish; the core story remains source-backed Company intelligence.

Close with: “The value is a reviewable, versioned company knowledge record whose claims can be traced back to preserved public evidence.”

## Recovery paths to demonstrate only if asked

### Research alternatives

From Ask RAVEN, open the plus menu and choose **Deep Research**. Submit a narrow question, then continue with normal Chat while the investigation runs. Open **Investigations** to show the durable result and explain that claims and cited URLs remain research material until reviewed.

In Investigations, open the searchable switcher and show Origin, Purpose, Topics, and status. Open a ready General Research Investigation, use **Analyze findings** if its derived analysis is absent, then **Mark as done** and **Reopen** to show the durable handling state. Use **Add to briefing** or the Briefings tab to create a Talent & Hiring Briefing from hand-picked Investigations. Show its Generated and Research through dates, source list, and version history. If newer relevant material exists, choose it in **Update briefing** and show that the old version remains available. **Research latest** and **Research gaps** hand off to existing research paths; Update itself does not search.

For **Strengthen Dossier**, point out the method distinction: Native RAVEN is the fast integrated default, Deep Research runs asynchronously, and External AI Assist remains unverified material. If Deep completes before Profile v1, show the muted **Profile required** state: the global ready card is not clickable, the result is excluded from Workspace Review, and it becomes reviewable only after the user creates a supported profile. Emphasize that only the user's final **Confirm Profile** action creates a profile; a failed or still-running RAVEN run cannot create one.

Open **External research**, prepare and copy a focused brief, paste fixture Markdown, analyze it asynchronously, review the AI-assisted comparison, and save it. Emphasize that imported claims and provider citations remain reviewable material; External Assist does not re-search or re-crawl the supplied links. From a ready Investigation, use **Improve profile** to prepare the existing server-owned profile patch from that material, then confirm only after human review. On Overview, show the optional headquarters map adapter and its normal external-map fallback.

From the Company List, open **Workspace Review** to show the grouped research queue. Individual items offer **Review** and **Mark done**. Under **More**, **Mark all research reviewed** requires confirmation and only acknowledges the current results; newer research can resurface. **Smart clean-up** separately clears repeated issue groups with confirmation. Neither action deletes research history or evidence.

- Cancel an in-progress research run. RAVEN returns to the blank research form and clears the saved run, ready for a new search.
- Navigate away from a known in-progress run and return. If a related restore request fails, RAVEN preserves the run and shows a targeted restore error with **Retry restore** rather than the generic API-unavailable message. A stale/missing run is the only case that resets to a blank form.
- Reopen the application with a stale saved run. A missing, cancelled, failed, completed, or legacy-unrestorable run is discarded rather than leaving the user on a misleading “company not found” state.
- If a provider fails, show the source-level failure and bounded fallback behavior. Never pretend the source was acquired.

## Demo truthfulness rules

- Label fixture-backed screens separately from controlled live-provider behavior.
- Identity hints and model topology are navigation aids, not accepted profile evidence.
- A saved investigation and a generated candidate are not accepted Company Profile truth.
- Do not expose secrets, full prompts, raw provider responses, or hidden reasoning.
