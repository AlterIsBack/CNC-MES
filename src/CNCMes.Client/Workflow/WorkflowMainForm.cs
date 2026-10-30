using System.Data;
using System.Drawing.Drawing2D;
using System.Text.Json;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client.Workflow;

public class WorkflowMainForm : Form
{
    private static class Palette
    {
        public static readonly Color Bg950 = Color.FromArgb(10, 16, 28);       // #0a101c
        public static readonly Color Bg900 = Color.FromArgb(15, 23, 42);       // #0f172a
        public static readonly Color Bg850 = Color.FromArgb(20, 31, 54);       // #141f36
        public static readonly Color Bg800 = Color.FromArgb(26, 41, 70);       // #1a2946
        public static readonly Color Border = Color.FromArgb(38, 56, 92);      // #26385c
        public static readonly Color BorderLight = Color.FromArgb(51, 74, 120);
        public static readonly Color AccentSky = Color.FromArgb(14, 165, 233);
        public static readonly Color Green = Color.FromArgb(16, 185, 129);
        public static readonly Color Amber = Color.FromArgb(245, 158, 11);
        public static readonly Color Rose = Color.FromArgb(239, 68, 68);
        public static readonly Color Purple = Color.FromArgb(168, 85, 247);
        public static readonly Color TextWhite = Color.FromArgb(248, 250, 252);
        public static readonly Color TextSlate300 = Color.FromArgb(203, 213, 225);
        public static readonly Color TextSlate400 = Color.FromArgb(148, 163, 184);
    }

    private readonly ApiClient _api;
    private readonly UserSession _session;

    // 1. Top Summary Bar
    private Label _lblWorkflowStats = null!;
    private Label _lblActiveMachineSummary = null!;
    private Label _lblLiveClock = null!;
    private Button _btnRefreshAll = null!;
    private Button _btnSwitchToClassic = null!;

    // 2, 3, 4. Left Sidebar
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
    private ListBox _lstChat = null!;
    private TextBox _txtChatInput = null!;
    private Button _btnSendChat = null!;
    private ComboBox _cboChatChannel = null!;

    // 8. Foldable Bottom History & Records
    private TabControl _bottomTabs = null!;
    private DataGridView _gridHistory = null!;
    private DataGridView _gridDefects = null!;
    private DataGridView _gridSubTasks = null!;
    private DataGridView _gridPostProcess = null!;
    private SplitContainer _centerVerticalSplitter = null!;

    private System.Windows.Forms.Timer _pollTimer = null!;

    // Local workflow cache folder
    private readonly string _localWorkflowDir;

    public WorkflowMainForm(ApiClient api, UserSession session)
    {
        _api = api;
        _session = session;

        _localWorkflowDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CNCMes", "workflows");
        try { Directory.CreateDirectory(_localWorkflowDir); } catch { }

        if (!string.IsNullOrEmpty(_session.MachineId))
            _currentMachineId = _session.MachineId;
        if (_session.SelectedMachines?.Count > 0)
        {
            foreach (var m in _session.SelectedMachines)
                _mySelectedMachines.Add(m);
        }
        else
        {
            _mySelectedMachines.Add(_currentMachineId);
        }

        InitializeUI();
    }

    private void InitializeUI()
    {
        Text = $"CNC-MES 스마트 현장 터미널 v1.3.8 [워크플로우 제어] - {_session.DisplayName} ({_session.Role})";
        Size = new Size(1600, 960);
        MinimumSize = new Size(1280, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Palette.Bg950;
        ForeColor = Palette.TextWhite;
        Font = new Font("맑은 고딕", 9f);
        Icon = Icon;

        var rootTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        rootTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f)); // Top Bar
        rootTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Main Area

        // 1. Top Summary Bar
        rootTable.Controls.Add(BuildTopSummaryBar(), 0, 0);

        // 3-Column Main Workspace (Left 310px | Center Fill | Right 365px)
        var mainContainer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(8, 6, 8, 6),
            Margin = new Padding(0),
            BackColor = Palette.Bg950
        };
        mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310f)); // Left Sidebar
        mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // Center Workspace
        mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 365f)); // Right Sidebar
        mainContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        mainContainer.Controls.Add(BuildLeftSidebar(), 0, 0);
        mainContainer.Controls.Add(BuildCenterWorkspace(), 1, 0);
        mainContainer.Controls.Add(BuildRightSidebar(), 2, 0);

        rootTable.Controls.Add(mainContainer, 0, 1);
        Controls.Add(rootTable);

        // Polling Timer (3.5s)
        _pollTimer = new System.Windows.Forms.Timer { Interval = 3500 };
        _pollTimer.Tick += async (s, e) => await PollRealtimeDataAsync();

        Shown += async (s, e) => await OnFormShownAsync();
    }

    private async Task OnFormShownAsync()
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
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(0)
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340f)); // Brand & Logo
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // Center Live Stats Box
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 450f)); // Actions & View Switch
        topBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        topBar.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.DrawLine(pen, 0, topBar.Height - 1, topBar.Width, topBar.Height - 1);
        };

        // Left Brand
        var pnlBrand = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var pnlIcon = new Panel { Location = new Point(0, 8), Size = new Size(36, 36), BackColor = Color.FromArgb(40, 14, 165, 233) };
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

        var lblBrand = new Label { Text = "CNC-MES", Location = new Point(44, 8), Font = new Font("맑은 고딕", 12f, FontStyle.Bold), ForeColor = Color.White, AutoSize = true };
        var lblVer = new Label { Text = "v1.3.8 [Workflow]", Location = new Point(136, 11), Font = new Font("Consolas", 8f, FontStyle.Bold), ForeColor = Palette.AccentSky, BackColor = Color.FromArgb(80, 8, 47, 73), AutoSize = true, Padding = new Padding(4, 1, 4, 1) };
        var lblStatus = new Label { Text = "● 서버 정상", Location = new Point(245, 11), Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.Green, BackColor = Color.FromArgb(80, 6, 78, 59), AutoSize = true, Padding = new Padding(5, 1, 5, 1) };
        var lblSub = new Label { Text = "WORKFLOW-BASED OPERATOR CONTROL SYSTEM", Location = new Point(45, 30), Font = new Font("Consolas", 7.2f), ForeColor = Palette.TextSlate400, AutoSize = true };
        pnlBrand.Controls.AddRange(new Control[] { lblBrand, lblVer, lblStatus, lblSub });
        topBar.Controls.Add(pnlBrand, 0, 0);

        // Center Stats Box (Pixel-perfect vertical & horizontal centering with 1-row TableLayoutPanel)
        var pnlCenter = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var pnlCenterBox = new Panel { Size = new Size(740, 36), BackColor = Palette.Bg850 };
        pnlCenterBox.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.BorderLight, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlCenterBox.Width - 1, pnlCenterBox.Height - 1);
        };

        void CenterTheBox()
        {
            pnlCenterBox.Location = new Point(
                Math.Max(0, (pnlCenter.Width - pnlCenterBox.Width) / 2),
                Math.Max(0, (pnlCenter.Height - pnlCenterBox.Height) / 2)
            );
        }
        pnlCenter.Resize += (s, e) => CenterTheBox();
        pnlCenter.Layout += (s, e) => CenterTheBox();

        // 1 row 6 columns: Exact vertical center for all labels
        var centerGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(6, 0, 6, 0)
        };
        centerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));  // 조작 장비:
        centerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95f));  // [⚙ R01]
        centerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18f));  // |
        centerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // 워크플로우 통계
        centerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18f));  // |
        centerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f)); // 시계
        centerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var lblMachPrompt = new Label
        {
            Text = "조작 장비:",
            ForeColor = Palette.TextSlate400,
            Font = new Font("맑은 고딕", 8.5f),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        _lblActiveMachineSummary = new Label
        {
            Text = $"[⚙ {_currentMachineId}]",
            Font = new Font("Consolas", 9.8f, FontStyle.Bold),
            ForeColor = Palette.Green,
            BackColor = Color.FromArgb(80, 6, 78, 59),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var div1 = new Label
        {
            Text = "|",
            ForeColor = Palette.BorderLight,
            Font = new Font("맑은 고딕", 9f),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        _lblWorkflowStats = new Label
        {
            Text = "워크플로우: 가공중 0건 | 측정대기 0건 | 완료 0건 | 이슈 0건",
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold),
            ForeColor = Palette.TextSlate300,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var div2 = new Label
        {
            Text = "|",
            ForeColor = Palette.BorderLight,
            Font = new Font("맑은 고딕", 9f),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        _lblLiveClock = new Label
        {
            Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Font = new Font("Consolas", 8.8f),
            ForeColor = Palette.TextSlate400,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        centerGrid.Controls.Add(lblMachPrompt, 0, 0);
        centerGrid.Controls.Add(_lblActiveMachineSummary, 1, 0);
        centerGrid.Controls.Add(div1, 2, 0);
        centerGrid.Controls.Add(_lblWorkflowStats, 3, 0);
        centerGrid.Controls.Add(div2, 4, 0);
        centerGrid.Controls.Add(_lblLiveClock, 5, 0);

        pnlCenterBox.Controls.Add(centerGrid);
        pnlCenter.Controls.Add(pnlCenterBox);
        topBar.Controls.Add(pnlCenter, 1, 0);

        // Right Actions (View Switcher & User Profile)
        var pnlRight = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };

        _btnSwitchToClassic = CreateFlatButton("🔀 클래식 터미널 뷰", Color.FromArgb(2, 132, 199), Color.White, 150, 34);
        _btnSwitchToClassic.Location = new Point(290, 9);
        _btnSwitchToClassic.Click += (s, e) => SwitchToClassicView();

        var pnlUser = new Panel { Size = new Size(140, 34), Location = new Point(142, 9), BackColor = Palette.Bg800 };
        pnlUser.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlUser.Width - 1, pnlUser.Height - 1);
            using var brushBg = new SolidBrush(Color.FromArgb(40, 14, 165, 233));
            e.Graphics.FillRectangle(brushBg, 5, 5, 24, 24);
            using var fontA = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
            using var brushSky = new SolidBrush(Palette.AccentSky);
            e.Graphics.DrawString("a", fontA, brushSky, new PointF(12, 8));
            using var fontTxt = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
            using var brushTxt = new SolidBrush(Palette.TextSlate300);
            e.Graphics.DrawString($"작업자 [{_session.DisplayName}]", fontTxt, brushTxt, new PointF(33, 8));
        };

        _btnRefreshAll = CreateFlatButton("🔄", Palette.Bg800, Palette.TextSlate300, 34, 34);
        _btnRefreshAll.Location = new Point(100, 9);
        _btnRefreshAll.Click += async (s, e) => await RefreshAllAsync();

        pnlRight.Controls.AddRange(new Control[] { _btnRefreshAll, pnlUser, _btnSwitchToClassic });
        topBar.Controls.Add(pnlRight, 2, 0);

        return topBar;
    }

    private Control BuildLeftSidebar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 175f)); // Card 1: 6 Action Buttons (2x3 grid, clean spacing)
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Card 2: Orders & Parts List (Full remaining height)

        // Card 1: Workflow Menu Buttons (6 Action Buttons per spec)
        var cardMenu = CreateCard("📋 워크플로우 제어 메뉴");
        var menuGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = new Padding(2),
            Padding = new Padding(2)
        };
        menuGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        menuGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        menuGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f));
        menuGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f));
        menuGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f));

        var btnGenDefault = CreateFlatButton("⚡ 기본 워크플로우", Palette.Bg800, Palette.AccentSky);
        btnGenDefault.Font = new Font("맑은 고딕", 8.3f, FontStyle.Bold);
        btnGenDefault.Click += (s, e) => GenerateStandardWorkflowForSelectedPart(confirmIfNotEmpty: false);

        var btnSaveDraft = CreateFlatButton("💾 로컬 임시저장", Palette.Bg800, Color.FromArgb(52, 211, 153));
        btnSaveDraft.Font = new Font("맑은 고딕", 8.3f, FontStyle.Bold);
        btnSaveDraft.Click += (s, e) => SaveWorkflowDraftLocally();

        var btnConfirmServer = CreateFlatButton("🌐 계획 확정·공유", Color.FromArgb(8, 47, 73), Palette.AccentSky);
        btnConfirmServer.Font = new Font("맑은 고딕", 8.3f, FontStyle.Bold);
        btnConfirmServer.Click += async (s, e) => await ConfirmAndShareWorkflowAsync();

        var btnExecuteSelected = CreateFlatButton("▶ 선택작업 실행", Color.FromArgb(5, 150, 105), Color.White);
        btnExecuteSelected.Font = new Font("맑은 고딕", 8.3f, FontStyle.Bold);
        btnExecuteSelected.Click += async (s, e) => await ExecuteSelectedWorkflowNodeAsync();

        var btnAutoLayout = CreateFlatButton("⟲ 워크플로우 정렬", Palette.Bg800, Palette.Green);
        btnAutoLayout.Font = new Font("맑은 고딕", 8.3f, FontStyle.Bold);
        btnAutoLayout.Click += (s, e) => _canvas.AutoLayout();

        var btnClearPlan = CreateFlatButton("🗑 전체 초기화", Palette.Bg800, Palette.Rose);
        btnClearPlan.Font = new Font("맑은 고딕", 8.3f, FontStyle.Bold);
        btnClearPlan.Click += (s, e) =>
        {
            if (MessageBox.Show("현재 캔버스의 모든 노드와 연결선을 초기화하시겠습니까?", "초기화 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _canvas.Clear();
                UpdateWorkflowStats();
            }
        };

        menuGrid.Controls.Add(btnGenDefault, 0, 0);
        menuGrid.Controls.Add(btnSaveDraft, 1, 0);
        menuGrid.Controls.Add(btnConfirmServer, 0, 1);
        menuGrid.Controls.Add(btnExecuteSelected, 1, 1);
        menuGrid.Controls.Add(btnAutoLayout, 0, 2);
        menuGrid.Controls.Add(btnClearPlan, 1, 2);
        cardMenu.Controls.Add(menuGrid);
        panel.Controls.Add(cardMenu, 0, 0);

        // Card 2: Work Orders & Parts List
        var cardOrders = CreateCard("📦 작업지시서 및 대상 부품 목록");
        var pnlOrderBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(2),
            Padding = new Padding(2)
        };
        pnlOrderBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlOrderBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f)); // Order combo
        pnlOrderBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Parts list

        _cboOrders = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 9f) };
        _cboOrders.SelectedIndexChanged += async (s, e) => await OnOrderSelectedAsync();
        pnlOrderBody.Controls.Add(_cboOrders, 0, 0);

        _lstParts = new ListBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Palette.TextWhite, BorderStyle = BorderStyle.FixedSingle, Font = new Font("맑은 고딕", 9f) };
        _lstParts.SelectedIndexChanged += (s, e) => OnPartSelected();
        pnlOrderBody.Controls.Add(_lstParts, 0, 1);

        cardOrders.Controls.Add(pnlOrderBody);
        panel.Controls.Add(cardOrders, 0, 1);

        return panel;
    }

    private Control BuildCenterWorkspace()
    {
        _centerVerticalSplitter = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 620,
            SplitterWidth = 6,
            BackColor = Palette.Border,
            Margin = new Padding(0)
        };

        // Top: Canvas & Palette Toolbar in TableLayoutPanel
        var pnlCanvasContainer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Palette.Bg900
        };
        pnlCanvasContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlCanvasContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f)); // Toolbar
        pnlCanvasContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Canvas

        // Canvas Toolbar (Top 36px)
        var canvasToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 36,
            BackColor = Palette.Bg900,
            Padding = new Padding(8, 3, 8, 3),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        canvasToolbar.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.DrawLine(pen, 0, canvasToolbar.Height - 1, canvasToolbar.Width, canvasToolbar.Height - 1);
        };

        var btnAddPrep = CreateFlatButton("+준비노드", Palette.Bg800, Color.FromArgb(56, 189, 248), 80, 28);
        btnAddPrep.Click += (s, e) => AddQuickNode(NodeCategory.Preparation, "소재 세팅 & 도면확인");

        var btnAddMach = CreateFlatButton("+가공노드", Palette.Bg800, Color.FromArgb(16, 185, 129), 80, 28);
        btnAddMach.Click += (s, e) => AddQuickNode(NodeCategory.Machining, "1차 황삭 가공");

        var btnAddInsp = CreateFlatButton("+검사노드", Palette.Bg800, Color.FromArgb(245, 158, 11), 80, 28);
        btnAddInsp.Click += (s, e) => AddQuickNode(NodeCategory.Inspection, "치수 정밀 중간측정");

        var btnAddCollab = CreateFlatButton("+서브인원", Palette.Bg800, Color.FromArgb(168, 85, 247), 80, 28);
        btnAddCollab.Click += (s, e) => AddQuickNode(NodeCategory.Collaboration, "서브인원 면취 및 버 제거");

        var btnAddPost = CreateFlatButton("+후공정", Palette.Bg800, Color.FromArgb(99, 102, 241), 80, 28);
        btnAddPost.Click += (s, e) => AddQuickNode(NodeCategory.PostProcess, "초음파 세척 및 건조");

        var btnAddIssue = CreateFlatButton("+이슈등록", Palette.Bg800, Color.FromArgb(239, 68, 68), 80, 28);
        btnAddIssue.Click += (s, e) => AddQuickNode(NodeCategory.Issue, "공구 마모 치수 주의");

        var divTool = new Label { Text = "|", ForeColor = Palette.Border, AutoSize = true, Margin = new Padding(6, 6, 6, 0) };

        var btnZoomReset = CreateFlatButton("🔍 100%", Palette.Bg800, Palette.TextSlate300, 68, 28);
        btnZoomReset.Click += (s, e) => _canvas.ResetView();

        var btnToggleBottom = CreateFlatButton("⤢ 하단 이력 접기/펼치기", Palette.Bg800, Palette.AccentSky, 150, 28);
        btnToggleBottom.Click += (s, e) =>
        {
            _centerVerticalSplitter.Panel2Collapsed = !_centerVerticalSplitter.Panel2Collapsed;
        };

        canvasToolbar.Controls.AddRange(new Control[]
        {
            btnAddPrep, btnAddMach, btnAddInsp, btnAddCollab, btnAddPost, btnAddIssue,
            divTool, btnZoomReset, btnToggleBottom
        });

        // Canvas Control
        _canvas = new WorkflowCanvasControl { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _canvas.NodeSelected += (node) => _detailPanel.BindNode(node);
        _canvas.WorkflowChanged += UpdateWorkflowStats;
        _canvas.RequestGenerateDefaultWorkflow += () => GenerateStandardWorkflowForSelectedPart(confirmIfNotEmpty: true);

        pnlCanvasContainer.Controls.Add(canvasToolbar, 0, 0);
        pnlCanvasContainer.Controls.Add(_canvas, 0, 1);
        _centerVerticalSplitter.Panel1.Controls.Add(pnlCanvasContainer);

        // Bottom History Tabs
        _centerVerticalSplitter.Panel2.Controls.Add(BuildBottomHistoryTabs());

        return _centerVerticalSplitter;
    }

    private Control BuildBottomHistoryTabs()
    {
        var pnl = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg900, Padding = new Padding(0) };
        _bottomTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Appearance = TabAppearance.Normal,
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold)
        };

        // Tab 1: Machine Work Logs
        var tabHistory = new TabPage("⏱ 장비 최근 작업 이력 (더블클릭 시 수정)") { BackColor = Palette.Bg900 };
        _gridHistory = CreateStyledGrid();
        _gridHistory.Columns.Add("colTime", "시각");
        _gridHistory.Columns.Add("colMach", "장비");
        _gridHistory.Columns.Add("colOrder", "작지번호");
        _gridHistory.Columns.Add("colPart", "품목(품명)");
        _gridHistory.Columns.Add("colDetail", "세부 작업내용");
        _gridHistory.Columns.Add("colWorker", "작업자");
        _gridHistory.Columns.Add("colStatus", "상태");
        _gridHistory.Columns["colDetail"]!.FillWeight = 220;
        tabHistory.Controls.Add(_gridHistory);
        _bottomTabs.TabPages.Add(tabHistory);

        // Tab 2: Defects & Countermeasures
        var tabDefects = new TabPage("💥 불량·수정·대책 히스토리") { BackColor = Palette.Bg900 };
        _gridDefects = CreateStyledGrid();
        _gridDefects.Columns.Add("colDefId", "No");
        _gridDefects.Columns.Add("colDefMach", "장비");
        _gridDefects.Columns.Add("colDefPart", "작지/품목");
        _gridDefects.Columns.Add("colDefType", "유형");
        _gridDefects.Columns.Add("colDefDesc", "불량 및 수정 내용");
        _gridDefects.Columns.Add("colDefCause", "발생 원인");
        _gridDefects.Columns.Add("colDefAction", "조치 및 재발방지 대책");
        _gridDefects.Columns.Add("colDefAuthor", "작성자/일시");
        _gridDefects.Columns["colDefDesc"]!.FillWeight = 160;
        _gridDefects.Columns["colDefAction"]!.FillWeight = 160;
        tabDefects.Controls.Add(_gridDefects);
        _bottomTabs.TabPages.Add(tabDefects);

        // Tab 3: Sub Worker Tasks
        var tabSubTasks = new TabPage("👥 서브인원 작업 지시 및 보고") { BackColor = Palette.Bg900 };
        _gridSubTasks = CreateStyledGrid();
        _gridSubTasks.Columns.Add("colSubId", "ID");
        _gridSubTasks.Columns.Add("colSubMach", "장비");
        _gridSubTasks.Columns.Add("colSubOrder", "작지번호");
        _gridSubTasks.Columns.Add("colSubPart", "품명");
        _gridSubTasks.Columns.Add("colSubWorker", "서브인원");
        _gridSubTasks.Columns.Add("colSubInstr", "지시 내용");
        _gridSubTasks.Columns.Add("colSubReport", "보고 내용");
        _gridSubTasks.Columns.Add("colSubStatus", "상태");
        _gridSubTasks.Columns["colSubInstr"]!.FillWeight = 180;
        tabSubTasks.Controls.Add(_gridSubTasks);
        _bottomTabs.TabPages.Add(tabSubTasks);

        // Tab 4: Post Processing
        var tabPost = new TabPage("⚙ 후공정(핀/각인/후처리) 추적") { BackColor = Palette.Bg900 };
        _gridPostProcess = CreateStyledGrid();
        _gridPostProcess.Columns.Add("colPostId", "ID");
        _gridPostProcess.Columns.Add("colPostOrder", "작지번호");
        _gridPostProcess.Columns.Add("colPostPart", "품명");
        _gridPostProcess.Columns.Add("colPostMach", "장비");
        _gridPostProcess.Columns.Add("colPostWorker", "작업자");
        _gridPostProcess.Columns.Add("colPostItems", "선택 공정");
        _gridPostProcess.Columns.Add("colPostStatus", "상태");
        tabPost.Controls.Add(_gridPostProcess);
        _bottomTabs.TabPages.Add(tabPost);

        pnl.Controls.Add(_bottomTabs);
        return pnl;
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
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 155f)); // 6: Notices
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 220f)); // 7: Realtime Chat

        // 9: Node Details Panel
        _detailPanel = new NodeDetailPanel { Dock = DockStyle.Fill };
        _detailPanel.StatusChangeRequested += async (node, newStatus) => await HandleNodeStatusChangeAsync(node, newStatus);
        _detailPanel.NodeUpdated += (node) => { _canvas.Invalidate(); UpdateWorkflowStats(); };
        _detailPanel.NodeDeleteRequested += (node) => _canvas.RemoveNode(node);
        _detailPanel.NodeDuplicateRequested += (node) => DuplicateSelectedNode(node);
        _detailPanel.NodeDisconnectRequested += (node) =>
        {
            _canvas.RemoveEdgesForNode(node);
            UpdateWorkflowStats();
            MessageBox.Show($"'{node.Title}' 노드의 모든 앞뒤 연결선이 끊어졌습니다.", "연결 끊기 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _canvas.RequestAddQuickNode += (cat) =>
        {
            string title = cat switch
            {
                NodeCategory.Preparation => "소재 준비 및 원점 설정",
                NodeCategory.Machining => "신규 NC 정밀 가공",
                NodeCategory.Inspection => "정밀 치수 중간측정",
                NodeCategory.Collaboration => "서브인원 작업 지시",
                NodeCategory.PostProcess => "세척 및 포장 후처리",
                NodeCategory.Issue => "공구 마모 치수 주의",
                _ => "신규 공정"
            };
            AddQuickNode(cat, title);
        };
        _detailPanel.SubWorkerTaskSendRequested += async (node, subWorker, instruction) => await SendSubWorkerTaskAsync(node, subWorker, instruction);
        panel.Controls.Add(_detailPanel, 0, 0);

        // 6: Notices Card
        var cardNotice = CreateCard("📢 공지사항 및 작업지시서 알림");
        var pnlNoticeHeader = new Panel { Dock = DockStyle.Top, Height = 24 };
        _lblNoticeBadge = new Label { Text = "0건 미확인", Dock = DockStyle.Right, AutoSize = true, ForeColor = Palette.Amber, Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold) };
        var btnOpenNotices = CreateFlatButton("목록 상세열기", Palette.Bg800, Palette.AccentSky, 90, 20);
        btnOpenNotices.Dock = DockStyle.Left;
        btnOpenNotices.Click += async (s, e) => await ShowNoticesDialogAsync();
        pnlNoticeHeader.Controls.AddRange(new Control[] { btnOpenNotices, _lblNoticeBadge });

        _lstNotices = new ListBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Palette.TextSlate300, BorderStyle = BorderStyle.FixedSingle, Font = new Font("맑은 고딕", 8.2f) };
        _lstNotices.DoubleClick += async (s, e) => await ShowNoticesDialogAsync();

        cardNotice.Controls.Add(_lstNotices);
        cardNotice.Controls.Add(pnlNoticeHeader);
        panel.Controls.Add(cardNotice, 0, 1);

        // 7: Realtime Chat Card
        var cardChat = CreateCard("💬 현장 실시간 소통 채팅");
        var pnlChatBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        pnlChatBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Channel selector
        pnlChatBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Chat messages
        pnlChatBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Chat input

        _cboChatChannel = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 8.2f) };
        _cboChatChannel.Items.AddRange(new object[] { "🌐 전체 공통 채널", "⚙ R01 작업 채널", "⚙ R02 작업 채널", "🚨 품질/이슈 채널" });
        _cboChatChannel.SelectedIndex = 0;
        _cboChatChannel.SelectedIndexChanged += async (s, e) => await LoadChatAsync();
        pnlChatBody.Controls.Add(_cboChatChannel, 0, 0);

        _lstChat = new ListBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Palette.TextWhite, BorderStyle = BorderStyle.FixedSingle, Font = new Font("맑은 고딕", 8.2f) };
        pnlChatBody.Controls.Add(_lstChat, 0, 1);

        var pnlChatSend = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        pnlChatSend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlChatSend.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55f));

        _txtChatInput = new TextBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("맑은 고딕", 8.5f), PlaceholderText = "메시지 입력..." };
        _txtChatInput.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) await SendChatAsync(); };

        _btnSendChat = CreateFlatButton("전송 ✈", Palette.AccentSky, Color.White);
        _btnSendChat.Click += async (s, e) => await SendChatAsync();

        pnlChatSend.Controls.Add(_txtChatInput, 0, 0);
        pnlChatSend.Controls.Add(_btnSendChat, 1, 0);
        pnlChatBody.Controls.Add(pnlChatSend, 0, 2);

        cardChat.Controls.Add(pnlChatBody);
        panel.Controls.Add(cardChat, 0, 2);

        return panel;
    }

    // =========================================================================
    // REAL-TIME DATA LOADERS & SYNC
    // =========================================================================
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
            if (list != null && list.Count > 0)
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
                Size = new Size(290, 48),
                BackColor = isFocused ? Color.FromArgb(40, 14, 165, 233) : Palette.Bg850,
                Margin = new Padding(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };

            card.Paint += (s, e) =>
            {
                using var pen = new Pen(isFocused ? Palette.AccentSky : (isMy ? Color.FromArgb(52, 211, 153) : Palette.Border), isFocused ? 2 : 1);
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            var lblTitle = new Label
            {
                Text = $"{id} ({status})",
                Font = new Font("Consolas", 8.8f, FontStyle.Bold),
                ForeColor = status == "가공중" ? Palette.Green : (status == "정지" ? Palette.Rose : Palette.Amber),
                Location = new Point(6, 6),
                AutoSize = true
            };

            var lblJob = new Label
            {
                Text = string.IsNullOrEmpty(jobNo) || jobNo == "-" ? "할당 작지 없음" : jobNo,
                Font = new Font("맑은 고딕", 7.5f),
                ForeColor = Palette.TextSlate400,
                Location = new Point(6, 26),
                Size = new Size(275, 16)
            };

            EventHandler clickHandler = (s, e) =>
            {
                _currentMachineId = id;
                _lblActiveMachineSummary.Text = $"[⚙ {_currentMachineId}]";
                RenderLeftMachineCards();
            };
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
            if (list != null && list.Count > 0)
            {
                _cachedOrders = list;
                _cboOrders.Items.Clear();
                foreach (var o in _cachedOrders)
                {
                    string orderNo = o.GetProperty("orderNumber").GetString() ?? "";
                    string customer = o.TryGetProperty("customerName", out var cn) ? cn.GetString() ?? "" : "";
                    string status = o.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "대기";
                    _cboOrders.Items.Add($"[{orderNo}] {customer} ({status})");
                }
                if (_cboOrders.Items.Count > 0 && _cboOrders.SelectedIndex < 0)
                {
                    _cboOrders.SelectedIndex = 0;
                }
            }
        }
        catch { }
    }

    private async Task OnOrderSelectedAsync()
    {
        if (_cboOrders.SelectedIndex < 0 || _cboOrders.SelectedIndex >= _cachedOrders.Count) return;
        var order = _cachedOrders[_cboOrders.SelectedIndex];

        _lstParts.Items.Clear();
        _cachedParts.Clear();

        if (order.TryGetProperty("parts", out var partsEl) && partsEl.ValueKind == JsonValueKind.Array)
        {
            _cachedParts = partsEl.EnumerateArray().ToList();
            int idx = 1;
            foreach (var p in _cachedParts)
            {
                string code = p.TryGetProperty("partCode", out var c) ? c.GetString() ?? "" : idx.ToString();
                string name = p.TryGetProperty("partName", out var n) ? n.GetString() ?? "" : $"부품 {code}";
                string mat = p.TryGetProperty("material", out var m) ? m.GetString() ?? "" : "AL6061";
                double thick = p.TryGetProperty("thickness", out var t) ? t.GetDouble() : 0;
                int qty = p.TryGetProperty("targetQty", out var q1) ? q1.GetInt32() : (p.TryGetProperty("targetQuantity", out var q2) ? q2.GetInt32() : 1);
                string prodType = p.TryGetProperty("productionType", out var pt) ? pt.GetString() ?? "단품" : "단품";

                string thickStr = thick > 0 ? $" {thick}T" : "";
                _lstParts.Items.Add($"[{code}] {name} ({mat}{thickStr}) — {qty}EA [{prodType}]");
                idx++;
            }
            if (_lstParts.Items.Count > 0)
            {
                _lstParts.SelectedIndex = 0;
            }
        }
    }

    private void OnPartSelected()
    {
        if (_lstParts.SelectedIndex < 0) return;

        // Try load existing workflow draft from local cache
        string cacheKey = GetCurrentPartCacheKey();
        string cacheFile = Path.Combine(_localWorkflowDir, $"{cacheKey}.json");

        if (File.Exists(cacheFile))
        {
            try
            {
                string json = File.ReadAllText(cacheFile);
                var data = JsonSerializer.Deserialize<WorkflowData>(json);
                if (data != null && data.Nodes.Count > 0)
                {
                    _canvas.Clear();
                    _canvas.Nodes.AddRange(data.Nodes);
                    _canvas.Edges.AddRange(data.Edges);
                    _canvas.Invalidate();
                    _canvas.SelectedNode = _canvas.Nodes.FirstOrDefault();
                    UpdateWorkflowStats();
                    return;
                }
            }
            catch { }
        }

        // If no cached workflow exists, clear canvas so the empty guide displays
        _canvas.Clear();
        UpdateWorkflowStats();
    }

    private string GetCurrentPartCacheKey()
    {
        string orderNo = "WO-000";
        if (_cboOrders.SelectedIndex >= 0 && _cboOrders.SelectedIndex < _cachedOrders.Count)
        {
            orderNo = _cachedOrders[_cboOrders.SelectedIndex].GetProperty("orderNumber").GetString() ?? "WO-000";
        }

        string partCode = "PART1";
        if (_lstParts.SelectedIndex >= 0 && _lstParts.SelectedIndex < _cachedParts.Count)
        {
            var p = _cachedParts[_lstParts.SelectedIndex];
            partCode = p.TryGetProperty("partCode", out var c) ? c.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(partCode) && p.TryGetProperty("partId", out var id))
                partCode = id.GetInt32().ToString();
            if (string.IsNullOrEmpty(partCode))
                partCode = (_lstParts.SelectedIndex + 1).ToString();
        }

        return $"{orderNo}_{partCode}";
    }

    // =========================================================================
    // WORKFLOW GENERATION, DRAFT SAVING & CONFIRMATION
    // =========================================================================
    public void GenerateStandardWorkflowForSelectedPart(bool confirmIfNotEmpty = false)
    {
        string orderNo = "WO-2026-002";
        int orderId = 0;
        if (_cboOrders.SelectedIndex >= 0 && _cboOrders.SelectedIndex < _cachedOrders.Count)
        {
            var o = _cachedOrders[_cboOrders.SelectedIndex];
            orderNo = o.GetProperty("orderNumber").GetString() ?? "WO-2026-002";
            if (o.TryGetProperty("orderId", out var oid)) orderId = oid.GetInt32();
        }

        // Determine lane offset & target machine for multi-lane multi-machine workflow
        float startY = 70f;
        string laneMachine = _currentMachineId;
        string partName = "소켓 상판 (PEEK 5T)";
        int partId = 1;

        if (_canvas.Nodes.Count > 0)
        {
            // Multi-machine / Multi-part append mode!
            float maxY = _canvas.Nodes.Max(n => n.Y);
            startY = maxY + 180f;

            // Pick another machine if already used (e.g. R01 -> R02 -> R03)
            var usedMachines = _canvas.Nodes.Select(n => n.MachineId).ToHashSet();
            if (usedMachines.Contains(laneMachine))
            {
                var candidates = new[] { "R02", "R03", "R04", "K01", "K02" };
                foreach (var c in candidates)
                {
                    if (!usedMachines.Contains(c))
                    {
                        laneMachine = c;
                        break;
                    }
                }
            }

            // Pick next part if available
            if (_cachedParts.Count > 1)
            {
                int nextPartIdx = (_lstParts.SelectedIndex + 1) % _cachedParts.Count;
                var p = _cachedParts[nextPartIdx];
                string n = p.TryGetProperty("partName", out var pn) ? pn.GetString() ?? "" : "소켓 하판";
                string m = p.TryGetProperty("material", out var mat) ? mat.GetString() ?? "" : "";
                partName = string.IsNullOrEmpty(m) ? n : $"{n} ({m})";
                if (p.TryGetProperty("partId", out var pid)) partId = pid.GetInt32();
            }
            else
            {
                partName = $"부품 (2공정 - {laneMachine})";
            }
        }
        else
        {
            if (_lstParts.SelectedIndex >= 0 && _lstParts.SelectedIndex < _cachedParts.Count)
            {
                var p = _cachedParts[_lstParts.SelectedIndex];
                string n = p.TryGetProperty("partName", out var pn) ? pn.GetString() ?? "" : "소켓 상판";
                string m = p.TryGetProperty("material", out var mat) ? mat.GetString() ?? "" : "";
                partName = string.IsNullOrEmpty(m) ? n : $"{n} ({m})";
                if (p.TryGetProperty("partId", out var pid)) partId = pid.GetInt32();
            }
        }

        if (_canvas.Nodes.Count == 0)
        {
            // Primary Lane (e.g. R01)
            var n1 = new WorkflowNode { OrderId = orderId, OrderNumber = orderNo, PartId = partId, PartName = partName, Title = "프로그램 작성 & 도면 확인", Category = NodeCategory.Preparation, Status = NodeStatus.Ready, MachineId = laneMachine, Assignee = _session.DisplayName, X = 40, Y = startY, Details = "CAD/CAM 도면 검토 및 NC프로그램 시뮬레이션 완료" };
            var n2 = new WorkflowNode { OrderId = orderId, OrderNumber = orderNo, PartId = partId, PartName = partName, Title = "소재 준비 & 1차 원점 세팅", Category = NodeCategory.Preparation, Status = NodeStatus.Ready, MachineId = laneMachine, Assignee = _session.DisplayName, X = 280, Y = startY, Details = "바이스 지그 장착 및 공작물 G54 좌표계 정밀 설정" };
            var n3 = new WorkflowNode { OrderId = orderId, OrderNumber = orderNo, PartId = partId, PartName = partName, Title = "1차 전면 황삭 (면가공)", Category = NodeCategory.Machining, Status = NodeStatus.Ready, MachineId = laneMachine, Assignee = _session.DisplayName, X = 520, Y = startY, Details = "엔드밀 고속 황삭 포켓 가공 진행" };
            var n4 = new WorkflowNode { OrderId = orderId, OrderNumber = orderNo, PartId = partId, PartName = partName, Title = "초물 치수 중간측정", Category = NodeCategory.Inspection, Status = NodeStatus.Ready, MachineId = "측정실", Assignee = "품질팀", X = 760, Y = startY, Details = "초품 두께 및 공차 치수 5.01mm 측정 합격" };

            // Sub-worker task node connecting R01 and R02
            var nSub = new WorkflowNode { OrderId = orderId, OrderNumber = orderNo, PartId = partId, PartName = partName, Title = "서브인원 소재이송 & 2차세팅", Category = NodeCategory.Collaboration, Status = NodeStatus.Ready, MachineId = "R02", Assignee = "이서브", SubWorkerName = "이서브", SubWorkerInstruction = "1차 가공품 R02로 이송 후 바이스 장착 및 2차 세팅 지원", X = 760, Y = startY + 130, Details = "1차 가공품 바이스 분리 후 R02호기로 운반 및 배면 고정" };

            // 2nd stage on R02
            var n5 = new WorkflowNode { OrderId = orderId, OrderNumber = orderNo, PartId = partId, PartName = partName, Title = "2차 배면 정밀 정삭", Category = NodeCategory.Machining, Status = NodeStatus.Ready, MachineId = "R02", Assignee = _session.DisplayName, X = 1010, Y = startY, Details = "초경 엔드밀 고속 정삭 및 표면 조도 가공" };

            // Post process & Final Inspection
            var n6 = new WorkflowNode { OrderId = orderId, OrderNumber = orderNo, PartId = partId, PartName = partName, Title = "초음파 세척 & 에어건 건조", Category = NodeCategory.PostProcess, Status = NodeStatus.Ready, MachineId = "세척실", Assignee = "이서브", X = 1260, Y = startY, Details = "절삭유 완벽 세척 후 보호비닐 포장" };
            var n7 = new WorkflowNode { OrderId = orderId, OrderNumber = orderNo, PartId = partId, PartName = partName, Title = "최종 검사 및 포장/납품", Category = NodeCategory.Inspection, Status = NodeStatus.Ready, MachineId = "품질팀", Assignee = "검사실", X = 1510, Y = startY, Details = "3차원 측정기 최종 검사 및 납품 대기" };

            _canvas.AddNode(n1);
            _canvas.AddNode(n2);
            _canvas.AddNode(n3);
            _canvas.AddNode(n4);
            _canvas.AddNode(nSub);
            _canvas.AddNode(n5);
            _canvas.AddNode(n6);
            _canvas.AddNode(n7);

            _canvas.AddEdge(n1.Id, n2.Id, EdgeType.Sequence);
            _canvas.AddEdge(n2.Id, n3.Id, EdgeType.Sequence);
            _canvas.AddEdge(n3.Id, n4.Id, EdgeType.Sequence);
            _canvas.AddEdge(n3.Id, nSub.Id, EdgeType.Delegation);
            _canvas.AddEdge(nSub.Id, n5.Id, EdgeType.Sequence);
            _canvas.AddEdge(n4.Id, n5.Id, EdgeType.Sequence);
            _canvas.AddEdge(n5.Id, n6.Id, EdgeType.Sequence);
            _canvas.AddEdge(n6.Id, n7.Id, EdgeType.Sequence);

            _canvas.SelectedNode = n1;
        }
        else
        {
            // Secondary Parallel Lane (e.g. for 2nd machine operated by same worker)
            var m1 = new WorkflowNode { OrderId = orderId, OrderNumber = orderNo, PartId = partId, PartName = partName, Title = $"[{laneMachine}] 2차 설비 소재 세팅", Category = NodeCategory.Preparation, Status = NodeStatus.Ready, MachineId = laneMachine, Assignee = _session.DisplayName, X = 280, Y = startY, Details = $"{laneMachine} 전용 지그 장착 및 공작물 세팅" };
            var m2 = new WorkflowNode { OrderId = orderId, OrderNumber = orderNo, PartId = partId, PartName = partName, Title = $"[{laneMachine}] 동시 병행 가공", Category = NodeCategory.Machining, Status = NodeStatus.Ready, MachineId = laneMachine, Assignee = _session.DisplayName, X = 520, Y = startY, Details = "다중 설비 병렬 가공 운전" };
            var m3 = new WorkflowNode { OrderId = orderId, OrderNumber = orderNo, PartId = partId, PartName = partName, Title = $"[{laneMachine}] 중간 치수 점검", Category = NodeCategory.Inspection, Status = NodeStatus.Ready, MachineId = "측정실", Assignee = "품질팀", X = 760, Y = startY, Details = "마이크로미터 정밀 치수 검사" };

            // Find an earlier node from primary lane to connect subworker flow
            var prevMachining = _canvas.Nodes.FirstOrDefault(n => n.Category == NodeCategory.Machining);

            _canvas.AddNode(m1);
            _canvas.AddNode(m2);
            _canvas.AddNode(m3);

            _canvas.AddEdge(m1.Id, m2.Id, EdgeType.Sequence);
            _canvas.AddEdge(m2.Id, m3.Id, EdgeType.Sequence);

            if (prevMachining != null)
            {
                // Subworker direction line connecting 1st machine and 2nd machine!
                _canvas.AddEdge(prevMachining.Id, m1.Id, EdgeType.Delegation);
            }

            _canvas.SelectedNode = m1;
        }

        UpdateWorkflowStats();
        SaveWorkflowDraftLocally(silent: true);
    }

    private void SaveWorkflowDraftLocally(bool silent = false)
    {
        try
        {
            string cacheKey = GetCurrentPartCacheKey();
            string cacheFile = Path.Combine(_localWorkflowDir, $"{cacheKey}.json");

            var data = new WorkflowData
            {
                OrderNumber = _cachedOrders.Count > 0 && _cboOrders.SelectedIndex >= 0 ? _cachedOrders[_cboOrders.SelectedIndex].GetProperty("orderNumber").GetString() ?? "" : "",
                PartName = _lstParts.SelectedItem?.ToString() ?? "",
                Nodes = _canvas.Nodes.ToList(),
                Edges = _canvas.Edges.ToList(),
                LastSavedAt = DateTime.Now
            };

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(cacheFile, json);

            if (!silent)
            {
                MessageBox.Show(
                    $"[로컬 임시저장 완료]\n작업지시서: {data.OrderNumber}\n부품: {data.PartName}\n총 {_canvas.Nodes.Count}개 노드가 클라이언트 로컬 초안으로 안전하게 저장되었습니다.",
                    "임시저장 완료",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show($"임시저장 중 오류가 발생했습니다: {ex.Message}", "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ConfirmAndShareWorkflowAsync()
    {
        if (_canvas.Nodes.Count == 0)
        {
            MessageBox.Show("확정할 워크플로우 노드가 없습니다. 먼저 공정을 생성해주세요.", "공정 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"현재 수립된 총 {_canvas.Nodes.Count}개의 공정 계획을 확정하고 중앙 서버 작업관리 시스템에 공유하시겠습니까?",
            "계획 확정 및 서버 공유",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question
        );
        if (confirm != DialogResult.Yes) return;

        // 1. Save local confirmed draft
        SaveWorkflowDraftLocally(silent: true);

        // 2. Broadcast / Log plan confirmation to existing Server API
        string orderNo = _canvas.Nodes[0].OrderNumber;
        string partName = _canvas.Nodes[0].PartName;
        string summaryFlow = string.Join(" → ", _canvas.Nodes.Take(5).Select(n => n.Title));
        if (_canvas.Nodes.Count > 5) summaryFlow += $" 외 {_canvas.Nodes.Count - 5}개 공정";

        try
        {
            bool ok = await _api.AddManualWorkLogAsync(
                _currentMachineId,
                orderNo,
                partName,
                $"[워크플로우 계획 확정] {summaryFlow}",
                "대기",
                _session.DisplayName,
                null,
                1
            );

            if (ok)
            {
                await LoadHistoryGridsAsync();
                MessageBox.Show(
                    $"[계획 확정 및 서버 공유 완료]\n\n작업지시서: {orderNo}\n대상 부품: {partName}\n공정 수: {_canvas.Nodes.Count}개\n\n중앙 서버 작업 관리 시스템에 작업 계획 확정 로그가 정상 등록되었습니다.\n(그래픽 캔버스 구조는 클라이언트 로컬 캐시에 저장되며, 서버 전체 동기화가 유지됩니다)",
                    "서버 공유 완료",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            else
            {
                MessageBox.Show(
                    "서버 통신에 응답이 없습니다. 네트워크 연결 또는 서버 상태를 확인 후 다시 시도해주세요.",
                    "서버 공유 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"서버 전송 중 오류 발생: {ex.Message}", "전송 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ExecuteSelectedWorkflowNodeAsync()
    {
        var node = _canvas.SelectedNode;
        if (node == null)
        {
            // Pick first ready or pending node
            node = _canvas.Nodes.FirstOrDefault(n => n.Status == NodeStatus.Ready) ?? _canvas.Nodes.FirstOrDefault();
        }

        if (node == null)
        {
            MessageBox.Show("실행할 작업 노드가 없습니다.", "노드 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var ans = MessageBox.Show(
            $"선택된 공정 [{node.Title}]의 실제 작업을 시작하시겠습니까?\n\n장비: {node.MachineId}\n작지: {node.OrderNumber}\n부품: {node.PartName}\n담당자: {_session.DisplayName}",
            "선택 작업 가공 시작",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question
        );
        if (ans != DialogResult.Yes) return;

        // Change status to running & call server API
        await HandleNodeStatusChangeAsync(node, NodeStatus.Running);
        _detailPanel.BindNode(node);
        _canvas.Invalidate();
    }

    private async Task SendSubWorkerTaskAsync(WorkflowNode node, string subWorker, string instruction)
    {
        try
        {
            bool ok = await _api.CreateSubWorkerTaskAsync(
                node.MachineId,
                node.OrderNumber,
                node.PartName,
                _session.Username,
                subWorker,
                instruction
            );

            if (ok)
            {
                node.IsSubWorkerTaskSent = true;
                node.SubWorkerName = subWorker;
                node.SubWorkerInstruction = instruction;
                _detailPanel.MarkSubWorkerTaskSent();

                await LoadHistoryGridsAsync();
                MessageBox.Show(
                    $"서브인원 [{subWorker}]에게 작업 지시가 성공적으로 전송되었습니다!\n\n장비: {node.MachineId}\n내용: {instruction}",
                    "서브인원 지시 발송 완료",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            else
            {
                MessageBox.Show("서버에서 서브인원 지시 등록을 거부했습니다. 서버 상태를 확인하세요.", "지시 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"서브인원 작업 지시 전송 중 오류: {ex.Message}", "전송 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

    private void UpdateWorkflowStats()
    {
        int running = _canvas.Nodes.Count(n => n.Status == NodeStatus.Running);
        int check = _canvas.Nodes.Count(n => n.Status == NodeStatus.PendingCheck);
        int completed = _canvas.Nodes.Count(n => n.Status == NodeStatus.Completed);
        int issue = _canvas.Nodes.Count(n => n.Status == NodeStatus.Issue);

        _lblWorkflowStats.Text = $"워크플로우: 진행중 {running}건 | 측정 {check}건 | 완료 {completed}건 | 이슈 {issue}건";
    }

    // =========================================================================
    // SERVER API SYNC & WORKFLOW PROGRESS ADVANCEMENT
    // =========================================================================
    private async Task HandleNodeStatusChangeAsync(WorkflowNode node, NodeStatus newStatus)
    {
        try
        {
            WorkflowNode? nextActivatedNode = null;

            // When current node is Completed (1차완료 확정), advance next connected steps to Running/Inspection!
            if (newStatus == NodeStatus.Completed)
            {
                node.CompletedAt = DateTime.Now;

                // Trace outgoing edges to find next process steps
                var outgoing = _canvas.Edges.Where(e => e.FromNodeId == node.Id).ToList();
                foreach (var edge in outgoing)
                {
                    var nextNode = _canvas.Nodes.FirstOrDefault(n => n.Id == edge.ToNodeId);
                    if (nextNode != null && nextNode.Status == NodeStatus.Ready)
                    {
                        // Set next step status (Inspection -> 측정, Machining/Prep/Post -> 진행중)
                        if (nextNode.Category == NodeCategory.Inspection)
                            nextNode.Status = NodeStatus.PendingCheck;
                        else
                            nextNode.Status = NodeStatus.Running;

                        if (nextNode.StartedAt == null)
                            nextNode.StartedAt = DateTime.Now;

                        nextActivatedNode ??= nextNode;
                    }
                }

                if (nextActivatedNode != null)
                {
                    _canvas.SelectedNode = nextActivatedNode;
                    _detailPanel.BindNode(nextActivatedNode);
                }
            }

            string serverStatus = newStatus switch
            {
                NodeStatus.Running => "가공중",
                NodeStatus.PendingCheck => "가공중",
                NodeStatus.Completed => (nextActivatedNode != null ? "가공중" : "완료"),
                NodeStatus.Issue => "정지",
                _ => "대기"
            };

            string statusPrompt = newStatus switch
            {
                NodeStatus.Running => $"[진행중] {node.Title}",
                NodeStatus.PendingCheck => $"[측정중] {node.Title}",
                NodeStatus.Completed => (nextActivatedNode != null ? $"[1차완료] {node.Title} ➔ 다음: [{nextActivatedNode.Title}]" : $"[완료] {node.Title}"),
                NodeStatus.Issue => $"[이슈] {node.Title}",
                _ => $"[대기] {node.Title}"
            };

            // 1. Call existing Server API: AddManualWorkLogAsync
            await _api.AddManualWorkLogAsync(
                node.MachineId,
                node.OrderNumber,
                node.PartName,
                $"[{WorkflowCanvasControl.GetCategoryKorean(node.Category)}] {node.Title} ({statusPrompt})" + (string.IsNullOrWhiteSpace(node.Details) ? "" : $" - {node.Details}"),
                serverStatus,
                _session.DisplayName,
                node.Assignee,
                1
            );

            // 2. Update machine status on server (reflecting directly to classic dashboard & web dashboard)
            await _api.UpdateMachineStatusAsync(
                node.MachineId,
                serverStatus,
                node.OrderNumber,
                node.PartName,
                statusPrompt,
                _session.DisplayName
            );

            // 3. If issue, register defect record
            if (newStatus == NodeStatus.Issue)
            {
                await _api.CreateDefectAndCountermeasureAsync(
                    $"{node.OrderNumber} / {node.PartName}",
                    node.MachineId,
                    "가공중",
                    "치수불량 / 현장점검요청",
                    $"워크플로우 노드 [{node.Title}] 긴급 이슈 등록: {node.Details}",
                    "기구 정밀점검 및 원점/공구옵셋 재측정",
                    "장비 긴급 정지 후 담당자 확인 조치",
                    _session.DisplayName
                );
            }

            // Immediately refresh data
            await LoadMachinesAsync();
            await LoadHistoryGridsAsync();
            UpdateWorkflowStats();
            _canvas.Invalidate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NodeStatusChange Error] {ex.Message}");
        }
    }

    // HISTORY GRIDS, NOTICES, CHAT
    // =========================================================================
    private async Task LoadHistoryGridsAsync()
    {
        try
        {
            // 1. Work logs
            var logs = await _api.GetMachineWorkLogsAsync(limit: 50);
            if (logs != null)
            {
                _gridHistory.Rows.Clear();
                foreach (var log in logs)
                {
                    string time = log.TryGetProperty("timestamp", out var t) ? t.GetString() ?? "" : "";
                    if (DateTime.TryParse(time, out var dt)) time = dt.ToString("MM-dd HH:mm");
                    string mach = log.TryGetProperty("machineId", out var m) ? m.GetString() ?? "" : "";
                    string order = log.TryGetProperty("orderNumber", out var o) ? o.GetString() ?? "" : "";
                    string part = log.TryGetProperty("partName", out var p) ? p.GetString() ?? "" : "";
                    string detail = log.TryGetProperty("workDetail", out var d) ? d.GetString() ?? "" : "";
                    string worker = log.TryGetProperty("workerName", out var w) ? w.GetString() ?? "" : "";
                    string status = log.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";

                    _gridHistory.Rows.Add(time, mach, order, part, detail, worker, status);
                }
            }

            // 2. Defects
            var defects = await _api.GetAllPermanentEventsAsync();
            if (defects != null)
            {
                _gridDefects.Rows.Clear();
                foreach (var def in defects)
                {
                    string id = def.TryGetProperty("id", out var i) ? i.GetInt32().ToString() : "";
                    string mach = def.TryGetProperty("machineId", out var m) ? m.GetString() ?? "" : "";
                    string part = def.TryGetProperty("partCode", out var p) ? p.GetString() ?? "" : "";
                    string type = def.TryGetProperty("eventType", out var et) ? et.GetString() ?? "" : "";
                    string desc = def.TryGetProperty("description", out var ds) ? ds.GetString() ?? "" : "";
                    string cause = def.TryGetProperty("rootCause", out var rc) ? rc.GetString() ?? "" : "";
                    string action = def.TryGetProperty("actionTaken", out var act) ? act.GetString() ?? "" : "";
                    string author = def.TryGetProperty("author", out var au) ? au.GetString() ?? "" : "";

                    _gridDefects.Rows.Add(id, mach, part, type, desc, cause, action, author);
                }
            }

            // 3. Sub Worker Tasks
            var subTasks = await _api.GetSubWorkerTasksAsync();
            if (subTasks != null)
            {
                _gridSubTasks.Rows.Clear();
                foreach (var st in subTasks)
                {
                    string id = st.TryGetProperty("id", out var i) ? i.GetInt32().ToString() : "";
                    string mach = st.TryGetProperty("machineId", out var m) ? m.GetString() ?? "" : "";
                    string order = st.TryGetProperty("orderNumber", out var o) ? o.GetString() ?? "" : "";
                    string part = st.TryGetProperty("partName", out var p) ? p.GetString() ?? "" : "";
                    string worker = st.TryGetProperty("subWorkerName", out var sw) ? sw.GetString() ?? "" : "";
                    string instr = st.TryGetProperty("instruction", out var ins) ? ins.GetString() ?? "" : "";
                    string report = st.TryGetProperty("reportContent", out var rep) ? rep.GetString() ?? "" : "";
                    string status = st.TryGetProperty("status", out var stt) ? stt.GetString() ?? "" : "";

                    _gridSubTasks.Rows.Add(id, mach, order, part, worker, instr, report, status);
                }
            }

            // 4. Post Process
            var posts = await _api.GetPostProcessReportsAsync();
            if (posts != null)
            {
                _gridPostProcess.Rows.Clear();
                foreach (var pp in posts)
                {
                    string id = pp.TryGetProperty("id", out var i) ? i.GetInt32().ToString() : "";
                    string order = pp.TryGetProperty("orderNumber", out var o) ? o.GetString() ?? "" : "";
                    string part = pp.TryGetProperty("partName", out var p) ? p.GetString() ?? "" : "";
                    string mach = pp.TryGetProperty("machineId", out var m) ? m.GetString() ?? "" : "";
                    string worker = pp.TryGetProperty("worker", out var w) ? w.GetString() ?? "" : "";
                    string procs = pp.TryGetProperty("selectedProcesses", out var sp) ? sp.GetString() ?? "" : "";
                    string status = pp.TryGetProperty("status", out var stt) ? stt.GetString() ?? "" : "";

                    _gridPostProcess.Rows.Add(id, order, part, mach, worker, procs, status);
                }
            }
        }
        catch { }
    }

    private (string channelType, string topicKey) GetCurrentChatTopic()
    {
        var sel = _cboChatChannel.SelectedItem?.ToString() ?? "";
        if (sel.Contains("R01")) return ("MACHINE", "R01");
        if (sel.Contains("R02")) return ("MACHINE", "R02");
        if (sel.Contains("이슈") || sel.Contains("품질")) return ("GENERAL", "품질이슈");
        return ("GENERAL", "공통");
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
                    string title = n.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    string date = n.TryGetProperty("createdAt", out var cd) ? cd.GetString() ?? "" : "";
                    string acks = n.TryGetProperty("officialAcksJson", out var ak) ? ak.GetString() ?? "" : "";
                    bool isAcked = acks.Contains(_session.Username) || acks.Contains(_session.DisplayName);

                    if (DateTime.TryParse(date, out var dt)) date = dt.ToString("MM-dd HH:mm");
                    string badge = isAcked ? "✔" : "🔔";
                    if (!isAcked) unread++;

                    _lstNotices.Items.Add($"{badge} {title} ({date})");
                }
                _lblNoticeBadge.Text = unread > 0 ? $"{unread}건 미확인" : "전원 확인완료";
                _lblNoticeBadge.ForeColor = unread > 0 ? Palette.Amber : Palette.Green;
            }
        }
        catch { }
    }

    private async Task ShowNoticesDialogAsync()
    {
        var notices = await _api.GetNoticesAsync();

        using var dlg = new Form
        {
            Text = "📢 현장 공지 및 긴급 지시사항 확인 (전자 서명)",
            Size = new Size(820, 520),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Palette.Bg950,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.5f)
        };

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Palette.Bg900,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Palette.Bg800;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.TextSlate300;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = Palette.Bg900;
        grid.DefaultCellStyle.ForeColor = Palette.TextWhite;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(8, 47, 73);
        grid.DefaultCellStyle.SelectionForeColor = Palette.AccentSky;

        grid.Columns.Add("Id", "No");
        grid.Columns["Id"]!.FillWeight = 35;
        grid.Columns.Add("Priority", "중요도");
        grid.Columns["Priority"]!.FillWeight = 50;
        grid.Columns.Add("Title", "공지 제목");
        grid.Columns["Title"]!.FillWeight = 120;
        grid.Columns.Add("Content", "지시 내용");
        grid.Columns["Content"]!.FillWeight = 180;
        grid.Columns.Add("AuthorTime", "작성자 / 일시");
        grid.Columns["AuthorTime"]!.FillWeight = 95;
        grid.Columns.Add("MyAck", "내 확인 서명");
        grid.Columns["MyAck"]!.FillWeight = 75;

        void PopulateGrid(List<JsonElement> list)
        {
            grid.Rows.Clear();
            foreach (var n in list)
            {
                int id = n.GetProperty("noticeId").GetInt32();
                string pri = n.GetProperty("priority").GetString() ?? "일반";
                string title = n.GetProperty("title").GetString() ?? "";
                string content = n.GetProperty("content").GetString() ?? "";
                string author = n.GetProperty("author").GetString() ?? "관리자";
                string createdAt = n.GetProperty("createdAt").GetString() ?? "";
                string acks = n.TryGetProperty("officialAcksJson", out var ak) ? ak.GetString() ?? "" : "";
                bool isAcked = acks.Contains(_session.Username) || acks.Contains(_session.DisplayName);

                int r = grid.Rows.Add(id, pri, title, content, $"{author} ({createdAt[..Math.Min(16, createdAt.Length)]})", isAcked ? "✔ 서명완료" : "❌ 미확인");
                if (pri == "긴급")
                {
                    grid.Rows[r].Cells["Priority"].Style.ForeColor = Palette.Rose;
                    grid.Rows[r].Cells["Priority"].Style.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
                }
                if (!isAcked)
                {
                    grid.Rows[r].Cells["MyAck"].Style.ForeColor = Palette.Amber;
                    grid.Rows[r].Cells["MyAck"].Style.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
                }
                else
                {
                    grid.Rows[r].Cells["MyAck"].Style.ForeColor = Palette.Green;
                }
            }
        }

        PopulateGrid(notices);

        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(10, 6, 10, 6), BackColor = Palette.Bg900 };
        var btnAck = CreateFlatButton("✍ 선택 공지사항 확인 서명하기", Palette.Green, Color.White, 220, 34);
        btnAck.Dock = DockStyle.Left;
        btnAck.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);

        btnAck.Click += async (s, e) =>
        {
            if (grid.SelectedRows.Count == 0) return;
            int nId = (int)grid.SelectedRows[0].Cells["Id"].Value;
            bool ok = await _api.AcknowledgeNoticeAsync(nId, _session.Username);
            if (ok)
            {
                MessageBox.Show("공지사항 확인 서명이 서버에 정상 기록되었습니다.", "확인 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                var refreshed = await _api.GetNoticesAsync();
                PopulateGrid(refreshed);
                await LoadNoticesAsync();
            }
        };

        var btnClose = CreateFlatButton("닫기", Palette.Bg800, Palette.TextSlate300, 90, 34);
        btnClose.Dock = DockStyle.Right;
        btnClose.Click += (s, e) => dlg.Close();

        bottomBar.Controls.AddRange(new Control[] { btnAck, btnClose });
        dlg.Controls.Add(grid);
        dlg.Controls.Add(bottomBar);

        dlg.ShowDialog(this);
    }

    private async Task LoadChatAsync()
    {
        try
        {
            var (cType, tKey) = GetCurrentChatTopic();
            var list = await _api.GetChatMessagesByChannelAsync(cType, tKey);
            if (list != null)
            {
                _lstChat.Items.Clear();
                foreach (var msg in list)
                {
                    string author = msg.TryGetProperty("author", out var s) ? s.GetString() ?? "" : "시스템";
                    string content = msg.TryGetProperty("content", out var m) ? m.GetString() ?? "" : "";
                    string createdAt = msg.TryGetProperty("createdAt", out var cd) ? cd.GetString() ?? "" : "";
                    if (DateTime.TryParse(createdAt, out var dt)) createdAt = dt.ToString("HH:mm");
                    _lstChat.Items.Add($"[{author}] {content} ({createdAt})");
                }
                if (_lstChat.Items.Count > 0)
                {
                    _lstChat.TopIndex = _lstChat.Items.Count - 1;
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
            var (cType, tKey) = GetCurrentChatTopic();
            await _api.SendChatMessageAsync(tKey, _session.DisplayName, text, cType);
            await LoadChatAsync();
        }
        catch { }
    }

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

        var lblHeader = new Label
        {
            Text = title,
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold),
            ForeColor = Palette.TextWhite,
            Dock = DockStyle.Top,
            Height = 22
        };
        pnl.Controls.Add(lblHeader);
        return pnl;
    }

    private Button CreateFlatButton(string text, Color bg, Color fg, int width = 0, int height = 0)
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
        if (width > 0 && height > 0)
        {
            btn.Size = new Size(width, height);
        }
        else
        {
            btn.Dock = DockStyle.Fill;
        }
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private DataGridView CreateStyledGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Palette.Bg900,
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
        grid.ColumnHeadersDefaultCellStyle.BackColor = Palette.Bg850;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.AccentSky;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = Palette.Bg900;
        grid.DefaultCellStyle.ForeColor = Palette.TextWhite;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 14, 165, 233);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        return grid;
    }

    // =========================================================================
    // VISUAL VERIFICATION HARNESS (Populate realistic sample data)
    // =========================================================================
    public void PopulateSampleWorkflowForVisual()
    {
        _canvas.Nodes.Clear();
        _canvas.Edges.Clear();

        string orderNo = "WO-2026-002";
        string partName = "소켓 상판 (PEEK 5T)";

        var n1 = new WorkflowNode { OrderId = 100, OrderNumber = orderNo, PartId = 1, PartName = partName, Title = "프로그램 작성 & 도면 확인", Category = NodeCategory.Preparation, Status = NodeStatus.Completed, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 40, Y = 100, Details = "CAD/CAM 도면 검토 및 NC프로그램 시뮬레이션 완료" };
        var n2 = new WorkflowNode { OrderId = 100, OrderNumber = orderNo, PartId = 1, PartName = partName, Title = "소재 세팅 & 기준좌표 설정", Category = NodeCategory.Preparation, Status = NodeStatus.Completed, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 290, Y = 100, Details = "PEEK 5T 바이스 지그 장착, G54 원점 세팅 완료" };
        var n3 = new WorkflowNode { OrderId = 100, OrderNumber = orderNo, PartId = 1, PartName = partName, Title = "1차 전면 황삭 (면가공)", Category = NodeCategory.Machining, Status = NodeStatus.Completed, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 540, Y = 100, Details = "10파이 엔드밀 포켓 깊이 황삭 진행" };
        var n4 = new WorkflowNode { OrderId = 100, OrderNumber = orderNo, PartId = 1, PartName = partName, Title = "초물 치수 중간측정", Category = NodeCategory.Inspection, Status = NodeStatus.Completed, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 790, Y = 100, Details = "버니어 및 마이크로미터 두께 5.01mm 측정 합격" };
        var n5 = new WorkflowNode { OrderId = 100, OrderNumber = orderNo, PartId = 1, PartName = partName, Title = "2차 전면 정밀 정삭", Category = NodeCategory.Machining, Status = NodeStatus.Running, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 1040, Y = 100, Details = "초경 엔드밀 고속 정삭 가공 진행 중 (현재 75%)", NextProcess = "초음파 세척 및 건조" };
        var n6 = new WorkflowNode { OrderId = 100, OrderNumber = orderNo, PartId = 1, PartName = partName, Title = "서브인원 면취 및 Burr 제거", Category = NodeCategory.Collaboration, Status = NodeStatus.Ready, MachineId = "서브", Assignee = "김기술", SubWorkerName = "김기술", SubWorkerInstruction = "외곽 R0.2 C면취 및 잔여 Burr 제거 지시", X = 790, Y = 260, Details = "외곽 R0.2 C면취 및 잔여 Burr 제거 지시", IsSubWorkerTaskSent = true };
        var n7 = new WorkflowNode { OrderId = 100, OrderNumber = orderNo, PartId = 1, PartName = partName, Title = "초음파 세척 및 포장/납품", Category = NodeCategory.PostProcess, Status = NodeStatus.Ready, MachineId = "후가공", Assignee = "포장팀", X = 1290, Y = 100, Details = "이물질 세척 후 보호비닐 포장" };
        var n8 = new WorkflowNode { OrderId = 100, OrderNumber = orderNo, PartId = 1, PartName = partName, Title = "포켓 깊이 치수 주의", Category = NodeCategory.Issue, Status = NodeStatus.Issue, MachineId = _currentMachineId, Assignee = _session.DisplayName, X = 1040, Y = 260, Details = "엔드밀 마모 시 Z축 공차 +0.02mm 주의 요망" };

        _canvas.Nodes.AddRange(new[] { n1, n2, n3, n4, n5, n6, n7, n8 });

        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n1.Id, ToNodeId = n2.Id, Type = EdgeType.Sequence });
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n2.Id, ToNodeId = n3.Id, Type = EdgeType.Sequence });
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n3.Id, ToNodeId = n4.Id, Type = EdgeType.Sequence });
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n4.Id, ToNodeId = n5.Id, Type = EdgeType.Sequence });
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n5.Id, ToNodeId = n7.Id, Type = EdgeType.Sequence });
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n3.Id, ToNodeId = n6.Id, Type = EdgeType.Delegation });
        _canvas.Edges.Add(new WorkflowEdge { FromNodeId = n5.Id, ToNodeId = n8.Id, Type = EdgeType.IssueLink });

        // Left Machines
        _cachedMachines.Clear();
        using (var doc = JsonDocument.Parse(@"[
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
        _cboOrders.Items.Add("[WO-2026-002] SK하이닉스 (진행중)");
        _cboOrders.Items.Add("[WO-2026-001] 삼성전자 (완료)");
        _cboOrders.SelectedIndex = 0;

        _lstParts.Items.Clear();
        _lstParts.Items.Add("[1] 소켓 상판 (PEEK 5T) — 2EA [단품]");
        _lstParts.Items.Add("[2] 가이드 핀 블록 (SUS304 10T) — 4EA [양산]");
        _lstParts.SelectedIndex = 0;

        // Sample Notices
        _lstNotices.Items.Clear();
        _lstNotices.Items.Add("📢 [공지] 3공구 엔드밀 교체 주기 준수 요망 (09:15)");
        _lblNoticeBadge.Text = "1건 확인";

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

        // Sample Subtasks Row
        _gridSubTasks.Rows.Clear();
        _gridSubTasks.Rows.Add("1", "R01", "WO-2026-002", "소켓 상판", "김기술", "외곽 R0.2 C면취 및 잔여 Burr 제거 지시", "C면취 완료, 잔여 버 제거 중", "진행중");

        // Select node and bind detail
        _canvas.SelectedNode = n5;
        _detailPanel.BindNode(n5);

        _canvas.Invalidate();
        UpdateWorkflowStats();
    }
}
