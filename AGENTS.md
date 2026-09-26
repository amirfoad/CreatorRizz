# CreatorRizz Engineering Guide

## Purpose

CreatorRizz produces commentary and storytelling Shorts through a workflow that handles sources, scripts, media rights, rendering, review, and publishing. Changes must protect the review gates and preserve a clear audit trail.

## Required checks

Run these before handing off a code change:

```powershell
dotnet build CreatorRizz.sln --no-restore --configuration Release
dotnet test CreatorRizz.sln --no-build --configuration Release
```

Run frontend checks when the Node dependencies are available:

```powershell
Set-Location web
npm run typecheck
npm run build
```

## Backend architecture

Keep the backend in Clean Architecture. Dependencies point inward only:

- `CreatorRizz.Domain` contains business concepts, lifecycle rules, policies, and no framework, database, HTTP, filesystem, or provider dependency.
- `CreatorRizz.Application` contains use cases and the ports they need. It depends only on Domain.
- `CreatorRizz.Infrastructure` contains adapters for storage, queues, AI, TTS, YouTube, and other external systems. Translate their data at this boundary.
- `CreatorRizz.Api` contains HTTP request/response shapes, authentication, dependency wiring, and orchestration. It must not duplicate domain rules.
- `CreatorRizz.Workers` contains asynchronous job execution. Jobs must be idempotent and their effects visible in logs and audit events.

Do not make the domain depend on Infrastructure, API, Worker, EF Core, HTTP clients, or provider SDKs. Add an interface only when it represents a real external boundary or behavior that actually varies.

## Design rules

- Prefer the smallest honest design. Apply KISS: do not add layers, configuration flags, patterns, or abstractions without a present use.
- Apply YAGNI: do not build speculative support for future channels, providers, workflows, or tenant modes.
- Apply DRY to repeated business rules, not merely similar syntax. Put a shared invariant in one clear owner; do not create generic helpers that obscure the domain.
- Use clear, domain-based names. Avoid vague names such as `Manager`, `Helper`, `Utils`, `Data`, or `Common`.
- Keep methods focused. Extract only when the new unit has a meaningful name and reduces the caller's burden.
- Prefer immutable values and explicit types for lifecycle, rights, approval, and publishing states.
- Model invalid states out of the normal path. Do not replace a state machine with unrelated booleans or nullable flags.
- Keep external DTOs, database entities, HTTP requests, and provider responses at the boundary. Convert them before they reach domain policies.
- Do not leak secrets, OAuth tokens, API keys, source media, or personally sensitive data into logs, tests, commits, or error messages.

## Workflow and rights invariants

- Script, rights, and publish approvals are separate human gates.
- Any script, asset, or timing change after rights approval invalidates that approval.
- `Unknown` and `Rejected` assets never enter the render path.
- A production reaches upload only from `PublishApproved` with approved assets.
- Every consequential state change records an audit event with actor, action, entity, time, and safe context.
- Automated content may draft or assist. It may not bypass source review, rights review, or publish approval.

## Errors and effects

- Make IO, network calls, time, queueing, retries, and mutations visible in the API or method contract.
- Return or surface expected operational failures with actionable context. Do not catch exceptions merely to hide them.
- Treat validation failures, unavailable dependencies, and provider failures as distinct cases.
- Give background jobs an idempotency key or a safe reconciliation path before adding retries.

## Tests and documentation

- Tests prove observable behavior, domain invariants, policy decisions, and failure paths. Do not write tests that only verify mocks or internal call order.
- Add regression coverage when fixing a bug.
- Every completed task that changes code, setup, architecture, status, branding, or workflow must update `README.md`, `docs/task-progress.md`, and any relevant technical or brand document in the same commit.
- Keep the checked items in `docs/task-progress.md` truthful. Do not mark work complete while a required provider, credential, runtime, or verification step remains unavailable.

## Change discipline

- Read the nearby code and existing tests before changing a module.
- Keep a change focused. Do not mix refactors, unrelated formatting, and feature work.
- Do not force-push, reset, or discard user changes.
- Commit only verified, purposeful changes with a concise imperative message.
