﻿﻿﻿﻿﻿﻿﻿using System.Drawing.Drawing2D;
using System.Text.Json;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client.Workflow;

public class WorkflowMainForm : Form
{
    private static class Palette
    {
        public static readonly Color Bg950 = Color.FromArgb(7, 11, 20);
        public static readonly Color Bg900 = Color.FromArgb(11, 19, 35);
        public static readonly Color Bg850 = Color.FromArgb(15, 27, 49);
        public static readonly Color Bg800 = Color.FromArgb(20, 35, 64);
        public static readonly Color Border = Color.FromArgb(29, 46, 78);
        public static readonly Color BorderLight = Color.FromArgb(42, 67, 111);
        public static readonly Color AccentSky = Color.FromArgb(14, 165, 233);
        public static readonly Color Green = Color.FromArgb(16, 185, 129);
        public static readonly Color Amber = Color.FromArgb(245, 158, 11);
        public static readonly Color Rose = Color.FromArgb(239, 68, 68);
        public static readonly Color TextWhite = Color.FromArgb(248, 250, 252);
        public static readonly Color TextSlate300 = Color.FromArgb(203, 213, 225);
        public static readonly Color TextSlate400 = Color.FromArgb(148, 163, 184);
    }

    private readonly ApiClient _api;
    private readonly UserSession _session;

    // 1. Top Bar Controls
    private Label _lblLiveClock = null!;
    private Label _lblActiveMachineSummary = null!;
    private Label _lblWorkflowStats = null!;
    private Button _btnSwitchToClassic = null!;
    private Button _btnRefreshAll = null!;

    // 2. Left Sidebar (Menu + Machine Cards + Work Orders & Parts)
    private ComboBox _cboOrders = null!;
    private ListBox _lstParts = null!;
    private FlowLayoutPanel _pnlLeftMachineCards = null!;
    private readonly HashSet<string> _mySelectedMachines = new();
    private string _currentMachineId = "R01";
    private List<JsonElement> _cachedMachines = new();
    private List<JsonElement> _cachedOrders = new();
    private List<JsonElement> _cachedParts = new();

    // 5. Central Workflow Canvas & Toolbar
    private WorkflowCanvasControl _canvas = null!;

    // 9. Right Selected Node Detail Panel
    private NodeDetailPanel _detailPanel = null!;

    // 6. Right Top Notice Panel
    private ListBox _lstNotices = null!;
    private Label _lblNoticeBadge = null!;

    // 7. Right Bottom Chat Panel
    private ComboBox _cboChatChannel = null!;
    private ListBox _lstChat = null!;
    private TextBox _txtChatInput = null!;
    private Button _btnSendChat = null!;
    private int _lastSeenChatId = -1;

    // 8. Bottom Foldable Tabbed History Pane
    private TabControl _bottomTabs = null!;
    private DataGridView _gridHistory = null!;
    private DataGridView _gridDefects = null!;
    private DataGridView _gridSubTasks = null!;
    private DataGridView _gridPostProcess = null!;
    private SplitContainer _centerVerticalSplitter = null!;

    // Polling Timer
    private System.Windows.Forms.Timer _pollTimer = null!;

    public WorkflowMainForm(ApiClient api, UserSession session)
    {
        _api = api;
        _session = session;

        if (session.SelectedMachines != null && session.SelectedMachines.Count > 0)
        {
            foreach (var m in session.SelectedMachines) _mySelectedMachines.Add(m);
            _currentMachineId = session.SelectedMachines[0];
        }
        else
        {
            _mySelectedMachines.Add("R01");
            _currentMachineId = "R01";
        }

        InitializeComponents();
        Shown += async (s, e) => await InitializeDataAsync();
    }

    private void InitializeComponents()
    {
        Text = $"[CNC-MES 워크플로우 스마트 작업자 터미널 v1.3.7] 작업자: {_session.DisplayName} ({_api.Config.ServerUrl})";
        Size = new Size(1600, 980);
        MinimumSize = new Size(1280, 800);
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Palette.Bg950;
        ForeColor = Palette.TextWhite;
        Font = new Font("맑은 고딕", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;

        // Root Table: Row 0 (Top Summary Bar 54px) | Row 1 (Main Workspace Fill)
        var rootTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        rootTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rootTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
        rootTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // ---------------------------------------------------------------------
        // 1. Top Summary Bar (Global Navigation & Live Stats)
        // ---------------------------------------------------------------------
        rootTable.Controls.Add(BuildTopSummaryBar(), 0, 0);

        // ---------------------------------------------------------------------
        // 3-Column Main Workspace (Left 280px | Center Fill | Right 360px)
        // ---------------------------------------------------------------------
        var mainContainer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(8, 6, 8, 6),
            Margin = new Padding(0),
            BackColor = Palette.Bg950
        };
        mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 305f)); // Left Sidebar
        mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // Center Workspace
        mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360f)); // Right Sidebar
        mainContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        mainContainer.Controls.Add(BuildLeftSidebar(), 0, 0);
        mainContainer.Controls.Add(BuildCenterWorkspace(), 1, 0);
        mainContainer.Controls.Add(BuildRightSidebar(), 2, 0);

        rootTable.Controls.Add(mainContainer, 0, 1);
        Controls.Add(rootTable);

        // Polling Timer (3.5s)
        _pollTimer = new System.Windows.Forms.Timer { Interval = 3500 };
        _pollTimer.Tick += async (s, e) => await PollRealtimeDataAsync();
    }

    // =========================================================================
    // 1. TOP SUMMARY BAR
    // =========================================================================
    private Control BuildTopSummaryBar()
    {
        var topBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Palette.Bg900,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0)
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340f)); // Brand & Logo
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // Center Workflow Live Stats
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 480f)); // Actions & View Switch

        topBar.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.DrawLine(pen, 0, topBar.Height - 1, topBar.Width, topBar.Height - 1);
        };

        // Left Brand
        var pnlBrand = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var pnlIcon = new Panel { Location = new Point(0, 9), Size = new Size(36, 36), BackColor = Color.FromArgb(40, 14, 165, 233) };
        pnlIcon.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(120, 14, 165, 233), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, 35, 35);
            using var font = new Font("Segoe UI Symbol", 12f, FontStyle.Bold);
            using var brush = new SolidBrush(Palette.AccentSky);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString("⚡", font, brush, new RectangleF(0, 0, 36, 36), sf);
        };
        pnlBrand.Controls.Add(pnlIcon);

        var lblBrand = new Label { Text = "CNC-MES", Location = new Point(44, 9), Font = new Font("맑은 고딕", 12f, FontStyle.Bold), ForeColor = Color.White, AutoSize = true };
        var lblVer = new Label { Text = "v1.3.7 [Workflow]", Location = new Point(136, 12), Font = new Font("Consolas", 8f, FontStyle.Bold), ForeColor = Palette.AccentSky, BackColor = Color.FromArgb(80, 8, 47, 73), AutoSize = true, Padding = new Padding(4, 1, 4, 1) };
        var lblStatus = new Label { Text = "● 서버 정상", Location = new Point(245, 12), Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.Green, BackColor = Color.FromArgb(80, 6, 78, 59), AutoSize = true, Padding = new Padding(5, 1, 5, 1) };
        var lblSub = new Label { Text = "WORKFLOW-BASED OPERATOR CONTROL SYSTEM", Location = new Point(45, 31), Font = new Font("Consolas", 7.2f), ForeColor = Palette.TextSlate400, AutoSize = true };
        pnlBrand.Controls.AddRange(new Control[] { lblBrand, lblVer, lblStatus, lblSub });
        topBar.Controls.Add(pnlBrand, 0, 0);

        // Center Stats Box
        var pnlCenter = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var pnlCenterBox = new Panel { Size = new Size(740, 36), BackColor = Palette.Bg850 };
        pnlCenterBox.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.BorderLight, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlCenterBox.Width - 1, pnlCenterBox.Height - 1);
        };
        pnlCenter.Resize += (s, e) =>
        {
            pnlCenterBox.Location = new Point(
                Math.Max(0, (pnlCenter.Width - pnlCenterBox.Width) / 2),
                Math.Max(0, (pnlCenter.Height - pnlCenterBox.Height) / 2)
            );
        };

        var flowCenter = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 4, 10, 4) };
        var lblMachPrompt = new Label { Text = "조작 장비:", ForeColor = Palette.TextSlate400, AutoSize = true, Margin = new Padding(0, 4, 4, 0) };
        _lblActiveMachineSummary = new Label { Text = $"[⚙ {_currentMachineId}]", Font = new Font("Consolas", 9.8f, FontStyle.Bold), ForeColor = Palette.Green, BackColor = Color.FromArgb(80, 6, 78, 59), AutoSize = true, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(0, 1, 10, 0) };
        var div1 = new Label { Text = "|", ForeColor = Palette.BorderLight, AutoSize = true, Margin = new Padding(0, 3, 10, 0) };
        _lblWorkflowStats = new Label { Text = "워크플로우: 가공중 0건 | 대기 0건 | 완료 0건", Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold), ForeColor = Palette.TextSlate300, AutoSize = true, Margin = new Padding(0, 4, 10, 0) };
        var div2 = new Label { Text = "|", ForeColor = Palette.BorderLight, AutoSize = true, Margin = new Padding(0, 3, 10, 0) };
        _lblLiveClock = new Label { Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Font = new Font("Consolas", 8.8f), ForeColor = Palette.TextSlate400, AutoSize = true, Margin = new Padding(0, 5, 0, 0) };

        flowCenter.Controls.AddRange(new Control[] { lblMachPrompt, _lblActiveMachineSummary, div1, _lblWorkflowStats, div2, _lblLiveClock });
        pnlCenterBox.Controls.Add(flowCenter);
        pnlCenter.Controls.Add(pnlCenterBox);
        topBar.Controls.Add(pnlCenter, 1, 0);

        // Right Actions (View Switcher & User Profile)
        var pnlRight = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 11, 0, 0) };

        _btnRefreshAll = CreateFlatButton("🔄", Palette.Bg800, Palette.TextSlate300, 32, 32);
        _btnRefreshAll.Click += async (s, e) => await RefreshAllAsync();

        var pnlUser = new Panel { Size = new Size(140, 32), BackColor = Palette.Bg800, Margin = new Padding(4, 0, 0, 0) };
        pnlUser.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlUser.Width - 1, pnlUser.Height - 1);
            using var brushBg = new SolidBrush(Color.FromArgb(40, 14, 165, 233));
            e.Graphics.FillRectangle(brushBg, 4, 4, 24, 24);
            using var fontA = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
            using var brushSky = new SolidBrush(Palette.AccentSky);
            e.Graphics.DrawString("a", fontA, brushSky, new PointF(11, 7));
            using var fontTxt = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
            using var brushTxt = new SolidBrush(Palette.TextSlate300);
            e.Graphics.DrawString($"작업자 [{_session.DisplayName}]", fontTxt, brushTxt, new PointF(32, 7));
        };

        _btnSwitchToClassic = CreateFlatButton("🔀 클래식 터미널 뷰", Color.FromArgb(2, 132, 199), Color.White, 145, 32);
        _btnSwitchToClassic.Click += (s, e) => SwitchToClassicView();

        pnlRight.Controls.AddRange(new Control[] { _btnRefreshAll, pnlUser, _btnSwitchToClassic });
        topBar.Controls.Add(pnlRight, 2, 0);

        return topBar;
    }

    // =========================================================================
    // 2. LEFT SIDEBAR: Menu, Machines, Work Orders & Parts
    // =========================================================================
    private Control BuildLeftSidebar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 135f)); // 2: Workflow Actions
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 155f)); // 3: My Machines Cards
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 4: Orders & Multi-parts list

        // Card 1: Workflow Menu Buttons
        var cardMenu = CreateCard("📋 워크플로우 제어 메뉴");
        var menuGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
        menuGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        menuGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var btnAutoPlan = CreateFlatButton("⚡표준공정", Palette.Bg800, Palette.AccentSky);
        btnAutoPlan.Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold);
        btnAutoPlan.Margin = new Padding(2);
        btnAutoPlan.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
        btnAutoPlan.Click += (s, e) => GenerateStandardWorkflowForSelectedPart();

        var btnClonePlan = CreateFlatButton("📋흐름복제", Palette.Bg800, Color.FromArgb(168, 85, 247));
        btnClonePlan.Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold);
        btnClonePlan.Margin = new Padding(2);
        btnClonePlan.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
        btnClonePlan.Click += (s, e) => DuplicateSelectedNodeOrWorkflow();

        var btnAutoLayout = CreateFlatButton("⟲자동배치", Palette.Bg800, Palette.Green);
        btnAutoLayout.Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold);
        btnAutoLayout.Margin = new Padding(2);
        btnAutoLayout.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
        btnAutoLayout.Click += (s, e) => _canvas.AutoLayout();

        var btnClearPlan = CreateFlatButton("🗑초기화", Palette.Bg800, Palette.Rose);
        btnClearPlan.Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold);
        btnClearPlan.Margin = new Padding(2);
        btnClearPlan.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
        btnClearPlan.Click += (s, e) =>
        {
            if (MessageBox.Show("현재 캔버스의 모든 노드를 초기화하시겠습니까?", "초기화 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                _canvas.Clear();
        };

        menuGrid.Controls.Add(btnAutoPlan, 0, 0);
        menuGrid.Controls.Add(btnClonePlan, 1, 0);
        menuGrid.Controls.Add(btnAutoLayout, 0, 1);
        menuGrid.Controls.Add(btnClearPlan, 1, 1);
        cardMenu.Controls.Add(menuGrid);
        panel.Controls.Add(cardMenu, 0, 0);

        // Card 2: My Machines Quick Cards
        var cardMach = CreateCard("🎛 내 담당 장비 실시간 현황");
        _pnlLeftMachineCards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown };
        cardMach.Controls.Add(_pnlLeftMachineCards);
        panel.Controls.Add(cardMach, 0, 1);

        // Card 3: Work Orders & Parts List
        var cardOrders = CreateCard("📦 작업지시서 및 대상 부품 목록");
        var pnlOrderBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        pnlOrderBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlOrderBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // Order combo
        pnlOrderBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Parts list
        pnlOrderBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f)); // Apply to canvas button

        _cboOrders = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Bg850, ForeColor = Color.White };
        _cboOrders.SelectedIndexChanged += async (s, e) => await OnOrderSelectedAsync();
        pnlOrderBody.Controls.Add(_cboOrders, 0, 0);

        _lstParts = new ListBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Palette.TextWhite, BorderStyle = BorderStyle.FixedSingle };
        pnlOrderBody.Controls.Add(_lstParts, 0, 1);

        var btnLoadToCanvas = CreateFlatButton("🎯 부품별 공정생성", Palette.Green, Color.White);
        btnLoadToCanvas.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnLoadToCanvas.Click += (s, e) => GenerateStandardWorkflowForSelectedPart();
        pnlOrderBody.Controls.Add(btnLoadToCanvas, 0, 2);

        cardOrders.Controls.Add(pnlOrderBody);
        panel.Controls.Add(cardOrders, 0, 2);

        return panel;
    }

    // =========================================================================
    // 5 & 8. CENTER WORKSPACE: Workflow Canvas + Foldable Bottom History
    // =========================================================================
    private Control BuildCenterWorkspace()
    {
        _centerVerticalSplitter = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 640,
            SplitterWidth = 6,
            BackColor = Palette.Border,
            Margin = new Padding(0)
        };

        // Top: Canvas & Palette Toolbar
        var pnlCanvasContainer = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg900 };
        
        // Canvas Toolbar (Top 36px)
        var canvasToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Palette.Bg900,
            Padding = new Padding(8, 3, 8, 3),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        canvasToolbar.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.DrawLine(pen, 0, canvasToolbar.Height - 1, canvasToolbar.Width, canvasToolbar.Height - 1);
        };

        var btnAddPrep = CreateFlatButton("+준비노드", Palette.Bg800, Color.FromArgb(56, 189, 248), 85, 28);
        btnAddPrep.Click += (s, e) => AddQuickNode(NodeCategory.Preparation, "소재 세팅 & 도면확인");

        var btnAddMach = CreateFlatButton("+가공노드", Palette.Bg800, Palette.Green, 85, 28);
        btnAddMach.Click += (s, e) => AddQuickNode(NodeCategory.Machining, "1차 황삭 가공");

        var btnAddInsp = CreateFlatButton("+검사노드", Palette.Bg800, Palette.Amber, 85, 28);
        btnAddInsp.Click += (s, e) => AddQuickNode(NodeCategory.Inspection, "치수 중간측정");

        var btnAddSub = CreateFlatButton("+서브인원", Palette.Bg800, Color.FromArgb(168, 85, 247), 85, 28);
        btnAddSub.Click += (s, e) => AddQuickNode(NodeCategory.Collaboration, "서브: 베이스교체 & 칩청소");

        var btnAddPost = CreateFlatButton("+후공정", Palette.Bg800, Color.FromArgb(99, 102, 241), 80, 28);
        btnAddPost.Click += (s, e) => AddQuickNode(NodeCategory.PostProcess, "세척 & 레이저 각인");

        var btnAddIssue = CreateFlatButton("+이슈등록", Palette.Bg800, Palette.Rose, 85, 28);
        btnAddIssue.Click += (s, e) => AddQuickNode(NodeCategory.Issue, "공구 마모 / 치수 편차");

        var divA = new Label { Text = "|", ForeColor = Palette.Border, AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
        var btnResetZoom = CreateFlatButton("🔍 100%", Palette.Bg800, Palette.TextSlate400, 65, 28);
        btnResetZoom.Click += (s, e) => _canvas.ResetView();

        var btnToggleBottom = CreateFlatButton("📂 하단 이력 접기/펼치기", Palette.Bg800, Palette.AccentSky, 155, 28);
        btnToggleBottom.Click += (s, e) =>
        {
            _centerVerticalSplitter.Panel2Collapsed = !_centerVerticalSplitter.Panel2Collapsed;
        };

        canvasToolbar.Controls.AddRange(new Control[] {
            btnAddPrep, btnAddMach, btnAddInsp, btnAddSub, btnAddPost, btnAddIssue,
            divA, btnResetZoom, btnToggleBottom
        });

        // The Canvas
        _canvas = new WorkflowCanvasControl { Dock = DockStyle.Fill };
        _canvas.NodeSelected += (node) => _detailPanel.BindNode(node);
        _canvas.WorkflowChanged += UpdateWorkflowStats;

        pnlCanvasContainer.Controls.Add(_canvas);
        pnlCanvasContainer.Controls.Add(canvasToolbar);
        _centerVerticalSplitter.Panel1.Controls.Add(pnlCanvasContainer);

        // Bottom: Foldable Tabbed History Pane (8)
        _centerVerticalSplitter.Panel2.Controls.Add(BuildBottomHistoryTabs());

        return _centerVerticalSplitter;
    }

    private Control BuildBottomHistoryTabs()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg900 };

        _bottomTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("맑은 고딕", 9f, FontStyle.Bold)
        };

        // Tab 1: Machine Work History
        var tabHistory = new TabPage("⏱ 장비 최근 작업 이력 (더블클릭 시 수정)") { BackColor = Palette.Bg900, Padding = new Padding(4) };
        _gridHistory = CreateStyledGrid();
        _gridHistory.Columns.Add("time", "시각");
        _gridHistory.Columns.Add("machine", "장비");
        _gridHistory.Columns.Add("jobNo", "작지번호");
        _gridHistory.Columns.Add("partName", "품목(품명)");
        _gridHistory.Columns.Add("desc", "세부 작업내용");
        _gridHistory.Columns.Add("worker", "작업자");
        _gridHistory.Columns.Add("status", "상태");
        tabHistory.Controls.Add(_gridHistory);

        // Tab 2: Defect History
        var tabDefects = new TabPage("💥 불량·수정·대책 히스토리") { BackColor = Palette.Bg900, Padding = new Padding(4) };
        _gridDefects = CreateStyledGrid();
        _gridDefects.Columns.Add("id", "No");
        _gridDefects.Columns.Add("machine", "장비");
        _gridDefects.Columns.Add("part", "작지/품목");
        _gridDefects.Columns.Add("type", "유형");
        _gridDefects.Columns.Add("desc", "불량 내용");
        _gridDefects.Columns.Add("cause", "발생 원인");
        _gridDefects.Columns.Add("counter", "조치 및 대책");
        _gridDefects.Columns.Add("author", "작성자/일시");
        tabDefects.Controls.Add(_gridDefects);

        // Tab 3: Sub Worker Tasks
        var tabSub = new TabPage("👥 서브인원 작업 지시 및 보고") { BackColor = Palette.Bg900, Padding = new Padding(4) };
        _gridSubTasks = CreateStyledGrid();
        _gridSubTasks.Columns.Add("id", "No");
        _gridSubTasks.Columns.Add("machine", "장비");
        _gridSubTasks.Columns.Add("sub", "서브인원");
        _gridSubTasks.Columns.Add("task", "지시 내용");
        _gridSubTasks.Columns.Add("report", "보고 내용");
        _gridSubTasks.Columns.Add("status", "상태");
        tabSub.Controls.Add(_gridSubTasks);

        // Tab 4: Post Process
        var tabPost = new TabPage("⚙ 후공정(핀/각인/후처리) 추적") { BackColor = Palette.Bg900, Padding = new Padding(4) };
        _gridPostProcess = CreateStyledGrid();
        _gridPostProcess.Columns.Add("id", "No");
        _gridPostProcess.Columns.Add("order", "작지번호");
        _gridPostProcess.Columns.Add("part", "부품명");
        _gridPostProcess.Columns.Add("step", "공정단계");
        _gridPostProcess.Columns.Add("status", "진행상태");
        _gridPostProcess.Columns.Add("worker", "담당자");
        tabPost.Controls.Add(_gridPostProcess);

        _bottomTabs.TabPages.AddRange(new[] { tabHistory, tabDefects, tabSub, tabPost });
        panel.Controls.Add(_bottomTabs);
        return panel;
    }

    // =========================================================================
    // 6, 7, 9. RIGHT SIDEBAR: Node Details, Notices, Chat
    // =========================================================================
    private Control BuildRightSidebar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(6, 0, 0, 0),
            Padding = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 9: Node Details Panel
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 160f)); // 6: Notices
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 240f)); // 7: Realtime Chat

        // 9: Node Details Panel
        _detailPanel = new NodeDetailPanel { Dock = DockStyle.Fill };
        _detailPanel.StatusChangeRequested += async (node, newStatus) => await HandleNodeStatusChangeAsync(node, newStatus);
        _detailPanel.NodeUpdated += (node) => { _canvas.Invalidate(); UpdateWorkflowStats(); };
        _detailPanel.NodeDeleteRequested += (node) => _canvas.RemoveNode(node);
        _detailPanel.NodeDuplicateRequested += (node) => DuplicateSelectedNode(node);
        panel.Controls.Add(_detailPanel, 0, 0);

        // 6: Notices Card
        var cardNotice = CreateCard("📢 공지사항 및 작업지시서 알림");
        var pnlNoticeHeader = new Panel { Dock = DockStyle.Top, Height = 24 };
        _lblNoticeBadge = new Label { Text = "0건 미확인", Dock = DockStyle.Right, AutoSize = true, ForeColor = Palette.Amber, Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold) };
        var btnOpenNotices = CreateFlatButton("목록 상세열기", Palette.Bg800, Palette.AccentSky, 90, 20);
        btnOpenNotices.Dock = DockStyle.Left;
        btnOpenNotices.Click += async (s, e) => await ShowNoticesDialogAsync();
        pnlNoticeHeader.Controls.AddRange(new Control[] { btnOpenNotices, _lblNoticeBadge });

        _lstNotices = new ListBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Palette.TextSlate300, BorderStyle = BorderStyle.FixedSingle };
        _lstNotices.DoubleClick += async (s, e) => await ShowNoticesDialogAsync();

        cardNotice.Controls.Add(_lstNotices);
        cardNotice.Controls.Add(pnlNoticeHeader);
        panel.Controls.Add(cardNotice, 0, 1);

        // 7: Realtime Chat Card
        var cardChat = CreateCard("💬 현장 실시간 소통 채팅");
        var chatLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        chatLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        chatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Channel
        chatLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Messages
        chatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Input + Send

        _cboChatChannel = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Bg850, ForeColor = Color.White };
        _cboChatChannel.Items.AddRange(new object[] { "🌐 전체 공통 채널", "라인 A (R01~R05)", "라인 B (K01~K02)", "품질관리실 / 측정실" });
        _cboChatChannel.SelectedIndex = 0;
        chatLayout.Controls.Add(_cboChatChannel, 0, 0);

        _lstChat = new ListBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Palette.TextSlate300, BorderStyle = BorderStyle.FixedSingle };
        chatLayout.Controls.Add(_lstChat, 0, 1);

        var pnlSend = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        pnlSend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlSend.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65f));

        _txtChatInput = new TextBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        _txtChatInput.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SendChatAsync(); } };

        _btnSendChat = CreateFlatButton("전송 ✈", Palette.AccentSky, Color.White);
        _btnSendChat.Click += async (s, e) => await SendChatAsync();

        pnlSend.Controls.Add(_txtChatInput, 0, 0);
        pnlSend.Controls.Add(_btnSendChat, 1, 0);
        chatLayout.Controls.Add(pnlSend, 0, 2);

        cardChat.Controls.Add(chatLayout);
        panel.Controls.Add(cardChat, 0, 2);

        return panel;
    }

    // =========================================================================
    // DATA & EVENT LOGIC
    // =========================================================================
    private async Task InitializeDataAsync()
    {
        await LoadMachinesAsync();
        await LoadWorkOrdersAsync();
        await LoadNoticesAsync();
        await LoadChatAsync();
        await LoadHistoryGridsAsync();

        // 1-second live clock
        var liveTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        liveTimer.Tick += (s, e) => _lblLiveClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        liveTimer.Start();

        _pollTimer.Start();
    }

    private async Task PollRealtimeDataAsync()
    {
        try
        {
            await LoadMachinesAsync();
            await LoadNoticesAsync();
            await LoadChatAsync();
            await LoadHistoryGridsAsync();
        }
        catch { }
    }

    private async Task RefreshAllAsync()
    {
        await LoadMachinesAsync();
        await LoadWorkOrdersAsync();
        await LoadNoticesAsync();
        await LoadChatAsync();
        await LoadHistoryGridsAsync();
    }

    private async Task LoadMachinesAsync()
    {
        try
        {
            var list = await _api.GetMachinesAsync();
            if (list != null)
            {
                _cachedMachines = list;
                RenderLeftMachineCards();
            }
        }
        catch { }
    }

    private void RenderLeftMachineCards()
    {
        _pnlLeftMachineCards.SuspendLayout();
        _pnlLeftMachineCards.Controls.Clear();

        foreach (var m in _cachedMachines)
        {
            string id = m.GetProperty("machineId").GetString() ?? "";
            string status = m.GetProperty("status").GetString() ?? "대기";
            string jobNo = m.TryGetProperty("activeJobNumber", out var jn) && jn.ValueKind == JsonValueKind.String ? jn.GetString() : "-";

            bool isFocused = id == _currentMachineId;
            bool isMy = _mySelectedMachines.Contains(id);

            var card = new Panel
            {
                Size = new Size(250, 42),
                BackColor = isFocused ? Color.FromArgb(40, 6, 78, 59) : (isMy ? Palette.Bg800 : Palette.Bg850),
                Margin = new Padding(0, 2, 0, 4),
                Cursor = Cursors.Hand
            };

            card.Paint += (s, e) =>
            {
                Color bClr = isFocused ? Palette.Green : (isMy ? Palette.Amber : Palette.Border);
                using var pen = new Pen(bClr, isFocused ? 2f : 1f);
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            var lblTitle = new Label
            {
                Text = $"{id} ({status})",
                Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold),
                ForeColor = isFocused ? Palette.Green : (isMy ? Palette.Amber : Palette.TextWhite),
                Location = new Point(6, 4),
                AutoSize = true
            };

            var lblJob = new Label
            {
                Text = jobNo == "-" ? "할당 작지 없음" : jobNo,
                Font = new Font("Consolas", 7.5f),
                ForeColor = Palette.TextSlate400,
                Location = new Point(6, 22),
                Width = 235,
                AutoEllipsis = true
            };

            var clickHandler = new EventHandler((s, e) =>
            {
                _currentMachineId = id;
                _lblActiveMachineSummary.Text = $"[⚙ {_currentMachineId}]";
                RenderLeftMachineCards();
            });

            card.Click += clickHandler;
            lblTitle.Click += clickHandler;
            lblJob.Click += clickHandler;

            card.Controls.AddRange(new Control[] { lblTitle, lblJob });
            _pnlLeftMachineCards.Controls.Add(card);
        }

        _pnlLeftMachineCards.ResumeLayout();
    }

    private async Task LoadWorkOrdersAsync()
    {
        try
        {
            var list = await _api.GetWorkOrdersAsync();
            if (list != null)
            {
                _cachedOrders = list;
                _cboOrders.Items.Clear();
                foreach (var o in _cachedOrders)
                {
                    string orderNo = o.GetProperty("orderNumber").GetString() ?? "";
                    string customer = o.TryGetProperty("customerName", out var cn) ? cn.GetString() ?? "" : "";
                    _cboOrders.Items.Add($"[{orderNo}] {customer}");
                }
                if (_cboOrders.Items.Count > 0) _cboOrders.SelectedIndex = 0;
            }
        }
        catch { }
    }

    private async Task OnOrderSelectedAsync()
    {
        if (_cboOrders.SelectedIndex < 0 || _cboOrders.SelectedIndex >= _cachedOrders.Count) return;
        var order = _cachedOrders[_cboOrders.SelectedIndex];
        string orderNo = order.GetProperty("orderNumber").GetString() ?? "";

        _lstParts.Items.Clear();
        _cachedParts.Clear();

        if (order.TryGetProperty("parts", out var partsEl) && partsEl.ValueKind == JsonValueKind.Array)
        {
            _cachedParts = partsEl.EnumerateArray().ToList();
            foreach (var p in _cachedParts)
            {
                string code = p.TryGetProperty("partCode", out var c) ? c.GetString() ?? "" : "";
                string name = p.TryGetProperty("partName", out var n) ? n.GetString() ?? "" : code;
                int qty = p.TryGetProperty("targetQuantity", out var q) ? q.GetInt32() : 1;
                _lstParts.Items.Add($"[{code}] {name} ({qty}EA)");
            }
            if (_lstParts.Items.Count > 0) _lstParts.SelectedIndex = 0;
        }
    }

    // =========================================================================
    // WORKFLOW GENERATION & NODE MANAGEMENT
    // =========================================================================
    private void GenerateStandardWorkflowForSelectedPart()
    {
        string orderNo = "WO-2026";
        if (_cboOrders.SelectedIndex >= 0 && _cboOrders.SelectedIndex < _cachedOrders.Count)
        {
            orderNo = _cachedOrders[_cboOrders.SelectedIndex].GetProperty("orderNumber").GetString() ?? "WO-2026";
        }

        string partName = "소켓 상판";
        if (_lstParts.SelectedIndex >= 0 && _lstParts.SelectedIndex < _cachedParts.Count)
        {
            var p = _cachedParts[_lstParts.SelectedIndex];
            partName = p.TryGetProperty("partName", out var n) ? n.GetString() ?? "소켓 상판" : "소켓 상판";
        }

        // Generate Standard CNC Workflow Process (Preparation -> Rough -> Inspection -> Finish -> Burr/Wash)
        _canvas.Clear();

        var n1 = new WorkflowNode { Title = "도면 및 소재 준비", Category = NodeCategory.Preparation, OrderNumber = orderNo, PartName = partName, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 60, Y = 80 };
        var n2 = new WorkflowNode { Title = "1차 황삭 (면가공)", Category = NodeCategory.Machining, OrderNumber = orderNo, PartName = partName, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 320, Y = 80 };
        var n3 = new WorkflowNode { Title = "치수 중간측정", Category = NodeCategory.Inspection, OrderNumber = orderNo, PartName = partName, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 580, Y = 80 };
        var n4 = new WorkflowNode { Title = "2차 정삭 가공", Category = NodeCategory.Machining, OrderNumber = orderNo, PartName = partName, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 840, Y = 80 };
        var n5 = new WorkflowNode { Title = "에어건 세척 & 적재", Category = NodeCategory.PostProcess, OrderNumber = orderNo, PartName = partName, MachineId = _currentMachineId, Assignee = "이서브", X = 1100, Y = 80 };

        // Parallel sub-worker task: Fixture Prep
        var nSub = new WorkflowNode { Title = "치구 클램프 준비", Category = NodeCategory.Collaboration, OrderNumber = orderNo, PartName = partName, MachineId = _currentMachineId, Assignee = "이서브", X = 320, Y = 200 };

        _canvas.AddNode(n1);
        _canvas.AddNode(n2);
        _canvas.AddNode(n3);
        _canvas.AddNode(n4);
        _canvas.AddNode(n5);
        _canvas.AddNode(nSub);

        _canvas.AddEdge(n1.Id, n2.Id, EdgeType.Sequence);
        _canvas.AddEdge(n1.Id, nSub.Id, EdgeType.Delegation);
        _canvas.AddEdge(n2.Id, n3.Id, EdgeType.Sequence);
        _canvas.AddEdge(n3.Id, n4.Id, EdgeType.Sequence);
        _canvas.AddEdge(n4.Id, n5.Id, EdgeType.Sequence);

        _canvas.SelectedNode = n1;
        UpdateWorkflowStats();
    }

    private void AddQuickNode(NodeCategory cat, string defaultTitle)
    {
        string orderNo = _cboOrders.SelectedIndex >= 0 ? _cachedOrders[_cboOrders.SelectedIndex].GetProperty("orderNumber").GetString() ?? "" : "";
        string partName = _lstParts.SelectedItem?.ToString() ?? "";

        float newX = 100;
        float newY = 100;
        if (_canvas.SelectedNode != null)
        {
            newX = _canvas.SelectedNode.X + 260;
            newY = _canvas.SelectedNode.Y;
        }

        var node = new WorkflowNode
        {
            Title = defaultTitle,
            Category = cat,
            OrderNumber = orderNo,
            PartName = partName,
            MachineId = _currentMachineId,
            Assignee = _session.DisplayName,
            X = newX,
            Y = newY
        };

        _canvas.AddNode(node);
        if (_canvas.SelectedNode != null && _canvas.SelectedNode != node)
        {
            _canvas.AddEdge(_canvas.SelectedNode.Id, node.Id, EdgeType.Sequence);
        }
        _canvas.SelectedNode = node;
    }

    private void DuplicateSelectedNode(WorkflowNode node)
    {
        var dup = new WorkflowNode
        {
            Title = $"{node.Title} (복사본)",
            Category = node.Category,
            Status = NodeStatus.Ready,
            OrderNumber = node.OrderNumber,
            PartName = node.PartName,
            MachineId = node.MachineId,
            Assignee = node.Assignee,
            Details = node.Details,
            X = node.X + 40,
            Y = node.Y + 40
        };
        _canvas.AddNode(dup);
    }

    private void DuplicateSelectedNodeOrWorkflow()
    {
        if (_canvas.SelectedNode != null)
        {
            DuplicateSelectedNode(_canvas.SelectedNode);
        }
        else if (_canvas.Nodes.Count > 0)
        {
            // Duplicate whole workflow for another part
            float offsetY = 160;
            var oldToNew = new Dictionary<string, string>();
            foreach (var n in _canvas.Nodes.ToList())
            {
                var dup = new WorkflowNode
                {
                    Title = n.Title,
                    Category = n.Category,
                    Status = NodeStatus.Ready,
                    OrderNumber = n.OrderNumber,
                    PartName = n.PartName,
                    MachineId = "R02", // Target second machine
                    Assignee = n.Assignee,
                    Details = n.Details,
                    X = n.X,
                    Y = n.Y + offsetY
                };
                _canvas.Nodes.Add(dup);
                oldToNew[n.Id] = dup.Id;
            }
            foreach (var e in _canvas.Edges.ToList())
            {
                if (oldToNew.TryGetValue(e.FromNodeId, out var f) && oldToNew.TryGetValue(e.ToNodeId, out var t))
                {
                    _canvas.Edges.Add(new WorkflowEdge { FromNodeId = f, ToNodeId = t, Type = e.Type });
                }
            }
            _canvas.Invalidate();
            UpdateWorkflowStats();
        }
    }

    private void UpdateWorkflowStats()
    {
        int running = _canvas.Nodes.Count(n => n.Status == NodeStatus.Running);
        int pending = _canvas.Nodes.Count(n => n.Status == NodeStatus.PendingCheck);
        int completed = _canvas.Nodes.Count(n => n.Status == NodeStatus.Completed);
        int issue = _canvas.Nodes.Count(n => n.Status == NodeStatus.Issue);

        _lblWorkflowStats.Text = $"워크플로우: 가공중 {running}건 | 측정대기 {pending}건 | 완료 {completed}건 | 이슈 {issue}건";
    }

    // =========================================================================
    // SERVER API SYNC ON WORKFLOW NODE STATUS CHANGE
    // =========================================================================
    private async Task HandleNodeStatusChangeAsync(WorkflowNode node, NodeStatus newStatus)
    {
        try
        {
            string serverStatus = newStatus switch
            {
                NodeStatus.Running => "가공중",
                NodeStatus.PendingCheck => "대기",
                NodeStatus.Completed => "완료",
                NodeStatus.Issue => "정지",
                _ => "대기"
            };

            // Call existing Server API: AddManualWorkLogAsync
            await _api.AddManualWorkLogAsync(
                node.MachineId,
                node.OrderNumber,
                node.PartName,
                node.Title + (string.IsNullOrWhiteSpace(node.Details) ? "" : $" ({node.Details})"),
                serverStatus,
                _session.DisplayName,
                node.Assignee,
                1
            );

            // If issue, also register defect record
            if (newStatus == NodeStatus.Issue)
            {
                await _api.CreateDefectAndCountermeasureAsync(
                    $"{node.OrderNumber} / {node.PartName}",
                    node.MachineId,
                    "가공중",
                    "치수불량",
                    node.Title + " 중 이상 발생: " + node.Details,
                    "작업자 확인 및 조치 진행 중",
                    "Z축 옵셋 점검 및 공구 상태 확인",
                    _session.DisplayName
                );
            }

            await LoadHistoryGridsAsync();
            UpdateWorkflowStats();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"서버 작업 보고 중 오류: {ex.Message}", "서버 연동 알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // =========================================================================
    // HISTORY GRIDS, NOTICES, CHAT
    // =========================================================================
    private async Task LoadHistoryGridsAsync()
    {
        try
        {
            var logs = await _api.GetMachineWorkLogsAsync(_currentMachineId, 30);
            _gridHistory.Rows.Clear();
            if (logs != null)
            {
                foreach (var log in logs)
                {
                    string time = log.TryGetProperty("timestamp", out var t) ? t.GetString() ?? "" : "";
                    string mach = log.TryGetProperty("machineId", out var m) ? m.GetString() ?? "" : "";
                    string order = log.TryGetProperty("jobNumber", out var j) ? j.GetString() ?? "" : "";
                    string part = log.TryGetProperty("partName", out var p) ? p.GetString() ?? "" : "";
                    string desc = log.TryGetProperty("actionDescription", out var d) ? d.GetString() ?? "" : "";
                    string worker = log.TryGetProperty("workerName", out var w) ? w.GetString() ?? "" : "";
                    string status = log.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                    _gridHistory.Rows.Add(time, mach, order, part, desc, worker, status);
                }
            }

            var defects = await _api.GetAllPermanentEventsAsync();
            _gridDefects.Rows.Clear();
            if (defects != null)
            {
                int no = 1;
                foreach (var def in defects)
                {
                    string mach = def.TryGetProperty("machineId", out var m) ? m.GetString() ?? "" : "";
                    string part = def.TryGetProperty("partCode", out var p) ? p.GetString() ?? "" : "";
                    string type = def.TryGetProperty("eventType", out var dt) ? dt.GetString() ?? "" : "";
                    string desc = def.TryGetProperty("description", out var dd) ? dd.GetString() ?? "" : "";
                    string cause = def.TryGetProperty("rootCause", out var rc) ? rc.GetString() ?? "" : "";
                    string counter = def.TryGetProperty("actionTaken", out var cm) ? cm.GetString() ?? "" : "";
                    string author = def.TryGetProperty("workerName", out var rp) ? rp.GetString() ?? "" : "";
                    _gridDefects.Rows.Add(no++, mach, part, type, desc, cause, counter, author);
                }
            }

            var subTasks = await _api.GetSubWorkerTasksAsync();
            _gridSubTasks.Rows.Clear();
            if (subTasks != null)
            {
                int no = 1;
                foreach (var st in subTasks)
                {
                    string mach = st.TryGetProperty("machineId", out var m) ? m.GetString() ?? "" : "";
                    string sub = st.TryGetProperty("subWorkerName", out var s) ? s.GetString() ?? "" : "";
                    string task = st.TryGetProperty("instruction", out var i) ? i.GetString() ?? "" : "";
                    string rep = st.TryGetProperty("reportContent", out var r) ? r.GetString() ?? "" : "";
                    string stt = st.TryGetProperty("status", out var stat) ? stat.GetString() ?? "" : "";
                    _gridSubTasks.Rows.Add(no++, mach, sub, task, rep, stt);
                }
            }
        }
        catch { }
    }

    private async Task LoadNoticesAsync()
    {
        try
        {
            var list = await _api.GetNoticesAsync();
            if (list != null)
            {
                _lstNotices.Items.Clear();
                int unread = 0;
                foreach (var n in list)
                {
                    string title = n.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "공지사항";
                    string date = n.TryGetProperty("createdAt", out var d) ? d.GetString() ?? "" : "";
                    _lstNotices.Items.Add($"📢 {title} ({date})");
                    unread++;
                }
                _lblNoticeBadge.Text = $"{unread}건 등록";
            }
        }
        catch { }
    }

    private async Task ShowNoticesDialogAsync()
    {
        MessageBox.Show("공지사항 목록을 확인합니다.", "공지사항 열람", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task LoadChatAsync()
    {
        try
        {
            var list = await _api.GetChatMessagesAsync("GENERAL");
            if (list != null)
            {
                _lstChat.Items.Clear();
                foreach (var msg in list)
                {
                    string sender = msg.TryGetProperty("senderName", out var s) ? s.GetString() ?? "" : "시스템";
                    string content = msg.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    _lstChat.Items.Add($"[{sender}] {content}");
                }
            }
        }
        catch { }
    }

    private async Task SendChatAsync()
    {
        string text = _txtChatInput.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _txtChatInput.Clear();
        try
        {
            await _api.SendChatMessageAsync("GENERAL", _session.DisplayName, text);
            await LoadChatAsync();
        }
        catch { }
    }

    // =========================================================================
    // VIEW SWITCHER (Seamless Recovery & Toggle between Workflow & Classic UI)
    // =========================================================================
    private void SwitchToClassicView()
    {
        var classicForm = new MainWorkerForm(_api, _session);
        classicForm.Show();
        Hide();
        classicForm.FormClosed += (s, e) => Close();
    }

    // =========================================================================
    // UI BUILDER HELPERS
    // =========================================================================
    private Panel CreateCard(string title)
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg900,
            Padding = new Padding(8),
            Margin = new Padding(0, 0, 0, 6)
        };
        pnl.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnl.Width - 1, pnl.Height - 1);
        };

        var lbl = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold),
            ForeColor = Palette.TextWhite
        };
        pnl.Controls.Add(lbl);
        return pnl;
    }

    private Button CreateFlatButton(string text, Color bg, Color fg, int w = 0, int h = 0)
    {
        var btn = new Button
        {
            Text = text,
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(2)
        };
        btn.FlatAppearance.BorderSize = 0;
        if (w > 0) btn.Width = w;
        if (h > 0) btn.Height = h;
        return btn;
    }

    private DataGridView CreateStyledGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Palette.Bg950,
            ForeColor = Palette.TextWhite,
            GridColor = Palette.Border,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Palette.Bg800;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.AccentSky;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = Palette.Bg900;
        grid.DefaultCellStyle.ForeColor = Palette.TextWhite;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 14, 165, 233);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        return grid;
    }

    public void PopulateSampleWorkflowForVisual()
    {
        _canvas.Nodes.Clear();
        _canvas.Edges.Clear();

        string orderNo = "WO-2026-002";
        string partName = "소켓 상판 (PEEK 5T)";

        var n1 = new WorkflowNode { Title = "도면 및 NC프로그램 확인", Category = NodeCategory.Preparation, Status = NodeStatus.Completed, OrderNumber = orderNo, PartName = partName, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 40, Y = 100, Details = "도면 03.96BGA Rev.00 치수 및 홀 위치 확인 완료" };
        var n2 = new WorkflowNode { Title = "소재 세팅 & 기준좌표 설정", Category = NodeCategory.Preparation, Status = NodeStatus.Completed, OrderNumber = orderNo, PartName = partName, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 290, Y = 100, Details = "PEEK 5T 바이스 지그 장착, G54 원점 세팅 완료" };
        var n3 = new WorkflowNode { Title = "1차 전면 황삭 가공", Category = NodeCategory.Machining, Status = NodeStatus.Completed, OrderNumber = orderNo, PartName = partName, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 540, Y = 100, Details = "10파이 엔드밀 포켓 깊이 황삭 진행" };
        var n4 = new WorkflowNode { Title = "초물 치수 중간측정", Category = NodeCategory.Inspection, Status = NodeStatus.Completed, OrderNumber = orderNo, PartName = partName, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 790, Y = 100, Details = "버니어 및 마이크로미터 두께 5.01mm 측정 합격" };
        var n5 = new WorkflowNode { Title = "2차 전면 정밀 정삭", Category = NodeCategory.Machining, Status = NodeStatus.Running, OrderNumber = orderNo, PartName = partName, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 1040, Y = 100, Details = "초경 엔드밀 고속 정삭 가공 진행 중 (현재 75%)" };
        var n6 = new WorkflowNode { Title = "서브인원 면취 및 Burr 제거", Category = NodeCategory.Collaboration, Status = NodeStatus.Ready, OrderNumber = orderNo, PartName = partName, MachineId = "서브", Assignee = "김기술", X = 790, Y = 260, Details = "외곽 R0.2 C면취 및 잔여 Burr 제거 지시" };
        var n7 = new WorkflowNode { Title = "초음파 세척 및 포장/납품", Category = NodeCategory.PostProcess, Status = NodeStatus.Ready, OrderNumber = orderNo, PartName = partName, MachineId = "후가공", Assignee = "포장팀", X = 1290, Y = 100, Details = "이물질 세척 후 보호비닐 포장" };
        var n8 = new WorkflowNode { Title = "포켓 깊이 치수 주의", Category = NodeCategory.Issue, Status = NodeStatus.Issue, OrderNumber = orderNo, PartName = partName, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 1040, Y = 260, Details = "엔드밀 마모 시 Z축 공차 +0.02mm 주의 요망" };

        _canvas.Nodes.AddRange(new[] { n1, n2, n3, n4, n5, n6, n7, n8 });

        // Edges
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n1.Id, ToNodeId = n2.Id, Type = EdgeType.Sequence });
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n2.Id, ToNodeId = n3.Id, Type = EdgeType.Sequence });
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n3.Id, ToNodeId = n4.Id, Type = EdgeType.Sequence });
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n4.Id, ToNodeId = n5.Id, Type = EdgeType.Sequence });
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n5.Id, ToNodeId = n7.Id, Type = EdgeType.Sequence });
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n3.Id, ToNodeId = n6.Id, Type = EdgeType.Delegation });
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n5.Id, ToNodeId = n8.Id, Type = EdgeType.IssueLink });

        // Left Machines
        _cachedMachines.Clear();
        using (var doc = System.Text.Json.JsonDocument.Parse(@"[
            {""machineId"":""R01"",""status"":""가공중"",""activeJobNumber"":""WO-2026-002 (소켓 상판)""},
            {""machineId"":""R02"",""status"":""대기"",""activeJobNumber"":""WO-2026-001 (하판 지그)""},
            {""machineId"":""R03"",""status"":""대기"",""activeJobNumber"":""-""},
            {""machineId"":""R04"",""status"":""대기"",""activeJobNumber"":""-""},
            {""machineId"":""R05"",""status"":""대기"",""activeJobNumber"":""-""}
        ]"))
        {
            foreach (var elem in doc.RootElement.EnumerateArray())
                _cachedMachines.Add(elem.Clone());
        }
        RenderLeftMachineCards();

        // Sample Orders
        _cboOrders.Items.Clear();
        _cboOrders.Items.Add("[WO-2026-002] 소켓 상판 (2EA) [진행중]");
        _cboOrders.Items.Add("[WO-2026-001] 하판 고정 지그 (1EA) [완료]");
        _cboOrders.SelectedIndex = 0;

        _lstParts.Items.Clear();
        _lstParts.Items.Add("▶ 1. 소켓 상판 | PEEK 5T | 2EA [단품]");
        _lstParts.Items.Add("  2. 가이드 핀 블록 | SUS304 10T | 4EA");
        _lstParts.SelectedIndex = 0;

        // Sample Notices
        _lstNotices.Items.Clear();
        _lstNotices.Items.Add("📢 [공지] 3공구 엔드밀 교체 주기 준수 요망 (09:15)");
        _lblNoticeBadge.Text = "1건 미확인";
        // Sample Chat
        _lstChat.Items.Clear();
        _lstChat.Items.Add("[10:15 현장반장] R01 소켓 상판 1차 황삭 후 초물 치수 측정실 인계 부탁드립니다.");
        _lstChat.Items.Add("[10:18 유과장] 네, 황삭 완료 후 즉시 5.01mm 두께 검사 진행하겠습니다.");

        // Sample Work Log Rows
        _gridHistory.Rows.Clear();
        _gridHistory.Rows.Add(DateTime.Now.ToString("MM-dd HH:mm"), "R01", "WO-2026-002", "소켓 상판", "2차 전면 정밀 정삭 가공 진행 중 (현재 75%)", "유과장", "가공중");
        _gridHistory.Rows.Add(DateTime.Now.AddMinutes(-35).ToString("MM-dd HH:mm"), "R01", "WO-2026-002", "소켓 상판", "초물 치수 중간측정 (5.01mm 합격)", "유과장", "완료");
        _gridHistory.Rows.Add(DateTime.Now.AddMinutes(-70).ToString("MM-dd HH:mm"), "R01", "WO-2026-002", "소켓 상판", "1차 전면 황삭 가공 진행", "유과장", "완료");

        // Sample Issues Row
        _gridDefects.Rows.Clear();
        _gridDefects.Rows.Add("1", "R01", "WO-2026-002 / 소켓상판", "치수주의", "포켓 깊이 +0.02mm 편차 발생 가능성 모니터링", "초경 엔드밀 마모 체크", "Z축 0.01mm 보정 적용", "유과장 / 10-30 11:20");

        // Select node and bind detail
        _canvas.SelectedNode = n5;
        _detailPanel.BindNode(n5);

        _canvas.Invalidate();
        UpdateWorkflowStats();
    }

}
