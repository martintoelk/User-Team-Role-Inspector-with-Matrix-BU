# User/Team Role Inspector

An XrmToolBox plugin (read-only) with two modes, switched via a toggle above the master list:

- **User mode**: for a selected Dataverse user, reports every security role they effectively
  hold and where each one comes from: assigned straight to the user, or inherited through
  membership in a team that itself holds the role.
- **Team mode**: for a selected team, reports the roles associated straight to that team and
  the users who are its members.

Built for orgs on the **modernized business units** (matrix data-access) model, where a role's
business unit is independent of the user's or team's own business unit.

## Language

**Direct Assignment**:
A security role associated straight to the user via `systemuserroles`.
_Avoid_: Personal role, own role.

**Team-Derived Assignment**:
A security role the user effectively holds because it's associated (via `teamroles`) to a
team the user is a member of (via `teammembership`). Carries the source team as part of its
identity, not just the role.
_Avoid_: Inherited role, indirect role (use "Team-Derived" as the canonical term; "indirect"
is fine in prose but the code/UI should say "Team-Derived").

**Assignment**:
The umbrella term for a row in the inspector's results: either a Direct Assignment or a
Team-Derived Assignment. Two Assignments are distinct rows even if they name the same Role,
as long as their source differs (e.g. Direct vs. via Team A vs. via Team B).
_Avoid_: Grant, entitlement.

**Role Business Unit**:
The business unit that owns the specific role instance in an Assignment. Under the modernized
model a user or team can hold a role whose Business Unit differs from their own.
_Avoid_: Role's BU (fine in prose, but keep "Role Business Unit" as the field/column name).

**Team Business Unit**:
The business unit a team belongs to (`team.businessunitid`), shown alongside a Team-Derived
Assignment's source team so the reader can see it's a different axis from the Role Business
Unit.

**User Home Business Unit**:
The business unit the selected user themselves belongs to (`systemuser.businessunitid`).
Shown for context; distinct from any Role Business Unit or Team Business Unit in the results.

**Source**:
Which path an Assignment came through: `Direct`, or `Team` (naming the specific team). Drives
grouping in the tree view and a column in the grid view.

**Team Role** (Team mode):
A security role associated straight to a team via `teamroles` - the same fact a Team-Derived
Assignment reports from the user's side, shown here from the team's side instead. Carries the
role's own Business Unit, same as a Direct Assignment.

**Team Member** (Team mode):
A user who belongs to a team via `teammembership`. Team mode's picker/detail carries no
Assignment or Source concept - it reports the team's own roles and its member users, not
"assignments" in the User mode sense.
