## Agent skills

### Issue tracker

Issues live as GitHub Issues in `martintoelk/User-Team-Role-Inspector-with-Matrix-BU`, via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context layout (root `CONTEXT.md` + `docs/adr/`). See `docs/agents/domain.md`.

## Inter-tool handoff

`UserTeamRoleInspectorControl` implements XrmToolBox's `IMessageBusHost` so "BU Matrix Security
Role Assigner" can open this tool on a specific team or user. Receive-only: `OnOutgoingMessage`
exists because the interface demands it and is never raised (hence the `#pragma warning disable
67`).

- The payload is a **string** with a documented format, parsed by
  `UserTeamRoleInspector.Core/RoleHandoff.cs`. The sender has its own copy of the format in its
  own repo; nothing is shared at build time, so the two must be kept in step by hand.
  `RoleHandoffTests` on both sides parses/produces the *same literal payloads* for that reason -
  a round-trip test through one side's own encoder would stay green through a breaking change.
- Delivery is synchronous with the tool being shown, so on a cold launch `OnIncomingMessage` can
  land before there is a `Service` or any loaded list. It only stashes `_pendingHandoff`;
  `ApplyHandoff` does the work, re-entering itself once via `ExecuteMethod` for the connection
  and once more after a load (`afterLoad` is what stops that looping on an empty environment).
- Anything it can't act on - a non-string payload, another tool's payload, a future format
  version, a record kind this build doesn't show - is ignored rather than surfaced as an error.
- The full research behind this, including the host's own broker source, is in the sender's repo
  at `docs/research/xrmtoolbox-inter-plugin-communication.md`.
