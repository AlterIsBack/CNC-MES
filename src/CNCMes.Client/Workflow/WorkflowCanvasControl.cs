using System.Drawing.Drawing2D;
using System.Drawing.Text;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client.Workflow;

public class WorkflowCanvasControl : UserControl
{
    public static class Palette
    {
        public static readonly Color CanvasBg = Color.FromArgb(10, 16, 28);       // #0a101c
        public static readonly Color GridDot = Color.FromArgb(30, 45, 70);         // #1e2d46
        public static readonly Color NodeBg = Color.FromArgb(17, 26, 44);          // #111a2c
        public static readonly Color NodeBorder = Color.FromArgb(45, 65, 105);     // #2d4169
        public static readonly Color NodeSelected = Color.FromArgb(14, 165, 233);  // Sky #0ea5e9
        public static readonly Color TextWhite = Color.FromArgb(248, 250, 252);
        public static readonly Color TextMuted = Color.FromArgb(160, 175, 195);
        
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

    // Workflow Confirmation Status
    public bool IsWorkflowConfirmed { get; set; } = false;
    public string ConfirmedInfoText { get; set; } = "";

    public event Action<WorkflowNode?>? NodeSelected;
    public event Action<WorkflowEdge?>? EdgeSelected;
    public event Action<WorkflowNode>? NodeDoubleClicked;
    public event Action? WorkflowChanged;
    public event Action? RequestGenerateDefaultWorkflow;
    public event Action<NodeCategory>? RequestAddQuickNode;

    private WorkflowNode? _selectedNode;
    private WorkflowEdge? _selectedEdge;
    private WorkflowNode? _draggedNode;
    private PointF _dragStartOffset;
    private bool _isPanning;
    private Point _panStartMouse;
    private Point _mouseDownLocation;
    private PointF _panOffset = new(40, 40);
    private float _zoom = 1.0f;

    // Edge Creation Dragging
    private WorkflowNode? _connectingFromNode;
    private PointF _currentConnectingPoint;

    // Hit / Click Rectangles for Interactive Elements in Screen Space
    private RectangleF _emptyButtonScreenRect;
    private RectangleF _selectedEdgeCutButtonScreenRect;

    public WorkflowCanvasControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        BackColor = Palette.CanvasBg;
        Font = new Font("맑은 고딕", 9f, FontStyle.Regular);
        Cursor = Cursors.Default;
        TabStop = true;
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
                if (_selectedNode != null)
                {
                    _selectedNode.IsSelected = true;
                    // Deselect edge when node is selected
                    if (_selectedEdge != null)
                    {
                        _selectedEdge.IsSelected = false;
                        _selectedEdge = null;
                        EdgeSelected?.Invoke(null);
                    }
                }
                Invalidate();
                NodeSelected?.Invoke(_selectedNode);
            }
        }
    }

    public WorkflowEdge? SelectedEdge
    {
        get => _selectedEdge;
        set
        {
            if (_selectedEdge != value)
            {
                foreach (var e in Edges) e.IsSelected = false;
                _selectedEdge = value;
                if (_selectedEdge != null)
                {
                    _selectedEdge.IsSelected = true;
                    // Deselect node when edge is selected
                    if (_selectedNode != null)
                    {
                        _selectedNode.IsSelected = false;
                        _selectedNode = null;
                        NodeSelected?.Invoke(null);
                    }
                }
                Invalidate();
                EdgeSelected?.Invoke(_selectedEdge);
            }
        }
    }

    public void Clear()
    {
        Nodes.Clear();
        Edges.Clear();
        _selectedNode = null;
        _selectedEdge = null;
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
        if (_selectedEdge != null && !Edges.Contains(_selectedEdge)) SelectedEdge = null;
        Invalidate();
        WorkflowChanged?.Invoke();
    }

    public void RemoveEdge(WorkflowEdge edge)
    {
        if (Edges.Remove(edge))
        {
            if (_selectedEdge == edge) SelectedEdge = null;
            Invalidate();
            WorkflowChanged?.Invoke();
        }
    }

    public void RemoveEdgesForNode(WorkflowNode node, bool incoming = true, bool outgoing = true)
    {
        int removed = 0;
        if (incoming && outgoing)
            removed = Edges.RemoveAll(e => e.FromNodeId == node.Id || e.ToNodeId == node.Id);
        else if (incoming)
            removed = Edges.RemoveAll(e => e.ToNodeId == node.Id);
        else if (outgoing)
            removed = Edges.RemoveAll(e => e.FromNodeId == node.Id);

        if (removed > 0)
        {
            if (_selectedEdge != null && !Edges.Contains(_selectedEdge)) _selectedEdge = null;
            Invalidate();
            WorkflowChanged?.Invoke();
        }
    }

    public void AddEdge(string fromId, string toId, EdgeType type = EdgeType.Sequence, string label = "")
    {
        if (fromId == toId) return;
        if (Edges.Any(e => e.FromNodeId == fromId && e.ToNodeId == toId)) return;
        
        // Prevent simple cycles
        if (Edges.Any(e => e.FromNodeId == toId && e.ToNodeId == fromId)) return;

        var edge = new WorkflowEdge { FromNodeId = fromId, ToNodeId = toId, Type = type, Label = label };
        Edges.Add(edge);
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

        var inDegree = Nodes.ToDictionary(n => n.Id, _ => 0);
        foreach (var edge in Edges)
        {
            if (inDegree.ContainsKey(edge.ToNodeId))
                inDegree[edge.ToNodeId]++;
        }

        var queue = new Queue<string>(Nodes.Where(n => inDegree[n.Id] == 0).Select(n => n.Id));
        if (queue.Count == 0 && Nodes.Count > 0)
            queue.Enqueue(Nodes[0].Id);

        var levels = new Dictionary<string, int>();
        while (queue.Count > 0)
        {
            var currId = queue.Dequeue();
            int currLevel = levels.TryGetValue(currId, out int lvl) ? lvl : 0;

            foreach (var edge in Edges.Where(e => e.FromNodeId == currId))
            {
                int nextLevel = currLevel + 1;
                if (!levels.ContainsKey(edge.ToNodeId) || levels[edge.ToNodeId] < nextLevel)
                {
                    levels[edge.ToNodeId] = nextLevel;
                    queue.Enqueue(edge.ToNodeId);
                }
            }
        }

        foreach (var node in Nodes)
        {
            if (!levels.ContainsKey(node.Id))
                levels[node.Id] = 0;
        }

        var levelGroups = Nodes.GroupBy(n => levels[n.Id]).OrderBy(g => g.Key);
        float startX = 60f;
        float spacingX = 260f;
        float spacingY = 140f;

        foreach (var group in levelGroups)
        {
            int level = group.Key;
            var nodesInLevel = group.ToList();

            for (int i = 0; i < nodesInLevel.Count; i++)
            {
                var n = nodesInLevel[i];
                n.X = startX + level * spacingX;
                n.Y = 80f + i * spacingY;
            }
        }

        ResetView();
        WorkflowChanged?.Invoke();
    }

    // =========================================================================
    // PAINTING / RENDERING
    // =========================================================================
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
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

        // Reset Transform for HUD / Empty Guide / Cut Button Overlay
        g.Transform = originalTransform;

        if (Nodes.Count == 0)
        {
            DrawEmptyCanvasGuide(g);
        }

        DrawCutButtonOverlay(g);
        DrawConfirmedBadgeOverlay(g);
        DrawHudOverlay(g);
    }

    private void DrawEmptyCanvasGuide(Graphics g)
    {
        float boxW = 540f;
        float boxH = 170f;
        float boxX = (Width - boxW) / 2f;
        float boxY = (Height - boxH) / 2f - 20f;

        var boxRect = new RectangleF(boxX, boxY, boxW, boxH);

        using var bgBrush = new SolidBrush(Color.FromArgb(235, 15, 23, 42));
        using var borderPen = new Pen(Palette.CatPrep, 1.5f);
        DrawRoundedRectangle(g, bgBrush, borderPen, boxRect, 10f);

        using var fontIcon = new Font("Segoe UI Symbol", 20f);
        using var fontTitle = new Font("맑은 고딕", 12f, FontStyle.Bold);
        using var fontSub = new Font("맑은 고딕", 9f);
        using var brushSky = new SolidBrush(Palette.CatPrep);
        using var brushWhite = new SolidBrush(Palette.TextWhite);
        using var brushMuted = new SolidBrush(Palette.TextMuted);

        g.DrawString("⚡", fontIcon, brushSky, boxX + 24, boxY + 22);
        g.DrawString("기본 공정 워크플로우를 자동 생성하시겠습니까?", fontTitle, brushWhite, boxX + 65, boxY + 24);
        g.DrawString("좌측에서 작업지시서와 부품을 선택한 후 아래 버튼을 클릭하면 표준 가공 공정이 배치됩니다.", fontSub, brushMuted, boxX + 65, boxY + 54);

        float btnW = 240f;
        float btnH = 38f;
        float btnX = boxX + (boxW - btnW) / 2f;
        float btnY = boxY + 98f;
        _emptyButtonScreenRect = new RectangleF(btnX, btnY, btnW, btnH);

        using var btnBg = new SolidBrush(Palette.CatMach);
        using var btnBorder = new Pen(Color.FromArgb(100, 255, 255, 255), 1.2f);
        DrawRoundedRectangle(g, btnBg, btnBorder, _emptyButtonScreenRect, 6f);

        using var fontBtn = new Font("맑은 고딕", 9.5f, FontStyle.Bold);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("⚡ 기본 워크플로우 생성", fontBtn, Brushes.White, _emptyButtonScreenRect, sf);
    }

    private void DrawGrid(Graphics g)
    {
        float dotSpacing = 24f;
        float startX = -_panOffset.X / _zoom;
        float startY = -_panOffset.Y / _zoom;
        float endX = startX + Width / _zoom;
        float endY = startY + Height / _zoom;

        float firstX = (float)Math.Floor(startX / dotSpacing) * dotSpacing;
        float firstY = (float)Math.Floor(startY / dotSpacing) * dotSpacing;

        using var brush = new SolidBrush(Palette.GridDot);
        for (float x = firstX; x <= endX; x += dotSpacing)
        {
            for (float y = firstY; y <= endY; y += dotSpacing)
            {
                g.FillRectangle(brush, x, y, 1.5f, 1.5f);
            }
        }
    }

    private void DrawEdges(Graphics g)
    {
        var nodeMap = Nodes.ToDictionary(n => n.Id, n => n);
        _selectedEdgeCutButtonScreenRect = RectangleF.Empty;

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

            bool isSelected = edge == _selectedEdge;

            if (isSelected)
            {
                edgeColor = Color.FromArgb(250, 204, 21); // Amber / Yellow Highlight
                edgeWidth = 3.6f;
            }
            else
            {
                switch (edge.Type)
                {
                    case EdgeType.Parallel:
                        edgeColor = Color.FromArgb(220, 56, 189, 248); // Sky
                        edgeWidth = 2.2f;
                        dash = DashStyle.Dash;
                        break;
                    case EdgeType.Delegation:
                        edgeColor = Color.FromArgb(230, 168, 85, 247); // Purple
                        edgeWidth = 2.4f;
                        dash = DashStyle.DashDot;
                        break;
                    case EdgeType.IssueLink:
                        edgeColor = Color.FromArgb(230, 239, 68, 68); // Red
                        edgeWidth = 2.2f;
                        break;
                    default:
                        edgeColor = Color.FromArgb(190, 100, 140, 180); // Classic Slate Blue
                        edgeWidth = 2.0f;
                        break;
                }
            }

            using var pen = new Pen(edgeColor, edgeWidth) { DashStyle = dash };
            using var path = new GraphicsPath();
            path.AddBezier(p1, c1, c2, p2);
            g.DrawPath(pen, path);

            // Draw arrow head at target
            DrawArrowHead(g, c2, p2, edgeColor);

            // Calculate midpoint on bezier curve (t = 0.5)
            float t = 0.5f;
            float it = 1f - t;
            float midX = it * it * it * p1.X + 3 * it * it * t * c1.X + 3 * it * t * t * c2.X + t * t * t * p2.X;
            float midY = it * it * it * p1.Y + 3 * it * it * t * c1.Y + 3 * it * t * t * c2.Y + t * t * t * p2.Y;

            if (isSelected)
            {
                // Save screen position of the cut button for click hit-testing
                PointF screenPt = WorldToScreen(new PointF(midX, midY));
                _selectedEdgeCutButtonScreenRect = new RectangleF(screenPt.X - 60f, screenPt.Y - 14f, 120f, 28f);
            }

            // Draw sub-worker movement / machine transfer direction badge along edge
            bool isDiffMachine = !string.IsNullOrEmpty(fromNode.MachineId) && !string.IsNullOrEmpty(toNode.MachineId) && !fromNode.MachineId.Equals(toNode.MachineId, StringComparison.OrdinalIgnoreCase);
            bool isDelegation = edge.Type == EdgeType.Delegation;

            if (!isSelected && (isDiffMachine || isDelegation))
            {
                bool isSubworker = isDelegation || toNode.Category == NodeCategory.Collaboration || fromNode.Category == NodeCategory.Collaboration;
                string badgeText = isDiffMachine
                    ? (isSubworker ? $"👥 서브인원 이동: {fromNode.MachineId} ➔ {toNode.MachineId}" : $"🔄 설비이송: {fromNode.MachineId} ➔ {toNode.MachineId}")
                    : $"👥 서브인원 지원: {toNode.Assignee}";

                using var fontBadge = new Font("맑은 고딕", 7.2f, FontStyle.Bold);
                var textSize = g.MeasureString(badgeText, fontBadge);
                var badgeRect = new RectangleF(midX - textSize.Width / 2f - 6f, midY - textSize.Height / 2f - 3f, textSize.Width + 12f, textSize.Height + 6f);

                Color badgeBg = isSubworker ? Color.FromArgb(240, 30, 20, 50) : Color.FromArgb(240, 15, 23, 42);
                Color badgeBorder = isSubworker ? Color.FromArgb(168, 85, 247) : Color.FromArgb(56, 189, 248);
                Color badgeTextColor = isSubworker ? Color.FromArgb(233, 213, 255) : Color.FromArgb(186, 230, 253);

                using var bgB = new SolidBrush(badgeBg);
                using var borderP = new Pen(badgeBorder, 1.2f);
                DrawRoundedRectangle(g, bgB, borderP, badgeRect, 4f);

                using var textB = new SolidBrush(badgeTextColor);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(badgeText, fontBadge, textB, badgeRect, sf);
            }
        }
    }

    private void DrawCutButtonOverlay(Graphics g)
    {
        if (_selectedEdge != null && !_selectedEdgeCutButtonScreenRect.IsEmpty)
        {
            using var bgBrush = new SolidBrush(Color.FromArgb(245, 220, 38, 38)); // High contrast red
            using var borderPen = new Pen(Color.White, 1.5f);
            DrawRoundedRectangle(g, bgBrush, borderPen, _selectedEdgeCutButtonScreenRect, 6f);

            using var font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("✂ 연결 끊기 (Del)", font, brush, _selectedEdgeCutButtonScreenRect, sf);
        }
    }

    private void DrawArrowHead(Graphics g, PointF from, PointF to, Color color)
    {
        float angle = (float)Math.Atan2(to.Y - from.Y, to.X - from.X);
        float arrowSize = 8f;

        PointF p1 = new(
            to.X - arrowSize * (float)Math.Cos(angle - Math.PI / 6),
            to.Y - arrowSize * (float)Math.Sin(angle - Math.PI / 6)
        );
        PointF p2 = new(
            to.X - arrowSize * (float)Math.Cos(angle + Math.PI / 6),
            to.Y - arrowSize * (float)Math.Sin(angle + Math.PI / 6)
        );

        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, new[] { to, p1, p2 });
    }

    private void DrawConnectingLine(Graphics g)
    {
        if (_connectingFromNode == null) return;

        PointF p1 = _connectingFromNode.OutputAnchor;
        PointF p2 = _currentConnectingPoint;

        using var pen = new Pen(Palette.CatPrep, 2f) { DashStyle = DashStyle.Dash };
        g.DrawLine(pen, p1, p2);
    }

    private void DrawNodes(Graphics g)
    {
        foreach (var node in Nodes)
        {
            DrawSingleNode(g, node);
        }
    }

    private void DrawSingleNode(Graphics g, WorkflowNode node)
    {
        var rect = node.Bounds;
        Color catColor = GetCategoryColor(node.Category);

        using (var bgBrush = new SolidBrush(Palette.NodeBg))
        using (var borderPen = new Pen(node.IsSelected ? Palette.NodeSelected : Palette.NodeBorder, node.IsSelected ? 2.2f : 1f))
        {
            DrawRoundedRectangle(g, bgBrush, borderPen, rect, 8f);
        }

        // Header Category Badge
        var badgeRect = new RectangleF(rect.X + 8, rect.Y + 8, 48, 18);
        using (var badgeBg = new SolidBrush(Color.FromArgb(40, catColor.R, catColor.G, catColor.B)))
        using (var badgePen = new Pen(catColor, 1f))
        {
            DrawRoundedRectangle(g, badgeBg, badgePen, badgeRect, 4f);
        }
        using (var fontBadge = new Font("맑은 고딕", 7.5f, FontStyle.Bold))
        using (var brushBadge = new SolidBrush(catColor))
        {
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString($"[{GetCategoryKorean(node.Category)}]", fontBadge, brushBadge, badgeRect, sf);
        }

        // Machine Badge (e.g. ⚙ R01)
        string machText = string.IsNullOrEmpty(node.MachineId) ? "⚙ R01" : $"⚙ {node.MachineId}";
        using var fontMach = new Font("Consolas", 7.5f, FontStyle.Bold);
        var machSize = g.MeasureString(machText, fontMach);
        var machRect = new RectangleF(rect.Right - machSize.Width - 14, rect.Y + 8, machSize.Width + 6, 18);

        Color machBg = node.MachineId switch
        {
            "R01" => Color.FromArgb(40, 16, 185, 129),
            "R02" => Color.FromArgb(40, 14, 165, 233),
            "R03" => Color.FromArgb(40, 245, 158, 11),
            "측정실" => Color.FromArgb(50, 245, 158, 11),
            "세척실" => Color.FromArgb(50, 99, 102, 241),
            "품질팀" => Color.FromArgb(50, 168, 85, 247),
            _ => Color.FromArgb(40, 148, 163, 184)
        };
        Color machBorder = node.MachineId switch
        {
            "R01" => Palette.CatMach,
            "R02" => Palette.CatPrep,
            "R03" => Palette.CatInsp,
            "측정실" => Palette.CatInsp,
            "세척실" => Palette.CatPost,
            "품질팀" => Palette.CatCollab,
            _ => Palette.TextMuted
        };

        using (var bBg = new SolidBrush(machBg))
        using (var bPen = new Pen(machBorder, 1f))
        {
            DrawRoundedRectangle(g, bBg, bPen, machRect, 4f);
        }
        using (var bText = new SolidBrush(machBorder))
        {
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(machText, fontMach, bText, machRect, sf);
        }

        // Title
        using (var fontTitle = new Font("맑은 고딕", 8.8f, FontStyle.Bold))
        using (var brushTitle = new SolidBrush(Palette.TextWhite))
        {
            var titleRect = new RectangleF(rect.X + 8, rect.Y + 31, rect.Width - 16, 20);
            var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(node.Title, fontTitle, brushTitle, titleRect, sf);
        }

        // Assignee & Subworker Footnote
        string workerText = string.IsNullOrEmpty(node.Assignee) ? "담당: 미정" : $"담당: {node.Assignee}";
        if (!string.IsNullOrEmpty(node.SubWorkerName) && node.Category == NodeCategory.Collaboration)
            workerText = $"서브: {node.SubWorkerName}";

        using (var fontWorker = new Font("맑은 고딕", 7.5f))
        using (var brushWorker = new SolidBrush(Palette.TextMuted))
        {
            g.DrawString(workerText, fontWorker, brushWorker, rect.X + 8, rect.Y + 54);
        }

        // Status Badge (대기, 진행중, 측정, 완료, 이슈)
        string statusText = node.Status switch
        {
            NodeStatus.Ready => "대기",
            NodeStatus.Running => "진행중",
            NodeStatus.PendingCheck => "측정",
            NodeStatus.Completed => "완료",
            NodeStatus.Issue => "이슈",
            _ => "대기"
        };
        Color statusBg = node.Status switch
        {
            NodeStatus.Ready => Color.FromArgb(42, 59, 76),
            NodeStatus.Running => Color.FromArgb(5, 150, 105),
            NodeStatus.PendingCheck => Color.FromArgb(217, 119, 6),
            NodeStatus.Completed => Color.FromArgb(2, 132, 199),
            NodeStatus.Issue => Color.FromArgb(220, 38, 38),
            _ => Color.FromArgb(42, 59, 76)
        };

        var statusRect = new RectangleF(rect.Right - 44, rect.Y + 52, 36, 18);
        using (var sBg = new SolidBrush(statusBg))
        using (var sPen = new Pen(Color.FromArgb(80, 255, 255, 255), 1f))
        {
            DrawRoundedRectangle(g, sBg, sPen, statusRect, 4f);
        }
        using (var sText = new SolidBrush(Color.White))
        using (var fontS = new Font("맑은 고딕", 7.2f, FontStyle.Bold))
        {
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(statusText, fontS, sText, statusRect, sf);
        }

        // Input & Output Anchors (Connection Handles)
        DrawAnchor(g, node.InputAnchor, false);
        DrawAnchor(g, node.OutputAnchor, true);
    }

    private void DrawAnchor(Graphics g, PointF pt, bool isOutput)
    {
        float size = 8f;
        var r = new RectangleF(pt.X - size / 2f, pt.Y - size / 2f, size, size);

        using var brush = new SolidBrush(isOutput ? Palette.CatPrep : Palette.TextMuted);
        using var pen = new Pen(Palette.CanvasBg, 1.5f);
        g.FillEllipse(brush, r);
        g.DrawEllipse(pen, r);
    }

    private void DrawConfirmedBadgeOverlay(Graphics g)
    {
        if (!IsWorkflowConfirmed) return;

        string badgeTitle = "🔒 [계획 확정 완료]";
        string badgeDetail = string.IsNullOrEmpty(ConfirmedInfoText) ? "중앙 서버 동기화됨" : ConfirmedInfoText;
        string fullText = $"{badgeTitle} {badgeDetail}";

        using var font = new Font("맑은 고딕", 9.2f, FontStyle.Bold);
        var size = g.MeasureString(fullText, font);

        float x = 16f;
        float y = 14f;
        var rect = new RectangleF(x, y, size.Width + 24f, size.Height + 12f);

        using var bg = new SolidBrush(Color.FromArgb(230, 6, 78, 59)); // Rich emerald green
        using var border = new Pen(Color.FromArgb(16, 185, 129), 1.8f);
        DrawRoundedRectangle(g, bg, border, rect, 6f);

        using var brush = new SolidBrush(Color.FromArgb(240, 253, 244)); // Crisp white-green
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(fullText, font, brush, rect, sf);
    }

    private void DrawHudOverlay(Graphics g)
    {
        string hudText = $"노드: {Nodes.Count}개 | 연결: {Edges.Count}개 | 줌: {(int)(_zoom * 100)}% | [연결선 클릭 후 Del 키로 끊기]";
        using var font = new Font("Consolas", 8.2f);
        var size = g.MeasureString(hudText, font);

        float x = Width - size.Width - 14;
        float y = Height - size.Height - 10;

        using var bg = new SolidBrush(Color.FromArgb(160, 10, 16, 28));
        g.FillRectangle(bg, x - 4, y - 2, size.Width + 8, size.Height + 4);

        using var brush = new SolidBrush(Palette.TextMuted);
        g.DrawString(hudText, font, brush, x, y);
    }

    private static void DrawRoundedRectangle(Graphics g, Brush brush, Pen pen, RectangleF r, float radius)
    {
        using var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }

    public static Color GetCategoryColor(NodeCategory cat) => cat switch
    {
        NodeCategory.Preparation => Palette.CatPrep,
        NodeCategory.Machining => Palette.CatMach,
        NodeCategory.Inspection => Palette.CatInsp,
        NodeCategory.PostProcess => Palette.CatPost,
        NodeCategory.Collaboration => Palette.CatCollab,
        NodeCategory.Issue => Palette.CatIssue,
        _ => Palette.CatMach
    };

    public static string GetCategoryKorean(NodeCategory cat) => cat switch
    {
        NodeCategory.Preparation => "준비",
        NodeCategory.Machining => "가공",
        NodeCategory.Inspection => "검사",
        NodeCategory.PostProcess => "후공정",
        NodeCategory.Collaboration => "협업",
        NodeCategory.Issue => "이슈",
        _ => "기타"
    };

    // =========================================================================
    // MOUSE & KEYBOARD INTERACTION (Pan, Zoom, Drag, Connect, Cut/Delete)
    // =========================================================================
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
        {
            if (_selectedEdge != null)
            {
                RemoveEdge(_selectedEdge);
                e.Handled = true;
            }
            else if (_selectedNode != null)
            {
                RemoveNode(_selectedNode);
                e.Handled = true;
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus(); // Ensure canvas captures keyboard focus for Delete key

        _mouseDownLocation = e.Location;

        // 1. Check if clicked the Empty Guide Button
        if (Nodes.Count == 0 && _emptyButtonScreenRect.Contains(e.Location))
        {
            RequestGenerateDefaultWorkflow?.Invoke();
            return;
        }

        // 2. Check if clicked the Cut Button on a Selected Edge
        if (_selectedEdge != null && !_selectedEdgeCutButtonScreenRect.IsEmpty && _selectedEdgeCutButtonScreenRect.Contains(e.Location))
        {
            RemoveEdge(_selectedEdge);
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            _isPanning = true;
            _panStartMouse = e.Location;
            Cursor = Cursors.SizeAll;
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            var worldPt = ScreenToWorld(e.Location);

            // 1. Check if clicking output anchor of any node to start edge creation
            foreach (var node in Nodes.AsEnumerable().Reverse())
            {
                if (IsPointNear(worldPt, node.OutputAnchor, 14f))
                {
                    _connectingFromNode = node;
                    _currentConnectingPoint = worldPt;
                    Cursor = Cursors.Cross;
                    return;
                }
            }

            // 2. Check if clicking a node body
            var hitNode = Nodes.AsEnumerable().Reverse().FirstOrDefault(n => n.Bounds.Contains(worldPt));
            if (hitNode != null)
            {
                SelectedNode = hitNode;
                _draggedNode = hitNode;
                _dragStartOffset = new PointF(worldPt.X - hitNode.X, worldPt.Y - hitNode.Y);
                return;
            }

            // 3. Check if clicking an edge (Hit test)
            var hitEdge = HitTestEdge(worldPt);
            if (hitEdge != null)
            {
                SelectedEdge = hitEdge;
                return;
            }

            // Clicked empty background
            SelectedNode = null;
            SelectedEdge = null;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isPanning)
        {
            _panOffset = new PointF(
                _panOffset.X + (e.X - _panStartMouse.X),
                _panOffset.Y + (e.Y - _panStartMouse.Y)
            );
            _panStartMouse = e.Location;
            Invalidate();
            return;
        }

        var worldPt = ScreenToWorld(e.Location);

        if (_connectingFromNode != null)
        {
            _currentConnectingPoint = worldPt;
            Invalidate();
            return;
        }

        if (_draggedNode != null)
        {
            _draggedNode.X = worldPt.X - _dragStartOffset.X;
            _draggedNode.Y = worldPt.Y - _dragStartOffset.Y;
            Invalidate();
            return;
        }

        // Hover cursor check
        if (_selectedEdge != null && _selectedEdgeCutButtonScreenRect.Contains(e.Location))
        {
            Cursor = Cursors.Hand;
            return;
        }

        bool nearPort = Nodes.Any(n => IsPointNear(worldPt, n.OutputAnchor, 10f) || IsPointNear(worldPt, n.InputAnchor, 10f));
        if (nearPort) Cursor = Cursors.Cross;
        else if (Nodes.Count == 0 && _emptyButtonScreenRect.Contains(e.Location)) Cursor = Cursors.Hand;
        else Cursor = Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Right)
        {
            _isPanning = false;
            Cursor = Cursors.Default;

            // Check if right click was a simple click (not panning drag)
            float moveDist = Math.Abs(e.X - _mouseDownLocation.X) + Math.Abs(e.Y - _mouseDownLocation.Y);
            if (moveDist <= 5f)
            {
                ShowContextMenu(e.Location);
            }
        }

        if (e.Button == MouseButtons.Left)
        {
            if (_connectingFromNode != null)
            {
                var worldPt = ScreenToWorld(e.Location);
                var targetNode = Nodes.FirstOrDefault(n => IsPointNear(worldPt, n.InputAnchor, 16f) || n.Bounds.Contains(worldPt));
                
                if (targetNode != null && targetNode != _connectingFromNode)
                {
                    EdgeType type = targetNode.Category == NodeCategory.Collaboration ? EdgeType.Delegation :
                                    (targetNode.Category == NodeCategory.Issue ? EdgeType.IssueLink : EdgeType.Sequence);
                    AddEdge(_connectingFromNode.Id, targetNode.Id, type);
                }

                _connectingFromNode = null;
                Cursor = Cursors.Default;
                Invalidate();
            }

            if (_draggedNode != null)
            {
                _draggedNode = null;
                WorkflowChanged?.Invoke();
            }
        }
    }

    private void ShowContextMenu(Point screenPt)
    {
        var worldPt = ScreenToWorld(screenPt);
        var hitNode = Nodes.AsEnumerable().Reverse().FirstOrDefault(n => n.Bounds.Contains(worldPt));
        var hitEdge = HitTestEdge(worldPt);

        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = Color.FromArgb(15, 27, 49),
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold),
            Renderer = new DarkContextMenuRenderer()
        };

        if (hitEdge != null)
        {
            SelectedEdge = hitEdge;
            var itemCut = menu.Items.Add("✂ 이 연결선 끊기 (Delete)");
            itemCut.ForeColor = Color.FromArgb(254, 202, 202); // Soft red
            itemCut.Click += (s, ev) => RemoveEdge(hitEdge);
        }
        else if (hitNode != null)
        {
            SelectedNode = hitNode;
            
            var itemCutIn = menu.Items.Add("✂ 이전 공정과의 연결 끊기 (앞쪽)");
            itemCutIn.ForeColor = Color.White;
            itemCutIn.Click += (s, ev) => RemoveEdgesForNode(hitNode, incoming: true, outgoing: false);

            var itemCutOut = menu.Items.Add("✂ 다음 공정과의 연결 끊기 (뒤쪽)");
            itemCutOut.ForeColor = Color.White;
            itemCutOut.Click += (s, ev) => RemoveEdgesForNode(hitNode, incoming: false, outgoing: true);

            var itemCutAll = menu.Items.Add("✂ 앞뒤 모든 연결선 끊기");
            itemCutAll.ForeColor = Color.FromArgb(254, 240, 138); // Yellow highlight
            itemCutAll.Click += (s, ev) => RemoveEdgesForNode(hitNode, incoming: true, outgoing: true);

            menu.Items.Add(new ToolStripSeparator());

            var itemDel = menu.Items.Add("🗑 이 공정 노드 삭제");
            itemDel.ForeColor = Color.FromArgb(252, 165, 165); // High contrast light red
            itemDel.Click += (s, ev) => RemoveNode(hitNode);
        }
        else
        {
            var itemAddMach = menu.Items.Add("➕ 가공 노드 추가");
            itemAddMach.ForeColor = Color.FromArgb(167, 243, 208); // Soft green
            itemAddMach.Click += (s, ev) => RequestAddQuickNode?.Invoke(NodeCategory.Machining);

            var itemAddInsp = menu.Items.Add("➕ 검사 노드 추가");
            itemAddInsp.ForeColor = Color.FromArgb(254, 215, 170); // Soft orange
            itemAddInsp.Click += (s, ev) => RequestAddQuickNode?.Invoke(NodeCategory.Inspection);

            var itemAddCollab = menu.Items.Add("➕ 서브인원 노드 추가");
            itemAddCollab.ForeColor = Color.FromArgb(233, 213, 255); // Soft purple
            itemAddCollab.Click += (s, ev) => RequestAddQuickNode?.Invoke(NodeCategory.Collaboration);

            menu.Items.Add(new ToolStripSeparator());

            var itemReset = menu.Items.Add("⟲ 화면 위치 초기화 (100%)");
            itemReset.ForeColor = Color.White;
            itemReset.Click += (s, ev) => ResetView();
        }

        menu.Show(this, screenPt);
    }

    private class DarkContextMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkContextMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Crisp high-contrast colors: Selected gets bright golden yellow, unselected gets custom ForeColor or white
            e.TextColor = e.Item.Selected ? Color.FromArgb(254, 240, 138) : (e.Item.ForeColor != Color.Empty ? e.Item.ForeColor : Color.White);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                using var brush = new SolidBrush(Color.FromArgb(30, 58, 102));
                using var borderPen = new Pen(Color.FromArgb(56, 189, 248), 1.5f);
                var rect = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
                e.Graphics.FillRectangle(brush, rect);
                e.Graphics.DrawRectangle(borderPen, rect);
            }
            else
            {
                using var brush = new SolidBrush(Color.FromArgb(15, 27, 49));
                e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(45, 65, 105), 1.2f);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 6, y, e.Item.Width - 6, y);
        }
    }

    private class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder => Color.FromArgb(56, 189, 248);
        public override Color MenuItemBorder => Color.FromArgb(56, 189, 248);
        public override Color MenuItemSelected => Color.FromArgb(30, 58, 102);
        public override Color ToolStripDropDownBackground => Color.FromArgb(15, 27, 49);
        public override Color ImageMarginGradientBegin => Color.FromArgb(15, 27, 49);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(15, 27, 49);
        public override Color ImageMarginGradientEnd => Color.FromArgb(15, 27, 49);
    }

    public WorkflowEdge? HitTestEdge(PointF worldPt)
    {
        var nodeMap = Nodes.ToDictionary(n => n.Id, n => n);
        foreach (var edge in Edges.AsEnumerable().Reverse())
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

            // Test points along the Bezier curve
            for (float t = 0f; t <= 1.0f; t += 0.05f)
            {
                float it = 1f - t;
                float x = it * it * it * p1.X + 3 * it * it * t * c1.X + 3 * it * t * t * c2.X + t * t * t * p2.X;
                float y = it * it * it * p1.Y + 3 * it * it * t * c1.Y + 3 * it * t * t * c2.Y + t * t * t * p2.Y;
                float diffX = worldPt.X - x;
                float diffY = worldPt.Y - y;
                if ((diffX * diffX + diffY * diffY) <= 196f) // 14px radius threshold
                    return edge;
            }
        }
        return null;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        float oldZoom = _zoom;
        if (e.Delta > 0) _zoom = Math.Min(2.5f, _zoom + 0.1f);
        else _zoom = Math.Max(0.35f, _zoom - 0.1f);

        // Zoom towards mouse location
        var mousePt = e.Location;
        _panOffset = new PointF(
            mousePt.X - (mousePt.X - _panOffset.X) * (_zoom / oldZoom),
            mousePt.Y - (mousePt.Y - _panOffset.Y) * (_zoom / oldZoom)
        );

        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_selectedNode != null && _selectedNode.Bounds.Contains(ScreenToWorld(e.Location)))
        {
            NodeDoubleClicked?.Invoke(_selectedNode);
        }
    }

    private PointF ScreenToWorld(Point screenPt)
    {
        return new PointF((screenPt.X - _panOffset.X) / _zoom, (screenPt.Y - _panOffset.Y) / _zoom);
    }

    private PointF WorldToScreen(PointF worldPt)
    {
        return new PointF(worldPt.X * _zoom + _panOffset.X, worldPt.Y * _zoom + _panOffset.Y);
    }

    private static bool IsPointNear(PointF p1, PointF p2, float threshold)
    {
        float dx = p1.X - p2.X;
        float dy = p1.Y - p2.Y;
        return (dx * dx + dy * dy) <= threshold * threshold;
    }
}
