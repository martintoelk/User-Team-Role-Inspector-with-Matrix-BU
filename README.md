# User/Team Role Inspector — XrmToolBox plugin

A **read-only** XrmToolBox plugin: pick a Dataverse user and see every security role they
effectively hold — assigned straight to them (a **Direct Assignment**), or held because
they're a member of a team that itself holds the role (a **Team-Derived Assignment**). Built
for orgs on the **modernized business units** (matrix data-access) model, where a role's
business unit is independent of the user's or team's own business unit — so each row also
shows the **Role Business Unit** it belongs to (and, for team-derived rows, the source team
and its **Team Business Unit**).

Two Team-Derived Assignments for the same role via two different teams show as two distinct
rows — nothing is collapsed. This tool never adds or removes anything; for bulk assign/remove
of roles on teams or users, see the sibling
[Modernized BU Security Role Assigner](https://github.com/martintoelk/Modernized-BU-Security-Role-Assigner)
plugin.

- **Load / Refresh Users** loads the user list; a text filter above it narrows by name or
  Business Unit, and a **Hide disabled users** checkbox (checked by default) excludes disabled
  users from the list. Both persist across reloads.
- Selecting a user loads their detail card: name, **Business Unit**, a **DISABLED** badge for
  disabled users, and Direct / Team-Derived stat tiles.
- **Grid | Tree** toggle switches how the results are shown — switching never re-queries
  Dataverse, it just re-renders what's already loaded:
  - **Grid** (two stacked grids): **Direct Assignments** (Role, Role Business Unit) and
    **Team-Derived Assignments** (Role, Role Business Unit, Team, Team Business Unit).
  - **Tree** (default view on load, 3 levels): **Direct Roles** node, plus one node per source
    team → Role node → `Role Business Unit: <name>` leaf.

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
2. Click **Load / Refresh Users**.
3. Optionally type in the filter box above the user list to narrow by name or Business Unit.
   Disabled users are hidden by default; uncheck **Hide disabled users** to include them.
4. Select a user — their detail card and results load automatically.
5. Read the detail card: name, Business Unit, a **DISABLED** badge if the user is disabled,
   and the Direct / Team-Derived stat tiles.
6. Toggle **Grid | Tree** to switch how the results are shown; both reflect the same
   underlying data, so switching is instant and never re-queries Dataverse.

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

## Files

| File | Purpose |
|------|---------|
| `UserTeamRoleInspector.Core/TeamRoleInspectionService.cs` | Query/aggregation logic (direct + team-derived roles), depends only on `IOrganizationService` |
| `UserTeamRoleInspector.Core/Models.cs` | `UserItem`, `Assignment`, `UserRoleInspectionResult`, `AssignmentSource` |
| `UserTeamRoleInspector.Core/UserTeamRoleInspector.Core.csproj` | Class library (net48), no WinForms/XTB dependency |
| `UserTeamRoleInspector/Plugin.cs` | XrmToolBox export/metadata (the plugin factory) |
| `UserTeamRoleInspector/UserTeamRoleInspectorControl.cs` | UI wiring, threading (`WorkAsync`), calls into Core |
| `UserTeamRoleInspector/UserTeamRoleInspectorControl.Designer.cs` | WinForms UI (master-detail layout, Grid/Tree toggle) |
| `UserTeamRoleInspector/UserTeamRoleInspector.csproj` | SDK-style project (net48, WinForms), references Core |

## License

[MIT](LICENSE)
