import { Link, Route, Routes } from "react-router-dom";

const Home = () => <main><p className="eyebrow">RESEARCH · ANALYSIS · VERIFICATION</p><h1>Company intelligence, grounded in evidence.</h1><p>RAVEN is initialized. The first end-to-end research workflow is the next delivery target.</p><Link className="button" to="/companies">Browse companies</Link></main>;
const Placeholder = ({ title }: { title: string }) => <main><p className="eyebrow">RAVEN</p><h1>{title}</h1><p>This area is scaffolded and awaiting the sacred vertical slice.</p></main>;

export function App() {
  return <><header><Link to="/">RAVEN</Link><nav><Link to="/companies">Companies</Link><Link to="/settings">Settings</Link></nav></header><Routes>
    <Route path="/" element={<Home />} />
    <Route path="/companies" element={<Placeholder title="Companies" />} />
    <Route path="/companies/new" element={<Placeholder title="New company" />} />
    <Route path="/companies/:id" element={<Placeholder title="Company profile" />} />
    <Route path="/companies/:id/research" element={<Placeholder title="Research company" />} />
    <Route path="/settings" element={<Placeholder title="Provider settings" />} />
  </Routes></>;
}
