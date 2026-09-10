using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UserTeamRoleInspector
{
    /// <summary>Which of the two palettes a band and its badge use. Matches the detail card's
    /// stat tiles: blue is the record's own (Direct roles / Team roles), green is what comes
    /// through someone else (Team-Derived roles / Team members).</summary>
    internal enum CardAccent
    {
        Own,
        Derived
    }

    internal enum CardPillStyle
    {
        /// <summary>A business unit that isn't the selected record's own.</summary>
        Neutral,
        /// <summary>The business unit the selected user/team themselves belong to.</summary>
        Home,
        /// <summary>Something the reader should notice - today only "disabled".</summary>
        Alert
    }

    internal struct CardPill
    {
        public readonly string Text;
        public readonly CardPillStyle Style;

        public CardPill(string text, CardPillStyle style)
        {
            Text = text ?? string.Empty;
            Style = style;
        }
    }

    /// <summary>
    /// One row in a <see cref="CardListView"/>: a section band, an entry (a role with its
    /// business-unit pills, or a team member), or a muted note.
    /// </summary>
    internal class CardRow
    {
        public CardRowKind Kind { get; private set; }
        public string Title { get; private set; }
        /// <summary>Dim text after the title - the band's own BU, or the source table.</summary>
        public string Meta { get; private set; }
        /// <summary>Band badge. Counts assignments, not deduplicated entry rows, so it keeps
        /// matching the stat tiles above.</summary>
        public int Count { get; private set; }
        public CardAccent Accent { get; private set; }
        public IList<CardPill> Pills { get; private set; }
        /// <summary>Set by the builder, not by painting: zebra striping has to survive rows of
        /// differing heights, so the parity is decided per section when the rows are made.</summary>
        public bool Alternate { get; set; }

        private static readonly CardPill[] NoPills = new CardPill[0];

        public static CardRow Section(string title, string meta, int count, CardAccent accent) =>
            new CardRow { Kind = CardRowKind.Section, Title = title, Meta = meta, Count = count, Accent = accent, Pills = NoPills };

        public static CardRow Entry(string title, IList<CardPill> pills) =>
            new CardRow { Kind = CardRowKind.Entry, Title = title, Pills = pills ?? NoPills };

        public static CardRow Note(string text) =>
            new CardRow { Kind = CardRowKind.Note, Title = text, Pills = NoPills };
    }

    internal enum CardRowKind
    {
        Section,
        Entry,
        Note
    }

    /// <summary>
    /// The detail pane's role list: section bands with a count badge, and one row per role
    /// carrying its business units as pills. A role scoped to three business units is one row
    /// with three pills, not three rows.
    ///
    /// Owner-drawn on a ListBox rather than a ListView because rows have to grow: when the pills
    /// don't fit beside the role name they wrap onto their own line, and only ListBox
    /// (DrawMode.OwnerDrawVariable + MeasureItem) supports variable row heights.
    /// </summary>
    internal class CardListView : ListBox
    {
        // --- metrics (at 100% DPI; everything else is derived from measured text) ---
        private const int PadLeft = 14;
        private const int PadRight = 12;
        private const int EntryHeight = 25;
        private const int SectionHeight = 27;
        private const int NoteHeight = 34;
        private const int PillHeight = 17;
        private const int PillPadX = 8;
        private const int PillGap = 5;
        private const int PillIndent = 28;
        private const int PillLineHeight = 21;
        private const int NameGap = 16;
        private const int AccentBarWidth = 4;

        private static readonly Color BandBack = Color.FromArgb(244, 246, 248);
        private static readonly Color BandRule = Color.FromArgb(232, 234, 237);
        private static readonly Color BandInk = Color.FromArgb(58, 68, 80);
        private static readonly Color DimInk = Color.FromArgb(107, 114, 128);
        private static readonly Color AltRow = Color.FromArgb(250, 251, 252);
        private static readonly Color HoverRow = Color.FromArgb(240, 246, 252);
        private static readonly Color SelectedRow = Color.FromArgb(204, 228, 247);

        private static readonly Color OwnLine = Color.FromArgb(0, 110, 190);
        private static readonly Color OwnFill = Color.FromArgb(220, 233, 247);
        private static readonly Color OwnInk = Color.FromArgb(19, 78, 126);
        private static readonly Color OwnBorder = Color.FromArgb(191, 216, 239);

        private static readonly Color DerivedLine = Color.FromArgb(46, 125, 82);
        private static readonly Color DerivedFill = Color.FromArgb(221, 238, 226);
        private static readonly Color DerivedInk = Color.FromArgb(32, 86, 58);
        private static readonly Color DerivedBorder = Color.FromArgb(195, 223, 205);

        private static readonly Color NeutralFill = Color.FromArgb(238, 241, 244);
        private static readonly Color NeutralInk = Color.FromArgb(65, 80, 95);
        private static readonly Color NeutralBorder = Color.FromArgb(223, 228, 233);

        // Same red as the detail card's DISABLED badge, so the two read as one signal.
        private static readonly Color AlertFill = Color.FromArgb(250, 230, 230);
        private static readonly Color AlertInk = Color.FromArgb(160, 30, 30);
        private static readonly Color AlertBorder = Color.FromArgb(233, 199, 199);

        private const TextFormatFlags TextFlags =
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;

        private readonly Font _bandFont = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);
        private readonly Font _metaFont = new Font("Segoe UI", 8.25f);
        private readonly Font _pillFont = new Font("Segoe UI", 8.25f);
        private readonly Font _badgeFont = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold);

        private int _hoverIndex = -1;
        private int _lastLayoutWidth;

        public CardListView()
        {
            DrawMode = DrawMode.OwnerDrawVariable;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            SelectionMode = SelectionMode.One;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        /// <summary>Replaces everything shown. Rows are built by the caller so this control
        /// knows nothing about assignments, teams or business units.</summary>
        public void SetRows(IEnumerable<CardRow> rows)
        {
            BeginUpdate();
            try
            {
                Items.Clear();
                _hoverIndex = -1;
                foreach (var row in rows)
                    Items.Add(row);
            }
            finally
            {
                EndUpdate();
            }

            if (Items.Count > 0) TopIndex = 0;
        }

        // ListBox measures items once, as they're added, so a resize would otherwise leave rows
        // sized for the old width - which is exactly when pills need to re-wrap.
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ClientSize.Width == _lastLayoutWidth) return;
            _lastLayoutWidth = ClientSize.Width;
            if (Items.Count > 0) RefreshItems();
        }

        protected override void OnMeasureItem(MeasureItemEventArgs e)
        {
            var row = RowAt(e.Index);
            if (row == null) { base.OnMeasureItem(e); return; }

            switch (row.Kind)
            {
                case CardRowKind.Section:
                    e.ItemHeight = SectionHeight;
                    break;
                case CardRowKind.Note:
                    e.ItemHeight = NoteHeight;
                    break;
                default:
                    e.ItemHeight = LayoutEntry(row, LayoutWidth(), e.Graphics).Height;
                    break;
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var row = RowAt(e.Index);
            if (row == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            switch (row.Kind)
            {
                case CardRowKind.Section:
                    DrawSection(g, row, e.Bounds);
                    break;
                case CardRowKind.Note:
                    using (var brush = new SolidBrush(BackColor))
                        g.FillRectangle(brush, e.Bounds);
                    TextRenderer.DrawText(g, row.Title, _metaFont,
                        new Rectangle(e.Bounds.X + PadLeft, e.Bounds.Y, e.Bounds.Width - PadLeft - PadRight, e.Bounds.Height),
                        DimInk, TextFlags | TextFormatFlags.EndEllipsis);
                    break;
                default:
                    DrawEntry(g, row, e.Bounds, e.Index);
                    break;
            }
        }

        private void DrawSection(Graphics g, CardRow row, Rectangle bounds)
        {
            var line = row.Accent == CardAccent.Own ? OwnLine : DerivedLine;

            using (var back = new SolidBrush(BandBack))
                g.FillRectangle(back, bounds);
            using (var rule = new Pen(BandRule))
            {
                g.DrawLine(rule, bounds.Left, bounds.Top, bounds.Right, bounds.Top);
                g.DrawLine(rule, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
            }
            using (var bar = new SolidBrush(line))
                g.FillRectangle(bar, bounds.Left, bounds.Top, AccentBarWidth, bounds.Height);

            var badgeWidth = DrawBadge(g, row, bounds);

            var x = bounds.Left + PadLeft;
            var titleWidth = TextRenderer.MeasureText(g, row.Title, _bandFont, Size.Empty, TextFlags).Width;
            var textRoom = bounds.Right - PadRight - badgeWidth - 10 - x;

            TextRenderer.DrawText(g, row.Title, _bandFont,
                new Rectangle(x, bounds.Y, Math.Min(titleWidth, Math.Max(0, textRoom)), bounds.Height),
                BandInk, TextFlags | TextFormatFlags.EndEllipsis);

            if (string.IsNullOrEmpty(row.Meta)) return;

            var metaX = x + titleWidth + 10;
            var metaRoom = bounds.Right - PadRight - badgeWidth - 10 - metaX;
            if (metaRoom > 20)
            {
                TextRenderer.DrawText(g, row.Meta, _metaFont,
                    new Rectangle(metaX, bounds.Y, metaRoom, bounds.Height),
                    DimInk, TextFlags | TextFormatFlags.EndEllipsis);
            }
        }

        /// <summary>Draws the band's count badge and returns the width it took.</summary>
        private int DrawBadge(Graphics g, CardRow row, Rectangle bounds)
        {
            var text = row.Count.ToString();
            var fill = row.Accent == CardAccent.Own ? OwnFill : DerivedFill;
            var ink = row.Accent == CardAccent.Own ? OwnInk : DerivedInk;

            var width = TextRenderer.MeasureText(g, text, _badgeFont, Size.Empty, TextFlags).Width + PillPadX * 2;
            var rect = new Rectangle(bounds.Right - PadRight - width,
                                     bounds.Y + (bounds.Height - PillHeight) / 2, width, PillHeight);

            using (var path = RoundedRect(rect, PillHeight / 2))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);

            TextRenderer.DrawText(g, text, _badgeFont, rect, ink,
                TextFlags | TextFormatFlags.HorizontalCenter);

            return width;
        }

        private void DrawEntry(Graphics g, CardRow row, Rectangle bounds, int index)
        {
            var back = BackColor;
            if (row.Alternate) back = AltRow;
            if (index == _hoverIndex) back = HoverRow;
            if (SelectedIndex == index) back = SelectedRow;

            using (var brush = new SolidBrush(back))
                g.FillRectangle(brush, bounds);

            var layout = LayoutEntry(row, LayoutWidth(), g);

            var nameRect = layout.NameRect;
            nameRect.Offset(bounds.X, bounds.Y);
            TextRenderer.DrawText(g, row.Title, Font, nameRect, ForeColor,
                TextFlags | TextFormatFlags.EndEllipsis);

            for (var i = 0; i < layout.PillRects.Count; i++)
            {
                var rect = layout.PillRects[i];
                rect.Offset(bounds.X, bounds.Y);
                DrawPill(g, row.Pills[i], rect);
            }
        }

        private void DrawPill(Graphics g, CardPill pill, Rectangle rect)
        {
            Color fill, ink, border;
            switch (pill.Style)
            {
                case CardPillStyle.Home:
                    fill = OwnFill; ink = OwnInk; border = OwnBorder;
                    break;
                case CardPillStyle.Alert:
                    fill = AlertFill; ink = AlertInk; border = AlertBorder;
                    break;
                default:
                    fill = NeutralFill; ink = NeutralInk; border = NeutralBorder;
                    break;
            }

            using (var path = RoundedRect(rect, PillHeight / 2))
            {
                using (var brush = new SolidBrush(fill))
                    g.FillPath(brush, path);
                using (var pen = new Pen(border))
                    g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, pill.Text, _pillFont, rect, ink,
                TextFlags | TextFormatFlags.HorizontalCenter);
        }

        // Measuring and painting must agree on where every pill sits, so both go through here.
        private EntryLayout LayoutEntry(CardRow row, int width, Graphics g)
        {
            var available = width - PadLeft - PadRight;
            var pills = row.Pills;

            if (pills.Count == 0)
            {
                return new EntryLayout(
                    new Rectangle(PadLeft, 0, Math.Max(0, available), EntryHeight),
                    new List<Rectangle>(),
                    EntryHeight);
            }

            var widths = new int[pills.Count];
            var pillsWidth = 0;
            for (var i = 0; i < pills.Count; i++)
            {
                widths[i] = TextRenderer.MeasureText(g, pills[i].Text, _pillFont, Size.Empty, TextFlags).Width + PillPadX * 2;
                pillsWidth += widths[i] + (i > 0 ? PillGap : 0);
            }

            var nameWidth = TextRenderer.MeasureText(g, row.Title, Font, Size.Empty, TextFlags).Width;
            var rects = new List<Rectangle>(pills.Count);

            // Everything on one line: name left, pills flush right.
            if (nameWidth + NameGap + pillsWidth <= available)
            {
                var x = PadLeft + available - pillsWidth;
                var y = (EntryHeight - PillHeight) / 2;
                for (var i = 0; i < pills.Count; i++)
                {
                    rects.Add(new Rectangle(x, y, widths[i], PillHeight));
                    x += widths[i] + PillGap;
                }
                return new EntryLayout(
                    new Rectangle(PadLeft, 0, available - pillsWidth - NameGap, EntryHeight),
                    rects,
                    EntryHeight);
            }

            // Otherwise the name keeps the first line and the pills wrap under it, indented.
            var lineLeft = PadLeft + PillIndent;
            var lineRoom = width - PadRight - lineLeft;
            var lineX = lineLeft;
            var lineY = EntryHeight - 3;

            for (var i = 0; i < pills.Count; i++)
            {
                if (lineX > lineLeft && lineX + widths[i] > lineLeft + lineRoom)
                {
                    lineX = lineLeft;
                    lineY += PillLineHeight;
                }
                rects.Add(new Rectangle(lineX, lineY, widths[i], PillHeight));
                lineX += widths[i] + PillGap;
            }

            return new EntryLayout(
                new Rectangle(PadLeft, 0, Math.Max(0, available), EntryHeight - 3),
                rects,
                lineY + PillHeight + 6);
        }

        private int LayoutWidth()
        {
            var width = ClientSize.Width;
            // Measured before the control has been laid out: fall back to something sane so the
            // first render isn't sized against a zero-width client area.
            return width > 40 ? width : 360;
        }

        private CardRow RowAt(int index) =>
            index >= 0 && index < Items.Count ? Items[index] as CardRow : null;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = IndexFromPoint(e.Location);
            if (index == _hoverIndex) return;

            var previous = _hoverIndex;
            _hoverIndex = RowAt(index)?.Kind == CardRowKind.Entry ? index : -1;
            InvalidateRow(previous);
            InvalidateRow(_hoverIndex);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            var previous = _hoverIndex;
            _hoverIndex = -1;
            InvalidateRow(previous);
        }

        private void InvalidateRow(int index)
        {
            if (index >= 0 && index < Items.Count) Invalidate(GetItemRectangle(index));
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            if (d <= 0 || rect.Width <= d || rect.Height <= d)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d - 1, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d - 1, rect.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bandFont.Dispose();
                _metaFont.Dispose();
                _pillFont.Dispose();
                _badgeFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private struct EntryLayout
        {
            public readonly Rectangle NameRect;
            public readonly IList<Rectangle> PillRects;
            public readonly int Height;

            public EntryLayout(Rectangle nameRect, IList<Rectangle> pillRects, int height)
            {
                NameRect = nameRect;
                PillRects = pillRects;
                Height = height;
            }
        }
    }
}
