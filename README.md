# User/Team Role Inspector — XrmToolBox plugin

A **read-only** XrmToolBox plugin with two modes, switched via a **Users | Teams** toggle above
the master list:

- **User mode**: pick a Dataverse user and see every security role they effectively hold —
  assigned straight to them (a **Direct Assignment**), or held because they're a member of a
  team that itself holds the role (a **Team-Derived Assignment**).
- **Team mode**: pick a team and see the roles associated straight to it, plus its member
  users.

Built for orgs on the **modernized business units** (matrix data-access) model, where a role's
business unit is independent of the user's or team's own business unit — so each role row also
shows the **Role Business Unit** it belongs to (and, for team-derived rows, the source team
and its **Team Business Unit**).

Two Team-Derived Assignments for the same role via two different teams show as two distinct
rows — nothing is collapsed. This tool never adds or removes anything; for bulk assign/remove
of roles on teams or users, see the sibling
[Modernized BU Security Role Assigner](https://github.com/martintoelk/Modernized-BU-Security-Role-Assigner)
plugin.

### User mode

- **Load / Refresh Users** loads the user list; a text filter above it narrows by name or
  Business Unit, and a **Hide disabled users** checkbox (checked by default) excludes disabled
  users from the list. Both persist across reloads. Each row shows separate
  `Direct Assignments` and `Team-Derived Assignments` count columns.
- Selecting a user loads their detail card: name, **Business Unit**, a **DISABLED** badge for
  disabled users, and Direct / Team-Derived stat tiles.
- **Grid | Tree** toggle switches how the results are shown — switching never re-queries
  Dataverse, it just re-renders what's already loaded:
  - **Grid** (two stacked grids): **Direct Assignments** (Role, Role Business Unit) and
    **Team-Derived Assignments** (Role, Role Business Unit, Team, Team Business Unit).
  - **Tree** (default view on load, 3 levels): **Direct Roles** node, plus one node per source
    team → Role node → `Role Business Unit: <name>` leaf.

### Team mode

- **Load / Refresh Teams** loads the team list; the same text filter narrows by name or
  Business Unit. **Ignore Agent Teams** and **Ignore Access Team** are checked by default;
  agent teams whose description contains `power virtual agents` and Access teams are excluded
  independently. Changing either checkbox reloads the team list. Each row shows its Team Type,
  Roles, and Members counts.
- Selecting a team loads its detail card: name, Business Unit, and Roles / Members stat tiles.
- Results always show as two stacked grids (no tree — there's no nested source grouping to
  show): **Team Roles** (Role, Role Business Unit) and **Team Members** (Name, with a
  `(disabled)` suffix for disabled members).

## Install

Install from XrmToolBox's **Tool Library**: search for **"User/Team Role Inspector"**. To
build and deploy from source instead, see Build/Deploy below.

## Build

Requires Visual Studio 2022 (or `dotnet` SDK) with the **.NET Framework 4.8** targeting pack
and the **.NET desktop development** workload.

```
dotnet restore
dotnet build -c Release
```

Output: `UserTeamRoleInspector\bin\Release\UserTeamRoleInspector.dll` (plus
`UserTeamRoleInspector.Core.dll`, which it depends on).

## Deploy (from a source build)

Copy `UserTeamRoleInspector.dll` **and** `UserTeamRoleInspector.Core.dll` into the XrmToolBox
plugins folder:

```
%AppData%\MscrmTools\XrmToolBox\Plugins
```

Don't copy the SDK / XrmToolBox assemblies from `bin` — the host already ships those, and
copying them can cause version conflicts. Restart XrmToolBox; the plugin appears as
**User/Team Role Inspector**.

## Use

1. Open the plugin and connect to an environment.
2. Pick **Users** or **Teams** from the toggle above the master list (Users by default).
3. Click **Load / Refresh Users** (or **Load / Refresh Teams**, if you switched modes).
4. Optionally type in the filter box above the list to narrow by name or Business Unit. In
  User mode, disabled users are hidden by default; uncheck **Hide disabled users** to include
  them. In Team mode, **Ignore Agent Teams** and **Ignore Access Team** are checked by default;
  uncheck either one to include that team category while preserving the other filter.
5. Select a user or team — its detail card and results load automatically.
6. In User mode, read the detail card: name, Business Unit, a **DISABLED** badge if the user
   is disabled, and the Direct / Team-Derived stat tiles. Toggle **Grid | Tree** to switch how
   the results are shown; both reflect the same underlying data, so switching is instant and
   never re-queries Dataverse.
7. In Team mode, read the detail card: name, Business Unit, and the Roles / Members stat
   tiles, with the team's roles and member users shown as two grids.

### Opened from the Role Assigner

This tool can also be opened *for you*, already pointed at a record. In the companion tool
[BU Matrix Security Role Assigner](https://github.com/martintoelk/Modernized-BU-Security-Role-Assigner),
selecting a team or user and clicking **Inspect in Role Inspector** hands it to this tool over
XrmToolBox's message bus: this tool opens (or comes to the front) on the same connection,
switches to Users or Teams to match, loads that list if it hasn't been loaded yet, and selects
the record.

Because a leftover filter — or **Hide disabled users**, which is on by default — could be hiding
the very row you asked for, both are cleared when a handoff arrives.

## Terminology

See `CONTEXT.md` for the full glossary; the short version:

| Term | Meaning |
|------|---------|
| Direct Assignment | A role associated straight to the user (`systemuserroles`) |
| Team-Derived Assignment | A role the user holds via a team they're a member of (`teammembership` + `teamroles`); carries the source team as part of its identity |
| Role Business Unit | The business unit that owns that specific role instance |
| Team Business Unit | The business unit the source team belongs to |
| User Home Business Unit | The business unit the selected user belongs to |
| Source | Which path an Assignment came through: `Direct` or `Team` (naming the team) |
| Team Role (Team mode) | A role associated straight to a team (`teamroles`) |
| Team Member (Team mode) | A user who belongs to a team (`teammembership`) |

## Files

| File | Purpose |
|------|---------|
| `UserTeamRoleInspector.Core/TeamRoleInspectionService.cs` | Query/aggregation logic (direct + team-derived roles for User mode, team roles + members for Team mode), depends only on `IOrganizationService` |
| `UserTeamRoleInspector.Core/Models.cs` | `UserItem`, `Assignment`, `UserRoleInspectionResult`, `AssignmentSource`, `TeamItem`, `TeamRoleItem`, `TeamMemberItem`, `TeamDetailResult` |
| `UserTeamRoleInspector.Core/RoleHandoff.cs` | Reader for the payload the Role Assigner hands over the message bus |
| `UserTeamRoleInspector.Core/UserTeamRoleInspector.Core.csproj` | Class library (net48), no WinForms/XTB dependency |
| `UserTeamRoleInspector/Plugin.cs` | XrmToolBox export/metadata (the plugin factory) |
| `UserTeamRoleInspector/UserTeamRoleInspectorControl.cs` | UI wiring, threading (`WorkAsync`), User/Team mode switch, calls into Core |
| `UserTeamRoleInspector/UserTeamRoleInspectorControl.Designer.cs` | WinForms UI (master-detail layout, User/Team and Grid/Tree toggles) |
| `UserTeamRoleInspector/UserTeamRoleInspector.csproj` | SDK-style project (net48, WinForms), references Core |

## License

[MIT](LICENSE)
