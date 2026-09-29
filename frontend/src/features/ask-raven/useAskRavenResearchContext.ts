import { useEffect, useRef, useState } from "react";
import type { BriefingListItem } from "../../api/briefings";
import type { Investigation } from "../../api/investigations";
import { getResearchContextAttachments, type ResearchContextAttachment } from "../../api/managedResearch";

export function useAskRavenResearchContext(companyId: string, conversationId: string | null) {
  const [investigations, setInvestigations] = useState<Investigation[]>([]);
  const [briefings, setBriefings] = useState<BriefingListItem[]>([]);
  const [researchContextAttachments, setResearchContextAttachments] = useState<ResearchContextAttachment[]>([]);
  const [attachingResearchId, setAttachingResearchId] = useState<string | null>(null);
  const [investigationPickerOpen, setInvestigationPickerOpen] = useState(false);
  const [contextDetailsOpen, setContextDetailsOpen] = useState(false);
  const [investigationSearch, setInvestigationSearch] = useState("");
  const researchContextLoadVersionRef = useRef(0);
  const localAttachmentConversationRef = useRef<string | null>(null);

  useEffect(() => {
    if (!conversationId) {
      researchContextLoadVersionRef.current += 1;
      setResearchContextAttachments([]);
      return;
    }

    let active = true;
    const loadVersion = ++researchContextLoadVersionRef.current;
    void getResearchContextAttachments(companyId, conversationId)
      .then((attachments) => {
        if (active && loadVersion === researchContextLoadVersionRef.current && localAttachmentConversationRef.current !== conversationId) {
          setResearchContextAttachments(attachments);
        }
      })
      .catch(() => {
        // The Chat conversation remains usable when the optional context
        // endpoint is unavailable. Existing attachments remain local until a
        // later conversation reload.
      });
    return () => { active = false; };
  }, [companyId, conversationId]);

  return {
    investigations, setInvestigations, briefings, setBriefings,
    researchContextAttachments, setResearchContextAttachments,
    attachingResearchId, setAttachingResearchId,
    investigationPickerOpen, setInvestigationPickerOpen,
    contextDetailsOpen, setContextDetailsOpen,
    investigationSearch, setInvestigationSearch,
    researchContextLoadVersionRef, localAttachmentConversationRef,
  };
}
