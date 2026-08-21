using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UserTeamRoleInspector.Core;
using XrmToolBox.Extensibility;

namespace UserTeamRoleInspector
{
    public partial class UserTeamRoleInspectorControl : PluginControlBase
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

            dgvTeam.Columns.Clear();
            if (isUsers)
                ConfigureGrid(dgvTeam, "Role", "Role Business Unit", "Team", "Team Business Unit");
            else
                ConfigureGrid(dgvTeam, "Member");

            ClearDetail();
            PopulateList();
        }

        // ------------------------------------------------------------------ Load

        private void LoadUsers()
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
                }
            });
        }

        private void LoadTeams()
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading teams...",
                Work = (worker, args) =>
                {
                    var service = new TeamRoleInspectionService(Service);
                    args.Result = service.RetrieveTeams();
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
            lbUsers.Items.AddRange(_filteredUsers.Select(ListLabel).Cast<object>().ToArray());
            lbUsers.EndUpdate();

            if (previouslySelected.HasValue)
            {
                var index = _filteredUsers.FindIndex(u => u.Id == previouslySelected.Value);
                if (index >= 0) lbUsers.SelectedIndex = index;
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
            lbUsers.Items.AddRange(_filteredTeams.Select(TeamLabel).Cast<object>().ToArray());
            lbUsers.EndUpdate();

            if (previouslySelected.HasValue)
            {
                var index = _filteredTeams.FindIndex(t => t.Id == previouslySelected.Value);
                if (index >= 0) lbUsers.SelectedIndex = index;
            }

            UpdateStatus();
        }

        private static string ListLabel(UserItem u)
        {
            var label = $"{u.Name}  (Team: {u.TeamCount}, Direct: {u.DirectCount})";
            return u.IsDisabled ? $"{label}  (disabled)" : label;
        }

        private static string TeamLabel(TeamItem t) =>
            $"{t.Name}  (Roles: {t.RoleCount}, Members: {t.MemberCount})";

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

        private UserItem GetSelectedUser() =>
            _mode == PickerMode.Users && lbUsers.SelectedIndex >= 0 && lbUsers.SelectedIndex < _filteredUsers.Count
                ? _filteredUsers[lbUsers.SelectedIndex]
                : null;

        private TeamItem GetSelectedTeam() =>
            _mode == PickerMode.Teams && lbUsers.SelectedIndex >= 0 && lbUsers.SelectedIndex < _filteredTeams.Count
                ? _filteredTeams[lbUsers.SelectedIndex]
                : null;
    }
}
