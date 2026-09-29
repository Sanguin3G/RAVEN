import { useEffect, useRef, type ReactNode } from "react";

interface WorkspaceDialogProps {
  open: boolean;
  title: string;
  labelledBy: string;
  children: ReactNode;
  onClose: () => void;
}

export function WorkspaceDialog({ open, title, labelledBy, children, onClose }: WorkspaceDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      if (typeof dialog.showModal === "function") dialog.showModal();
      else dialog.setAttribute("open", "");
    } else if (!open && dialog.open) {
      if (typeof dialog.close === "function") dialog.close();
      else dialog.removeAttribute("open");
    }
  }, [open]);

  return (
    <dialog ref={dialogRef} className="workspace-dialog" aria-labelledby={labelledBy} onCancel={onClose}>
      {open ? <div className="workspace-dialog__content">
        <header className="workspace-dialog__header"><h2 id={labelledBy}>{title}</h2><button className="modal-close" type="button" aria-label="Close dialog" onClick={onClose}>×</button></header>
        {children}
      </div> : null}
    </dialog>
  );
}
