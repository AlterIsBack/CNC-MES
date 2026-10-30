using System.Drawing.Drawing2D;
using System.Drawing.Text;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client.Workflow;

public class WorkflowCanvasControl : UserControl
{
    public static class Palette
    {
        public static readonly Color CanvasBg = Color.FromArgb(10, 16, 28);       // #0a101c
        public static readonly Color GridDot = Color.FromArgb(30, 45, 70);         // #1e2d46 (slightly brighter)
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

    public event Action<WorkflowNode?>? NodeSelected;
    public event Action<WorkflowNode>? NodeDoubleClicked;
    public event Action? WorkflowChanged;
    public event Action? RequestGenerateDefaultWorkflow;

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

    // Empty state button bounds in screen coordinates
    private RectangleF _emptyButtonScreenRect;

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

    public void AddEdge(string fromId, string toId, EdgeType type = EdgeType.Sequence, string label = "")
    {
        if (fromId == toId) return;
        if (Edges.Any(e => e.FromNodeId == fromId && e.ToNodeId == toId)) return;
        
        // Prevent simple cycles
        if (Edges.Any(e => e.FromNodeId == toId && e.ToNodeId == fromId)) return;

        Edges.Add(new WorkflowEdge { FromNodeId = fromId, ToNodeId = toId, Type = type, Label = label });
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

        // Build adjacency map & in-degree for topological-like layering
        var nodeMap = Nodes.ToDictionary(n => n.Id, n => n);
        var inDegree = Nodes.ToDictionary(n => n.Id, n => 0);
        var adj = Nodes.ToDictionary(n => n.Id, n => new List<string>());

        foreach (var edge in Edges)
        {
            if (nodeMap.ContainsKey(edge.FromNodeId) && nodeMap.ContainsKey(edge.ToNodeId))
            {
                adj[edge.FromNodeId].Add(edge.ToNodeId);
                inDegree[edge.ToNodeId]++;
            }
        }

        // Assign levels (BFS)
        var levels = new Dictionary<string, int>();
        var queue = new Queue<string>();

        foreach (var kvp in inDegree)
        {
            if (kvp.Value == 0)
            {
                levels[kvp.Key] = 0;
                queue.Enqueue(kvp.Key);
            }
        }

        // If cycle or disconnected, default level 0
        foreach (var n in Nodes)
        {
            if (!levels.ContainsKey(n.Id))
            {
                levels[n.Id] = 0;
                queue.Enqueue(n.Id);
            }
        }

        while (queue.Count > 0)
        {
            string curr = queue.Dequeue();
            int currLevel = levels[curr];

            foreach (var neighbor in adj[curr])
            {
                if (!levels.ContainsKey(neighbor) || levels[neighbor] < currLevel + 1)
                {
                    levels[neighbor] = currLevel + 1;
                    queue.Enqueue(neighbor);
                }
            }
        }

        // Group by level and assign coordinates
        var levelGroups = Nodes.GroupBy(n => levels.TryGetValue(n.Id, out var lv) ? lv : 0)
                               .OrderBy(g => g.Key)
                               .ToList();

        float startX = 60f;
        float startY = 80f;
        float xSpacing = 265f;
        float ySpacing = 130f;

        foreach (var group in levelGroups)
        {
            int levelIndex = group.Key;
            var nodesInLevel = group.ToList();

            for (int i = 0; i < nodesInLevel.Count; i++)
            {
                var node = nodesInLevel[i];
                node.X = startX + levelIndex * xSpacing;
                
                // If it is collaboration or issue, offset downwards for clear visibility
                if (node.Category == NodeCategory.Collaboration || node.Category == NodeCategory.Issue)
                {
                    node.Y = startY + (i + 1) * ySpacing + 20f;
                }
                else
                {
                    node.Y = startY + i * ySpacing;
                }
            }
        }

        Invalidate();
        WorkflowChanged?.Invoke();
    }

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

        // Reset Transform for HUD / Empty Guide Overlay
        g.Transform = originalTransform;

        if (Nodes.Count == 0)
        {
            DrawEmptyCanvasGuide(g);
        }

        DrawHudOverlay(g);
    }

    private void DrawEmptyCanvasGuide(Graphics g)
    {
        float boxW = 540f;
        float boxH = 170f;
        float boxX = (Width - boxW) / 2f;
        float boxY = (Height - boxH) / 2f - 20f;

        var boxRect = new RectangleF(boxX, boxY, boxW, boxH);

        // Backdrop
        using (var brush = new SolidBrush(Color.FromArgb(235, 15, 23, 42))) // Dark slate #0f172a
        using (var pen = new Pen(Color.FromArgb(180, 56, 189, 248), 1.5f))
        {
            DrawRoundedRectangle(g, brush, pen, boxRect, 10f);
        }

        // Icon + Title
        using (var iconFont = new Font("Segoe UI Symbol", 16f, FontStyle.Bold))
        using (var titleFont = new Font("맑은 고딕", 11.5f, FontStyle.Bold))
        using (var subFont = new Font("맑은 고딕", 9f, FontStyle.Regular))
        using (var textBrush = new SolidBrush(Palette.TextWhite))
        using (var mutedBrush = new SolidBrush(Palette.TextMuted))
        {
            var sf = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString("📋 작업지시서와 부품을 선택한 뒤 기본 워크플로우를 생성하세요.", titleFont, textBrush, new PointF(Width / 2f, boxY + 24f), sf);
            g.DrawString("선택된 부품의 공정 단계에 맞추어 표준 CNC 작업 흐름(황삭→측정→정삭→세척)이 구성됩니다.", subFont, mutedBrush, new PointF(Width / 2f, boxY + 56f), sf);
        }

        // Action Button: [ ⚡ 기본 워크플로우 생성 ]
        float btnW = 240f;
        float btnH = 38f;
        float btnX = (Width - btnW) / 2f;
        float btnY = boxY + 98f;

        _emptyButtonScreenRect = new RectangleF(btnX, btnY, btnW, btnH);

        using (var btnBrush = new SolidBrush(Color.FromArgb(16, 185, 129))) // Emerald green
        using (var btnPen = new Pen(Color.FromArgb(52, 211, 153), 1.5f))
        using (var font = new Font("맑은 고딕", 9.8f, FontStyle.Bold))
        using (var brush = new SolidBrush(Color.White))
        {
            DrawRoundedRectangle(g, btnBrush, btnPen, _emptyButtonScreenRect, 6f);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("⚡ 기본 워크플로우 생성", font, brush, _emptyButtonScreenRect, sf);
        }
    }

    private void DrawGrid(Graphics g)
    {
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
                    edgeColor = Color.FromArgb(190, 100, 140, 180); // Classic Blue/Slate
                    edgeWidth = 2.0f;
                    break;
            }

            using var pen = new Pen(edgeColor, edgeWidth) { DashStyle = dash };
            using var path = new GraphicsPath();
            path.AddBezier(p1, c1, c2, p2);
            g.DrawPath(pen, path);

            // Draw arrow head at target
            DrawArrowHead(g, c2, p2, edgeColor);

            // Draw sub-worker movement / machine transfer direction badge along edge
            bool isDiffMachine = !string.IsNullOrEmpty(fromNode.MachineId) && !string.IsNullOrEmpty(toNode.MachineId) && !fromNode.MachineId.Equals(toNode.MachineId, StringComparison.OrdinalIgnoreCase);
            bool isDelegation = edge.Type == EdgeType.Delegation;

            if (isDiffMachine || isDelegation)
            {
                float t = 0.5f;
                float midX = (1 - t) * (1 - t) * (1 - t) * p1.X + 3 * (1 - t) * (1 - t) * t * c1.X + 3 * (1 - t) * t * t * c2.X + t * t * t * p2.X;
                float midY = (1 - t) * (1 - t) * (1 - t) * p1.Y + 3 * (1 - t) * (1 - t) * t * c1.Y + 3 * (1 - t) * t * t * c2.Y + t * t * t * p2.Y;

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

        float dx = Math.Abs(p2.X - p1.X) * 0.5f;
        float ctrlDist = Math.Max(40f, dx);

        PointF c1 = new(p1.X + ctrlDist, p1.Y);
        PointF c2 = new(p2.X - ctrlDist, p2.Y);

        using var pen = new Pen(Color.FromArgb(220, 14, 165, 233), 2.2f) { DashStyle = DashStyle.Dash };
        using var path = new GraphicsPath();
        path.AddBezier(p1, c1, c2, p2);
        g.DrawPath(pen, path);

        DrawArrowHead(g, c2, p2, Color.FromArgb(220, 14, 165, 233));
    }

    private void DrawNodes(Graphics g)
    {
        using var fontTitle = new Font("맑은 고딕", 9.2f, FontStyle.Bold);
        using var fontCat = new Font("맑은 고딕", 7.8f, FontStyle.Bold);
        using var fontMeta = new Font("맑은 고딕", 7.5f, FontStyle.Regular);

        foreach (var node in Nodes)
        {
            var bounds = node.Bounds;
            Color accentColor = GetCategoryColor(node.Category);

            // 1. Draw Glow if running / issue / selected
            if (node.Status == NodeStatus.Running)
            {
                using var glowPen = new Pen(Color.FromArgb(70, 16, 185, 129), 8f);
                DrawRoundedRectangle(g, null, glowPen, RectangleF.Inflate(bounds, 4, 4), 10f);
            }
            else if (node.Status == NodeStatus.Issue)
            {
                using var glowPen = new Pen(Color.FromArgb(70, 239, 68, 68), 8f);
                DrawRoundedRectangle(g, null, glowPen, RectangleF.Inflate(bounds, 4, 4), 10f);
            }
            else if (node.IsSelected)
            {
                using var glowPen = new Pen(Color.FromArgb(80, 14, 165, 233), 6f);
                DrawRoundedRectangle(g, null, glowPen, RectangleF.Inflate(bounds, 3, 3), 10f);
            }

            // 2. Draw Card Body
            using var bgBrush = new SolidBrush(Palette.NodeBg);
            Color borderColor = node.IsSelected ? Palette.NodeSelected : (node.Status == NodeStatus.Issue ? Palette.CatIssue : Palette.NodeBorder);
            float borderWidth = node.IsSelected ? 2.0f : 1.2f;
            using var borderPen = new Pen(borderColor, borderWidth);

            DrawRoundedRectangle(g, bgBrush, borderPen, bounds, 8f);

            // 3. Category Top Pill/Bar
            var pillRect = new RectangleF(bounds.X + 8, bounds.Y + 8, 54, 17);
            using (var catBrush = new SolidBrush(Color.FromArgb(35, accentColor)))
            using (var catBorderPen = new Pen(accentColor, 1f))
            {
                DrawRoundedRectangle(g, catBrush, catBorderPen, pillRect, 4f);
            }

            using (var catTextBrush = new SolidBrush(accentColor))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString($"[{GetCategoryKorean(node.Category)}]", fontCat, catTextBrush, pillRect, sf);
            }

            // Machine Pill (Color-coded by equipment with icon)
            if (!string.IsNullOrEmpty(node.MachineId))
            {
                var machRect = new RectangleF(bounds.Right - 68, bounds.Y + 8, 58, 18);
                Color machBgColor = node.MachineId switch
                {
                    "R01" => Color.FromArgb(45, 14, 165, 233), // Sky
                    "R02" => Color.FromArgb(45, 16, 185, 129), // Emerald
                    "R03" or "R04" or "R05" => Color.FromArgb(45, 59, 130, 246), // Blue
                    "측정실" => Color.FromArgb(50, 245, 158, 11), // Amber
                    "세척실" => Color.FromArgb(45, 99, 102, 241), // Indigo
                    "품질팀" => Color.FromArgb(45, 217, 70, 239), // Fuchsia
                    _ => Color.FromArgb(40, 148, 163, 184)
                };
                Color machBorderColor = node.MachineId switch
                {
                    "R01" => Color.FromArgb(140, 14, 165, 233),
                    "R02" => Color.FromArgb(140, 16, 185, 129),
                    "측정실" => Color.FromArgb(160, 245, 158, 11),
                    "세척실" => Color.FromArgb(140, 99, 102, 241),
                    "품질팀" => Color.FromArgb(140, 217, 70, 239),
                    _ => Color.FromArgb(100, 148, 163, 184)
                };
                using var machBg = new SolidBrush(machBgColor);
                using var machPen = new Pen(machBorderColor, 1.2f);
                DrawRoundedRectangle(g, machBg, machPen, machRect, 4f);

                using var machTextBrush = new SolidBrush(Color.White);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString($"⚙ {node.MachineId}", fontMeta, machTextBrush, machRect, sf);
            }

            // 4. Title Text
            using (var titleBrush = new SolidBrush(Palette.TextWhite))
            {
                var titleRect = new RectangleF(bounds.X + 10, bounds.Y + 29, bounds.Width - 20, 22);
                var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
                g.DrawString(node.Title, fontTitle, titleBrush, titleRect, sf);
            }

            // 5. Footer (Assignee & Status badge)
            using (var metaBrush = new SolidBrush(Palette.TextMuted))
            {
                string assigneeText = string.IsNullOrEmpty(node.Assignee) ? "담당: 미배정" : $"담당: {node.Assignee}";
                g.DrawString(assigneeText, fontMeta, metaBrush, bounds.X + 10, bounds.Y + 54);
            }

            // Status Badge on bottom right
            DrawStatusBadge(g, node, bounds.Right - 52, bounds.Y + 53, 44, 18);

            // 6. Output & Input Connector Anchors
            DrawPortAnchor(g, node.InputAnchor, false);
            DrawPortAnchor(g, node.OutputAnchor, true);
        }
    }

    private void DrawStatusBadge(Graphics g, WorkflowNode node, float x, float y, float w, float h)
    {
        var rect = new RectangleF(x, y, w, h);
        Color badgeBg;
        Color badgeFg;
        string statusText;

        switch (node.Status)
        {
            case NodeStatus.Running:
                badgeBg = Color.FromArgb(40, 16, 185, 129);
                badgeFg = Color.FromArgb(52, 211, 153);
                statusText = "가공중";
                break;
            case NodeStatus.PendingCheck:
                badgeBg = Color.FromArgb(40, 245, 158, 11);
                badgeFg = Color.FromArgb(251, 191, 36);
                statusText = "측정";
                break;
            case NodeStatus.Completed:
                badgeBg = Color.FromArgb(40, 14, 165, 233);
                badgeFg = Color.FromArgb(56, 189, 248);
                statusText = "완료";
                break;
            case NodeStatus.Issue:
                badgeBg = Color.FromArgb(45, 239, 68, 68);
                badgeFg = Color.FromArgb(248, 113, 113);
                statusText = "이슈";
                break;
            default:
                badgeBg = Color.FromArgb(30, 255, 255, 255);
                badgeFg = Palette.TextMuted;
                statusText = "대기";
                break;
        }

        using var bgBrush = new SolidBrush(badgeBg);
        using var fgBrush = new SolidBrush(badgeFg);
        using var pen = new Pen(badgeFg, 0.8f);
        using var font = new Font("맑은 고딕", 7.2f, FontStyle.Bold);

        DrawRoundedRectangle(g, bgBrush, pen, rect, 3f);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(statusText, font, fgBrush, rect, sf);
    }

    private void DrawPortAnchor(Graphics g, PointF p, bool isOutput)
    {
        float r = 5.5f;
        var rect = new RectangleF(p.X - r, p.Y - r, r * 2, r * 2);

        using var brush = new SolidBrush(isOutput ? Palette.NodeSelected : Color.FromArgb(52, 211, 153));
        using var pen = new Pen(Color.FromArgb(15, 23, 42), 1.5f);

        g.FillEllipse(brush, rect);
        g.DrawEllipse(pen, rect);
    }

    private void DrawHudOverlay(Graphics g)
    {
        // Bottom Right HUD: Node Count & Zoom
        string hudText = $"노드: {Nodes.Count}개 | 연결: {Edges.Count}개 | 줌: {(int)(_zoom * 100)}%";
        using var font = new Font("Consolas", 8.2f, FontStyle.Regular);
        using var brush = new SolidBrush(Palette.TextMuted);

        var size = g.MeasureString(hudText, font);
        g.DrawString(hudText, font, brush, Width - size.Width - 12, Height - size.Height - 8);

        // Bottom Left HUD: Mouse Help
        string helpText = "마우스 드래그: 이동 | 우클릭 드래그: 팬(화면 이동) | 휠: 줌 | 우측 원 드래그: 연결선 생성";
        using var fontHelp = new Font("맑은 고딕", 7.5f, FontStyle.Regular);
        g.DrawString(helpText, fontHelp, brush, 12, Height - 22);
    }

    private void DrawRoundedRectangle(Graphics g, Brush? brush, Pen? pen, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath();
        float diameter = radius * 2f;
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        if (brush != null) g.FillPath(brush, path);
        if (pen != null) g.DrawPath(pen, path);
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
    // MOUSE INTERACTION (Pan, Zoom, Drag, Connect)
    // =========================================================================
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        // Check if clicked the Empty Guide Button
        if (Nodes.Count == 0 && _emptyButtonScreenRect.Contains(e.Location))
        {
            RequestGenerateDefaultWorkflow?.Invoke();
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
            }
            else
            {
                SelectedNode = null;
            }
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

    private static bool IsPointNear(PointF p1, PointF p2, float threshold)
    {
        float dx = p1.X - p2.X;
        float dy = p1.Y - p2.Y;
        return (dx * dx + dy * dy) <= threshold * threshold;
    }
}
