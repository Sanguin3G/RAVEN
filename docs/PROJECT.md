# RAVEN Project
## Business problem

Organizations research prospective companies manually across scattered public sources, then repeatedly reconstruct the same facts when circumstances change. RAVEN should turn that work into reusable, standardized Company Profiles with inspectable evidence.

## Core user flow

~~~text
Company
  ↓
Public-source research
  ↓
Source collection
  ↓
Standardized Company Profile
  ↓
Central storage
  ↓
Ask / reuse knowledge
  ↓
Refresh
  ↓
Track changes
~~~

## Core Company Profile

~~~text
Company name             Legal name
Website                  Country
Headquarters

Industry                 Company size
Products / services      Markets

Summary                  Sources / evidence
Research timestamp
~~~

Unknown information remains unknown. AI must not invent values merely to complete a schema.

## Main product features

### P0

~~~text
Company management       Automated research
Source preservation      Standardized profiles
SQLite persistence       Research history
Profile refresh          Profile versions
Change tracking          RAG
Ask Company
~~~

### P1

~~~text
Microsoft Agent Framework    Deep Research
MCP                          Multiple search providers
Multiple crawler providers   Provider settings and fallback
Global company intelligence
~~~

### P2

~~~text
Scheduled monitoring     Notifications
Advanced analytics       Local models
~~~

## Non-goals

RAVEN is not intended to become a distributed enterprise platform during this project. The initial scope excludes microservices, Kafka, Kubernetes, enterprise-scale crawling, and large multi-agent swarms. Technical implementation belongs in [ARCHITECTURE.md](ARCHITECTURE.md).
