import { useEffect, useState } from "react";
import { getRuntimeConfig } from "../../api/system";
import styles from "./demo-mode-notice.module.css";

export function DemoModeNotice() {
  const [enabled, setEnabled] = useState(false);

  useEffect(() => {
    let active = true;
    void getRuntimeConfig().then(config => {
      if (active) setEnabled(config.demoMode);
    }).catch(() => undefined);
    return () => { active = false; };
  }, []);

  return enabled ? <p className={styles.notice} role="status">Demo workspace · changes may reset</p> : null;
}
