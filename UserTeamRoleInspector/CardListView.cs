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
    /// Rows have to grow - when the pills don't fit beside the role name they wrap onto their own
    /// line - and this draws and scrolls itself rather than deriving from ListBox to get that. An
    /// owner-drawn ListBox takes its row heights from WM_MEASUREITEM, which Windows sends to the
    /// *parent* window for the parent to reflect back to the list; hosted inside XrmToolBox that
    /// reflection doesn't arrive, so every row collapsed to one line of text while the painting
    /// still drew 25px-tall rows into it and consecutive rows overlapped. Owning the layout drops
    /// the dependency on that message.
    /// </summary>
    internal class CardListView : Control
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
        private const int FallbackWheelRows = 3;

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

        private readonly VScrollBar _scroll = new VScrollBar { Dock = DockStyle.Right, Visible = false };
        private readonly List<CardRow> _rows = new List<CardRow>();
        /// <summary>Per row: its top in content coordinates, its height, and - for entries - where
        /// the name and each pill sit relative to that top. Measuring and painting have to agree
        /// on all of it, so both read this instead of working it out twice.</summary>
        private readonly List<RowLayout> _layout = new List<RowLayout>();

        private int _contentWidth;
        private int _contentHeight;
        /// <summary>The client width the current <see cref="_layout"/> was measured against, so a
        /// resize that doesn't change it can skip re-measuring. -1 forces the next measure.</summary>
        private int _measuredAt = -1;
        private int _offset;
        private int _hoverIndex = -1;
        private int _selectedIndex = -1;

        public CardListView()
        {
            BackColor = Color.White;
            ForeColor = SystemColors.ControlText;
            Font = new Font("Segoe UI", 9.5f);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            _scroll.Scroll += (s, e) => ScrollTo(e.NewValue);
            Controls.Add(_scroll);
        }

        /// <summary>Replaces everything shown. Rows are built by the caller so this control
        /// knows nothing about assignments, teams or business units.</summary>
        public void SetRows(IEnumerable<CardRow> rows)
        {
            _rows.Clear();
            if (rows != null) _rows.AddRange(rows);
            _hoverIndex = -1;
            _selectedIndex = -1;
            _offset = 0;
            _measuredAt = -1;
            Relayout();
        }

        // ------------------------------------------------------------------ layout

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Relayout();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            _measuredAt = -1; // text metrics changed, so the cached heights can't be reused
            Relayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Rows can arrive before the control has a handle, and without one there is no
            // Graphics to measure text against - so that first layout has to happen here.
            if (_rows.Count > 0) Relayout();
        }

        /// <summary>Measures every row against the current width. The scrollbar takes width away,
        /// and whether it's needed depends on the heights that width decides, so this measures
        /// once without it and re-measures only if it turns out to be needed.</summary>
        private void Relayout()
        {
            // Row heights depend only on the width, so a change that leaves the width alone - and
            // doesn't flip the scrollbar on or off, which would take width away - reuses them.
            if (_measuredAt == ClientSize.Width && _layout.Count == _rows.Count &&
                (_contentHeight > ClientSize.Height) == _scroll.Visible)
            {
                UpdateScrollBar();
                Invalidate();
                return;
            }

            _layout.Clear();
            _measuredAt = -1;
            _contentWidth = Math.Max(0, ClientSize.Width);
            _contentHeight = 0;

            if (_rows.Count > 0 && IsHandleCreated)
            {
                using (var g = CreateGraphics())
                {
                    _contentHeight = Measure(g, _contentWidth);
                    if (_contentHeight > ClientSize.Height)
                    {
                        _contentWidth = Math.Max(0, ClientSize.Width - _scroll.Width);
                        _contentHeight = Measure(g, _contentWidth);
                    }
                }
                _measuredAt = ClientSize.Width;
            }

            UpdateScrollBar();
            Invalidate();
        }

        private int Measure(Graphics g, int width)
        {
            _layout.Clear();
            var top = 0;
            foreach (var row in _rows)
            {
                switch (row.Kind)
                {
                    case CardRowKind.Section:
                        _layout.Add(RowLayout.Fixed(top, SectionHeight));
                        top += SectionHeight;
                        break;
                    case CardRowKind.Note:
                        _layout.Add(RowLayout.Fixed(top, NoteHeight));
                        top += NoteHeight;
                        break;
                    default:
                        var entry = LayoutEntry(row, width, g);
                        _layout.Add(RowLayout.ForEntry(top, entry));
                        top += entry.Height;
                        break;
                }
            }
            return top;
        }

        private void UpdateScrollBar()
        {
            var overflow = _contentHeight - ClientSize.Height;
            if (overflow <= 0)
            {
                _offset = 0;
                _scroll.Visible = false;
                return;
            }

            _offset = Math.Min(_offset, overflow);
            _scroll.Visible = true;
            // A scrollbar's thumb covers LargeChange of the range, so its last reachable value is
            // Maximum - LargeChange + 1. Setting Maximum to the full content height is what makes
            // the end of the list reachable rather than stopping one screenful short.
            _scroll.LargeChange = Math.Max(1, ClientSize.Height);
            _scroll.SmallChange = EntryHeight;
            _scroll.Maximum = Math.Max(0, _contentHeight - 1);
            _scroll.Value = Math.Min(_offset, _scroll.Maximum);
        }

        private void ScrollTo(int value)
        {
            var clamped = Math.Max(0, Math.Min(value, Math.Max(0, _contentHeight - ClientSize.Height)));
            if (clamped == _offset) return;
            _offset = clamped;
            if (_scroll.Visible && _scroll.Value != clamped) _scroll.Value = clamped;
            Invalidate();
        }

        // ------------------------------------------------------------------ painting

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var back = new SolidBrush(BackColor))
                g.FillRectangle(back, e.ClipRectangle);

            var viewBottom = _offset + ClientSize.Height;
            for (var i = 0; i < _rows.Count && i < _layout.Count; i++)
            {
                var layout = _layout[i];
                if (layout.Top + layout.Height <= _offset) continue;
                if (layout.Top >= viewBottom) break;

                var bounds = new Rectangle(0, layout.Top - _offset, _contentWidth, layout.Height);
                if (!bounds.IntersectsWith(e.ClipRectangle)) continue;

                var row = _rows[i];
                switch (row.Kind)
                {
                    case CardRowKind.Section:
                        DrawSection(g, row, bounds);
                        break;
                    case CardRowKind.Note:
                        TextRenderer.DrawText(g, row.Title, _metaFont,
                            new Rectangle(bounds.X + PadLeft, bounds.Y, bounds.Width - PadLeft - PadRight, bounds.Height),
                            DimInk, TextFlags | TextFormatFlags.EndEllipsis);
                        break;
                    default:
                        DrawEntry(g, row, bounds, layout.Entry, i);
                        break;
                }
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

        private void DrawEntry(Graphics g, CardRow row, Rectangle bounds, EntryLayout layout, int index)
        {
            var back = BackColor;
            if (row.Alternate) back = AltRow;
            if (index == _hoverIndex) back = HoverRow;
            if (index == _selectedIndex) back = SelectedRow;

            if (back != BackColor)
            {
                using (var brush = new SolidBrush(back))
                    g.FillRectangle(brush, bounds);
            }

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

            if (pills.Count == 0 || available <= 0)
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
            // A pill wider than the line still gets its own line rather than none at all.
            var lineRoom = Math.Max(widths[0], width - PadRight - lineLeft);
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

        // ------------------------------------------------------------------ input

        /// <summary>Index of the entry row under a client point, or -1 for bands, notes and gaps.</summary>
        private int EntryAt(Point location)
        {
            if (location.X < 0 || location.X > _contentWidth) return -1;

            var y = location.Y + _offset;
            for (var i = 0; i < _layout.Count; i++)
            {
                if (y < _layout[i].Top) return -1;
                if (y >= _layout[i].Top + _layout[i].Height) continue;
                return _rows[i].Kind == CardRowKind.Entry ? i : -1;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = EntryAt(e.Location);
            if (index == _hoverIndex) return;

            var previous = _hoverIndex;
            _hoverIndex = index;
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            var index = EntryAt(e.Location);
            if (index == _selectedIndex) return;

            var previous = _selectedIndex;
            _selectedIndex = index;
            InvalidateRow(previous);
            InvalidateRow(_selectedIndex);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!_scroll.Visible) return;

            var lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0) lines = FallbackWheelRows;
            ScrollTo(_offset - e.Delta * lines * EntryHeight / 120);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.End:
                    return true;
                default:
                    return base.IsInputKey(keyData);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!_scroll.Visible) return;

            switch (e.KeyCode)
            {
                case Keys.Up: ScrollTo(_offset - EntryHeight); break;
                case Keys.Down: ScrollTo(_offset + EntryHeight); break;
                case Keys.PageUp: ScrollTo(_offset - ClientSize.Height); break;
                case Keys.PageDown: ScrollTo(_offset + ClientSize.Height); break;
                case Keys.Home: ScrollTo(0); break;
                case Keys.End: ScrollTo(_contentHeight); break;
                default: return;
            }
            e.Handled = true;
        }

        private void InvalidateRow(int index)
        {
            if (index < 0 || index >= _layout.Count) return;
            Invalidate(new Rectangle(0, _layout[index].Top - _offset, _contentWidth, _layout[index].Height));
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

        private struct RowLayout
        {
            public readonly int Top;
            public readonly int Height;
            /// <summary>Only meaningful when the row is an entry.</summary>
            public readonly EntryLayout Entry;

            private RowLayout(int top, int height, EntryLayout entry)
            {
                Top = top;
                Height = height;
                Entry = entry;
            }

            public static RowLayout Fixed(int top, int height) =>
                new RowLayout(top, height, default(EntryLayout));

            public static RowLayout ForEntry(int top, EntryLayout entry) =>
                new RowLayout(top, entry.Height, entry);
        }
    }
}
