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
        // Full, unfiltered cache. lbUsers is populated from this (with the text filter applied).
        private List<UserItem> _allUsers = new List<UserItem>();

        // Parallel to lbUsers.Items: which UserItem each visible row maps to.
        private List<UserItem> _filteredUsers = new List<UserItem>();

        public UserTeamRoleInspectorControl()
        {
            InitializeComponent();
        }

        // ------------------------------------------------------------------ UI events

        private void tsbLoad_Click(object sender, EventArgs e) => ExecuteMethod(LoadUsers);

        private void txtUserFilter_TextChanged(object sender, EventArgs e) => PopulateUserList();

        private void chkHideDisabled_CheckedChanged(object sender, EventArgs e) => PopulateUserList();

        private void lbUsers_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selected = GetSelectedUser();
            if (selected == null)
            {
                ClearDetail();
                return;
            }

            ExecuteMethod(() => LoadAssignments(selected));
        }

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
                    PopulateUserList();
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

        // ------------------------------------------------------------------ List / detail population

        private static bool Match(string filter, params string[] fields)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            return fields.Any(f => !string.IsNullOrEmpty(f) &&
                                   f.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
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

        private static string ListLabel(UserItem u)
        {
            var label = $"{u.Name}  (Team: {u.TeamCount}, Direct: {u.DirectCount})";
            return u.IsDisabled ? $"{label}  (disabled)" : label;
        }

        private void ClearDetail()
        {
            dgvDirect.Rows.Clear();
            dgvTeam.Rows.Clear();
            tvAssignments.Nodes.Clear();

            lblName.Text = "Select a user";
            lblBusinessUnit.Text = "";
            lblDisabledBadge.Visible = false;
            lblDirectTile.Text = "Direct\n—";
            lblTeamTile.Text = "Team-Derived\n—";

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

        // 3-level tree: Direct Roles / one node per source team -> Role node -> Role Business Unit leaf.
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
            lblStatus.Text = $"Users: {lbUsers.Items.Count} shown ({_allUsers.Count} total)   |   " +
                             $"Direct: {dgvDirect.Rows.Count}   Team-Derived: {dgvTeam.Rows.Count}";
        }

        private UserItem GetSelectedUser() =>
            lbUsers.SelectedIndex >= 0 ? _filteredUsers[lbUsers.SelectedIndex] : null;
    }
}
