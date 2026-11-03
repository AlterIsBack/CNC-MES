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
        public static readonly Color NodeSelected = Color.FromArgb(14, 165, 233);
        public static readonly Color AccentSky = Color.FromArgb(14, 165, 233);  // Sky #0ea5e9
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
    public List<WorkflowTitleBox> TitleBoxes { get; } = new();
    public bool ShowTimelineGuides { get; set; } = true;
    public double? CustomTimelineBaseHour { get; set; } = null;
    public float PixelsPerHour { get; set; } = 280f;

    public static double? ParseTimeString(string? timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return null;
        var parts = timeStr.Trim().Split(new[] { ':', '시', ' ', '-', '~' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && double.TryParse(parts[0], out double h) && double.TryParse(parts[1].Replace("분", "").Trim(), out double m))
        {
            return h + (m / 60.0);
        }
        if (parts.Length == 1 && double.TryParse(parts[0].Replace("시", "").Trim(), out double onlyH))
        {
            return onlyH;
        }
        return null;
    }

    public void SetTimelineInterval(float pxPerHour)
    {
        PixelsPerHour = Math.Clamp(pxPerHour, 120f, 720f);
        ArrangeByTimeline(autoFitBaseHour: false);
        TimelineIntervalChanged?.Invoke(PixelsPerHour);
        Invalidate();
    }

    public void ZoomTimelineInterval(float delta)
    {
        SetTimelineInterval(PixelsPerHour + delta);
    }

    public (string Size, string Material, string Fixing) GetFirstNodeMaterialInfo()
    {
        var first = Nodes.FirstOrDefault();
        if (first == null) return ("", "", "");
        return (first.MaterialSize ?? "", first.MaterialType ?? "", first.FixingMethod ?? "");
    }

    public void PropagateFirstNodeMaterial(string size, string material, string fixing)
    {
        if (Nodes.Count <= 1) return;
        foreach (var node in Nodes.Skip(1))
        {
            if (!node.IsMaterialCustomized)
            {
                node.MaterialSize = size;
                node.MaterialType = material;
                node.FixingMethod = fixing;
            }
        }
        Invalidate();
    }

        public double GetEffectiveTimelineBaseHour()
    {
        if (CustomTimelineBaseHour.HasValue)
            return CustomTimelineBaseHour.Value;

        var validTimes = Nodes
            .Select(n => ParseTimeString(n.StartTime))
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .ToList();

        if (validTimes.Count > 0)
        {
            return Math.Floor(validTimes.Min());
        }

        return Math.Max(0, (double)DateTime.Now.Hour);
    }
    public event Action<WorkflowTitleBox>? TitleBoxDoubleClicked;

    private WorkflowTitleBox? _selectedTitleBox;
    private WorkflowTitleBox? _draggedTitleBox;
    private PointF _dragStartOffsetTitleBox;
    private List<WorkflowNode> _nodesInDraggedTitleBox = new();
    private WorkflowTitleBox? _resizingTitleBox;
    private PointF _resizeStartMouse;
    private SizeF _resizeStartSize;

    // Workflow Confirmation Status
    public bool IsWorkflowConfirmed { get; set; } = false;
    public string ConfirmedInfoText { get; set; } = "";

    public List<WorkflowNode> SelectedNodes { get; } = new();
    public event Action<IReadOnlyList<WorkflowNode>>? SelectionChanged;
    public event Action<WorkflowNode?>? NodeSelected;
    public event Action<WorkflowEdge?>? EdgeSelected;
    public event Action<WorkflowNode>? NodeDoubleClicked;
    public event Action? WorkflowChanged;
    public event Action? RequestGenerateDefaultWorkflow;
    public event Action<NodeCategory>? RequestAddQuickNode;
    public event Action<WorkflowNode>? IssueBadgeClicked;

    private WorkflowNode? _selectedNode;
    private WorkflowEdge? _selectedEdge;
    private List<WorkflowNode> _draggedNodes = new();
    private Dictionary<WorkflowNode, PointF> _dragStartOffsets = new();
    private bool _isPanning;
    private Point _panStartMouse;
    private Point _mouseDownLocation;
    private PointF _panOffset = new(40, 40);
    private float _zoom = 1.0f;

    // Marquee / Box Selection
    private bool _isBoxSelecting;
    private Point _boxSelectStartScreen;
    private Point _boxSelectCurrentScreen;
    private List<WorkflowNode> _initialSelectionBeforeBox = new();

    // Edge Creation Dragging
    private WorkflowNode? _connectingFromNode;
    private PointF _currentConnectingPoint;

    // Hit / Click Rectangles for Interactive Elements in Screen Space
    private RectangleF _emptyButtonScreenRect;
    private RectangleF _emptyCloseButtonTopRect;
    private RectangleF _emptyCloseButtonBottomRect;
    private bool _hideEmptyCanvasGuide = false;
    private bool _isDraggingTimelineRuler = false;
    private Point _timelineDragStartMouse;
    private float _timelineDragStartPixelsPerHour;
    public event Action<float>? TimelineIntervalChanged;

    public bool HideEmptyCanvasGuide
    {
        get => _hideEmptyCanvasGuide;
        set { _hideEmptyCanvasGuide = value; Invalidate(); }
    }
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
            if (_selectedNode != value || (_selectedNode != null && SelectedNodes.Count > 1))
            {
                foreach (var n in Nodes) n.IsSelected = false;
                SelectedNodes.Clear();
                _selectedNode = value;
                if (_selectedNode != null)
                {
                    _selectedNode.IsSelected = true;
                    SelectedNodes.Add(_selectedNode);
                    if (_selectedEdge != null)
                    {
                        _selectedEdge.IsSelected = false;
                        _selectedEdge = null;
                        EdgeSelected?.Invoke(null);
                    }
                }
                Invalidate();
                NodeSelected?.Invoke(_selectedNode);
                SelectionChanged?.Invoke(SelectedNodes);
            }
        }
    }

    public void SetSelectedNodes(IEnumerable<WorkflowNode> nodes)
    {
        foreach (var n in Nodes) n.IsSelected = false;
        SelectedNodes.Clear();
        foreach (var n in nodes)
        {
            if (Nodes.Contains(n))
            {
                n.IsSelected = true;
                SelectedNodes.Add(n);
            }
        }
        _selectedNode = SelectedNodes.FirstOrDefault();
        if (_selectedEdge != null)
        {
            _selectedEdge.IsSelected = false;
            _selectedEdge = null;
            EdgeSelected?.Invoke(null);
        }
        Invalidate();
        NodeSelected?.Invoke(_selectedNode);
        SelectionChanged?.Invoke(SelectedNodes);
    }

    public void SelectAllNodes()
    {
        SetSelectedNodes(Nodes);
    }

    public void ClearSelection()
    {
        SelectedNode = null;
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
        TitleBoxes.Clear();
        _selectedTitleBox = null;
        SelectedNodes.Clear();
        _selectedNode = null;
        _selectedEdge = null;
        Invalidate();
        WorkflowChanged?.Invoke();
        SelectionChanged?.Invoke(SelectedNodes);
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
        SelectedNodes.Remove(node);
        Edges.RemoveAll(e => e.FromNodeId == node.Id || e.ToNodeId == node.Id);
        if (_selectedNode == node) _selectedNode = SelectedNodes.FirstOrDefault();
        if (_selectedEdge != null && !Edges.Contains(_selectedEdge)) SelectedEdge = null;
        Invalidate();
        WorkflowChanged?.Invoke();
        NodeSelected?.Invoke(_selectedNode);
        SelectionChanged?.Invoke(SelectedNodes);
    }

    public void RemoveNodes(IEnumerable<WorkflowNode> nodes)
    {
        var list = nodes.ToList();
        foreach (var n in list)
        {
            Nodes.Remove(n);
            SelectedNodes.Remove(n);
            Edges.RemoveAll(e => e.FromNodeId == n.Id || e.ToNodeId == n.Id);
        }
        _selectedNode = SelectedNodes.FirstOrDefault();
        if (_selectedEdge != null && !Edges.Contains(_selectedEdge)) SelectedEdge = null;
        Invalidate();
        WorkflowChanged?.Invoke();
        NodeSelected?.Invoke(_selectedNode);
        SelectionChanged?.Invoke(SelectedNodes);
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

        var fromNode = Nodes.FirstOrDefault(n => n.Id == fromId);
        var toNode = Nodes.FirstOrDefault(n => n.Id == toId);
        var edge = new WorkflowEdge 
        { 
            FromNodeId = fromId, 
            ToNodeId = toId, 
            FromJobId = fromNode?.JobId ?? "",
            ToJobId = toNode?.JobId ?? "",
            Type = type, 
            Label = label 
        };
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

    /// <summary>
    /// 노드의 [시작시간]과 [설비(MachineId)]에 맞춰 행과 열을 계산하여 타임라인 형태로 칼각 정렬
    /// </summary>
    /// <summary>
    /// 기존 노드들의 형태(Y축 레이아웃, 상하 배치 구조)를 보존하면서 각 노드의 [시작시간]에 맞춰 X좌표를 타임라인 칼각 정렬
    /// </summary>
    public void ArrangeByTimeline(bool autoFitBaseHour = true)
    {
        if (Nodes.Count == 0) return;

        // 1. 이미 입력된 유효한 StartTime들을 먼저 탐색
        var validStartTimes = Nodes
            .Select(n => ParseTimeString(n.StartTime))
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .ToList();

        // 기준 시간 결정 (가공시간이 16시면 16시에 맞게 타임라인이 정렬)
        double baseTime;
        if (CustomTimelineBaseHour.HasValue && !autoFitBaseHour)
        {
            baseTime = CustomTimelineBaseHour.Value;
        }
        else if (validStartTimes.Count > 0)
        {
            baseTime = Math.Floor(validStartTimes.Min());
            CustomTimelineBaseHour = baseTime;
        }
        else
        {
            baseTime = Math.Floor((double)DateTime.Now.Hour);
            CustomTimelineBaseHour = baseTime;
        }

        // 2. 시간이 비어있는 노드들에 대해, 연결(Edge) 관계를 기반으로 시간 스마트 추정
        double defaultDuration = 0.5; // 30분 기본 가공
        double runningTime = baseTime;

        var inDegree = Nodes.ToDictionary(n => n.Id, n => 0);
        foreach (var edge in Edges)
        {
            if (inDegree.ContainsKey(edge.ToNodeId))
                inDegree[edge.ToNodeId]++;
        }

        var queue = new Queue<WorkflowNode>(Nodes.Where(n => inDegree[n.Id] == 0));
        var visited = new HashSet<string>();

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            visited.Add(node.Id);

            double? tStart = ParseTimeString(node.StartTime);
            if (!tStart.HasValue)
            {
                var incomingEdges = Edges.Where(e => e.ToNodeId == node.Id).ToList();
                double prevEnd = baseTime;
                foreach (var ie in incomingEdges)
                {
                    var parentNode = Nodes.FirstOrDefault(n => n.Id == ie.FromNodeId);
                    if (parentNode != null)
                    {
                        double? pe = ParseTimeString(parentNode.EndTime) ?? (ParseTimeString(parentNode.StartTime) + defaultDuration);
                        if (pe.HasValue && pe.Value > prevEnd)
                            prevEnd = pe.Value;
                    }
                }

                double calcStart = Math.Max(runningTime, prevEnd);
                int sh = (int)calcStart;
                int sm = (int)Math.Round((calcStart - sh) * 60);
                node.StartTime = $"{sh:D2}:{sm:D2}";

                double calcEnd = calcStart + defaultDuration;
                int eh = (int)calcEnd;
                int em = (int)Math.Round((calcEnd - eh) * 60);
                node.EndTime = $"{eh:D2}:{em:D2}";

                runningTime = calcEnd;
            }
            else
            {
                double? tEnd = ParseTimeString(node.EndTime);
                if (!tEnd.HasValue)
                {
                    double calcEnd = tStart.Value + defaultDuration;
                    int eh = (int)calcEnd;
                    int em = (int)Math.Round((calcEnd - eh) * 60);
                    node.EndTime = $"{eh:D2}:{em:D2}";
                }
                runningTime = Math.Max(runningTime, (ParseTimeString(node.EndTime) ?? (tStart.Value + defaultDuration)));
            }

            foreach (var edge in Edges.Where(e => e.FromNodeId == node.Id))
            {
                if (inDegree.ContainsKey(edge.ToNodeId))
                {
                    inDegree[edge.ToNodeId]--;
                    if (inDegree[edge.ToNodeId] == 0 && !visited.Contains(edge.ToNodeId))
                    {
                        var nextNode = Nodes.FirstOrDefault(n => n.Id == edge.ToNodeId);
                        if (nextNode != null) queue.Enqueue(nextNode);
                    }
                }
            }
        }

        foreach (var node in Nodes.Where(n => !visited.Contains(n.Id)))
        {
            if (!ParseTimeString(node.StartTime).HasValue)
            {
                int sh = (int)runningTime;
                int sm = (int)Math.Round((runningTime - sh) * 60);
                node.StartTime = $"{sh:D2}:{sm:D2}";

                double calcEnd = runningTime + defaultDuration;
                int eh = (int)calcEnd;
                int em = (int)Math.Round((calcEnd - eh) * 60);
                node.EndTime = $"{eh:D2}:{em:D2}";

                runningTime = calcEnd;
            }
        }

        // 3. 기존 형태(Y좌표)는 100% 보존하고, X좌표만 시간에 맞게 정렬!
        float startX = 60f;
        float pixelsPerHour = PixelsPerHour;

        foreach (var node in Nodes)
        {
            double nodeStart = ParseTimeString(node.StartTime) ?? baseTime;
            float targetX = startX + (float)((nodeStart - baseTime) * pixelsPerHour);
            node.X = Math.Max(40f, targetX);
            // 중요: node.Y는 기존 배치 형태를 100% 보존하여 절대 일자로 덮어쓰지 않습니다!
        }

        // 4. 동일한 Y 높이 레벨(오차 65px 이내)에 위치한 노드들 간의 가로 겹침 방지 (최소 간격 25px)
        var sortedByX = Nodes.OrderBy(n => n.X).ToList();
        for (int i = 0; i < sortedByX.Count; i++)
        {
            for (int j = i + 1; j < sortedByX.Count; j++)
            {
                var nA = sortedByX[i];
                var nB = sortedByX[j];

                if (Math.Abs(nA.Y - nB.Y) < 65f)
                {
                    float minAllowedX = nA.X + nA.Width + 25f;
                    if (nB.X < minAllowedX)
                    {
                        nB.X = minAllowedX;
                    }
                }
            }
        }

        // 5. 타이틀 그룹 박스 영역 자동 갱신 (내부 노드들이 이동했으므로 박스 영역 자동 동기화)
        foreach (var box in TitleBoxes)
        {
            var insideNodes = Nodes.Where(n => box.ContainsNode(n)).ToList();
            if (insideNodes.Count > 0)
            {
                float minX = insideNodes.Min(n => n.X) - 30;
                float minY = insideNodes.Min(n => n.Y) - 50;
                float maxX = insideNodes.Max(n => n.X + n.Width) + 30;
                float maxY = insideNodes.Max(n => n.Y + n.Height) + 30;

                box.X = Math.Max(20, minX);
                box.Y = Math.Max(20, minY);
                box.Width = Math.Max(220, maxX - box.X);
                box.Height = Math.Max(120, maxY - box.Y);
            }
        }

        ResetView();
        Invalidate();
        WorkflowChanged?.Invoke();
    }
    // =========================================================================

    public WorkflowTitleBox WrapNodesWithTitleBox(string title = "공정 그룹군", string colorHex = "#0284c7")
    {
        var targetNodes = SelectedNodes.Count > 0 ? SelectedNodes.ToList() : Nodes.ToList();
        var box = new WorkflowTitleBox
        {
            Title = title,
            ColorHex = colorHex
        };
        if (targetNodes.Count > 0)
        {
            box.AutoFit(targetNodes);
        }
        else
        {
            float cx = -_panOffset.X / _zoom + (Width / (2f * _zoom)) - 210f;
            float cy = -_panOffset.Y / _zoom + (Height / (2f * _zoom)) - 110f;
            box.X = Math.Max(40f, cx);
            box.Y = Math.Max(40f, cy);
            box.Width = 420f;
            box.Height = 220f;
        }

        TitleBoxes.Add(box);
        Invalidate();
        WorkflowChanged?.Invoke();
        return box;
    }

    public List<WorkflowNode> DuplicateSelectedNodes()
    {
        var sourceNodes = SelectedNodes.Count > 0 ? SelectedNodes.ToList() : (_selectedNode != null ? new List<WorkflowNode> { _selectedNode } : new List<WorkflowNode>());
        if (sourceNodes.Count == 0) return new List<WorkflowNode>();

        var clonedNodes = new List<WorkflowNode>();
        var idMap = new Dictionary<string, string>();

        foreach (var src in sourceNodes)
        {
            var dup = new WorkflowNode
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Title = $"{src.Title} (복사본)",
                Category = src.Category,
                Status = NodeStatus.Ready,
                OrderNumber = src.OrderNumber,
                PartName = src.PartName,
                MachineId = src.MachineId,
                Assignee = src.Assignee,
                Details = src.Details,
                NextProcess = src.NextProcess,
                SubWorkerName = src.SubWorkerName,
                SubWorkerInstruction = src.SubWorkerInstruction,
                StartTime = src.StartTime,
                EndTime = src.EndTime,
                MaterialSize = src.MaterialSize,
                MaterialType = src.MaterialType,
                FixingMethod = src.FixingMethod,
                JobId = src.JobId,
                X = src.X + 45f,
                Y = src.Y + 45f,
                Width = src.Width,
                Height = src.Height
            };
            idMap[src.Id] = dup.Id;
            Nodes.Add(dup);
            clonedNodes.Add(dup);
        }

        // Duplicate internal edges between source nodes if any
        var internalEdges = Edges.Where(e => idMap.ContainsKey(e.FromNodeId) && idMap.ContainsKey(e.ToNodeId)).ToList();
        foreach (var edge in internalEdges)
        {
            Edges.Add(new WorkflowEdge
            {
                FromNodeId = idMap[edge.FromNodeId],
                ToNodeId = idMap[edge.ToNodeId],
                FromJobId = edge.FromJobId,
                ToJobId = edge.ToJobId,
                Type = edge.Type,
                Label = edge.Label
            });
        }

        // Immediately select all newly created duplicated nodes!
        SelectedNodes.Clear();
        foreach (var n in Nodes) n.IsSelected = false;

        foreach (var dup in clonedNodes)
        {
            dup.IsSelected = true;
            SelectedNodes.Add(dup);
        }
        _selectedNode = clonedNodes.LastOrDefault();

        Invalidate();
        NodeSelected?.Invoke(_selectedNode);
        SelectionChanged?.Invoke(SelectedNodes);
        WorkflowChanged?.Invoke();

        return clonedNodes;
    }

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
        if (ShowTimelineGuides) DrawTimelineGridLines(g);
        DrawTitleBoxes(g);
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
        DrawMarqueeSelectionOverlay(g);
        if (ShowTimelineGuides) DrawTimelineRulerHeader(g);
    }

    private void DrawEmptyCanvasGuide(Graphics g)
    {
        if (_hideEmptyCanvasGuide) return;

        float boxW = 580f;
        float boxH = 175f;
        float boxX = (Width - boxW) / 2f;
        float boxY = (Height - boxH) / 2f - 20f;

        var boxRect = new RectangleF(boxX, boxY, boxW, boxH);

        using var bgBrush = new SolidBrush(Color.FromArgb(240, 15, 23, 42));
        using var borderPen = new Pen(Palette.CatPrep, 1.5f);
        DrawRoundedRectangle(g, bgBrush, borderPen, boxRect, 10f);

        // 1. 우측 상단 닫기 [✕] 버튼
        _emptyCloseButtonTopRect = new RectangleF(boxX + boxW - 36f, boxY + 12f, 24f, 24f);
        using var brushCloseX = new SolidBrush(Color.FromArgb(148, 163, 184));
        using var fontCloseX = new Font("맑은 고딕", 10.5f, FontStyle.Bold);
        var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("✕", fontCloseX, brushCloseX, _emptyCloseButtonTopRect, sfCenter);

        // 2. 제목 및 안내 문구
        using var fontIcon = new Font("Segoe UI Symbol", 20f);
        using var fontTitle = new Font("맑은 고딕", 12f, FontStyle.Bold);
        using var fontSub = new Font("맑은 고딕", 9f);
        using var brushSky = new SolidBrush(Palette.CatPrep);
        using var brushWhite = new SolidBrush(Palette.TextWhite);
        using var brushMuted = new SolidBrush(Palette.TextMuted);

        g.DrawString("⚡", fontIcon, brushSky, boxX + 24, boxY + 22);
        g.DrawString("기본 공정 워크플로우를 자동 생성하시겠습니까?", fontTitle, brushWhite, boxX + 65, boxY + 24);
        g.DrawString("좌측에서 작업지시서와 부품을 선택한 후 아래 버튼을 클릭하면 표준 가공 공정이 배치됩니다.", fontSub, brushMuted, boxX + 65, boxY + 54);

        // 3. 하단 액션 버튼: [⚡ 기본 워크플로우 생성] 및 [✕ 닫기]
        float btnW = 230f;
        float btnH = 38f;
        float closeW = 90f;
        float gap = 12f;
        float totalW = btnW + closeW + gap;
        float startBtnX = boxX + (boxW - totalW) / 2f;
        float btnY = boxY + 102f;

        // 생성 버튼
        _emptyButtonScreenRect = new RectangleF(startBtnX, btnY, btnW, btnH);
        using var btnBg = new SolidBrush(Palette.CatMach);
        using var btnBorder = new Pen(Color.FromArgb(100, 255, 255, 255), 1.2f);
        DrawRoundedRectangle(g, btnBg, btnBorder, _emptyButtonScreenRect, 6f);
        using var fontBtn = new Font("맑은 고딕", 9.5f, FontStyle.Bold);
        g.DrawString("⚡ 기본 워크플로우 생성", fontBtn, Brushes.White, _emptyButtonScreenRect, sfCenter);

        // [✕ 닫기] 버튼
        _emptyCloseButtonBottomRect = new RectangleF(startBtnX + btnW + gap, btnY, closeW, btnH);
        using var closeBg = new SolidBrush(Color.FromArgb(51, 65, 85));
        using var closeBorder = new Pen(Color.FromArgb(100, 148, 163, 184), 1f);
        DrawRoundedRectangle(g, closeBg, closeBorder, _emptyCloseButtonBottomRect, 6f);
        g.DrawString("✕ 닫기", fontBtn, Brushes.White, _emptyCloseButtonBottomRect, sfCenter);
    }
    private void DrawTimelineGridLines(Graphics g)
    {
        if (Nodes.Count == 0 && TitleBoxes.Count == 0) return;

        double baseTime = GetEffectiveTimelineBaseHour();
        float startX = 60f;
        float pixelsPerHour = PixelsPerHour;

        float worldTop = -_panOffset.Y / _zoom;
        float worldBottom = worldTop + Height / _zoom;
        float worldLeft = -_panOffset.X / _zoom;
        float worldRight = worldLeft + Width / _zoom;

        using var linePenHour = new Pen(Color.FromArgb(40, 56, 189, 248), 1f) { DashStyle = DashStyle.Dash };
        using var linePenHalf = new Pen(Color.FromArgb(16, 56, 189, 248), 1f) { DashStyle = DashStyle.Dot };

        int minH = (int)Math.Floor(baseTime + (worldLeft - startX - 100f) / pixelsPerHour);
        int maxH = (int)Math.Ceiling(baseTime + (worldRight - startX + 100f) / pixelsPerHour);
        minH = Math.Max(0, minH);
        maxH = Math.Min(48, maxH);

        for (int h = minH; h <= maxH; h++)
        {
            float hourX = startX + (float)((h - baseTime) * pixelsPerHour);
            float halfX = hourX + (0.5f * pixelsPerHour);

            if (hourX >= worldLeft - 60 && hourX <= worldRight + 60)
            {
                g.DrawLine(linePenHour, hourX, worldTop, hourX, worldBottom);
            }

            if (halfX >= worldLeft - 60 && halfX <= worldRight + 60)
            {
                g.DrawLine(linePenHalf, halfX, worldTop, halfX, worldBottom);
            }
        }

        // NOW Guide Line (world space)
        DateTime now = DateTime.Now;
        double nowHour = now.Hour + (now.Minute / 60.0);
        float nowX = startX + (float)((nowHour - baseTime) * pixelsPerHour);
        if (nowX >= worldLeft - 60 && nowX <= worldRight + 60)
        {
            using var penNow = new Pen(Color.FromArgb(239, 68, 68), 1.5f) { DashStyle = DashStyle.DashDot };
            g.DrawLine(penNow, nowX, worldTop, nowX, worldBottom);
        }
    }

    private void DrawTimelineRulerHeader(Graphics g)
    {
        if (Nodes.Count == 0 && TitleBoxes.Count == 0) return;

        double baseTime = GetEffectiveTimelineBaseHour();
        float startX = 60f;
        float pixelsPerHour = PixelsPerHour;

        using var rulerBg = new SolidBrush(Color.FromArgb(245, 15, 23, 42));
        using var rulerBorder = new Pen(Color.FromArgb(80, 56, 189, 248), 1.2f);
        using var badgeBg = new SolidBrush(Color.FromArgb(240, 30, 41, 59));
        using var textSky = new SolidBrush(Color.FromArgb(56, 189, 248));
        using var textSlate = new SolidBrush(Color.FromArgb(148, 163, 184));
        using var fontHour = new Font("Consolas", 8.2f, FontStyle.Bold);
        using var fontHalf = new Font("Consolas", 7.2f);
        using var fontNow = new Font("맑은 고딕", 7.5f, FontStyle.Bold);
        using var fontGrip = new Font("맑은 고딕", 7.5f, FontStyle.Bold);

        // Fixed Top Header Strip in Screen Space
        g.FillRectangle(rulerBg, 0, 0, Width, 36);
        g.DrawLine(rulerBorder, 0, 36, Width, 36);

        using var sfLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        g.DrawString("⏱ 타임라인 (좌우 드래그: 간격조절 ↔)", fontGrip, textSlate, new RectangleF(10, 0, 230, 36), sfLeft);

        float worldLeft = -_panOffset.X / _zoom;
        float worldRight = worldLeft + Width / _zoom;

        int minH = (int)Math.Floor(baseTime + (worldLeft - startX - 100f) / pixelsPerHour);
        int maxH = (int)Math.Ceiling(baseTime + (worldRight - startX + 100f) / pixelsPerHour);
        minH = Math.Max(0, minH);
        maxH = Math.Min(48, maxH);

        using var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        for (int h = minH; h <= maxH; h++)
        {
            float hourX = startX + (float)((h - baseTime) * pixelsPerHour);
            float screenX = hourX * _zoom + _panOffset.X;

            if (screenX >= -60 && screenX <= Width + 60)
            {
                int displayHour = h % 24;
                string daySuffix = h >= 24 ? " (+1일)" : "";
                var badgeRect = new RectangleF(screenX - 30f, 6f, 60f, 22f);
                DrawRoundedRectangle(g, badgeBg, rulerBorder, badgeRect, 4f);
                g.DrawString($"{displayHour:D2}:00{daySuffix}", fontHour, textSky, badgeRect, sfCenter);
            }

            float halfX = hourX + (0.5f * pixelsPerHour);
            float halfScreenX = halfX * _zoom + _panOffset.X;
            if (halfScreenX >= -40 && halfScreenX <= Width + 40)
            {
                var halfBadgeRect = new RectangleF(halfScreenX - 16f, 10f, 32f, 16f);
                g.DrawString(":30", fontHalf, textSlate, halfBadgeRect, sfCenter);
            }
        }

        // NOW Indicator in Screen Space
        DateTime now = DateTime.Now;
        double nowHour = now.Hour + (now.Minute / 60.0);
        float nowX = startX + (float)((nowHour - baseTime) * pixelsPerHour);
        float nowScreenX = nowX * _zoom + _panOffset.X;
        if (nowScreenX >= -50 && nowScreenX <= Width + 50)
        {
            using var penNow = new Pen(Color.FromArgb(239, 68, 68), 1.5f);
            using var brushNowBg = new SolidBrush(Color.FromArgb(220, 239, 68, 68));
            var nowBadgeRect = new RectangleF(nowScreenX - 38f, 7f, 76f, 20f);
            DrawRoundedRectangle(g, brushNowBg, penNow, nowBadgeRect, 3f);
            g.DrawString($"NOW {now:HH:mm}", fontNow, Brushes.White, nowBadgeRect, sfCenter);
        }
    }
    private void DrawTitleBoxes(Graphics g)
    {
        if (TitleBoxes.Count == 0) return;

        using var fontTitle = new Font("맑은 고딕", 9.5f, FontStyle.Bold);
        using var fontSub = new Font("맑은 고딕", 8f);
        using var fontBtn = new Font("맑은 고딕", 7.5f, FontStyle.Bold);

        foreach (var box in TitleBoxes)
        {
            Color accentColor;
            try { accentColor = ColorTranslator.FromHtml(box.ColorHex); }
            catch { accentColor = Palette.AccentSky; }

            // 1. Box Body (Semi-transparent background)
            var bodyRect = box.Bounds;
            using (var bBg = new SolidBrush(Color.FromArgb(20, accentColor)))
            using (var bPen = new Pen(box.IsSelected ? Color.White : Color.FromArgb(160, accentColor), box.IsSelected ? 2.2f : 1.4f))
            {
                if (!box.IsSelected) bPen.DashStyle = DashStyle.Dash;
                DrawRoundedRectangle(g, bBg, bPen, bodyRect, 8f);
            }

            // 2. Header Bar
            var headerRect = box.HeaderBounds;
            using (var hBg = new SolidBrush(Color.FromArgb(220, 15, 23, 42)))
            using (var hPen = new Pen(Color.FromArgb(120, accentColor), 1f))
            {
                DrawRoundedRectangle(g, hBg, hPen, headerRect, 6f);
            }

            // Top accent indicator strip
            using (var stripBrush = new SolidBrush(accentColor))
            {
                g.FillRectangle(stripBrush, headerRect.X + 6, headerRect.Y + 2, headerRect.Width - 12, 3);
            }

            // Title and member count
            int memberCount = Nodes.Count(n => box.Bounds.Contains(n.Bounds));
            string titleText = $"📦 {box.Title}";
            using (var tBrush = new SolidBrush(Palette.TextWhite))
            {
                g.DrawString(titleText, fontTitle, tBrush, headerRect.X + 10, headerRect.Y + 7);
            }

            var titleSize = g.MeasureString(titleText, fontTitle);
            string countText = $"({memberCount}개 공정 포함)";
            using (var cBrush = new SolidBrush(Color.FromArgb(148, 163, 184)))
            {
                g.DrawString(countText, fontSub, cBrush, headerRect.X + 16 + titleSize.Width, headerRect.Y + 9);
            }

            // Auto-fit button in header
            var btnRect = box.AutoFitButtonBounds;
            using (var btnBg = new SolidBrush(Color.FromArgb(40, accentColor)))
            using (var btnPen = new Pen(Color.FromArgb(120, accentColor), 1f))
            using (var btnText = new SolidBrush(Color.FromArgb(203, 213, 225)))
            {
                DrawRoundedRectangle(g, btnBg, btnPen, btnRect, 4f);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("⟲ 자동맞춤", fontBtn, btnText, btnRect, sf);
            }

            // 3. Bottom-Right Resize Handle
            var gripRect = box.ResizeHandleBounds;
            using (var gripPen = new Pen(Color.FromArgb(180, accentColor), 1.5f))
            {
                g.DrawLine(gripPen, gripRect.Right - 4, gripRect.Bottom - 12, gripRect.Right - 12, gripRect.Bottom - 4);
                g.DrawLine(gripPen, gripRect.Right - 4, gripRect.Bottom - 8, gripRect.Right - 8, gripRect.Bottom - 4);
                g.DrawLine(gripPen, gripRect.Right - 4, gripRect.Bottom - 4, gripRect.Right - 4, gripRect.Bottom - 4);
            }
        }
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

    public static (PointF p1, PointF c1, PointF c2, PointF p2) GetEdgeCurvePoints(WorkflowNode from, WorkflowNode to)
    {
        float dx = (to.X + to.Width / 2f) - (from.X + from.Width / 2f);
        float dy = (to.Y + to.Height / 2f) - (from.Y + from.Height / 2f);

        if (Math.Abs(dy) > Math.Abs(dx) * 1.15f)
        {
            if (dy > 0)
            {
                PointF p1 = from.BottomAnchor;
                PointF p2 = to.TopAnchor;
                float dist = Math.Max(35f, (p2.Y - p1.Y) * 0.45f);
                return (p1, new PointF(p1.X, p1.Y + dist), new PointF(p2.X, p2.Y - dist), p2);
            }
            else
            {
                PointF p1 = from.TopAnchor;
                PointF p2 = to.BottomAnchor;
                float dist = Math.Max(35f, (p1.Y - p2.Y) * 0.45f);
                return (p1, new PointF(p1.X, p1.Y - dist), new PointF(p2.X, p2.Y + dist), p2);
            }
        }
        else
        {
            if (dx >= 0)
            {
                PointF p1 = from.OutputAnchor;
                PointF p2 = to.InputAnchor;
                float dist = Math.Max(45f, (p2.X - p1.X) * 0.5f);
                return (p1, new PointF(p1.X + dist, p1.Y), new PointF(p2.X - dist, p2.Y), p2);
            }
            else
            {
                PointF p1 = from.InputAnchor;
                PointF p2 = to.OutputAnchor;
                float dist = Math.Max(45f, (p1.X - p2.X) * 0.5f);
                return (p1, new PointF(p1.X - dist, p1.Y), new PointF(p2.X + dist, p2.Y), p2);
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

            var (p1, c1, c2, p2) = GetEdgeCurvePoints(fromNode, toNode);

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
                // Adjust Y offset when nodes are close horizontally so text never collides with anchors
                float horizontalDist = Math.Abs(p2.X - p1.X);
                float yAdjust = horizontalDist < 140f ? -16f : 0f;
                var badgeRect = new RectangleF(midX - textSize.Width / 2f - 6f, midY - textSize.Height / 2f - 3f + yAdjust, textSize.Width + 12f, textSize.Height + 6f);

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

        PointF p1 = _connectingFromNode.GetClosestAnchor(_currentConnectingPoint);
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

    public RectangleF GetIssueBadgeBounds(WorkflowNode node)
    {
        var rect = node.Bounds;
        if (node.Status == NodeStatus.Issue)
        {
            // [🚨 이슈] 붉은색 배지 영역 (48x20)
            return new RectangleF(rect.Right - 54, rect.Y + 52, 46, 20);
        }
        else if (node.HasIssueHistory)
        {
            // [⚠️ 조치완료] 호박색/주황색 배지 영역 (68x20)
            return new RectangleF(rect.Right - 116, rect.Y + 52, 68, 20);
        }
        return RectangleF.Empty;
    }

    private void DrawSingleNode(Graphics g, WorkflowNode node)
    {
        var rect = node.Bounds;
        Color catColor = GetCategoryColor(node.Category);
        bool isSelected = node.IsSelected || SelectedNodes.Contains(node);

        using (var bgBrush = new SolidBrush(Palette.NodeBg))
        using (var borderPen = new Pen(isSelected ? Palette.NodeSelected : Palette.NodeBorder, isSelected ? 2.6f : 1f))
        {
            DrawRoundedRectangle(g, bgBrush, borderPen, rect, 8f);
        }

        // Multi-select Check Badge (Top-left corner indicator)
        if (isSelected && SelectedNodes.Count > 1)
        {
            var selBadgeRect = new RectangleF(rect.X - 4f, rect.Y - 4f, 18f, 18f);
            using var badgeBg = new SolidBrush(Color.FromArgb(240, 14, 165, 233));
            using var badgeBorder = new Pen(Color.White, 1.2f);
            g.FillEllipse(badgeBg, selBadgeRect);
            g.DrawEllipse(badgeBorder, selBadgeRect);

            using var fontCheck = new Font("맑은 고딕", 7.2f, FontStyle.Bold);
            var sfCheck = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("✓", fontCheck, Brushes.White, selBadgeRect, sfCheck);
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

        // Timeline Start/End Time Badge (e.g. 🕒 08:30~09:20)
        if (!string.IsNullOrEmpty(node.StartTime))
        {
            string timeBadgeText = string.IsNullOrEmpty(node.EndTime) ? $"🕒 {node.StartTime}" : $"🕒 {node.StartTime}~{node.EndTime}";
            using var fontTime = new Font("Consolas", 7.2f, FontStyle.Bold);
            var timeSize = g.MeasureString(timeBadgeText, fontTime);
            float timeX = badgeRect.Right + 6f;
            float maxTimeW = machRect.Left - timeX - 6f;

            if (maxTimeW > 30f)
            {
                var timeRect = new RectangleF(timeX, rect.Y + 8, Math.Min(timeSize.Width + 6f, maxTimeW), 18);
                using var timeBg = new SolidBrush(Color.FromArgb(50, 14, 165, 233));
                using var timePen = new Pen(Palette.AccentSky, 1f);
                DrawRoundedRectangle(g, timeBg, timePen, timeRect, 4f);

                using var timeBrush = new SolidBrush(Palette.AccentSky);
                var sfTime = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                g.DrawString(timeBadgeText, fontTime, timeBrush, timeRect, sfTime);
            }
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

        // Status Badge & Issue History Badge
        if (node.Status == NodeStatus.Issue)
        {
            // 노드가 현재 이슈 발생 상태인 경우: [🚨 이슈] 붉은색 고대비 배지
            var issueRect = GetIssueBadgeBounds(node);
            using (var sBg = new SolidBrush(Color.FromArgb(220, 38, 38)))
            using (var sPen = new Pen(Color.FromArgb(254, 202, 202), 1.2f))
            {
                DrawRoundedRectangle(g, sBg, sPen, issueRect, 4f);
            }
            using (var sText = new SolidBrush(Color.White))
            using (var fontS = new Font("맑은 고딕", 7.2f, FontStyle.Bold))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("🚨 이슈", fontS, sText, issueRect, sf);
            }
        }
        else
        {
            // 기본 상태 배지 (대기, 진행중, 측정, 완료)
            string statusText = node.Status switch
            {
                NodeStatus.Ready => "대기",
                NodeStatus.Running => "진행중",
                NodeStatus.PendingCheck => "측정",
                NodeStatus.Completed => "완료",
                _ => "대기"
            };
            Color statusBg = node.Status switch
            {
                NodeStatus.Ready => Color.FromArgb(42, 59, 76),
                NodeStatus.Running => Color.FromArgb(5, 150, 105),
                NodeStatus.PendingCheck => Color.FromArgb(217, 119, 6),
                NodeStatus.Completed => Color.FromArgb(2, 132, 199),
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

            // 이슈 해결 완료 후 노드 안에 이슈 이력 표시 유지: [⚠️ 조치완료] (클릭 시 이전 이슈 상세 열람)
            if (node.HasIssueHistory)
            {
                var resolvedRect = GetIssueBadgeBounds(node);
                using (var resBg = new SolidBrush(Color.FromArgb(60, 245, 158, 11)))
                using (var resPen = new Pen(Palette.CatInsp, 1.2f))
                {
                    DrawRoundedRectangle(g, resBg, resPen, resolvedRect, 4f);
                }
                using (var resText = new SolidBrush(Palette.CatInsp))
                using (var fontRes = new Font("맑은 고딕", 7.0f, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("⚠️ 조치완료", fontRes, resText, resolvedRect, sf);
                }
            }
        }

        // Connection Anchors: Left(Input), Right(Output), Top, Bottom
        DrawAnchor(g, node.InputAnchor, false);
        DrawAnchor(g, node.OutputAnchor, true);
        DrawAnchor(g, node.TopAnchor, false);
        DrawAnchor(g, node.BottomAnchor, false);
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

    private void DrawMarqueeSelectionOverlay(Graphics g)
    {
        if (!_isBoxSelecting) return;

        float sx = Math.Min(_boxSelectStartScreen.X, _boxSelectCurrentScreen.X);
        float sy = Math.Min(_boxSelectStartScreen.Y, _boxSelectCurrentScreen.Y);
        float sw = Math.Abs(_boxSelectStartScreen.X - _boxSelectCurrentScreen.X);
        float sh = Math.Abs(_boxSelectStartScreen.Y - _boxSelectCurrentScreen.Y);

        if (sw < 1f || sh < 1f) return;

        var boxRect = new RectangleF(sx, sy, sw, sh);

        using var brush = new SolidBrush(Color.FromArgb(45, 14, 165, 233)); // Sky translucent
        g.FillRectangle(brush, boxRect);

        using var pen = new Pen(Color.FromArgb(56, 189, 248), 1.5f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, boxRect.X, boxRect.Y, boxRect.Width, boxRect.Height);
    }

    private void DrawHudOverlay(Graphics g)
    {
        string selInfo = SelectedNodes.Count > 1
            ? $" | ⚡ {SelectedNodes.Count}개 노드 다중 선택됨 (우측에서 설비/상태 일괄변경 | Esc 선택해제)"
            : (SelectedNodes.Count == 1 ? $" | 선택: [{SelectedNodes[0].Title}] (설비: {SelectedNodes[0].MachineId})" : "");
        string hudText = $"노드: {Nodes.Count}개 | 연결: {Edges.Count}개 | 줌: {(int)(_zoom * 100)}%{selInfo} | [마우스 드래그: 영역 다중선택 | Ctrl+A 전체선택]";
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

        if (e.Control && e.KeyCode == Keys.A)
        {
            SelectAllNodes();
            e.Handled = true;
            return;
        }

        if (e.Control && (e.KeyCode == Keys.D || e.KeyCode == Keys.C))
        {
            DuplicateSelectedNodes();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            ClearSelection();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
        {
            if (_selectedEdge != null)
            {
                RemoveEdge(_selectedEdge);
                e.Handled = true;
            }
            else if (SelectedNodes.Count > 0)
            {
                RemoveNodes(SelectedNodes);
                e.Handled = true;
            }
            else if (_selectedTitleBox != null)
            {
                TitleBoxes.Remove(_selectedTitleBox);
                _selectedTitleBox = null;
                Invalidate();
                WorkflowChanged?.Invoke();
                e.Handled = true;
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus(); // Ensure canvas captures keyboard focus for Delete/Ctrl+A keys

        _mouseDownLocation = e.Location;

        // 눈금자 영역(상단 42px) 클릭 시 마우스 좌우 드래그로 타임라인 간격 실시간 조절 시작
        if (ShowTimelineGuides && e.Button == MouseButtons.Left && e.Y <= 42)
        {
            Capture = true;
            _isDraggingTimelineRuler = true;
            _timelineDragStartMouse = e.Location;
            _timelineDragStartPixelsPerHour = PixelsPerHour;
            Cursor = Cursors.SizeWE;
            return;
        }

        // 1. Check if clicked the Empty Guide Close Buttons
        if (Nodes.Count == 0 && !_hideEmptyCanvasGuide &&
            (_emptyCloseButtonTopRect.Contains(e.Location) || _emptyCloseButtonBottomRect.Contains(e.Location)))
        {
            _hideEmptyCanvasGuide = true;
            Invalidate();
            return;
        }

        // 1-1. Check if clicked the Empty Guide Generate Button
        if (Nodes.Count == 0 && !_hideEmptyCanvasGuide && _emptyButtonScreenRect.Contains(e.Location))
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

            // 1. Check if clicking any anchor port (Left, Right, Top, Bottom) of any node to start edge creation
            foreach (var node in Nodes.AsEnumerable().Reverse())
            {
                if (IsPointNear(worldPt, node.OutputAnchor, 14f) ||
                    IsPointNear(worldPt, node.TopAnchor, 14f) ||
                    IsPointNear(worldPt, node.BottomAnchor, 14f) ||
                    IsPointNear(worldPt, node.InputAnchor, 14f))
                {
                    _connectingFromNode = node;
                    _currentConnectingPoint = worldPt;
                    Cursor = Cursors.Cross;
                    return;
                }
            }

            // 2. Check if clicking a node body or Issue Badge
            var hitNode = Nodes.AsEnumerable().Reverse().FirstOrDefault(n => n.Bounds.Contains(worldPt));
            if (hitNode != null)
            {
                // Check if clicked the Issue or Resolved Issue Badge inside node
                var issueBadgeRect = GetIssueBadgeBounds(hitNode);
                if (!issueBadgeRect.IsEmpty && issueBadgeRect.Contains(worldPt))
                {
                    SetSelectedNodes(new[] { hitNode });
                    IssueBadgeClicked?.Invoke(hitNode);
                    return;
                }
                bool isCtrlOrShift = (ModifierKeys & (Keys.Control | Keys.Shift)) != 0;

                if (isCtrlOrShift)
                {
                    // Toggle selection of this node
                    if (SelectedNodes.Contains(hitNode))
                    {
                        SelectedNodes.Remove(hitNode);
                        hitNode.IsSelected = false;
                        _selectedNode = SelectedNodes.FirstOrDefault();
                    }
                    else
                    {
                        SelectedNodes.Add(hitNode);
                        hitNode.IsSelected = true;
                        _selectedNode = hitNode;
                    }

                    if (_selectedEdge != null)
                    {
                        _selectedEdge.IsSelected = false;
                        _selectedEdge = null;
                        EdgeSelected?.Invoke(null);
                    }

                    Invalidate();
                    NodeSelected?.Invoke(_selectedNode);
                    SelectionChanged?.Invoke(SelectedNodes);
                    return;
                }

                // Normal click: if clicked node is not part of multi-selection, select only this node
                if (!SelectedNodes.Contains(hitNode))
                {
                    SetSelectedNodes(new[] { hitNode });
                }

                // Prepare multi-drag for all selected nodes
                _draggedNodes = SelectedNodes.ToList();
                _dragStartOffsets = _draggedNodes.ToDictionary(n => n, n => new PointF(worldPt.X - n.X, worldPt.Y - n.Y));
                return;
            }

            // 3. Check if clicking an edge (Hit test)
            var hitEdge = HitTestEdge(worldPt);
            if (hitEdge != null)
            {
                SelectedEdge = hitEdge;
                return;
            }

            // 3.5 Check TitleBoxes (Auto-fit button, Resize handle, or Body drag)
            foreach (var box in TitleBoxes.AsEnumerable().Reverse())
            {
                if (box.AutoFitButtonBounds.Contains(worldPt))
                {
                    var memberNodes = Nodes.Where(n => box.Bounds.Contains(n.Bounds)).ToList();
                    if (memberNodes.Count > 0)
                    {
                        box.AutoFit(memberNodes);
                        Invalidate();
                        WorkflowChanged?.Invoke();
                    }
                    return;
                }

                if (box.ResizeHandleBounds.Contains(worldPt))
                {
                    _resizingTitleBox = box;
                    _resizeStartMouse = worldPt;
                    _resizeStartSize = new SizeF(box.Width, box.Height);
                    Cursor = Cursors.SizeNWSE;
                    return;
                }

                if (box.HeaderBounds.Contains(worldPt) || (box.Bounds.Contains(worldPt) && !Nodes.Any(n => n.Bounds.Contains(worldPt))))
                {
                    _draggedTitleBox = box;
                    _dragStartOffsetTitleBox = new PointF(worldPt.X - box.X, worldPt.Y - box.Y);
                    _nodesInDraggedTitleBox = Nodes.Where(n => box.Bounds.Contains(n.Bounds)).ToList();

                    foreach (var b in TitleBoxes) b.IsSelected = (b == box);
                    _selectedTitleBox = box;
                    Invalidate();
                    return;
                }
            }

            foreach (var b in TitleBoxes) b.IsSelected = false;
            _selectedTitleBox = null;

            // 4. Clicked empty background: Start Box / Rubberband Drag Selection!
            bool keepOld = (ModifierKeys & (Keys.Control | Keys.Shift)) != 0;
            if (!keepOld)
            {
                SetSelectedNodes(Array.Empty<WorkflowNode>());
                SelectedEdge = null;
            }

            _isBoxSelecting = true;
            _boxSelectStartScreen = e.Location;
            _boxSelectCurrentScreen = e.Location;
            _initialSelectionBeforeBox = SelectedNodes.ToList();
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isDraggingTimelineRuler)
        {
            int deltaX = e.X - _timelineDragStartMouse.X;
            float newInterval = Math.Clamp(_timelineDragStartPixelsPerHour + (deltaX * 1.5f), 120f, 720f);
            if (Math.Abs(newInterval - PixelsPerHour) > 1f)
            {
                PixelsPerHour = newInterval;
                ArrangeByTimeline(autoFitBaseHour: false);
                TimelineIntervalChanged?.Invoke(PixelsPerHour);
                Invalidate();
            }
            return;
        }

        // 눈금자 영역에 마우스 올렸을 때 SizeWE(좌우 화살표) 커서 표시
        if (ShowTimelineGuides && e.Y <= 42 && !_isPanning && _draggedNodes.Count == 0 && !_isBoxSelecting)
        {
            Cursor = Cursors.SizeWE;
            return;
        }

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

        // Resizing TitleBox
        if (_resizingTitleBox != null)
        {
            float newW = Math.Max(200f, _resizeStartSize.Width + (worldPt.X - _resizeStartMouse.X));
            float newH = Math.Max(120f, _resizeStartSize.Height + (worldPt.Y - _resizeStartMouse.Y));
            _resizingTitleBox.Width = newW;
            _resizingTitleBox.Height = newH;
            Invalidate();
            return;
        }

        // Dragging TitleBox and enclosed nodes
        if (_draggedTitleBox != null)
        {
            float newX = worldPt.X - _dragStartOffsetTitleBox.X;
            float newY = worldPt.Y - _dragStartOffsetTitleBox.Y;
            float dx = newX - _draggedTitleBox.X;
            float dy = newY - _draggedTitleBox.Y;

            _draggedTitleBox.X = newX;
            _draggedTitleBox.Y = newY;

            foreach (var n in _nodesInDraggedTitleBox)
            {
                n.X += dx;
                n.Y += dy;
            }

            Invalidate();
            return;
        }

        // Marquee Box Drag Selection in progress
        if (_isBoxSelecting)
        {
            _boxSelectCurrentScreen = e.Location;

            float sx = Math.Min(_boxSelectStartScreen.X, _boxSelectCurrentScreen.X);
            float sy = Math.Min(_boxSelectStartScreen.Y, _boxSelectCurrentScreen.Y);
            float sw = Math.Abs(_boxSelectStartScreen.X - _boxSelectCurrentScreen.X);
            float sh = Math.Abs(_boxSelectStartScreen.Y - _boxSelectCurrentScreen.Y);

            PointF p1 = ScreenToWorld(new PointF(sx, sy));
            PointF p2 = ScreenToWorld(new PointF(sx + sw, sy + sh));
            var worldBox = new RectangleF(p1.X, p1.Y, p2.X - p1.X, p2.Y - p1.Y);

            bool keepOld = (ModifierKeys & (Keys.Control | Keys.Shift)) != 0;
            var currentSet = keepOld ? new HashSet<WorkflowNode>(_initialSelectionBeforeBox) : new HashSet<WorkflowNode>();

            foreach (var node in Nodes)
            {
                if (node.Bounds.IntersectsWith(worldBox))
                {
                    currentSet.Add(node);
                }
            }

            foreach (var node in Nodes)
            {
                node.IsSelected = currentSet.Contains(node);
            }

            SelectedNodes.Clear();
            SelectedNodes.AddRange(currentSet);
            _selectedNode = SelectedNodes.FirstOrDefault();

            Invalidate();
            return;
        }

        // Moving multiple selected nodes
        if (_draggedNodes.Count > 0)
        {
            foreach (var n in _draggedNodes)
            {
                if (_dragStartOffsets.TryGetValue(n, out var offset))
                {
                    n.X = worldPt.X - offset.X;
                    n.Y = worldPt.Y - offset.Y;
                }
            }
            Invalidate();
            return;
        }

        // Hover cursor check
        if (_selectedEdge != null && _selectedEdgeCutButtonScreenRect.Contains(e.Location))
        {
            Cursor = Cursors.Hand;
            return;
        }

        bool nearPort = Nodes.Any(n => 
            IsPointNear(worldPt, n.OutputAnchor, 12f) || 
            IsPointNear(worldPt, n.InputAnchor, 12f) ||
            IsPointNear(worldPt, n.TopAnchor, 12f) ||
            IsPointNear(worldPt, n.BottomAnchor, 12f));
        bool overIssueBadge = Nodes.Any(n => {
            var b = GetIssueBadgeBounds(n);
            return !b.IsEmpty && b.Contains(worldPt);
        });

        bool overResize = TitleBoxes.Any(b => b.ResizeHandleBounds.Contains(worldPt));
        bool overAutoFit = TitleBoxes.Any(b => b.AutoFitButtonBounds.Contains(worldPt));
        bool overHeader = TitleBoxes.Any(b => b.HeaderBounds.Contains(worldPt));

        if (overResize) Cursor = Cursors.SizeNWSE;
        else if (overAutoFit || overIssueBadge) Cursor = Cursors.Hand;
        else if (overHeader) Cursor = Cursors.SizeAll;
        else if (nearPort) Cursor = Cursors.Cross;
        else if (Nodes.Count == 0 && !_hideEmptyCanvasGuide && 
                 (_emptyButtonScreenRect.Contains(e.Location) || _emptyCloseButtonTopRect.Contains(e.Location) || _emptyCloseButtonBottomRect.Contains(e.Location)))
        {
            Cursor = Cursors.Hand;
        }
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
            if (_isDraggingTimelineRuler)
            {
                _isDraggingTimelineRuler = false;
                Capture = false;
                Cursor = Cursors.Default;
                WorkflowChanged?.Invoke();
                return;
            }

            if (_connectingFromNode != null)
            {
                var worldPt = ScreenToWorld(e.Location);
                var targetNode = Nodes.FirstOrDefault(n => 
                    IsPointNear(worldPt, n.InputAnchor, 16f) || 
                    IsPointNear(worldPt, n.TopAnchor, 16f) || 
                    IsPointNear(worldPt, n.BottomAnchor, 16f) || 
                    IsPointNear(worldPt, n.OutputAnchor, 16f) || 
                    n.Bounds.Contains(worldPt));
                
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

            if (_isBoxSelecting)
            {
                _isBoxSelecting = false;
                Invalidate();
                NodeSelected?.Invoke(_selectedNode);
                SelectionChanged?.Invoke(SelectedNodes);
            }

            if (_draggedNodes.Count > 0)
            {
                _draggedNodes.Clear();
                _dragStartOffsets.Clear();
                WorkflowChanged?.Invoke();
            }

            if (_resizingTitleBox != null || _draggedTitleBox != null)
            {
                _resizingTitleBox = null;
                _draggedTitleBox = null;
                _nodesInDraggedTitleBox.Clear();
                Cursor = Cursors.Default;
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

        if (SelectedNodes.Count > 1 && (hitNode == null || SelectedNodes.Contains(hitNode)))
        {
            var itemMultiHeader = menu.Items.Add($"⚡ 선택된 {SelectedNodes.Count}개 노드 일괄 관리");
            itemMultiHeader.Enabled = false;
            itemMultiHeader.ForeColor = Color.FromArgb(56, 189, 248);

            menu.Items.Add(new ToolStripSeparator());

            var itemCutAllMulti = menu.Items.Add($"✂ 선택 노드들 연결선 모두 끊기");
            itemCutAllMulti.ForeColor = Color.FromArgb(254, 240, 138); // Yellow highlight
            itemCutAllMulti.Click += (s, ev) =>
            {
                foreach (var n in SelectedNodes.ToList())
                    RemoveEdgesForNode(n, incoming: true, outgoing: true);
            };

            var itemDelMulti = menu.Items.Add($"🗑 선택된 {SelectedNodes.Count}개 노드 일괄 삭제 (Del)");
            itemDelMulti.ForeColor = Color.FromArgb(252, 165, 165); // Red highlight
            itemDelMulti.Click += (s, ev) =>
            {
                RemoveNodes(SelectedNodes);
            };

            menu.Items.Add(new ToolStripSeparator());

            var itemDeselect = menu.Items.Add("✕ 선택 해제 (Esc)");
            itemDeselect.ForeColor = Color.White;
            itemDeselect.Click += (s, ev) => ClearSelection();
        }
        else if (hitEdge != null)
        {
            SelectedEdge = hitEdge;
            var itemCut = menu.Items.Add("✂ 이 연결선 끊기 (Delete)");
            itemCut.ForeColor = Color.FromArgb(254, 202, 202); // Soft red
            itemCut.Click += (s, ev) => RemoveEdge(hitEdge);
        }
        else if (hitNode != null)
        {
            SelectedNode = hitNode;

            var itemEditNode = menu.Items.Add("⚙ 공정 정보 수정 (더블클릭)");
            itemEditNode.ForeColor = Color.FromArgb(56, 189, 248); // Sky blue
            itemEditNode.Click += (s, ev) => NodeDoubleClicked?.Invoke(hitNode);

            menu.Items.Add(new ToolStripSeparator());
            
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
            var itemSelectAll = menu.Items.Add("🔲 전체 노드 선택 (Ctrl+A)");
            itemSelectAll.ForeColor = Color.FromArgb(56, 189, 248);
            itemSelectAll.Click += (s, ev) => SelectAllNodes();

            menu.Items.Add(new ToolStripSeparator());

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

            var (p1, c1, c2, p2) = GetEdgeCurvePoints(fromNode, toNode);

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
        // 눈금자 영역(상단 40px) 또는 Ctrl+Shift 마우스 휠 스크롤 시 타임라인 간격 조절
        if (ShowTimelineGuides && (e.Y <= 40 || (ModifierKeys & (Keys.Control | Keys.Shift)) == (Keys.Control | Keys.Shift)))
        {
            float deltaPx = e.Delta > 0 ? 30f : -30f;
            SetTimelineInterval(PixelsPerHour + deltaPx);
            return;
        }

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
        var worldPt = ScreenToWorld(e.Location);
        if (_selectedNode != null && _selectedNode.Bounds.Contains(worldPt))
        {
            NodeDoubleClicked?.Invoke(_selectedNode);
            return;
        }

        var hitBox = TitleBoxes.AsEnumerable().Reverse().FirstOrDefault(b => b.HeaderBounds.Contains(worldPt) || b.Bounds.Contains(worldPt));
        if (hitBox != null)
        {
            TitleBoxDoubleClicked?.Invoke(hitBox);
            return;
        }
    }

    private PointF ScreenToWorld(Point screenPt) => ScreenToWorld(new PointF(screenPt.X, screenPt.Y));

    private PointF ScreenToWorld(PointF screenPt)
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
