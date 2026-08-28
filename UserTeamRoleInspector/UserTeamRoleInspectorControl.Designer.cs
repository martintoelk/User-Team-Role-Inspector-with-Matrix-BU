using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UserTeamRoleInspector
{
    partial class UserTeamRoleInspectorControl
    {
        private ToolStrip toolStrip;
        private ToolStripButton tsbLoad;

        private TableLayoutPanel mainLayout;

        private Panel pickerModePill;
        private Button btnPillModeUsers;
        private Button btnPillModeTeams;

        private Label lblUsers;
        private TextBox txtUserFilter;
        private CheckBox chkHideDisabled;
        private CheckBox chkIgnoreAgentTeams;
        private CheckBox chkIgnoreAccessTeams;
        private ListView lbUsers;

        private FlowLayoutPanel detailCard;
        private Label lblName;
        private Label lblBusinessUnit;
        private Label lblDisabledBadge;
        private Label lblDirectTile;
        private Label lblTeamTile;

        private Panel viewTogglePill;
        private Button btnPillGrid;
        private Button btnPillTree;

        private SplitContainer gridsSplit;
        private Label lblDirectHeader;
        private DataGridView dgvDirect;
        private Label lblTeamHeader;
        private DataGridView dgvTeam;
        private TreeView tvAssignments;

        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;

        private void InitializeComponent()
        {
            this.toolStrip = new ToolStrip();
            this.tsbLoad = new ToolStripButton();

            this.mainLayout = new TableLayoutPanel();

            this.pickerModePill = new Panel();
            this.btnPillModeUsers = new Button();
            this.btnPillModeTeams = new Button();

            this.lblUsers = new Label();
            this.txtUserFilter = new TextBox();
            this.chkHideDisabled = new CheckBox();
            this.chkIgnoreAgentTeams = new CheckBox();
            this.chkIgnoreAccessTeams = new CheckBox();
            this.lbUsers = new ListView();

            this.detailCard = new FlowLayoutPanel();
            this.lblName = new Label();
            this.lblBusinessUnit = new Label();
            this.lblDisabledBadge = new Label();
            this.lblDirectTile = StatTile(Color.FromArgb(225, 235, 250));
            this.lblTeamTile = StatTile(Color.FromArgb(235, 245, 230));

            this.viewTogglePill = new Panel();
            this.btnPillGrid = new Button();
            this.btnPillTree = new Button();

            this.gridsSplit = new SplitContainer();
            this.lblDirectHeader = new Label();
            this.dgvDirect = new DataGridView();
            this.lblTeamHeader = new Label();
            this.dgvTeam = new DataGridView();
            this.tvAssignments = new TreeView();

            this.statusStrip = new StatusStrip();
            this.lblStatus = new ToolStripStatusLabel();

            ((System.ComponentModel.ISupportInitialize)(this.dgvDirect)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvTeam)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridsSplit)).BeginInit();
            this.SuspendLayout();

            // ---- ToolStrip ----
            this.tsbLoad.Text = "Load / Refresh Users";
            this.tsbLoad.Image = CreateRefreshIcon();
            this.tsbLoad.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            this.tsbLoad.Click += new System.EventHandler(this.tsbLoad_Click);

            this.toolStrip.Items.AddRange(new ToolStripItem[] { this.tsbLoad });
            this.toolStrip.Location = new System.Drawing.Point(0, 0);
            this.toolStrip.GripStyle = ToolStripGripStyle.Hidden;

            // ---- Main 2-pane layout: master list | detail + results ----
            // Percent-sized columns keep both panes responsive as the XrmToolBox host is resized.
            this.mainLayout.Dock = DockStyle.Fill;
            this.mainLayout.ColumnCount = 2;
            this.mainLayout.RowCount = 1;
            this.mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            this.mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            this.mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.mainLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;

            // Left: master list
            // ---- Users|Teams segmented toggle, above the master list ----
            this.pickerModePill.Location = new Point(2, 4);
            this.pickerModePill.Size = new Size(160, 26);
            this.pickerModePill.BackColor = Color.FromArgb(230, 230, 230);

            this.btnPillModeUsers.Text = "Users";
            this.btnPillModeUsers.Size = new Size(80, 26);
            this.btnPillModeUsers.FlatStyle = FlatStyle.Flat;
            this.btnPillModeUsers.FlatAppearance.BorderSize = 0;
            this.btnPillModeUsers.Click += new System.EventHandler(this.btnPillModeUsers_Click);

            this.btnPillModeTeams.Text = "Teams";
            this.btnPillModeTeams.Size = new Size(80, 26);
            this.btnPillModeTeams.Left = 80;
            this.btnPillModeTeams.FlatStyle = FlatStyle.Flat;
            this.btnPillModeTeams.FlatAppearance.BorderSize = 0;
            this.btnPillModeTeams.Click += new System.EventHandler(this.btnPillModeTeams_Click);

            this.pickerModePill.Controls.Add(this.btnPillModeUsers);
            this.pickerModePill.Controls.Add(this.btnPillModeTeams);

            var modeHost = new Panel { Dock = DockStyle.Top, Height = 34 };
            modeHost.Controls.Add(this.pickerModePill);

            this.lblUsers.Text = "Users";
            this.lblUsers.Dock = DockStyle.Top;
            this.lblUsers.Height = 20;
            this.lblUsers.Padding = new Padding(2, 3, 0, 0);

            this.txtUserFilter.Dock = DockStyle.Fill;
            this.txtUserFilter.BorderStyle = BorderStyle.None;
            this.txtUserFilter.TextChanged += new System.EventHandler(this.txtUserFilter_TextChanged);
            var userFilterBox = CreateSearchBox(this.txtUserFilter);

            this.chkHideDisabled.Text = "Hide disabled users";
            this.chkHideDisabled.Dock = DockStyle.Top;
            this.chkHideDisabled.AutoSize = true;
            this.chkHideDisabled.Padding = new Padding(2, 4, 0, 4);
            this.chkHideDisabled.Checked = true;
            this.chkHideDisabled.CheckedChanged += new System.EventHandler(this.chkHideDisabled_CheckedChanged);

            this.chkIgnoreAgentTeams.Text = "Ignore Agent Teams";
            this.chkIgnoreAgentTeams.Dock = DockStyle.Top;
            this.chkIgnoreAgentTeams.AutoSize = true;
            this.chkIgnoreAgentTeams.Padding = new Padding(2, 4, 0, 4);
            this.chkIgnoreAgentTeams.Checked = true;
            this.chkIgnoreAgentTeams.Visible = false;
            this.chkIgnoreAgentTeams.CheckedChanged += new System.EventHandler(this.chkIgnoreAgentTeams_CheckedChanged);

            this.chkIgnoreAccessTeams.Text = "Ignore Access Team";
            this.chkIgnoreAccessTeams.Dock = DockStyle.Top;
            this.chkIgnoreAccessTeams.AutoSize = true;
            this.chkIgnoreAccessTeams.Padding = new Padding(2, 4, 0, 4);
            this.chkIgnoreAccessTeams.Checked = true;
            this.chkIgnoreAccessTeams.Visible = false;
            this.chkIgnoreAccessTeams.CheckedChanged += new System.EventHandler(this.chkIgnoreAccessTeams_CheckedChanged);

            this.lbUsers.Dock = DockStyle.Fill;
            this.lbUsers.View = View.Details;
            this.lbUsers.FullRowSelect = true;
            this.lbUsers.GridLines = true;
            this.lbUsers.HideSelection = false;
            this.lbUsers.MultiSelect = false;
            this.lbUsers.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            this.lbUsers.Font = new Font("Segoe UI", 10f);
            ConfigureListView(this.lbUsers, "Full Name", "Direct Assignments", "Team-Derived Assignments");
            this.lbUsers.SelectedIndexChanged += new System.EventHandler(this.lbUsers_SelectedIndexChanged);

            var listPanel = new Panel { Dock = DockStyle.Fill };
            // NOTE: add order = docked controls draw top-most last, so add Fill first, then the Top items.
            listPanel.Controls.Add(this.lbUsers);
            listPanel.Controls.Add(this.chkHideDisabled);
            listPanel.Controls.Add(this.chkIgnoreAgentTeams);
            listPanel.Controls.Add(this.chkIgnoreAccessTeams);
            listPanel.Controls.Add(userFilterBox);
            listPanel.Controls.Add(this.lblUsers);
            listPanel.Controls.Add(modeHost);
            this.mainLayout.Controls.Add(listPanel, 0, 0);

            SetPillActive(this.btnPillModeUsers, this.btnPillModeTeams);

            // Right: detail card + split results grids
            this.detailCard.Dock = DockStyle.Top;
            this.detailCard.Height = 78;
            this.detailCard.BackColor = Color.FromArgb(250, 250, 250);
            this.detailCard.Padding = new Padding(10);

            var nameCol = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(0, 0, 24, 0) };
            var nameRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };

            this.lblName.AutoSize = true;
            this.lblName.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
            this.lblName.Text = "Select a user";

            this.lblDisabledBadge.AutoSize = true;
            this.lblDisabledBadge.Padding = new Padding(8, 3, 8, 3);
            this.lblDisabledBadge.BackColor = Color.FromArgb(250, 230, 230);
            this.lblDisabledBadge.ForeColor = Color.FromArgb(160, 30, 30);
            this.lblDisabledBadge.Text = "DISABLED";
            this.lblDisabledBadge.Visible = false;
            this.lblDisabledBadge.Margin = new Padding(10, 4, 0, 0);

            nameRow.Controls.Add(this.lblName);
            nameRow.Controls.Add(this.lblDisabledBadge);

            this.lblBusinessUnit.AutoSize = true;
            this.lblBusinessUnit.ForeColor = Color.DimGray;

            nameCol.Controls.Add(nameRow);
            nameCol.Controls.Add(this.lblBusinessUnit);

            this.detailCard.Controls.Add(nameCol);
            this.detailCard.Controls.Add(this.lblDirectTile);
            this.detailCard.Controls.Add(this.lblTeamTile);

            // ---- Grid|Tree segmented toggle, by the detail card / stat tiles ----
            this.viewTogglePill.Size = new Size(140, 28);
            this.viewTogglePill.Margin = new Padding(24, 12, 0, 0);
            this.viewTogglePill.BackColor = Color.FromArgb(230, 230, 230);

            this.btnPillGrid.Text = "Grid";
            this.btnPillGrid.Size = new Size(70, 28);
            this.btnPillGrid.FlatStyle = FlatStyle.Flat;
            this.btnPillGrid.FlatAppearance.BorderSize = 0;
            this.btnPillGrid.Click += new System.EventHandler(this.btnPillGrid_Click);

            this.btnPillTree.Text = "Tree";
            this.btnPillTree.Size = new Size(70, 28);
            this.btnPillTree.Left = 70;
            this.btnPillTree.FlatStyle = FlatStyle.Flat;
            this.btnPillTree.FlatAppearance.BorderSize = 0;
            this.btnPillTree.Click += new System.EventHandler(this.btnPillTree_Click);

            this.viewTogglePill.Controls.Add(this.btnPillGrid);
            this.viewTogglePill.Controls.Add(this.btnPillTree);
            this.detailCard.Controls.Add(this.viewTogglePill);

            this.gridsSplit.Dock = DockStyle.Fill;
            this.gridsSplit.Orientation = Orientation.Horizontal;
            this.gridsSplit.SplitterDistance = 140;

            this.lblDirectHeader.Text = "Direct Assignments";
            this.lblDirectHeader.Dock = DockStyle.Top;
            this.lblDirectHeader.Height = 22;
            this.lblDirectHeader.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            this.lblDirectHeader.Padding = new Padding(2, 4, 0, 0);

            this.dgvDirect.Dock = DockStyle.Fill;
            ConfigureGrid(this.dgvDirect, "Role", "Role Business Unit");

            var directPanel = new Panel { Dock = DockStyle.Fill };
            directPanel.Controls.Add(this.dgvDirect);
            directPanel.Controls.Add(this.lblDirectHeader);
            this.gridsSplit.Panel1.Controls.Add(directPanel);

            this.lblTeamHeader.Text = "Team-Derived Assignments";
            this.lblTeamHeader.Dock = DockStyle.Top;
            this.lblTeamHeader.Height = 22;
            this.lblTeamHeader.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            this.lblTeamHeader.Padding = new Padding(2, 4, 0, 0);

            this.dgvTeam.Dock = DockStyle.Fill;
            ConfigureGrid(this.dgvTeam, "Role", "Role Business Unit", "Team", "Team Business Unit");

            var teamPanel = new Panel { Dock = DockStyle.Fill };
            teamPanel.Controls.Add(this.dgvTeam);
            teamPanel.Controls.Add(this.lblTeamHeader);
            this.gridsSplit.Panel2.Controls.Add(teamPanel);

            this.tvAssignments.Dock = DockStyle.Fill;
            this.tvAssignments.Font = new Font("Segoe UI", 9.5f);

            var detailPanel = new Panel { Dock = DockStyle.Fill };
            detailPanel.Controls.Add(this.tvAssignments);
            detailPanel.Controls.Add(this.gridsSplit);
            detailPanel.Controls.Add(this.detailCard);
            this.mainLayout.Controls.Add(detailPanel, 1, 0);

            // Default view on load is Tree, not Grid (decided when resolving #6).
            this.gridsSplit.Visible = false;
            this.tvAssignments.Visible = true;
            SetPillActive(this.btnPillTree, this.btnPillGrid);

            // ---- StatusStrip ----
            this.lblStatus.Text = "Click \"Load / Refresh Users\" after connecting to an environment.";
            this.lblStatus.Spring = true;
            this.lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            this.statusStrip.Items.Add(this.lblStatus);

            // ---- Control ----
            this.Controls.Add(this.mainLayout);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.toolStrip);
            this.Name = "UserTeamRoleInspectorControl";
            this.Size = new System.Drawing.Size(820, 560);

            ((System.ComponentModel.ISupportInitialize)(this.dgvDirect)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvTeam)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridsSplit)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private static void ConfigureGrid(DataGridView grid, params string[] columns)
        {
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ColumnHeaderMouseClick -= Grid_ColumnHeaderMouseClick;
            grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
            foreach (var c in columns)
            {
                var column = grid.Columns[grid.Columns.Add(c, c)];
                // Results are added as unbound text rows. The control handles header clicks and
                // issues the sort command explicitly for these programmatic columns.
                column.SortMode = DataGridViewColumnSortMode.Programmatic;
            }
        }

        // Fixed-width columns are re-added and sized by ConfigureListView; the last column stretches
        // to fill the remaining width so it survives the split container being dragged wider.
        internal static void ConfigureListView(ListView listView, params string[] columns)
        {
            listView.Columns.Clear();
            for (var i = 0; i < columns.Length; i++)
            {
                var isLast = i == columns.Length - 1;
                listView.Columns.Add(columns[i], isLast ? -2 : 140);
            }
        }

        private static readonly Color PillActiveColor = Color.FromArgb(0, 110, 190);

        private static void SetPillActive(Button active, Button inactive)
        {
            active.BackColor = PillActiveColor;
            active.ForeColor = Color.White;
            inactive.BackColor = SystemColors.Control;
            inactive.ForeColor = SystemColors.ControlText;
        }

        private static Label StatTile(Color back) => new Label
        {
            AutoSize = false,
            Size = new Size(120, 52),
            BackColor = back,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 0, 12, 0)
        };

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private const int EM_SETCUEBANNER = 0x1501;

        // Native "cue banner" placeholder text - shown only while the box is empty and unfocused,
        // and never interferes with the actual Text value used by the filter logic. Deferred to
        // HandleCreated (rather than sent immediately) so it doesn't force premature Win32 handle
        // creation on a still-unparented control, and so it's reapplied if the handle is ever
        // recreated (e.g. by host theming).
        private static void SetCueBanner(TextBox textBox, string text) =>
            textBox.HandleCreated += (s, e) => SendMessage(textBox.Handle, EM_SETCUEBANNER, IntPtr.Zero, text);

        // Wraps a filter TextBox with a magnifying-glass icon and a bordered frame so it reads as
        // a search box rather than a plain, purpose-unclear textbox. The caller must already have
        // set the TextBox's Dock to Fill and BorderStyle to None. Matches the sibling Assigner
        // repo's search box styling exactly.
        private static Panel CreateSearchBox(TextBox textBox)
        {
            var icon = new PictureBox
            {
                Image = CreateSearchIcon(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Left,
                Cursor = Cursors.IBeam,
                BackColor = SystemColors.Window,
            };
            // Clicking the icon should focus the box, same as clicking the box itself.
            icon.Click += (s, e) => textBox.Focus();

            var container = new Panel
            {
                Dock = DockStyle.Top,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
            };
            // Add order = docked controls draw top-most last, so add Fill first, then Left.
            container.Controls.Add(textBox);
            container.Controls.Add(icon);

            // TextBox.Font is ambient (falls back to Control.DefaultFont until the control is
            // actually parented into a tree with a real Font, e.g. once the XTB host themes the
            // plugin), so size off FontChanged rather than computing PreferredHeight once here -
            // that would freeze in the wrong height if the host's font differs from the default.
            void SyncToFont(object s, EventArgs e)
            {
                container.Height = textBox.PreferredHeight + 2;
                icon.Width = container.Height;
            }
            textBox.FontChanged += SyncToFont;
            SyncToFont(null, EventArgs.Empty);

            SetCueBanner(textBox, "Search...");

            return container;
        }

        // Monochrome magnifying glass for the search-box icon; deliberately uncolored so it reads
        // as an inline glyph inside a white textbox rather than a toolbar badge.
        internal static Image CreateSearchIcon()
        {
            var bmp = new Bitmap(14, 14);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var pen = new Pen(Color.FromArgb(120, 120, 120), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawEllipse(pen, 1f, 1f, 7.5f, 7.5f);
                    g.DrawLine(pen, 8.0f, 8.0f, 12.5f, 12.5f);
                }
            }
            return bmp;
        }

        // Toolbar-sized (16x16) circular arrow, matching the sibling Assigner plugin's style.
        internal static Image CreateRefreshIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var brush = new SolidBrush(Color.FromArgb(0, 110, 190)))
                    g.FillEllipse(brush, 0, 0, 16, 16);

                var rect = new RectangleF(3.5f, 3.5f, 9, 9);
                using (var pen = new Pen(Color.White, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, rect, -40, 260);

                const double endAngle = 220 * Math.PI / 180;
                var cx = rect.X + rect.Width / 2;
                var cy = rect.Y + rect.Height / 2;
                var tipX = cx + rect.Width / 2 * Math.Cos(endAngle);
                var tipY = cy + rect.Height / 2 * Math.Sin(endAngle);
                var tip = new PointF((float)tipX, (float)tipY);
                var p1 = new PointF((float)(tipX - 2.6), (float)(tipY - 0.5));
                var p2 = new PointF((float)(tipX + 1.0), (float)(tipY - 2.3));
                using (var whiteBrush = new SolidBrush(Color.White))
                    g.FillPolygon(whiteBrush, new[] { p1, p2, tip });
            }
            return bmp;
        }
    }
}
