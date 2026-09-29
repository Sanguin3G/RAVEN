import type { MouseEvent } from "react";
import { Archive } from "@phosphor-icons/react/dist/csr/Archive";
import { ArrowUUpLeft } from "@phosphor-icons/react/dist/csr/ArrowUUpLeft";
import { DotsThreeVertical } from "@phosphor-icons/react/dist/csr/DotsThreeVertical";
import { Trash } from "@phosphor-icons/react/dist/csr/Trash";
import type { Company } from "../../types/company";

interface Props {
  company: Company;
  open: boolean;
  placement: "up" | "down";
  busy: boolean;
  onToggle: (event: MouseEvent<HTMLButtonElement>) => void;
  onRefresh: () => void;
  onImprove: () => void;
  onMonitor: () => void;
  onArchive: (restore: boolean) => void;
  onDelete: () => void;
}

export function CompanyActionsMenu({ company, open, placement, busy, onToggle, onRefresh, onImprove, onMonitor, onArchive, onDelete }: Props) {
  return <div className="action-menu">
    <button className="action-menu__trigger" type="button" aria-label={`Actions for ${company.name}`} aria-expanded={open} onClick={onToggle}><DotsThreeVertical size={20} weight="bold" aria-hidden="true" /></button>
    {open ? <div className={`action-menu__panel action-menu__panel--${placement}`} role="menu">
      <button role="menuitem" type="button" onClick={onRefresh}>Refresh research</button>
      <button role="menuitem" type="button" onClick={onImprove}>Improve profile</button>
      <button role="menuitem" type="button" onClick={onMonitor}>Monitor company</button>
      <span className="action-menu__divider" />
      {company.archivedAt ? <button role="menuitem" type="button" disabled={busy} onClick={() => onArchive(true)}><ArrowUUpLeft size={16} aria-hidden="true" /> Restore</button> : <button role="menuitem" type="button" disabled={busy} onClick={() => onArchive(false)}><Archive size={16} aria-hidden="true" /> Archive</button>}
      <button className="action-menu__danger" role="menuitem" type="button" onClick={onDelete}><Trash size={16} aria-hidden="true" /> Delete permanently</button>
    </div> : null}
  </div>;
}
