import { Link } from "react-router-dom";
import { Panel } from "../components/Panel";

export function NotFoundPage() {
  return (
    <Panel className="narrow-page empty-state" title="Page not found" eyebrow="WRONG COORDINATES">
      <p>That part of the RAVEN workspace does not exist.</p>
      <Link className="button button--secondary" to="/">Return home</Link>
    </Panel>
  );
}
