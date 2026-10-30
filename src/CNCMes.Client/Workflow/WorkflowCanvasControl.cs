using System.Drawing.Drawing2D;
using System.Drawing.Text;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client.Workflow;

public class WorkflowCanvasControl : UserControl
{
    private static class Palette
    {
        public static readonly Color CanvasBg = Color.FromArgb(10, 16, 28);       // #0a101c
        public static readonly Color GridDot = Color.FromArgb(24, 36, 58);         // #18243a
        public static readonly Color NodeBg = Color.FromArgb(17, 26, 44);          // #111a2c
        public static readonly Color NodeBorder = Color.FromArgb(35, 52, 85);      // #233455
        public static readonly Color NodeSelected = Color.FromArgb(14, 165, 233);  // Sky #0ea5e9
        public static readonly Color TextWhite = Color.FromArgb(241, 245, 249);
        public static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
        
        // Category Accent Colors
        public static readonly Color CatPrep = Color.FromArgb(56, 189, 248);       // Sky
        public static readonly Color CatMach = Color.FromArgb(16, 185, 129);       // Green
        public static readonly Color CatInsp = Color.FromArgb(245, 158, 11);       // Amber
        public static readonly Color CatPost = Color.FromArgb(99, 102, 241);       // Indigo
        public static readonly Color CatCollab = Color.FromArgb(168, 85, 247);     // Purple
        public static readonly Color CatIssue = Color.FromArgb(239, 68, 68);       // Red
    }

    public List<WorkflowNode> Nodes { get; } = new();
    public List<WorkflowEdge> Edges { get; } = new();

    public event Action<WorkflowNode?>? NodeSelected;
    public event Action<WorkflowNode>? NodeDoubleClicked;
    public event Action? WorkflowChanged;

    private WorkflowNode? _selectedNode;
    private WorkflowNode? _draggedNode;
    private PointF _dragStartOffset;
    private bool _isPanning;
    private Point _panStartMouse;
    private PointF _panOffset = new(40, 40);
    private float _zoom = 1.0f;

    // Edge Creation Dragging
    private WorkflowNode? _connectingFromNode;
    private PointF _currentConnectingPoint;

    public WorkflowCanvasControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Palette.CanvasBg;
        Font = new Font("맑은 고딕", 9f, FontStyle.Regular);
        Cursor = Cursors.Default;
    }

    public WorkflowNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (_selectedNode != value)
            {
                foreach (var n in Nodes) n.IsSelected = false;
                _selectedNode = value;
                if (_selectedNode != null) _selectedNode.IsSelected = true;
                Invalidate();
                NodeSelected?.Invoke(_selectedNode);
            }
        }
    }

    public void Clear()
    {
        Nodes.Clear();
        Edges.Clear();
        _selectedNode = null;
        Invalidate();
        WorkflowChanged?.Invoke();
    }

    public void AddNode(WorkflowNode node)
    {
        Nodes.Add(node);
        SelectedNode = node;
        Invalidate();
        WorkflowChanged?.Invoke();
    }

    public void RemoveNode(WorkflowNode node)
    {
        Nodes.Remove(node);
        Edges.RemoveAll(e => e.FromNodeId == node.Id || e.ToNodeId == node.Id);
        if (_selectedNode == node) SelectedNode = null;
        Invalidate();
        WorkflowChanged?.Invoke();
    }

    public void AddEdge(string fromId, string toId, EdgeType type = EdgeType.Sequence)
    {
        if (fromId == toId) return;
        if (Edges.Any(e => e.FromNodeId == fromId && e.ToNodeId == toId)) return;
        
        // Prevent simple cycles
        if (Edges.Any(e => e.FromNodeId == toId && e.ToNodeId == fromId)) return;

        Edges.Add(new WorkflowEdge { FromNodeId = fromId, ToNodeId = toId, Type = type });
        Invalidate();
        WorkflowChanged?.Invoke();
    }

    public void ResetView()
    {
        _zoom = 1.0f;
        _panOffset = new PointF(40, 40);
        Invalidate();
    }

    public void AutoLayout()
    {
        if (Nodes.Count == 0) return;

        // Group by MachineId or Part
        var inDegree = new Dictionary<string, int>();
        var adj = new Dictionary<string, List<string>>();
        foreach (var n in Nodes)
        {
            inDegree[n.Id] = 0;
            adj[n.Id] = new List<string>();
        }
        foreach (var e in Edges)
        {
            if (adj.ContainsKey(e.FromNodeId)) adj[e.FromNodeId].Add(e.ToNodeId);
            if (inDegree.ContainsKey(e.ToNodeId)) inDegree[e.ToNodeId]++;
        }

        // Topological column assignment
        var levels = new Dictionary<string, int>();
        var queue = new Queue<string>();
        foreach (var n in Nodes)
        {
            if (inDegree[n.Id] == 0)
            {
                levels[n.Id] = 0;
                queue.Enqueue(n.Id);
            }
        }

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            int currL = levels[u];
            foreach (var v in adj[u])
            {
                if (!levels.ContainsKey(v) || levels[v] < currL + 1)
                {
                    levels[v] = currL + 1;
                }
                inDegree[v]--;
                if (inDegree[v] == 0) queue.Enqueue(v);
            }
        }

        // Arrange in columns
        var colGroups = new Dictionary<int, List<WorkflowNode>>();
        foreach (var n in Nodes)
        {
            int lvl = levels.TryGetValue(n.Id, out int l) ? l : 0;
            if (!colGroups.ContainsKey(lvl)) colGroups[lvl] = new List<WorkflowNode>();
            colGroups[lvl].Add(n);
        }

        float startX = 60;
        float startY = 60;
        float gapX = 260;
        float gapY = 110;

        foreach (var col in colGroups.Keys.OrderBy(k => k))
        {
            var list = colGroups[col];
            for (int r = 0; r < list.Count; r++)
            {
                list[r].X = startX + col * gapX;
                list[r].Y = startY + r * gapY;
            }
        }

        Invalidate();
        WorkflowChanged?.Invoke();
    }

    // -------------------------------------------------------------------------
    // MOUSE EVENTS
    // -------------------------------------------------------------------------
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        var worldPt = ScreenToWorld(e.Location);

        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Right && ModifierKeys == Keys.None))
        {
            _isPanning = true;
            _panStartMouse = e.Location;
            Cursor = Cursors.SizeAll;
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            // Check if clicking on output port (+ anchor) to start connect
            foreach (var node in Nodes.AsEnumerable().Reverse())
            {
                var outRect = new RectangleF(node.X + node.Width - 14, node.Y + node.Height / 2 - 8, 16, 16);
                if (outRect.Contains(worldPt))
                {
                    _connectingFromNode = node;
                    _currentConnectingPoint = worldPt;
                    Cursor = Cursors.Cross;
                    return;
                }
            }

            // Check if clicking inside a node
            WorkflowNode? hit = null;
            foreach (var node in Nodes.AsEnumerable().Reverse())
            {
                if (node.Bounds.Contains(worldPt))
                {
                    hit = node;
                    break;
                }
            }

            SelectedNode = hit;

            if (hit != null)
            {
                _draggedNode = hit;
                _dragStartOffset = new PointF(worldPt.X - hit.X, worldPt.Y - hit.Y);
                Cursor = Cursors.Hand;
            }
            else
            {
                _isPanning = true;
                _panStartMouse = e.Location;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var worldPt = ScreenToWorld(e.Location);

        if (_isPanning)
        {
            float dx = e.X - _panStartMouse.X;
            float dy = e.Y - _panStartMouse.Y;
            _panOffset = new PointF(_panOffset.X + dx, _panOffset.Y + dy);
            _panStartMouse = e.Location;
            Invalidate();
            return;
        }

        if (_connectingFromNode != null)
        {
            _currentConnectingPoint = worldPt;
            Invalidate();
            return;
        }

        if (_draggedNode != null)
        {
            _draggedNode.X = (float)Math.Round((worldPt.X - _dragStartOffset.X) / 10f) * 10f; // 10px snap
            _draggedNode.Y = (float)Math.Round((worldPt.Y - _dragStartOffset.Y) / 10f) * 10f;
            Invalidate();
            return;
        }

        // Hover test for cursor
        bool isAnchorHover = false;
        foreach (var node in Nodes)
        {
            var outRect = new RectangleF(node.X + node.Width - 14, node.Y + node.Height / 2 - 8, 16, 16);
            if (outRect.Contains(worldPt))
            {
                isAnchorHover = true;
                break;
            }
        }
        Cursor = isAnchorHover ? Cursors.Cross : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_connectingFromNode != null)
        {
            var worldPt = ScreenToWorld(e.Location);
            foreach (var node in Nodes)
            {
                if (node != _connectingFromNode && node.Bounds.Contains(worldPt))
                {
                    AddEdge(_connectingFromNode.Id, node.Id, EdgeType.Sequence);
                    break;
                }
            }
            _connectingFromNode = null;
            Invalidate();
        }

        _isPanning = false;
        _draggedNode = null;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_selectedNode != null && e.Button == MouseButtons.Left)
        {
            NodeDoubleClicked?.Invoke(_selectedNode);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float oldZoom = _zoom;
        if (e.Delta > 0) _zoom = Math.Min(2.0f, _zoom + 0.1f);
        else if (e.Delta < 0) _zoom = Math.Max(0.4f, _zoom - 0.1f);

        // Zoom towards mouse position
        float mouseWorldX = (e.X - _panOffset.X) / oldZoom;
        float mouseWorldY = (e.Y - _panOffset.Y) / oldZoom;

        _panOffset.X = e.X - mouseWorldX * _zoom;
        _panOffset.Y = e.Y - mouseWorldY * _zoom;

        Invalidate();
    }

    // -------------------------------------------------------------------------
    // PAINTING / RENDERING
    // -------------------------------------------------------------------------
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Apply World Transform
        var originalTransform = g.Transform;
        g.TranslateTransform(_panOffset.X, _panOffset.Y);
        g.ScaleTransform(_zoom, _zoom);

        DrawGrid(g);
        DrawEdges(g);
        DrawConnectingLine(g);
        DrawNodes(g);

        // Reset Transform for HUD / Minimap Overlay
        g.Transform = originalTransform;
        DrawHudOverlay(g);
    }

    private void DrawGrid(Graphics g)
    {
        // Viewport bounds in world coords
        float left = (-_panOffset.X) / _zoom;
        float top = (-_panOffset.Y) / _zoom;
        float right = (Width - _panOffset.X) / _zoom;
        float bottom = (Height - _panOffset.Y) / _zoom;

        float gridSpacing = 40f;
        int startX = (int)(left / gridSpacing) * (int)gridSpacing - (int)gridSpacing;
        int startY = (int)(top / gridSpacing) * (int)gridSpacing - (int)gridSpacing;

        using var dotBrush = new SolidBrush(Palette.GridDot);
        for (float x = startX; x < right + gridSpacing; x += gridSpacing)
        {
            for (float y = startY; y < bottom + gridSpacing; y += gridSpacing)
            {
                g.FillRectangle(dotBrush, x - 1, y - 1, 2, 2);
            }
        }
    }

    private void DrawEdges(Graphics g)
    {
        var nodeMap = Nodes.ToDictionary(n => n.Id, n => n);

        foreach (var edge in Edges)
        {
            if (!nodeMap.TryGetValue(edge.FromNodeId, out var fromNode) ||
                !nodeMap.TryGetValue(edge.ToNodeId, out var toNode))
                continue;

            PointF p1 = fromNode.OutputAnchor;
            PointF p2 = toNode.InputAnchor;

            float dx = Math.Abs(p2.X - p1.X) * 0.5f;
            float ctrlDist = Math.Max(50f, dx);

            PointF c1 = new(p1.X + ctrlDist, p1.Y);
            PointF c2 = new(p2.X - ctrlDist, p2.Y);

            Color edgeColor;
            float edgeWidth;
            DashStyle dash = DashStyle.Solid;

            switch (edge.Type)
            {
                case EdgeType.Parallel:
                    edgeColor = Color.FromArgb(200, 56, 189, 248); // Sky
                    edgeWidth = 2.2f;
                    dash = DashStyle.Dash;
                    break;
                case EdgeType.Delegation:
                    edgeColor = Color.FromArgb(220, 168, 85, 247); // Purple
                    edgeWidth = 2.5f;
                    dash = DashStyle.DashDot;
                    break;
                case EdgeType.IssueLink:
                    edgeColor = Color.FromArgb(240, 239, 68, 68); // Red
                    edgeWidth = 2.5f;
                    break;
                default:
                    edgeColor = fromNode.Status == NodeStatus.Completed 
                        ? Color.FromArgb(180, 16, 185, 129) // Green completed line
                        : Color.FromArgb(140, 100, 116, 139);
                    edgeWidth = 2.0f;
                    break;
            }

            using var pen = new Pen(edgeColor, edgeWidth) { DashStyle = dash };
            g.DrawBezier(pen, p1, c1, c2, p2);

            // Draw directional arrow on p2
            DrawArrow(g, edgeColor, p2);
        }
    }

    private void DrawConnectingLine(Graphics g)
    {
        if (_connectingFromNode == null) return;
        PointF p1 = _connectingFromNode.OutputAnchor;
        PointF p2 = _currentConnectingPoint;

        float ctrlDist = Math.Max(40f, Math.Abs(p2.X - p1.X) * 0.5f);
        PointF c1 = new(p1.X + ctrlDist, p1.Y);
        PointF c2 = new(p2.X - ctrlDist, p2.Y);

        using var pen = new Pen(Color.FromArgb(240, 14, 165, 233), 2.5f) { DashStyle = DashStyle.Dash };
        g.DrawBezier(pen, p1, c1, c2, p2);
        DrawArrow(g, Color.FromArgb(240, 14, 165, 233), p2);
    }

    private void DrawArrow(Graphics g, Color color, PointF tip)
    {
        PointF[] pts = {
            new(tip.X - 8, tip.Y - 5),
            new(tip.X, tip.Y),
            new(tip.X - 8, tip.Y + 5)
        };
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, pts);
    }

    private void DrawNodes(Graphics g)
    {
        foreach (var node in Nodes)
        {
            var r = node.Bounds;
            Color accent = GetCategoryColor(node.Category);

            // Glow / Shadow effect if selected or running
            if (node.IsSelected)
            {
                using var glowPen = new Pen(Color.FromArgb(80, Palette.NodeSelected), 8f);
                g.DrawRoundedRectangle(glowPen, r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4, 10);
            }
            else if (node.Status == NodeStatus.Running)
            {
                using var pulsePen = new Pen(Color.FromArgb(70, Palette.CatMach), 6f);
                g.DrawRoundedRectangle(pulsePen, r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2, 8);
            }

            // Node Background
            using var bgBrush = new SolidBrush(Palette.NodeBg);
            g.FillRoundedRectangle(bgBrush, r.X, r.Y, r.Width, r.Height, 8);

            // Top Status Accent Bar
            using var barBrush = new SolidBrush(accent);
            g.FillRoundedRectangle(barBrush, r.X, r.Y, r.Width, 5, 4);

            // Border
            Color borderClr = node.IsSelected ? Palette.NodeSelected : (node.Status == NodeStatus.Running ? Palette.CatMach : Palette.NodeBorder);
            float borderW = node.IsSelected ? 2.0f : 1.2f;
            using var borderPen = new Pen(borderClr, borderW);
            g.DrawRoundedRectangle(borderPen, r.X, r.Y, r.Width, r.Height, 8);

            // Category Icon Badge & Machine Badge
            string catTag = GetCategoryShortName(node.Category);
            using var badgeFont = new Font("맑은 고딕", 7.5f, FontStyle.Bold);
            using var catBrush = new SolidBrush(accent);
            g.DrawString($"[{catTag}]", badgeFont, catBrush, r.X + 8, r.Y + 10);

            // Machine Pill
            string machText = string.IsNullOrEmpty(node.MachineId) ? "-" : node.MachineId;
            var machSize = g.MeasureString(machText, badgeFont);
            var machRect = new RectangleF(r.X + r.Width - machSize.Width - 14, r.Y + 9, machSize.Width + 6, 14);
            using var machBg = new SolidBrush(Color.FromArgb(40, 56, 189, 248));
            g.FillRoundedRectangle(machBg, machRect.X, machRect.Y, machRect.Width, machRect.Height, 3);
            using var machFg = new SolidBrush(Palette.CatPrep);
            g.DrawString(machText, badgeFont, machFg, machRect.X + 3, machRect.Y + 1);

            // Node Title
            using var titleFont = new Font("맑은 고딕", 9.5f, FontStyle.Bold);
            using var titleBrush = new SolidBrush(Palette.TextWhite);
            g.DrawString(node.Title, titleFont, titleBrush, r.X + 8, r.Y + 28);

            // Assignee or Part info
            using var subFont = new Font("맑은 고딕", 8f);
            using var subBrush = new SolidBrush(Palette.TextMuted);
            string subText = !string.IsNullOrEmpty(node.Assignee) 
                ? $"담당: {node.Assignee}" 
                : (!string.IsNullOrEmpty(node.PartName) ? node.PartName : "미지정");
            g.DrawString(subText, subFont, subBrush, r.X + 8, r.Y + 52);

            // Status Indicator Pill (Right Bottom)
            string statusText = GetStatusText(node.Status);
            Color statusColor = GetStatusColor(node.Status);
            using var sFont = new Font("맑은 고딕", 7.5f, FontStyle.Bold);
            var sSize = g.MeasureString(statusText, sFont);
            var sRect = new RectangleF(r.X + r.Width - sSize.Width - 14, r.Y + 52, sSize.Width + 6, 15);
            using var sBg = new SolidBrush(Color.FromArgb(45, statusColor));
            g.FillRoundedRectangle(sBg, sRect.X, sRect.Y, sRect.Width, sRect.Height, 3);
            using var sFg = new SolidBrush(statusColor);
            g.DrawString(statusText, sFont, sFg, sRect.X + 3, sRect.Y + 1);

            // Input Anchor Dot
            using var inBrush = new SolidBrush(Palette.NodeBorder);
            g.FillEllipse(inBrush, node.InputAnchor.X - 5, node.InputAnchor.Y - 5, 10, 10);
            using var inPen = new Pen(Palette.NodeBg, 1.5f);
            g.DrawEllipse(inPen, node.InputAnchor.X - 5, node.InputAnchor.Y - 5, 10, 10);

            // Output Anchor (+)
            var outRect = new RectangleF(node.OutputAnchor.X - 5, node.OutputAnchor.Y - 5, 10, 10);
            using var outBrush = new SolidBrush(accent);
            g.FillEllipse(outBrush, outRect);
            using var outPen = new Pen(Color.White, 1.2f);
            g.DrawEllipse(outPen, outRect);
        }
    }

    private void DrawHudOverlay(Graphics g)
    {
        // Top-Right View Status Info
        string info = $"노드: {Nodes.Count}개 | 연결: {Edges.Count}개 | 줌: {(int)(_zoom * 100)}%";
        using var font = new Font("Consolas", 8.5f);
        using var brush = new SolidBrush(Color.FromArgb(180, 148, 163, 184));
        var sz = g.MeasureString(info, font);
        g.DrawString(info, font, brush, Width - sz.Width - 12, Height - sz.Height - 8);

        // Control Hint
        string hint = "마우스 드래그: 이동 | 우클릭 드래그: 팬(화면 이동) | 휠: 줌 | 우측 원 드래그: 연결선 생성";
        using var hintFont = new Font("맑은 고딕", 8f);
        using var hintBrush = new SolidBrush(Color.FromArgb(120, 148, 163, 184));
        g.DrawString(hint, hintFont, hintBrush, 12, Height - 22);
    }

    private Color GetCategoryColor(NodeCategory cat) => cat switch
    {
        NodeCategory.Preparation => Palette.CatPrep,
        NodeCategory.Machining => Palette.CatMach,
        NodeCategory.Inspection => Palette.CatInsp,
        NodeCategory.PostProcess => Palette.CatPost,
        NodeCategory.Collaboration => Palette.CatCollab,
        NodeCategory.Issue => Palette.CatIssue,
        _ => Palette.CatMach
    };

    private string GetCategoryShortName(NodeCategory cat) => cat switch
    {
        NodeCategory.Preparation => "준비",
        NodeCategory.Machining => "가공",
        NodeCategory.Inspection => "검사",
        NodeCategory.PostProcess => "후공정",
        NodeCategory.Collaboration => "협업",
        NodeCategory.Issue => "이슈",
        _ => "공정"
    };

    private string GetStatusText(NodeStatus s) => s switch
    {
        NodeStatus.Ready => "대기",
        NodeStatus.Running => "진행중",
        NodeStatus.PendingCheck => "측정대기",
        NodeStatus.Completed => "완료",
        NodeStatus.Issue => "이슈",
        _ => "대기"
    };

    private Color GetStatusColor(NodeStatus s) => s switch
    {
        NodeStatus.Ready => Palette.TextMuted,
        NodeStatus.Running => Palette.CatMach,
        NodeStatus.PendingCheck => Palette.CatInsp,
        NodeStatus.Completed => Color.FromArgb(100, 116, 139),
        NodeStatus.Issue => Palette.CatIssue,
        _ => Palette.TextMuted
    };

    private PointF ScreenToWorld(Point pt)
    {
        return new PointF((pt.X - _panOffset.X) / _zoom, (pt.Y - _panOffset.Y) / _zoom);
    }
}

public static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float width, float height, float radius)
    {
        using var path = CreateRoundedRectanglePath(x, y, width, height, radius);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics g, Pen pen, float x, float y, float width, float height, float radius)
    {
        using var path = CreateRoundedRectanglePath(x, y, width, height, radius);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedRectanglePath(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
