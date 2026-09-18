# Codex Agent Workflow

The objective is to maximize useful work from the Plus allowance while using stronger models only when their additional reasoning is valuable.

## Normal path

```text
Luna xhigh
  -> investigate
  -> implement
  -> test
  -> debug
  -> review
  -> finish
```

This is the expected path for most tasks. No subagent is required.

## Cheap horizontal delegation

Use Luna High workers for bounded, independent work.

```text
                 +-> Luna High: independent exploration
                 |
Luna xhigh ------+-> Luna High: isolated routine work
                 |
                 +-> root continues useful work
```

Delegation is for parallelism and context isolation, not because the root needs another agent for every easy operation.

Good examples:

- The root implements a backend change while a worker maps independent frontend callers.
- A worker finds all implementations of an interface while the root designs the new API.
- A worker analyzes a large independent test failure while the root inspects production code.

Bad examples:

- Spawning an agent to rename one field or change a five-line function.
- Spawning an agent for work the root must wait on before doing anything else.
- Splitting a tightly coupled implementation across several agents.

## Vertical escalation

When stronger reasoning is needed:

```text
Luna xhigh
    |
    +-> Terra Medium consultant
    |      |
    |      +-> diagnosis / architecture / decision
    |      |
    <------+
    |
Luna xhigh implements and verifies
```

Terra does not automatically take ownership of the whole task.

## Rare escalation

If the decision remains unusually difficult or high risk:

```text
Luna xhigh
  -> Terra Medium
      -> Terra High if justified
  -> Luna xhigh implementation
```

Terra High is exceptional.

## Escalation examples

### Straightforward feature

“Add an endpoint using the existing service/repository pattern.”

Use Luna xhigh only.

### Large mechanical migration

“Rename this API throughout 80 files.”

Use Luna xhigh. Optionally use one or two Luna High workers if directories can be changed independently.

### Ambiguous architecture

“Add caching, but it is unclear whether invalidation belongs in the repository, service, or event layer.”

Luna xhigh investigates. If ambiguity remains, Terra Medium makes the architectural recommendation. Luna xhigh implements it.

### Difficult bug

Luna investigates. If two credible fixes fail or evidence remains contradictory, ask Terra Medium to analyze the evidence and propose the next hypothesis. Luna resumes debugging.

### High-risk correctness

Examples include a migration capable of deleting data, an authentication or security boundary, a subtle race condition, or distributed transaction behavior. Use Terra Medium, or Terra High when genuinely warranted, for focused review or a decision.

## Consultant handoff format

When escalating, summarize the problem as:

### Goal

What must be achieved.

### Relevant context

Only the components and files necessary to understand the decision.

### Evidence

Observed behavior, logs, tests, or invariants.

### Attempts

What has already been tried and what happened.

### Decision needed

The exact question the consultant should answer.

The consultant should not redo routine repository exploration unless it is necessary to answer the decision.

## General rule

Subagents should earn their overhead. A useful subagent either performs meaningful independent work concurrently or supplies reasoning capability the root currently lacks. Otherwise the root should work directly.
