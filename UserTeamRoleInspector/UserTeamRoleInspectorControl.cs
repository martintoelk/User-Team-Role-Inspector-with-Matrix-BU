using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UserTeamRoleInspector.Core;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace UserTeamRoleInspector
{
    // IMessageBusHost is XrmToolBox's host-mediated channel between tools; implementing it is what
    // lets "BU Matrix Security Role Assigner" hand us the team/user it has selected. See
    // OnIncomingMessage for the receiving path.
    public partial class UserTeamRoleInspectorControl : PluginControlBase, IMessageBusHost
    {
        // Which dataset the master list currently shows. The list, filter box, detail card and
        // results grids are all shared controls whose content/labels swap per mode - there is no
        // separate Team screen.
        private enum PickerMode { Users, Teams }
        private PickerMode _mode = PickerMode.Users;

        // Full, unfiltered caches. lbUsers is populated from whichever one matches _mode (with the
        // text filter, and for Users, the hide-disabled checkbox, applied).
        private List<UserItem> _allUsers = new List<UserItem>();
        private List<TeamItem> _allTeams = new List<TeamItem>();

        // Parallel to lbUsers.Items for the active mode: which item each visible row maps to.
        private List<UserItem> _filteredUsers = new List<UserItem>();
        private List<TeamItem> _filteredTeams = new List<TeamItem>();

        public UserTeamRoleInspectorControl()
        {
            InitializeComponent();
        }

        // ------------------------------------------------------------------ UI events

        private void tsbLoad_Click(object sender, EventArgs e) =>
            ExecuteMethod(_mode == PickerMode.Users ? (Action)LoadUsers : LoadTeams);

        private void txtUserFilter_TextChanged(object sender, EventArgs e) => PopulateList();

        private void chkHideDisabled_CheckedChanged(object sender, EventArgs e) => PopulateList();

        private void chkIgnoreAgentTeams_CheckedChanged(object sender, EventArgs e)
        {
            if (_mode != PickerMode.Teams) return;
            ExecuteMethod(() => LoadTeams());
        }

        private void lbUsers_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_mode == PickerMode.Users)
            {
                var selected = GetSelectedUser();
                if (selected == null) { ClearDetail(); return; }
                ExecuteMethod(() => LoadAssignments(selected));
            }
            else
            {
                var selected = GetSelectedTeam();
                if (selected == null) { ClearDetail(); return; }
                ExecuteMethod(() => LoadTeamDetail(selected));
            }
        }

        private void btnPillModeUsers_Click(object sender, EventArgs e) => SwitchMode(PickerMode.Users);

        private void btnPillModeTeams_Click(object sender, EventArgs e) => SwitchMode(PickerMode.Teams);

        // Switching the toggle re-renders the already-loaded result; it never re-queries Dataverse.
        private void btnPillGrid_Click(object sender, EventArgs e)
        {
            gridsSplit.Visible = true;
            tvAssignments.Visible = false;
            SetPillActive(btnPillGrid, btnPillTree);
        }

        private void btnPillTree_Click(object sender, EventArgs e)
        {
            tvAssignments.Visible = true;
            gridsSplit.Visible = false;
            SetPillActive(btnPillTree, btnPillGrid);
        }

        // ------------------------------------------------------------------ Mode switch

        private void SwitchMode(PickerMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;

            var isUsers = mode == PickerMode.Users;
            if (isUsers)
                SetPillActive(btnPillModeUsers, btnPillModeTeams);
            else
                SetPillActive(btnPillModeTeams, btnPillModeUsers);

            lblUsers.Text = isUsers ? "Users" : "Teams";
            chkHideDisabled.Visible = isUsers;
            chkIgnoreAgentTeams.Visible = !isUsers;
            tsbLoad.Text = isUsers ? "Load / Refresh Users" : "Load / Refresh Teams";

            // Team mode has no nested source grouping to show, so there's nothing for the tree to
            // group by - hide the Grid|Tree toggle and always show the twin grids.
            viewTogglePill.Visible = isUsers;
            if (isUsers)
            {
                tvAssignments.Visible = true;
                gridsSplit.Visible = false;
                SetPillActive(btnPillTree, btnPillGrid);
            }
            else
            {
                tvAssignments.Visible = false;
                gridsSplit.Visible = true;
            }

            lblDirectHeader.Text = isUsers ? "Direct Assignments" : "Team Roles";
            lblTeamHeader.Text = isUsers ? "Team-Derived Assignments" : "Team Members";

            ConfigureListView(lbUsers, isUsers
                ? new[] { "Full Name", "Roles Assigned" }
                : new[] { "Team Name", "Roles", "Members" });

            dgvTeam.Columns.Clear();
            if (isUsers)
                ConfigureGrid(dgvTeam, "Role", "Role Business Unit", "Team", "Team Business Unit");
            else
                ConfigureGrid(dgvTeam, "Member");

            ClearDetail();
            PopulateList();
        }

        // ------------------------------------------------------------------ Message-bus handoff

        // Set by OnIncomingMessage, consumed by ApplyHandoff. It has to survive the trip because
        // the handoff can arrive before this control can act on it: the host delivers the message
        // right after showing the tool, which on a cold launch is before there is a connection to
        // query with, and possibly before anything is loaded to select from.
        private RoleHandoff _pendingHandoff;

        /// <summary>
        /// Required by <see cref="IMessageBusHost"/>. This tool only ever receives - it has no
        /// action that opens another tool - so nothing raises it.
        /// </summary>
#pragma warning disable 67 // "never used" is the point: the interface requires it, we only receive.
        public event EventHandler<MessageBusEventArgs> OnOutgoingMessage;
#pragma warning restore 67

        /// <summary>
        /// Called by XrmToolBox when another tool targets this one. Today that is "BU Matrix
        /// Security Role Assigner" saying "show me the roles on this team/user"; anything else is
        /// ignored, since a tool that cannot act on a message should say nothing rather than fail
        /// in front of the user.
        /// </summary>
        public void OnIncomingMessage(MessageBusEventArgs message)
        {
            // TargetArgument is dynamic: cast to object first, or the call becomes a dynamic one
            // and the `out` argument won't compile.
            if (!RoleHandoff.TryParse((object)message?.TargetArgument, out var handoff)) return;

            _pendingHandoff = handoff;

            // The host calls this from its own message-broker path, so hop onto this control's
            // message loop before touching any of its state.
            if (IsHandleCreated)
                BeginInvoke(new MethodInvoker(() => ApplyHandoff(afterLoad: false)));
            else
                HandleCreated += HandoffOnHandleCreated;
        }

        private void HandoffOnHandleCreated(object sender, EventArgs e)
        {
            HandleCreated -= HandoffOnHandleCreated;
            BeginInvoke(new MethodInvoker(() => ApplyHandoff(afterLoad: false)));
        }

        /// <summary>
        /// Opens whatever the pending handoff points at. Runs up to twice: once as the message
        /// arrives, and - if the relevant list hasn't been loaded yet - once more after loading
        /// it. <paramref name="afterLoad"/> is what stops that from looping on an environment
        /// that legitimately has nothing to list.
        /// </summary>
        private void ApplyHandoff(bool afterLoad)
        {
            var handoff = _pendingHandoff;
            if (handoff == null) return;

            PickerMode mode;
            switch (handoff.Entity)
            {
                case "systemuser": mode = PickerMode.Users; break;
                case "team": mode = PickerMode.Teams; break;
                default: _pendingHandoff = null; return;   // a record kind this build can't show
            }

            // ExecuteMethod opens the host's connection dialog when there is no service yet,
            // then calls back - the same path every other action here uses.
            if (Service == null)
            {
                ExecuteMethod(() => ApplyHandoff(afterLoad));
                return;
            }

            SwitchMode(mode);   // no-op when already in that mode

            var loadedCount = mode == PickerMode.Users ? _allUsers.Count : _allTeams.Count;
            if (loadedCount == 0 && !afterLoad)
            {
                if (mode == PickerMode.Users) LoadUsers(() => ApplyHandoff(afterLoad: true));
                else LoadTeams(() => ApplyHandoff(afterLoad: true));
                return;
            }

            _pendingHandoff = null;

            // Dropping the selection first means the repopulation below doesn't restore the old
            // one - which would cost a round trip loading a record we're about to replace.
            lbUsers.SelectedIndices.Clear();

            // A leftover filter - or "Hide disabled users", which is on by default - can easily be
            // hiding the very row we were asked to open, and a handoff that lands on an empty list
            // looks like the tool did nothing. Each setter repopulates the list, but only if it
            // actually changes the value, so repopulate by hand when neither of them did.
            var wasFiltered = txtUserFilter.Text.Length > 0;
            var wasHidingDisabled = mode == PickerMode.Users && chkHideDisabled.Checked;
            txtUserFilter.Text = "";
            if (mode == PickerMode.Users) chkHideDisabled.Checked = false;
            if (!wasFiltered && !wasHidingDisabled) PopulateList();

            var index = mode == PickerMode.Users
                ? _filteredUsers.FindIndex(u => u.Id == handoff.Id)
                : _filteredTeams.FindIndex(t => t.Id == handoff.Id);

            if (index < 0)
            {
                var noun = mode == PickerMode.Users ? "user" : "team";
                MessageBox.Show(this,
                    $"The {noun} '{handoff.Name}' was not found in this environment.\r\n\r\n" +
                    $"It may have been deleted, or the sending tool may be connected to a different environment.",
                    "Not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Selecting it is what loads the detail - lbUsers_SelectedIndexChanged does the rest.
            SelectListViewIndex(index);
        }

        // ------------------------------------------------------------------ Load

        private void LoadUsers() => LoadUsers(null);

        // onLoaded runs only after a successful load, and only on the UI thread. It exists for
        // the message-bus handoff, which can land on a tool the host has just cold-launched with
        // nothing loaded yet - see ApplyHandoff. Kept as an overload rather than an optional
        // parameter so tsbLoad_Click can still convert these method groups to an Action.
        private void LoadUsers(Action onLoaded)
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading users...",
                Work = (worker, args) =>
                {
                    var service = new TeamRoleInspectionService(Service);
                    args.Result = service.RetrieveUsers();
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        MessageBox.Show(this, args.Error.Message, "Load failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    _allUsers = (List<UserItem>)args.Result;
                    PopulateList();
                    onLoaded?.Invoke();
                }
            });
        }

        private void LoadTeams() => LoadTeams(null);

        private void LoadTeams(Action onLoaded)
        {
            var ignoreAgentTeams = chkIgnoreAgentTeams.Checked;

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading teams...",
                Work = (worker, args) =>
                {
                    var service = new TeamRoleInspectionService(Service);
                    args.Result = service.RetrieveTeams(ignoreAgentTeams);
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        MessageBox.Show(this, args.Error.Message, "Load failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    _allTeams = (List<TeamItem>)args.Result;
                    PopulateList();
                    onLoaded?.Invoke();
                }
            });
        }

        private void LoadAssignments(UserItem user)
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = $"Loading roles for {user.Name}...",
                Work = (worker, args) =>
                {
                    var service = new TeamRoleInspectionService(Service);
                    args.Result = service.GetAssignments(user.Id);
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        MessageBox.Show(this, args.Error.Message, "Load failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    PopulateDetail((UserRoleInspectionResult)args.Result);
                }
            });
        }

        private void LoadTeamDetail(TeamItem team)
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = $"Loading roles for {team.Name}...",
                Work = (worker, args) =>
                {
                    var service = new TeamRoleInspectionService(Service);
                    args.Result = service.GetTeamDetail(team.Id);
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        MessageBox.Show(this, args.Error.Message, "Load failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    PopulateTeamDetail((TeamDetailResult)args.Result);
                }
            });
        }

        // ------------------------------------------------------------------ List / detail population

        private static bool Match(string filter, params string[] fields)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            return fields.Any(f => !string.IsNullOrEmpty(f) &&
                                   f.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void PopulateList()
        {
            if (_mode == PickerMode.Users)
                PopulateUserList();
            else
                PopulateTeamList();
        }

        private void PopulateUserList()
        {
            var filter = txtUserFilter.Text?.Trim();
            var previouslySelected = GetSelectedUser()?.Id;

            _filteredUsers = _allUsers
                .Where(u => Match(filter, u.Name, u.BusinessUnitName))
                .Where(u => !chkHideDisabled.Checked || !u.IsDisabled)
                .ToList();

            lbUsers.BeginUpdate();
            lbUsers.Items.Clear();
            lbUsers.Items.AddRange(_filteredUsers.Select(UserRow).ToArray());
            lbUsers.EndUpdate();

            if (previouslySelected.HasValue)
            {
                var index = _filteredUsers.FindIndex(u => u.Id == previouslySelected.Value);
                if (index >= 0) SelectListViewIndex(index);
            }

            UpdateStatus();
        }

        private void PopulateTeamList()
        {
            var filter = txtUserFilter.Text?.Trim();
            var previouslySelected = GetSelectedTeam()?.Id;

            _filteredTeams = _allTeams
                .Where(t => Match(filter, t.Name, t.BusinessUnitName))
                .ToList();

            lbUsers.BeginUpdate();
            lbUsers.Items.Clear();
            lbUsers.Items.AddRange(_filteredTeams.Select(TeamRow).ToArray());
            lbUsers.EndUpdate();

            if (previouslySelected.HasValue)
            {
                var index = _filteredTeams.FindIndex(t => t.Id == previouslySelected.Value);
                if (index >= 0) SelectListViewIndex(index);
            }

            UpdateStatus();
        }

        private void SelectListViewIndex(int index)
        {
            lbUsers.Items[index].Selected = true;
            lbUsers.Items[index].EnsureVisible();
        }

        private static ListViewItem UserRow(UserItem u)
        {
            var name = u.IsDisabled ? $"{u.Name}  (disabled)" : u.Name;
            var totalRoles = u.DirectCount + u.TeamCount;
            return new ListViewItem(new[] { name, totalRoles.ToString() });
        }

        private static ListViewItem TeamRow(TeamItem t) =>
            new ListViewItem(new[] { t.Name, t.RoleCount.ToString(), t.MemberCount.ToString() });

        private void ClearDetail()
        {
            dgvDirect.Rows.Clear();
            dgvTeam.Rows.Clear();
            tvAssignments.Nodes.Clear();

            var isUsers = _mode == PickerMode.Users;
            lblName.Text = isUsers ? "Select a user" : "Select a team";
            lblBusinessUnit.Text = "";
            lblDisabledBadge.Visible = false;
            lblDirectTile.Text = isUsers ? "Direct\n—" : "Roles\n—";
            lblTeamTile.Text = isUsers ? "Team-Derived\n—" : "Members\n—";

            UpdateStatus();
        }

        private void PopulateDetail(UserRoleInspectionResult result)
        {
            dgvDirect.Rows.Clear();
            dgvTeam.Rows.Clear();

            lblName.Text = result.UserName;
            lblBusinessUnit.Text = result.HomeBusinessUnitName;
            lblDisabledBadge.Visible = result.IsDisabled;

            var direct = result.Assignments.Where(a => a.Source == AssignmentSource.Direct).ToList();
            var team = result.Assignments.Where(a => a.Source == AssignmentSource.Team).ToList();

            lblDirectTile.Text = $"Direct\n{direct.Count}";
            lblTeamTile.Text = $"Team-Derived\n{team.Count}";

            foreach (var a in direct)
                dgvDirect.Rows.Add(a.RoleName, a.RoleBusinessUnitName);
            foreach (var a in team)
                dgvTeam.Rows.Add(a.RoleName, a.RoleBusinessUnitName, a.SourceTeamName, a.TeamBusinessUnitName);

            PopulateTree(direct, team);

            UpdateStatus();
        }

        private void PopulateTeamDetail(TeamDetailResult result)
        {
            dgvDirect.Rows.Clear();
            dgvTeam.Rows.Clear();

            lblName.Text = result.TeamName;
            lblBusinessUnit.Text = result.BusinessUnitName;
            lblDisabledBadge.Visible = false;

            lblDirectTile.Text = $"Roles\n{result.Roles.Count}";
            lblTeamTile.Text = $"Members\n{result.Members.Count}";

            foreach (var r in result.Roles)
                dgvDirect.Rows.Add(r.RoleName, r.RoleBusinessUnitName);
            foreach (var m in result.Members)
                dgvTeam.Rows.Add(m.IsDisabled ? $"{m.Name}  (disabled)" : m.Name);

            UpdateStatus();
        }

        private static void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var grid = (DataGridView)sender;
            var newColumn = grid.Columns[e.ColumnIndex];
            var oldColumn = grid.SortedColumn;

            var direction = oldColumn == newColumn && grid.SortOrder == SortOrder.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            if (oldColumn != null && oldColumn != newColumn)
                oldColumn.HeaderCell.SortGlyphDirection = SortOrder.None;

            grid.Sort(newColumn, direction);
            newColumn.HeaderCell.SortGlyphDirection = direction == ListSortDirection.Ascending
                ? SortOrder.Ascending
                : SortOrder.Descending;
        }

        // 3-level tree: Direct Roles / one node per source team -> Role node -> Role Business Unit leaf.
        // Users mode only - Team mode has no nested source grouping to show.
        private void PopulateTree(List<Assignment> direct, List<Assignment> team)
        {
            tvAssignments.BeginUpdate();
            tvAssignments.Nodes.Clear();

            var directNode = new TreeNode("Direct Roles");
            foreach (var a in direct)
            {
                var roleNode = new TreeNode(a.RoleName);
                roleNode.Nodes.Add($"Role Business Unit: {a.RoleBusinessUnitName}");
                directNode.Nodes.Add(roleNode);
            }
            tvAssignments.Nodes.Add(directNode);

            foreach (var teamGroup in team.GroupBy(a => (a.SourceTeamName, a.TeamBusinessUnitName)))
            {
                var teamNode = new TreeNode($"Team: {teamGroup.Key.SourceTeamName}  (BU: {teamGroup.Key.TeamBusinessUnitName})");
                foreach (var a in teamGroup)
                {
                    var roleNode = new TreeNode(a.RoleName);
                    roleNode.Nodes.Add($"Role Business Unit: {a.RoleBusinessUnitName}");
                    teamNode.Nodes.Add(roleNode);
                }
                tvAssignments.Nodes.Add(teamNode);
            }

            tvAssignments.ExpandAll();
            tvAssignments.EndUpdate();
        }

        private void UpdateStatus()
        {
            if (_mode == PickerMode.Users)
            {
                lblStatus.Text = $"Users: {lbUsers.Items.Count} shown ({_allUsers.Count} total)   |   " +
                                 $"Direct: {dgvDirect.Rows.Count}   Team-Derived: {dgvTeam.Rows.Count}";
            }
            else
            {
                lblStatus.Text = $"Teams: {lbUsers.Items.Count} shown ({_allTeams.Count} total)   |   " +
                                 $"Roles: {dgvDirect.Rows.Count}   Members: {dgvTeam.Rows.Count}";
            }
        }

        private int SelectedListViewIndex => lbUsers.SelectedIndices.Count > 0 ? lbUsers.SelectedIndices[0] : -1;

        private UserItem GetSelectedUser() =>
            _mode == PickerMode.Users && SelectedListViewIndex >= 0 && SelectedListViewIndex < _filteredUsers.Count
                ? _filteredUsers[SelectedListViewIndex]
                : null;

        private TeamItem GetSelectedTeam() =>
            _mode == PickerMode.Teams && SelectedListViewIndex >= 0 && SelectedListViewIndex < _filteredTeams.Count
                ? _filteredTeams[SelectedListViewIndex]
                : null;
    }
}
