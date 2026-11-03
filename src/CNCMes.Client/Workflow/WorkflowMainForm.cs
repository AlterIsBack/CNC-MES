using System.Data;
using System.Drawing.Drawing2D;
using System.Text.Json;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client.Workflow;

public class WorkflowMainForm : Form
{
    public static class Palette
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
        public static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
    }

    private readonly ApiClient _api;
    private readonly UserSession _session;

    // 1. Top Summary Bar
    private Label _lblWorkflowStats = null!;
    private Label _lblActiveMachineSummary = null!;
    private Label _lblLiveClock = null!;
    private Button _btnToggleTimelineGuides = null!;
    private Button _btnTimelineBaseConfig = null!;
    private Button _btnTimelineInterval = null!;
    private FlowLayoutPanel _pnlSubWorkerCards = null!;
    private Button _btnRefreshAll = null!;
    private Button _btnManualRefresh = null!;
    private Button _btnAutoRefreshToggle = null!;
    private int _autoRefreshSeconds = 5;
    private bool _isAutoRefreshEnabled = true;

    private readonly HashSet<string> _knownWorkOrderNumbers = new();
    private readonly HashSet<string> _knownNoticeTitles = new();
    private readonly HashSet<string> _knownChatSignatures = new();
    private bool _isInitialDataLoaded = false;

    // Dedicated Fast Chat Polling Timer & Diff Caches (Anti-flicker)
    private System.Windows.Forms.Timer _chatPollTimer = null!;
    private string _lastMachineSummary = "";
    private string _lastOrdersSummary = "";
    private string _lastNoticesSummary = "";
    private string _lastHistorySummary = "";
    private string _lastDefectsSummary = "";
    private string _lastSubTasksSummary = "";
    private string _lastPostProcessSummary = "";
    private string _lastChatSummary = "";
    private Button _btnOpenNoticesTop = null!;
    private Label _lblAutoSaveBadge = null!;
    private Label _lblTopNoticeBanner = null!;

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
    private Button _btnToggleBottom = null!;
    private Button _btnConfirmPlan = null!;
    private Label _lblConfirmedStatusBadge = null!;
    private bool _isPlanConfirmed = false;
    private DateTime? _planConfirmedAt;

    private System.Windows.Forms.Timer _pollTimer = null!;

    // Local workflow cache folder
    private readonly string _localWorkflowDir;
    private readonly WorkflowStorageManager _storageManager;
    private WorkUnitJob? _currentJob;

    public WorkflowMainForm(ApiClient api, UserSession session)
    {
        _api = api;
        _session = session;

        _localWorkflowDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CNCMes", "workflows");
        try { Directory.CreateDirectory(_localWorkflowDir); } catch { }

        _storageManager = new WorkflowStorageManager(_localWorkflowDir);
        _storageManager.StatusChanged += (s, e) =>
        {
            if (_lblAutoSaveBadge == null || _lblAutoSaveBadge.IsDisposed) return;
            if (InvokeRequired)
            {
                try { Invoke(new Action(() => UpdateSaveBadge(e))); } catch { }
            }
            else
            {
                UpdateSaveBadge(e);
            }
        };

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
        Text = $"CNC-MES 스마트 현장 터미널 v1.6.0 [워크플로우 제어] - {_session.DisplayName} ({_session.Role})";
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
        mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330f)); // Left Sidebar
        mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // Center Workspace
        mainContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390f)); // Right Sidebar
        mainContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        mainContainer.Controls.Add(BuildLeftSidebar(), 0, 0);
        mainContainer.Controls.Add(BuildCenterWorkspace(), 1, 0);
        mainContainer.Controls.Add(BuildRightSidebar(), 2, 0);

        rootTable.Controls.Add(mainContainer, 0, 1);
        Controls.Add(rootTable);

        // Polling Timer (3.5s)
        KeyPreview = true;
        _canvas.TimelineIntervalChanged += (newPx) => UpdateTimelineIntervalButtonText();

        _pollTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _pollTimer.Tick += async (s, e) => await PollRealtimeDataAsync();

        // 2초 전용 고속 채팅 폴링 타이머 (메시지 입력 없이도 가공자 채팅 실시간 수신)
        _chatPollTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _chatPollTimer.Tick += async (s, e) => await PollChatDirectAsync();
        _chatPollTimer.Start();

        EnableDoubleBuffering(this);

        Shown += async (s, e) => await OnFormShownAsync();
    }

    private async Task OnFormShownAsync()
    {
        await LoadMachinesAsync();
        await LoadWorkOrdersAsync();
        await LoadNoticesAsync();
        await LoadChatAsync();
        await LoadHistoryGridsAsync();
        TryRestoreAutoSavedWorkflow();
        _isInitialDataLoaded = true;

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
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 680f)); // Actions & View Switch
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
        var lblVer = new Label { Text = "v1.6.0 [Workflow]", Location = new Point(136, 11), Font = new Font("Consolas", 8f, FontStyle.Bold), ForeColor = Palette.AccentSky, BackColor = Color.FromArgb(80, 8, 47, 73), AutoSize = true, Padding = new Padding(4, 1, 4, 1) };
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

        // Right Actions (AutoSave Badge, Notices, User Profile, Manual Refresh, Auto Refresh)
        var pnlRight = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 8, 4, 0)
        };

        _lblAutoSaveBadge = new Label
        {
            Text = "💾 실시간 자동저장 대기",
            Font = new Font("맑은 고딕", 8.0f, FontStyle.Bold),
            ForeColor = Palette.TextSlate400,
            BackColor = Color.FromArgb(40, 15, 23, 42),
            Size = new Size(130, 34),
            Margin = new Padding(3, 0, 3, 0),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var pnlUser = new Panel { Size = new Size(120, 34), Margin = new Padding(3, 0, 3, 0), BackColor = Palette.Bg800 };
        pnlUser.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlUser.Width - 1, pnlUser.Height - 1);
            using var brushBg = new SolidBrush(Color.FromArgb(40, 14, 165, 233));
            e.Graphics.FillRectangle(brushBg, 5, 5, 24, 24);
            using var fontA = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
            using var brushSky = new SolidBrush(Palette.AccentSky);
            e.Graphics.DrawString("a", fontA, brushSky, new PointF(12, 8));
            using var fontTxt = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
            using var brushTxt = new SolidBrush(Palette.TextSlate300);
            e.Graphics.DrawString($"[{_session.DisplayName}]", fontTxt, brushTxt, new PointF(33, 8));
        };

        _btnOpenNoticesTop = CreateFlatButton("📢 공지목록", Palette.Bg800, Palette.Amber, 90, 34);
        _btnOpenNoticesTop.Margin = new Padding(3, 0, 3, 0);
        _btnOpenNoticesTop.Click += async (s, e) => await ShowNoticesDialogAsync();

        _btnAutoRefreshToggle = CreateFlatButton("⚡ 자동: 5초 ▾", Palette.Bg800, Color.FromArgb(52, 211, 153), 108, 34);
        _btnAutoRefreshToggle.Margin = new Padding(3, 0, 3, 0);
        _btnAutoRefreshToggle.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        _btnAutoRefreshToggle.Click += (s, e) => ShowAutoRefreshMenu(_btnAutoRefreshToggle);

        _btnManualRefresh = CreateFlatButton("🔄 새로고침 (F5)", Palette.Bg800, Palette.AccentSky, 125, 34);
        _btnManualRefresh.Margin = new Padding(3, 0, 3, 0);
        _btnManualRefresh.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
        _btnManualRefresh.Click += async (s, e) => await TriggerManualRefreshAsync();
        _btnRefreshAll = _btnManualRefresh;

        _lblConfirmedStatusBadge = new Label
        {
            Text = "🔒 [계획 확정]",
            Font = new Font("Consolas", 8.8f, FontStyle.Bold),
            ForeColor = Palette.Green,
            BackColor = Color.FromArgb(70, 6, 78, 59),
            Size = new Size(100, 34),
            Margin = new Padding(3, 0, 3, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            Visible = false
        };

        pnlRight.Controls.AddRange(new Control[] {
            _lblAutoSaveBadge,
            pnlUser,
            _btnOpenNoticesTop,
            _btnAutoRefreshToggle,
            _btnManualRefresh,
            _lblConfirmedStatusBadge
        });
        topBar.Controls.Add(pnlRight, 2, 0);

        return topBar;
    }


    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            _ = TriggerManualRefreshAsync();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }


    private static void EnableDoubleBuffering(Control c)
    {
        try
        {
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(c, true);
            foreach (Control child in c.Controls) EnableDoubleBuffering(child);
        }
        catch { }
    }

    private async Task PollChatDirectAsync()
    {
        try
        {
            await LoadChatAsync();
        }
        catch { }
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
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 295f)); // Card 1: 12 Action Buttons (Height 295px, 6 rows)
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Card 2: Orders & Parts List (Full remaining height)

        // Card 1: Workflow Menu Buttons (12 Action Buttons: Standard, Probecard, Template Save/Load, Execution, Layout, Rework, History, Draft, Reset)
        var menuGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Margin = new Padding(2, 4, 2, 2),
            Padding = new Padding(1)
        };
        menuGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        menuGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        for (int r = 0; r < 6; r++)
            menuGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 16.66f));

        var btnGenDefault = CreateFlatButton("⚡ 기본 워크플로우", Palette.Bg800, Palette.AccentSky);
        btnGenDefault.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnGenDefault.Click += (s, e) => GenerateStandardWorkflowForSelectedPart(confirmIfNotEmpty: false);

        var btnProbecard = CreateFlatButton("🎯 프로브카드 템플릿", Palette.Bg800, Color.FromArgb(245, 158, 11));
        btnProbecard.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnProbecard.Click += (s, e) => ApplyProbecardTemplate();

        var btnSaveTemplate = CreateFlatButton("💾 템플릿 저장", Palette.Bg800, Color.FromArgb(16, 185, 129));
        btnSaveTemplate.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnSaveTemplate.Click += (s, e) => SaveCurrentWorkflowAsTemplate();

        var btnLoadTemplate = CreateFlatButton("📂 템플릿 불러오기", Palette.Bg800, Color.FromArgb(168, 85, 247));
        btnLoadTemplate.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnLoadTemplate.Click += (s, e) => OpenLoadTemplateDialog();

        _btnConfirmPlan = CreateFlatButton("🔒 계획 확정", Color.FromArgb(8, 47, 73), Palette.AccentSky);
        _btnConfirmPlan.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        _btnConfirmPlan.Click += async (s, e) => await ConfirmAndShareWorkflowAsync();

        var btnExecuteSelected = CreateFlatButton("▶ 선택작업 실행", Color.FromArgb(5, 150, 105), Color.White);
        btnExecuteSelected.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnExecuteSelected.Click += async (s, e) => await ExecuteSelectedWorkflowNodeAsync();

        var btnAutoLayout = CreateFlatButton("⟲ 공정 자동정렬", Palette.Bg800, Palette.Green);
        btnAutoLayout.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnAutoLayout.Click += (s, e) => _canvas.AutoLayout();

        var btnTimeline = CreateFlatButton("⏱ 타임라인 정렬", Palette.Bg800, Color.FromArgb(14, 165, 233));
        btnTimeline.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnTimeline.Click += (s, e) => _canvas.ArrangeByTimeline();

        var btnSaveDraft = CreateFlatButton("💾 임시저장", Palette.Bg800, Color.FromArgb(52, 211, 153));
        btnSaveDraft.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnSaveDraft.Click += (s, e) => SaveWorkflowDraftLocally();

        var btnClearPlan = CreateFlatButton("🗑 전체 초기화", Palette.Bg800, Palette.Rose);
        btnClearPlan.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnClearPlan.Click += (s, e) =>
        {
            if (MessageBox.Show("현재 캔버스의 모든 노드와 연결선을 초기화하시겠습니까?", "초기화 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _canvas.Clear();
                ResetWorkflowConfirmation();
                UpdateWorkflowStats();
            }
        };

        menuGrid.Controls.Add(btnGenDefault, 0, 0);
        menuGrid.Controls.Add(btnProbecard, 1, 0);
        menuGrid.Controls.Add(btnSaveTemplate, 0, 1);
        menuGrid.Controls.Add(btnLoadTemplate, 1, 1);
        menuGrid.Controls.Add(_btnConfirmPlan, 0, 2);
        menuGrid.Controls.Add(btnExecuteSelected, 1, 2);
        menuGrid.Controls.Add(btnAutoLayout, 0, 3);
        menuGrid.Controls.Add(btnTimeline, 1, 3);

        var btnRework = CreateFlatButton("🔄 재가공 생성", Palette.Bg800, Color.FromArgb(251, 146, 60));
        btnRework.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnRework.Click += async (s, e) => await CreateReworkWorkflowActionAsync();

        var btnHistory = CreateFlatButton("📜 계획/이력 조회", Palette.Bg800, Color.FromArgb(147, 197, 253));
        btnHistory.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnHistory.Click += async (s, e) => await ShowJobHistoryDialogAsync();

        menuGrid.Controls.Add(btnRework, 0, 4);
        menuGrid.Controls.Add(btnHistory, 1, 4);
        menuGrid.Controls.Add(btnSaveDraft, 0, 5);
        menuGrid.Controls.Add(btnClearPlan, 1, 5);
        var cardMenu = CreateCard("📋 워크플로우 제어 메뉴", menuGrid);
        panel.Controls.Add(cardMenu, 0, 0);

        // Card 2: Work Orders & Parts List
        var pnlOrderBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(1)
        };
        pnlOrderBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlOrderBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f)); // Order combo
        pnlOrderBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Parts list

        _cboOrders = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 9f) };
        _cboOrders.SelectedIndexChanged += async (s, e) => await OnOrderSelectedAsync();
        pnlOrderBody.Controls.Add(_cboOrders, 0, 0);

        _lstParts = new ListBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Palette.TextWhite, BorderStyle = BorderStyle.FixedSingle, Font = new Font("맑은 고딕", 9f) };
        _lstParts.SelectedIndexChanged += (s, e) => OnPartSelected();
        _lstParts.DoubleClick += (s, e) => OnPartDoubleClicked();
        pnlOrderBody.Controls.Add(_lstParts, 0, 1);

        var cardOrders = CreateCard("📦 작업지시서 및 대상 부품 목록", pnlOrderBody);
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

        var btnSelectAll = CreateFlatButton("🔲 전체선택", Palette.Bg800, Palette.TextSlate300, 80, 28);
        btnSelectAll.Click += (s, e) => _canvas.SelectAllNodes();

        var btnAddTitleGroup = CreateFlatButton("📦 +타이틀 그룹", Palette.Bg800, Color.FromArgb(56, 189, 248), 115, 28);
        btnAddTitleGroup.Font = new Font("맑은 고딕", 8f, FontStyle.Bold);
        btnAddTitleGroup.Click += (s, e) => AddTitleGroupBox();

        _btnToggleTimelineGuides = CreateFlatButton("⏱ 타임라인선: 켜짐", Palette.Bg800, Color.FromArgb(14, 165, 233), 125, 28);
        _btnToggleTimelineGuides.Font = new Font("맑은 고딕", 8f, FontStyle.Bold);
        _btnToggleTimelineGuides.Click += (s, e) => ToggleTimelineGuides();

        _btnTimelineBaseConfig = CreateFlatButton("⏱ 기준: 공정맞춤 ▾", Palette.Bg800, Color.FromArgb(56, 189, 248), 135, 28);
        _btnTimelineBaseConfig.Font = new Font("맑은 고딕", 8f, FontStyle.Bold);
        _btnTimelineBaseConfig.Click += (s, e) => ShowTimelineBaseMenu(_btnTimelineBaseConfig);

        var btnIntervalDec = CreateFlatButton("➖", Palette.Bg800, Palette.TextSlate300, 28, 28);
        btnIntervalDec.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
        btnIntervalDec.Click += (s, e) =>
        {
            _canvas.ZoomTimelineInterval(-30f);
            UpdateTimelineIntervalButtonText();
            AutoSaveWorkflow();
        };

        _btnTimelineInterval = CreateFlatButton("⏱ 간격: 보통(280px) ▾", Palette.Bg800, Color.FromArgb(16, 185, 129), 145, 28);
        _btnTimelineInterval.Font = new Font("맑은 고딕", 8f, FontStyle.Bold);
        _btnTimelineInterval.Click += (s, e) => ShowTimelineIntervalMenu(_btnTimelineInterval);

        var btnIntervalInc = CreateFlatButton("➕", Palette.Bg800, Palette.TextSlate300, 28, 28);
        btnIntervalInc.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
        btnIntervalInc.Click += (s, e) =>
        {
            _canvas.ZoomTimelineInterval(30f);
            UpdateTimelineIntervalButtonText();
            AutoSaveWorkflow();
        };

        var btnTimelineTop = CreateFlatButton("⏱ 타임라인 정렬", Palette.Bg800, Color.FromArgb(14, 165, 233), 110, 28);
        btnTimelineTop.Click += (s, e) =>
        {
            _canvas.ArrangeByTimeline(autoFitBaseHour: !_canvas.CustomTimelineBaseHour.HasValue);
            UpdateTimelineBaseButtonText();
            AutoSaveWorkflow();
        };

        // Bottom History Toggle Button
        _btnToggleBottom = CreateFlatButton("🔽 하단창 숨기기", Palette.Bg800, Palette.AccentSky, 140, 28);
        _btnToggleBottom.Click += (s, e) => SetBottomPanelCollapsed(!_centerVerticalSplitter.Panel2Collapsed);

        canvasToolbar.Controls.AddRange(new Control[]
        {
            btnAddPrep, btnAddMach, btnAddInsp, btnAddCollab, btnAddPost, btnAddIssue,
            divTool, btnAddTitleGroup, _btnToggleTimelineGuides, _btnTimelineBaseConfig, btnIntervalDec, _btnTimelineInterval, btnIntervalInc, btnTimelineTop, btnZoomReset, btnSelectAll, _btnToggleBottom
        });

        // Canvas Control
        _canvas = new WorkflowCanvasControl { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _canvas.SelectionChanged += (nodes) => _detailPanel.BindNodes(nodes);
        _canvas.NodeSelected += (node) =>
        {
            if (_canvas.SelectedNodes.Count <= 1)
                _detailPanel.BindNode(node);
        };
        _canvas.NodeDoubleClicked += (node) =>
        {
            OpenNodeEditDialog(node);
        };
        _canvas.TitleBoxDoubleClicked += (box) =>
        {
            PromptEditTitleBox(box);
        };
        _canvas.WorkflowChanged += () =>
        {
            UpdateWorkflowStats();
        };
        _canvas.RequestGenerateDefaultWorkflow += () => GenerateStandardWorkflowForSelectedPart(confirmIfNotEmpty: true);

        pnlCanvasContainer.Controls.Add(canvasToolbar, 0, 0);
        pnlCanvasContainer.Controls.Add(_canvas, 0, 1);
        _centerVerticalSplitter.Panel1.Controls.Add(pnlCanvasContainer);

        // Bottom History Tabs
        _centerVerticalSplitter.Panel2.Controls.Add(BuildBottomHistoryTabs());

        return _centerVerticalSplitter;
    }

    private void SetBottomPanelCollapsed(bool collapse)
    {
        _centerVerticalSplitter.Panel2Collapsed = collapse;
        if (collapse)
        {
            _btnToggleBottom.Text = "🔼 하단 이력창 열기";
            _btnToggleBottom.BackColor = Color.FromArgb(5, 150, 105);
            _btnToggleBottom.ForeColor = Color.White;
        }
        else
        {
            _btnToggleBottom.Text = "🔽 하단창 숨기기";
            _btnToggleBottom.BackColor = Palette.Bg800;
            _btnToggleBottom.ForeColor = Palette.AccentSky;
        }
    }

    private Control BuildBottomHistoryTabs()
    {
        var rootPanel = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg900, Padding = new Padding(0) };

        // Compact Header Bar for Bottom Panel with Hide/Collapse Button
        var pnlBottomHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(8, 2, 8, 2)
        };
        pnlBottomHeader.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.DrawLine(pen, 0, pnlBottomHeader.Height - 1, pnlBottomHeader.Width, pnlBottomHeader.Height - 1);
        };

        var lblBottomTitle = new Label
        {
            Text = "📊 현장 장비 실시간 가공 이력 / 불량 대책 / 서브인원 / 후공정 추적",
            Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold),
            ForeColor = Palette.TextSlate300,
            Dock = DockStyle.Left,
            AutoSize = true,
            Padding = new Padding(0, 3, 0, 0)
        };

        var btnHideBottom = CreateFlatButton("🔽 하단창 숨김 (접기) ✕", Color.FromArgb(30, 41, 59), Palette.Amber, 160, 24);
        btnHideBottom.Dock = DockStyle.Right;
        btnHideBottom.Font = new Font("맑은 고딕", 8f, FontStyle.Bold);
        btnHideBottom.Click += (s, e) => SetBottomPanelCollapsed(true);

        pnlBottomHeader.Controls.AddRange(new Control[] { lblBottomTitle, btnHideBottom });

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
        _gridDefects.Columns.Add("colDefTime", "발생시각");
        _gridDefects.Columns.Add("colDefMach", "설비");
        _gridDefects.Columns.Add("colDefOrder", "작지/품목");
        _gridDefects.Columns.Add("colDefType", "불량유형");
        _gridDefects.Columns.Add("colDefReason", "원인분석");
        _gridDefects.Columns.Add("colDefAction", "조치내용");
        _gridDefects.Columns.Add("colDefWorker", "보고자");
        _gridDefects.Columns["colDefReason"]!.FillWeight = 160;
        _gridDefects.Columns["colDefAction"]!.FillWeight = 160;
        tabDefects.Controls.Add(_gridDefects);
        _bottomTabs.TabPages.Add(tabDefects);

        // Tab 3: Sub-worker Dispatch History & Real-Time Collaboration Dashboard
        var tabSub = new TabPage("👥 서브인원 위치·상태 실시간 공유 대시보드") { BackColor = Palette.Bg900 };
        var pnlSubDashboard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        pnlSubDashboard.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f)); // Live Status Cards Bar + Action Button
        pnlSubDashboard.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Grid

        var pnlSubTop = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(6, 3, 6, 3)
        };

        var btnOpenDispatch = CreateFlatButton("⚡ 서브인원 실시간 지시 / 위치 이동 지정", Palette.Purple, Color.White, 230, 28);
        btnOpenDispatch.Dock = DockStyle.Right;
        btnOpenDispatch.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnOpenDispatch.Click += (s, e) => OpenSubWorkerDispatchModal();

        _pnlSubWorkerCards = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        pnlSubTop.Controls.Add(_pnlSubWorkerCards);
        pnlSubTop.Controls.Add(btnOpenDispatch);

        pnlSubDashboard.Controls.Add(pnlSubTop, 0, 0);

        _gridSubTasks = CreateStyledGrid();
        _gridSubTasks.Columns.Add("colSubTime", "지시시각");
        _gridSubTasks.Columns.Add("colSubWorker", "서브인원");
        _gridSubTasks.Columns.Add("colSubLocation", "현재 위치(설비/구역)");
        _gridSubTasks.Columns.Add("colSubStatus", "진행상태");
        _gridSubTasks.Columns.Add("colSubTask", "작업지시 내용");
        _gridSubTasks.Columns.Add("colSubPart", "대상작지/부품");
        _gridSubTasks.Columns.Add("colSubLeader", "지시 작업자");
        _gridSubTasks.Columns.Add("colSubReport", "최근 보고결과");
        _gridSubTasks.Columns["colSubTask"]!.FillWeight = 220;
        _gridSubTasks.Columns["colSubReport"]!.FillWeight = 160;

        pnlSubDashboard.Controls.Add(_gridSubTasks, 0, 1);
        tabSub.Controls.Add(pnlSubDashboard);
        _bottomTabs.TabPages.Add(tabSub);

        // Tab 4: Post-Process Tracking
        var tabPost = new TabPage("⚙ 후공정(핀/각인/후처리) 추적") { BackColor = Palette.Bg900 };
        _gridPostProcess = CreateStyledGrid();
        _gridPostProcess.Columns.Add("colPostTime", "공정시각");
        _gridPostProcess.Columns.Add("colPostOrder", "작지번호");
        _gridPostProcess.Columns.Add("colPostPart", "부품명");
        _gridPostProcess.Columns.Add("colPostType", "후공정구분");
        _gridPostProcess.Columns.Add("colPostWorker", "담당자");
        _gridPostProcess.Columns.Add("colPostStatus", "상태");
        _gridPostProcess.Columns["colPostType"]!.FillWeight = 140;
        tabPost.Controls.Add(_gridPostProcess);
        _bottomTabs.TabPages.Add(tabPost);

        rootPanel.Controls.Add(_bottomTabs);
        rootPanel.Controls.Add(pnlBottomHeader);

        return rootPanel;
    }

    private Control BuildRightSidebar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(6, 0, 0, 0),
            Padding = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Node Details Panel (Fills maximum height!)
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 210f)); // Compact Tabbed Communication (Notices + Chat)

        // 1: Node Details Panel
        _detailPanel = new NodeDetailPanel { Dock = DockStyle.Fill };
        _detailPanel.IsFirstNodePredicate = (n) => _canvas.Nodes.Count > 0 && _canvas.Nodes[0].Id == n.Id;
        _detailPanel.GetFirstNodeMaterialProvider = () => _canvas.GetFirstNodeMaterialInfo();
        _detailPanel.FirstNodeMaterialChanged = (sz, mat, fix) =>
        {
            _canvas.PropagateFirstNodeMaterial(sz, mat, fix);
            AutoSaveWorkflow();
        };
        _detailPanel.StatusChangeRequested += async (node, newStatus) => await HandleNodeStatusChangeAsync(node, newStatus);
        _detailPanel.BatchStatusChangeRequested += async (nodes, newStatus) =>
        {
            _canvas.Invalidate();
            UpdateWorkflowStats();
            if (nodes.Count > 0)
            {
                try
                {
                    string mId = nodes[0].MachineId;
                    if (string.IsNullOrEmpty(mId)) mId = _currentMachineId;
                    string statusText = NodeDetailPanel.GetStatusText(newStatus);
                    string serverStatus = newStatus switch
                    {
                        NodeStatus.Running => "가공중",
                        NodeStatus.PendingCheck => "가공중",
                        NodeStatus.Completed => "완료",
                        NodeStatus.Issue => "정지",
                        _ => "대기"
                    };
                    await _api.UpdateMachineStatusAsync(
                        mId,
                        serverStatus,
                        nodes[0].OrderNumber,
                        nodes[0].PartName,
                        $"[일괄 {statusText}] {nodes.Count}개 공정",
                        _session.DisplayName
                    );
                }
                catch { }
            }
        };
        _detailPanel.NodeUpdated += (node) => { _canvas.Invalidate(); UpdateWorkflowStats(); };
        _detailPanel.NodesUpdated += (nodes) => { _canvas.Invalidate(); UpdateWorkflowStats(); };
        _detailPanel.NodeDeleteRequested += (node) => _canvas.RemoveNode(node);
        _detailPanel.BatchNodesDeleteRequested += (nodes) => _canvas.RemoveNodes(nodes);
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
        _detailPanel.IssueDialogRequested += (node) => OpenIssueDialog(node);
        _detailPanel.NodeUpdated += (node) => AutoSaveWorkflow();
        _detailPanel.NodesUpdated += (nodes) => AutoSaveWorkflow();
        _detailPanel.StatusChangeRequested += (node, status) => AutoSaveWorkflow();
        _detailPanel.BatchStatusChangeRequested += (nodes, status) => AutoSaveWorkflow();
        _detailPanel.NodeDeleteRequested += (node) => AutoSaveWorkflow();
        _detailPanel.BatchNodesDeleteRequested += (nodes) => AutoSaveWorkflow();

        _canvas.IssueBadgeClicked += (node) => OpenIssueDialog(node);
        _canvas.WorkflowChanged += () => AutoSaveWorkflow();

        Deactivate += (s, e) => AutoSaveWorkflow();
        FormClosing += (s, e) => AutoSaveWorkflow();

        panel.Controls.Add(_detailPanel, 0, 0);

        // 2: Dedicated Always-Visible Realtime Chat Card with Top Notice Banner
        var pnlChatCardBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(2)
        };
        pnlChatCardBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f)); // Notice & Channel Header
        pnlChatCardBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Chat Message Stream
        pnlChatCardBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Message Input Bar

        // Row 0: Top Banner with Notice ticker & Channel dropdown
        var pnlChatTop = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        pnlChatTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
        pnlChatTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

        _lblTopNoticeBanner = new Label
        {
            Text = "📢 공지: 등록된 최신 공지가 없습니다.",
            Font = new Font("맑은 고딕", 7.6f, FontStyle.Bold),
            ForeColor = Palette.Amber,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand
        };
        _lblTopNoticeBanner.Click += async (s, e) => await ShowNoticesDialogAsync();

        _cboChatChannel = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 8.0f)
        };
        _cboChatChannel.Items.AddRange(new object[] { "🌐 전체 공통", "⚙ R01 전용", "⚙ R02 전용", "🚨 이슈/품질" });
        _cboChatChannel.SelectedIndex = 0;
        _cboChatChannel.SelectedIndexChanged += async (s, e) => await LoadChatAsync();

        pnlChatTop.Controls.Add(_lblTopNoticeBanner, 0, 0);
        pnlChatTop.Controls.Add(_cboChatChannel, 1, 0);
        pnlChatCardBody.Controls.Add(pnlChatTop, 0, 0);

        // Row 1: Chat Message ListBox
        _lstChat = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg850,
            ForeColor = Palette.TextWhite,
            BorderStyle = BorderStyle.None,
            Font = new Font("맑은 고딕", 8.4f),
            ItemHeight = 19
        };
        pnlChatCardBody.Controls.Add(_lstChat, 0, 1);

        // Row 2: Message Input & Send Button
        var pnlChatSend = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        pnlChatSend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlChatSend.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60f));

        _txtChatInput = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 8.8f),
            PlaceholderText = "메시지 입력 (Enter로 전송)..."
        };
        _txtChatInput.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) await SendChatAsync(); };

        _btnSendChat = CreateFlatButton("전송 ✈", Palette.AccentSky, Color.White);
        _btnSendChat.Click += async (s, e) => await SendChatAsync();

        pnlChatSend.Controls.Add(_txtChatInput, 0, 0);
        pnlChatSend.Controls.Add(_btnSendChat, 1, 0);
        pnlChatCardBody.Controls.Add(pnlChatSend, 0, 2);

        var cardChat = CreateCard("💬 현장 실시간 소통 채팅", pnlChatCardBody);
        panel.Controls.Add(cardChat, 0, 1);

        return panel;
    }

    private void ShowAutoRefreshMenu(Control anchor)
    {
        var menu = new ContextMenuStrip { BackColor = Palette.Bg800, ForeColor = Palette.TextSlate300 };
        menu.RenderMode = ToolStripRenderMode.System;

        var intervals = new (int sec, string label)[]
        {
            (3, "⚡ 3초 간격 (초고속 실시간)"),
            (5, "⚡ 5초 간격 (기본 권장)"),
            (10, "⚡ 10초 간격"),
            (30, "⚡ 30초 간격")
        };

        foreach (var (sec, label) in intervals)
        {
            var item = new ToolStripMenuItem(label)
            {
                Checked = _isAutoRefreshEnabled && _autoRefreshSeconds == sec
            };
            int s = sec;
            item.Click += (sender, e) =>
            {
                _autoRefreshSeconds = s;
                _isAutoRefreshEnabled = true;
                _pollTimer.Interval = _autoRefreshSeconds * 1000;
                if (!_pollTimer.Enabled) _pollTimer.Start();
                _btnAutoRefreshToggle.Text = $"⚡ 자동: {_autoRefreshSeconds}초 ▾";
                _btnAutoRefreshToggle.ForeColor = Color.FromArgb(52, 211, 153);
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        var itemStop = new ToolStripMenuItem("⏸ 자동 갱신 끄기 (수동 전용)")
        {
            Checked = !_isAutoRefreshEnabled
        };
        itemStop.Click += (sender, e) =>
        {
            _isAutoRefreshEnabled = false;
            _pollTimer.Stop();
            _btnAutoRefreshToggle.Text = "⏸ 자동: OFF ▾";
            _btnAutoRefreshToggle.ForeColor = Palette.TextSlate400;
        };
        menu.Items.Add(itemStop);

        menu.Show(anchor, new Point(0, anchor.Height + 2));
    }

    private async Task TriggerManualRefreshAsync()
    {
        if (_btnManualRefresh == null) return;
        try
        {
            _btnManualRefresh.Enabled = false;
            _btnManualRefresh.Text = "🔄 갱신중...";
            _btnManualRefresh.ForeColor = Palette.Amber;

            await RefreshAllAsync();

            _btnManualRefresh.Text = "✔ 새로고침 완료";
            _btnManualRefresh.ForeColor = Palette.Green;

            var t = new System.Windows.Forms.Timer { Interval = 1000 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                t.Dispose();
                if (!_btnManualRefresh.IsDisposed)
                {
                    _btnManualRefresh.Text = "🔄 새로고침 (F5)";
                    _btnManualRefresh.ForeColor = Palette.AccentSky;
                    _btnManualRefresh.Enabled = true;
                }
            };
            t.Start();
        }
        catch
        {
            if (!_btnManualRefresh.IsDisposed)
            {
                _btnManualRefresh.Text = "🔄 새로고침 (F5)";
                _btnManualRefresh.ForeColor = Palette.AccentSky;
                _btnManualRefresh.Enabled = true;
            }
        }
    }

    private async Task PollRealtimeDataAsync()
    {
        if (!_isAutoRefreshEnabled) return;
        try
        {
            await LoadMachinesAsync();
            await LoadWorkOrdersAsync();
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
                var sb = new System.Text.StringBuilder();
                foreach (var m in list)
                {
                    string id = m.GetProperty("machineId").GetString() ?? "";
                    string status = m.GetProperty("status").GetString() ?? "대기";
                    string jobNo = m.TryGetProperty("activeJobNumber", out var jn) && jn.ValueKind == JsonValueKind.String ? jn.GetString() : "-";
                    sb.Append(id).Append(':').Append(status).Append(':').Append(jobNo).Append(';');
                }
                string newSummary = sb.ToString();
                if (newSummary != _lastMachineSummary)
                {
                    _lastMachineSummary = newSummary;
                    _cachedMachines = list;
                    RenderLeftMachineCards();
                }
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
                var sb = new System.Text.StringBuilder();
                foreach (var o in list)
                {
                    string orderNo = o.GetProperty("orderNumber").GetString() ?? "";
                    string status = o.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
                    sb.Append(orderNo).Append(':').Append(status).Append(';');

                    if (_isInitialDataLoaded && !string.IsNullOrEmpty(orderNo) && !_knownWorkOrderNumbers.Contains(orderNo))
                    {
                        string customer = o.TryGetProperty("customerName", out var cn) ? cn.GetString() ?? "" : "";
                        string custText = string.IsNullOrEmpty(customer) ? "" : $" ({customer})";
                        ToastNotificationForm.ShowToast(
                            "📋 신규 작업지시서 등록",
                            $"새 작업지시서 [{orderNo}]{custText} 등록되었습니다.",
                            ToastType.WorkOrder,
                            () => {
                                if (_cboOrders != null && !_cboOrders.IsDisposed) _cboOrders.Focus();
                            }
                        );
                    }
                    if (!string.IsNullOrEmpty(orderNo)) _knownWorkOrderNumbers.Add(orderNo);
                }

                string newSummary = sb.ToString();
                if (newSummary != _lastOrdersSummary)
                {
                    _lastOrdersSummary = newSummary;
                    _cachedOrders = list;
                    int prevIdx = _cboOrders.SelectedIndex;
                    _cboOrders.BeginUpdate();
                    _cboOrders.Items.Clear();
                    foreach (var o in _cachedOrders)
                    {
                        string orderNo = o.GetProperty("orderNumber").GetString() ?? "";
                        string customer = o.TryGetProperty("customerName", out var cn) ? cn.GetString() ?? "" : "";
                        string status = o.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "대기";
                        _cboOrders.Items.Add($"[{orderNo}] {customer} ({status})");
                    }
                    if (_cboOrders.Items.Count > 0)
                    {
                        _cboOrders.SelectedIndex = (prevIdx >= 0 && prevIdx < _cboOrders.Items.Count) ? prevIdx : 0;
                    }
                    _cboOrders.EndUpdate();
                }
            }
        }
        catch { }

        if (_cboOrders.Items.Count == 0 && string.IsNullOrEmpty(_lastOrdersSummary))
        {
            PopulateFallbackOrders();
        }
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
                    if (data.TitleBoxes != null) _canvas.TitleBoxes.AddRange(data.TitleBoxes);
                    _canvas.IsWorkflowConfirmed = data.IsConfirmed;
                    _canvas.ConfirmedInfoText = data.ConfirmedBy;
                    _canvas.Invalidate();
                    _canvas.SelectedNode = _canvas.Nodes.FirstOrDefault();
                    _detailPanel.BindNode(_canvas.SelectedNode);
                    UpdateWorkflowStats();
                    return;
                }
            }
            catch { }
        }

        // Check if there is an active autosaved workflow so user work is NEVER lost!
        string activeFile = Path.Combine(_localWorkflowDir, "autosave_current_active.json");
        if (File.Exists(activeFile))
        {
            try
            {
                string json = File.ReadAllText(activeFile);
                var data = JsonSerializer.Deserialize<WorkflowData>(json);
                if (data != null && data.Nodes.Count > 0)
                {
                    _canvas.Clear();
                    _canvas.Nodes.AddRange(data.Nodes);
                    _canvas.Edges.AddRange(data.Edges);
                    if (data.TitleBoxes != null) _canvas.TitleBoxes.AddRange(data.TitleBoxes);
                    _canvas.IsWorkflowConfirmed = data.IsConfirmed;
                    _canvas.ConfirmedInfoText = data.ConfirmedBy;
                    _canvas.Invalidate();
                    _canvas.SelectedNode = _canvas.Nodes.FirstOrDefault();
                    _detailPanel.BindNode(_canvas.SelectedNode);
                    UpdateWorkflowStats();
                    return;
                }
            }
            catch { }
        }

        // If currently nodes exist on canvas, keep them and auto-save instead of wiping out!
        if (_canvas.Nodes.Count > 0)
        {
            AutoSaveWorkflow();
            return;
        }

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

    private void OnPartDoubleClicked()
    {
        if (_lstParts.SelectedIndex < 0) return;
        string partText = _lstParts.SelectedItem?.ToString() ?? "";
        ApplyProbecardTemplate(partText);
    }

    /// <summary>
    /// 프로브카드 전용 9단계 표준 공정 템플릿 생성 및 세부지시 커스텀 필드 / 타임라인 정렬 적용
    /// (준비작업, 소재작업, 베이스판교체, 1차황삭, 1차중간측정, 2차피치정삭, 챔퍼/버제거, 최종검사, 세척포장)
    /// </summary>

    private void OpenNodeEditDialog(WorkflowNode node)
    {
        _canvas.SelectedNode = node;
        _detailPanel.BindNode(node);

        using var dlg = new NodeEditDialog(node, _session.DisplayName);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _detailPanel.BindNode(node);
            _canvas.Invalidate();
            UpdateWorkflowStats();
            AutoSaveWorkflow();

            if (dlg.StatusChanged)
            {
                _ = HandleNodeStatusChangeAsync(node, node.Status);
            }
        }
    }


    

    private void AddTitleGroupBox()
    {
        using var dlg = new InputTitleBoxDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var box = new WorkflowTitleBox
            {
                Title = dlg.BoxTitle,
                ColorHex = dlg.BoxColorHex
            };

            if (_canvas.SelectedNodes.Count > 0)
            {
                float minX = _canvas.SelectedNodes.Min(n => n.X) - 30;
                float minY = _canvas.SelectedNodes.Min(n => n.Y) - 50;
                float maxX = _canvas.SelectedNodes.Max(n => n.X + n.Width) + 30;
                float maxY = _canvas.SelectedNodes.Max(n => n.Y + n.Height) + 30;

                box.X = Math.Max(20, minX);
                box.Y = Math.Max(20, minY);
                box.Width = Math.Max(220, maxX - box.X);
                box.Height = Math.Max(120, maxY - box.Y);
            }
            else
            {
                box.X = 100;
                box.Y = 100;
                box.Width = 420;
                box.Height = 260;
            }

            _canvas.TitleBoxes.Add(box);
            _canvas.Invalidate();
            AutoSaveWorkflow();
            MessageBox.Show($"타이틀 그룹 박스 '{box.Title}'가 추가되었습니다.\r\n박스 헤더를 잡고 드래그하면 내부 노드가 함께 이동하며, 우측 하단 모서리로 크기를 조절할 수 있습니다.", "타이틀 박스 추가", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ToggleTimelineGuides()
    {
        _canvas.ShowTimelineGuides = !_canvas.ShowTimelineGuides;
        if (_btnToggleTimelineGuides != null)
        {
            _btnToggleTimelineGuides.Text = _canvas.ShowTimelineGuides ? "⏱ 타임라인선: 켜짐" : "⏱ 타임라인선: 꺼짐";
            _btnToggleTimelineGuides.BackColor = _canvas.ShowTimelineGuides ? Color.FromArgb(16, 185, 129) : Palette.Bg800;
        }
        _canvas.Invalidate();
    }

    private void ShowTimelineBaseMenu(Control anchor)
    {
        var cms = new ContextMenuStrip();
        cms.BackColor = Color.FromArgb(15, 23, 42);
        cms.ForeColor = Color.White;

        double currentBase = _canvas.GetEffectiveTimelineBaseHour();

        var mAuto = cms.Items.Add($"🎯 공정 시작시간에 자동 맞춤 (현재 {currentBase:00}:00 기준)");
        mAuto.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
        mAuto.Click += (s, e) =>
        {
            _canvas.CustomTimelineBaseHour = null;
            _canvas.ArrangeByTimeline(autoFitBaseHour: true);
            UpdateTimelineBaseButtonText();
            _canvas.Invalidate();
            AutoSaveWorkflow();
        };

        cms.Items.Add(new ToolStripSeparator());

        var mNow = cms.Items.Add($"🕒 현재 시각 기준 맞춤 ({DateTime.Now.Hour:00}:00)");
        mNow.Click += (s, e) =>
        {
            _canvas.CustomTimelineBaseHour = (double)DateTime.Now.Hour;
            _canvas.ArrangeByTimeline(autoFitBaseHour: false);
            UpdateTimelineBaseButtonText();
            _canvas.Invalidate();
            AutoSaveWorkflow();
        };

        cms.Items.Add(new ToolStripSeparator());

        int[] presetHours = { 6, 8, 9, 13, 14, 16, 18, 20 };
        foreach (int h in presetHours)
        {
            var item = cms.Items.Add($"   {h:00}:00 부터 정렬");
            int capturedH = h;
            item.Click += (s, e) =>
            {
                _canvas.CustomTimelineBaseHour = capturedH;
                _canvas.ArrangeByTimeline(autoFitBaseHour: false);
                UpdateTimelineBaseButtonText();
                _canvas.Invalidate();
                AutoSaveWorkflow();
            };
        }

        cms.Items.Add(new ToolStripSeparator());

        var mCustom = cms.Items.Add("✏️ 시작 시간 직접 입력 (예: 16:00)...");
        mCustom.Click += (s, e) => PromptCustomTimelineBaseHour();

        cms.Show(anchor, new Point(0, anchor.Height));
    }

    private void UpdateTimelineBaseButtonText()
    {
        if (_btnTimelineBaseConfig == null) return;
        double eff = _canvas.GetEffectiveTimelineBaseHour();
        if (_canvas.CustomTimelineBaseHour.HasValue)
            _btnTimelineBaseConfig.Text = $"⏱ 기준: {_canvas.CustomTimelineBaseHour.Value:00}:00 ▾";
        else
            _btnTimelineBaseConfig.Text = $"⏱ 기준: 자동({eff:00}:00) ▾";
    }

    private void ShowTimelineIntervalMenu(Control anchor)
    {
        var cms = new ContextMenuStrip();
        cms.BackColor = Color.FromArgb(15, 23, 42);
        cms.ForeColor = Color.White;

        int currentPx = (int)_canvas.PixelsPerHour;

        (int px, string desc)[] presets = new[]
        {
            (160, "🔍 매우 좁게 (160px/시간) - 한눈에 전체 조망"),
            (220, "🔍 좁게 (220px/시간) - 컴팩트 보기"),
            (280, "🔍 보통 (280px/시간) - 표준 기본값"),
            (360, "🔍 넓게 (360px/시간) - 여유로운 간격"),
            (480, "🔍 매우 넓게 (480px/시간) - 상세 타임라인"),
            (600, "🔍 최대 확대 (600px/시간) - 초정밀 확인")
        };

        foreach (var p in presets)
        {
            string check = Math.Abs(currentPx - p.px) < 15 ? " ✔ " : "    ";
            var item = cms.Items.Add($"{check}{p.desc}");
            int targetPx = p.px;
            item.Click += (s, e) =>
            {
                _canvas.SetTimelineInterval(targetPx);
                UpdateTimelineIntervalButtonText();
                AutoSaveWorkflow();
            };
        }

        cms.Items.Add(new ToolStripSeparator());

        var mCustom = cms.Items.Add("✏️ 픽셀 간격 직접 입력 (120~720px)...");
        mCustom.Click += (s, e) => PromptCustomTimelineInterval();

        cms.Show(anchor, new Point(0, anchor.Height));
    }

    private void UpdateTimelineIntervalButtonText()
    {
        if (_btnTimelineInterval == null) return;
        int px = (int)_canvas.PixelsPerHour;
        string label = px switch
        {
            <= 180 => "매우좁게",
            <= 240 => "좁게",
            <= 310 => "보통",
            <= 400 => "넓게",
            _ => "상세"
        };
        _btnTimelineInterval.Text = $"⏱ 간격: {label}({px}px) ▾";
    }

    private void PromptCustomTimelineInterval()
    {
        using var form = new Form
        {
            Text = "⏱ 타임라인 1시간당 픽셀 간격 설정",
            Size = new Size(360, 180),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = new Font("맑은 고딕", 9f)
        };

        var lbl = new Label
        {
            Text = "1시간당 타임라인 픽셀 간격을 입력하세요 (120 ~ 720):",
            Dock = DockStyle.Top,
            Height = 35,
            Padding = new Padding(12, 10, 12, 0)
        };

        var txt = new TextBox
        {
            Text = ((int)_canvas.PixelsPerHour).ToString(),
            Location = new Point(16, 50),
            Width = 310,
            Font = new Font("맑은 고딕", 10.5f),
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnOk = new Button
        {
            Text = "적용",
            DialogResult = DialogResult.OK,
            Location = new Point(130, 95),
            Width = 100,
            Height = 32,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnOk.FlatAppearance.BorderSize = 0;

        var btnCancel = new Button
        {
            Text = "취소",
            DialogResult = DialogResult.Cancel,
            Location = new Point(240, 95),
            Width = 85,
            Height = 32,
            BackColor = Color.FromArgb(51, 65, 85),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnCancel.FlatAppearance.BorderSize = 0;

        form.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            if (float.TryParse(txt.Text.Trim(), out float val))
            {
                _canvas.SetTimelineInterval(val);
                UpdateTimelineIntervalButtonText();
                AutoSaveWorkflow();
            }
            else
            {
                MessageBox.Show("올바른 숫자를 입력해주세요 (120 ~ 720).", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private void PromptCustomTimelineBaseHour()
    {
        using var form = new Form
        {
            Text = "⏱ 타임라인 기준 시작 시간 직접 설정",
            Size = new Size(380, 180),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = new Font("맑은 고딕", 9f)
        };

        var lbl = new Label
        {
            Text = "타임라인을 정렬할 기준 시작 시각을 입력하세요 (예: 16:00, 14:30):",
            Dock = DockStyle.Top,
            Height = 35,
            Padding = new Padding(12, 10, 12, 0)
        };

        var txt = new TextBox
        {
            Text = $"{_canvas.GetEffectiveTimelineBaseHour():00}:00",
            Location = new Point(16, 50),
            Width = 330,
            Font = new Font("맑은 고딕", 10.5f),
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnOk = new Button
        {
            Text = "적용 및 정렬",
            DialogResult = DialogResult.OK,
            Location = new Point(140, 95),
            Width = 110,
            Height = 32,
            BackColor = Color.FromArgb(14, 165, 233),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnOk.FlatAppearance.BorderSize = 0;

        var btnCancel = new Button
        {
            Text = "취소",
            DialogResult = DialogResult.Cancel,
            Location = new Point(260, 95),
            Width = 85,
            Height = 32,
            BackColor = Color.FromArgb(51, 65, 85),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnCancel.FlatAppearance.BorderSize = 0;

        form.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            double? parsed = WorkflowCanvasControl.ParseTimeString(txt.Text);
            if (parsed.HasValue)
            {
                _canvas.CustomTimelineBaseHour = parsed.Value;
                _canvas.ArrangeByTimeline(autoFitBaseHour: false);
                UpdateTimelineBaseButtonText();
                _canvas.Invalidate();
                AutoSaveWorkflow();
            }
            else
            {
                MessageBox.Show("올바른 시간 형식(HH:mm)을 입력해주세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private void PromptEditTitleBox(WorkflowTitleBox box)
    {
        using var dlg = new InputTitleBoxDialog(box.Title, box.ColorHex);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            box.Title = dlg.BoxTitle;
            box.ColorHex = dlg.BoxColorHex;
            _canvas.Invalidate();
            AutoSaveWorkflow();
        }
    }

    private async void OpenSubWorkerDispatchModal(string defaultSubWorker = "이서브", string defaultLocation = "R01")
    {
        string currentOrder = _cboOrders.SelectedIndex >= 0 && _cboOrders.SelectedIndex < _cachedOrders.Count
            ? _cachedOrders[_cboOrders.SelectedIndex].GetProperty("orderNumber").GetString() ?? "" : "";
        string currentPart = _lstParts.SelectedItem?.ToString() ?? "";

        using var dlg = new SubWorkerDispatchDialog(defaultSubWorker, defaultLocation, currentOrder, currentPart);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                bool ok = await _api.CreateSubWorkerTaskAsync(
                    dlg.SelectedLocation,
                    dlg.OrderNumber,
                    dlg.PartName,
                    _session.DisplayName ?? _session.Username,
                    dlg.SelectedSubWorker,
                    dlg.Instruction
                );

                if (ok)
                {
                    MessageBox.Show($"[서브인원 작업 지시 완료]\r\n담당 서브인원: {dlg.SelectedSubWorker}\r\n배치 위치: {dlg.SelectedLocation}\r\n지시 내용: {dlg.Instruction}", "지시 전송 성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    await LoadHistoryGridsAsync();
                }
                else
                {
                    MessageBox.Show("서브인원 작업 지시 등록에 실패하였습니다.", "등록 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"서브인원 작업 지시 전송 중 오류: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void SaveCurrentWorkflowAsTemplate()
    {
        if (_canvas.Nodes.Count == 0)
        {
            MessageBox.Show("저장할 공정 노드가 캔버스에 존재하지 않습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string mat = _canvas.Nodes.FirstOrDefault()?.MaterialType ?? "PEEK 5T";
        string fix = _canvas.Nodes.FirstOrDefault()?.FixingMethod ?? "바이스 고정";

        using var dlg = new TemplateSaveDialog(_canvas.Nodes.Count, mat, fix);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                TemplateManager.SaveTemplate(
                    dlg.TemplateName,
                    dlg.TemplateDescription,
                    dlg.TargetMaterial,
                    dlg.FixingMethod,
                    _canvas.Nodes,
                    _canvas.Edges,
                    _canvas.TitleBoxes
                );
                MessageBox.Show($"'{dlg.TemplateName}' 템플릿이 성공적으로 저장되었습니다.\r\n언제든 [📂 템플릿 불러오기]에서 재사용할 수 있습니다.", "템플릿 저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"템플릿 저장 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void OpenLoadTemplateDialog()
    {
        using var dlg = new TemplateLoadDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedTemplates.Count > 0)
        {
            var templates = dlg.SelectedTemplates;
            bool append = dlg.IsAppendMode;

            string orderNo = "WO-2026-001";
            if (_cboOrders.SelectedIndex >= 0 && _cboOrders.SelectedIndex < _cachedOrders.Count)
            {
                orderNo = _cachedOrders[_cboOrders.SelectedIndex].GetProperty("orderNumber").GetString() ?? "WO-2026-001";
            }
            string basePartName = _lstParts.SelectedItem?.ToString() ?? "";

            // 새로 대체 모드이고 기존 노드가 있으면 확인
            if (!append && _canvas.Nodes.Count > 0)
            {
                var dr = MessageBox.Show(
                    "기존 캔버스의 모든 공정 노드를 지우고 새로 불러오시겠습니까?\r\n\r\n(기존 공정을 유지한 채 함께 배치하려면 '아니요'를 누른 후 '기존 화면에 추가'를 클릭하세요)",
                    "캔버스 대체 확인",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (dr != DialogResult.Yes) return;

                _canvas.Clear();
                ResetWorkflowConfirmation();
            }

            // 테마 색상 팔레트 (템플릿별로 색상 구분)
            string[] boxColors = { "#0284c7", "#10b981", "#f59e0b", "#a855f7", "#ec4899", "#3b82f6" };
            int colorIdx = _canvas.TitleBoxes.Count % boxColors.Length;

            int addedTemplateCount = 0;
            int totalAddedNodes = 0;

            foreach (var t in templates)
            {
                string partName = string.IsNullOrEmpty(basePartName) ? t.Name : basePartName;
                var (newNodes, newEdges, newBoxes) = TemplateManager.InstantiateTemplate(t, orderNo, partName);

                if (newNodes.Count == 0) continue;

                // 캔버스에 기존 노드가 이미 있다면, 겹치지 않도록 Y축 아래로 오프셋 이동
                if (_canvas.Nodes.Count > 0)
                {
                    float currentMaxY = _canvas.Nodes.Max(n => n.Y + n.Height);
                    float minNewY = newNodes.Min(n => n.Y);
                    float offsetY = (currentMaxY + 80f) - minNewY;

                    foreach (var n in newNodes)
                    {
                        n.Y += offsetY;
                    }
                    foreach (var b in newBoxes)
                    {
                        b.Y += offsetY;
                    }
                }

                // 템플릿별 타이틀 그룹 박스가 없으면 해당 템플릿을 감싸는 타이틀 박스 자동 생성
                if (newBoxes.Count == 0)
                {
                    float minX = newNodes.Min(n => n.X) - 30;
                    float minY = newNodes.Min(n => n.Y) - 50;
                    float maxX = newNodes.Max(n => n.X + n.Width) + 30;
                    float maxY = newNodes.Max(n => n.Y + n.Height) + 30;

                    var box = new WorkflowTitleBox
                    {
                        Title = $"{t.Name} ({t.TargetMaterial})",
                        ColorHex = boxColors[colorIdx % boxColors.Length],
                        X = Math.Max(20, minX),
                        Y = Math.Max(20, minY),
                        Width = Math.Max(240, maxX - minX),
                        Height = Math.Max(140, maxY - minY)
                    };
                    newBoxes.Add(box);
                }

                _canvas.Nodes.AddRange(newNodes);
                _canvas.Edges.AddRange(newEdges);
                _canvas.TitleBoxes.AddRange(newBoxes);

                addedTemplateCount++;
                totalAddedNodes += newNodes.Count;
                colorIdx++;
            }

            _canvas.ArrangeByTimeline(autoFitBaseHour: false);

            _canvas.SelectedNode = _canvas.Nodes.LastOrDefault();
            if (_canvas.SelectedNode != null)
            {
                _detailPanel.BindNode(_canvas.SelectedNode);
            }

            _canvas.ResetView();
            UpdateWorkflowStats();
            AutoSaveWorkflow();

            string modeDesc = append ? "기존 캔버스에 추가 배치" : "캔버스에 새로 적용";
            MessageBox.Show($"[템플릿 불러오기 완료]\r\n불러온 템플릿: {addedTemplateCount}개\r\n총 추가 공정 수: {totalAddedNodes}개\r\n방식: {modeDesc}\r\n\r\n각 템플릿은 타이틀 그룹 박스로 묶여 있어 박스 헤더를 잡고 원하는 위치로 자유롭게 이동할 수 있습니다.", "템플릿 불러오기 성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    public void ApplyProbecardTemplate(string? rawPartName = null)
    {
        string orderNo = "WO-2026-001";
        int orderId = 1;
        if (_cboOrders.SelectedIndex >= 0 && _cboOrders.SelectedIndex < _cachedOrders.Count)
        {
            var o = _cachedOrders[_cboOrders.SelectedIndex];
            orderNo = o.GetProperty("orderNumber").GetString() ?? "WO-2026-001";
            if (o.TryGetProperty("orderId", out var oid)) orderId = oid.GetInt32();
        }

        string pName = string.IsNullOrWhiteSpace(rawPartName) ? "프로브카드 인터포저 (PEEK 5T)" : rawPartName;
        if (pName.StartsWith("[") && pName.Contains("]"))
        {
            int closeIdx = pName.IndexOf(']');
            pName = pName.Substring(closeIdx + 1).Trim();
        }
        if (pName.Contains("—"))
        {
            pName = pName.Split('—')[0].Trim();
        }

        _canvas.Clear();
        ResetWorkflowConfirmation();

        // 9단계 프로브카드 표준 공정 노드 생성 (각 단계별 시작/종료시간, 소재사이즈, 소재종류 기본 탑재)
        var n1 = new WorkflowNode
        {
            OrderId = orderId,
            OrderNumber = orderNo,
            PartName = pName,
            Title = "준비작업 (도면 & NC 확인)",
            Category = NodeCategory.Preparation,
            Status = NodeStatus.Ready,
            MachineId = "R01",
            Assignee = _session.DisplayName,
            StartTime = "08:30",
            EndTime = "09:00",
            MaterialSize = "150x120x5T",
            MaterialType = "PEEK 5T",
            FixingMethod = "진공 흡착 (진공 플레이트)",
            Details = "[시작시간]: 08:30\r\n[종료시간]: 09:00\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 프로브카드 도면(DWG) 치수 및 마이크로 피치 공차(±0.003) 확인\r\n- 가공 NC 프로그램 로드 및 툴 시트(Tool Sheet) 점검",
            X = 60,
            Y = 80
        };

        var n2 = new WorkflowNode
        {
            OrderId = orderId,
            OrderNumber = orderNo,
            PartName = pName,
            Title = "소재작업 (치수 & 평면도 검수)",
            Category = NodeCategory.Preparation,
            Status = NodeStatus.Ready,
            MachineId = "R01",
            Assignee = _session.DisplayName,
            StartTime = "09:00",
            EndTime = "09:30",
            MaterialSize = "150x120x5T",
            MaterialType = "PEEK 5T",
            FixingMethod = "진공 흡착 (진공 플레이트)",
            Details = "[시작시간]: 09:00\r\n[종료시간]: 09:30\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- PEEK 소재 5T 정밀 평면도 측정 및 버(Burr) 사전 제거\r\n- 마이크로미터 두께 실측 검사 (5.000 ±0.005mm)",
            X = 300,
            Y = 80
        };

        var n3 = new WorkflowNode
        {
            OrderId = orderId,
            OrderNumber = orderNo,
            PartName = pName,
            Title = "베이스판 교체 & 원점 세팅",
            Category = NodeCategory.Preparation,
            Status = NodeStatus.Ready,
            MachineId = "R01",
            Assignee = _session.DisplayName,
            StartTime = "09:30",
            EndTime = "10:10",
            MaterialSize = "150x120x5T",
            MaterialType = "PEEK 5T",
            FixingMethod = "진공 흡착 (진공 플레이트)",
            Details = "[시작시간]: 09:30\r\n[종료시간]: 10:10\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 전용 진공 흡착 베이스판 교체 장착 및 수평 레벨링 다이얼게이지 세팅\r\n- 공작물 원점(G54) 터치 프로브 정밀 셋업",
            X = 540,
            Y = 80
        };

        var n4 = new WorkflowNode
        {
            OrderId = orderId,
            OrderNumber = orderNo,
            PartName = pName,
            Title = "1차 전면 황삭 (면가공)",
            Category = NodeCategory.Machining,
            Status = NodeStatus.Ready,
            MachineId = "R01",
            Assignee = _session.DisplayName,
            StartTime = "10:10",
            EndTime = "11:20",
            MaterialSize = "150x120x5T",
            MaterialType = "PEEK 5T",
            FixingMethod = "진공 흡착 (진공 플레이트)",
            Details = "[시작시간]: 10:10\r\n[종료시간]: 11:20\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 전면 기준면 페이스밀링 황삭 가공 진행\r\n- 절삭유 분사 및 진공 흡착 압력 게이지 상시 모니터링",
            X = 780,
            Y = 80
        };

        var n5 = new WorkflowNode
        {
            OrderId = orderId,
            OrderNumber = orderNo,
            PartName = pName,
            Title = "1차 중간측정 (CMM 정밀측정)",
            Category = NodeCategory.Inspection,
            Status = NodeStatus.Ready,
            MachineId = "측정실",
            Assignee = "품질팀",
            StartTime = "11:20",
            EndTime = "11:50",
            MaterialSize = "150x120x5T",
            MaterialType = "PEEK 5T",
            FixingMethod = "진공 흡착 (진공 플레이트)",
            Details = "[시작시간]: 11:20\r\n[종료시간]: 11:50\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 1차 가공 후 평면도 및 기준 가이드홀 3차원 CMM 측정\r\n- 홀간 피치 누적 공차 ±0.003mm 규격 합격 판정",
            X = 1020,
            Y = 80
        };

        var n6 = new WorkflowNode
        {
            OrderId = orderId,
            OrderNumber = orderNo,
            PartName = pName,
            Title = "2차 미세피치 정삭 (홀가공)",
            Category = NodeCategory.Machining,
            Status = NodeStatus.Ready,
            MachineId = "R02",
            Assignee = _session.DisplayName,
            StartTime = "13:00",
            EndTime = "14:30",
            MaterialSize = "150x120x5T",
            MaterialType = "PEEK 5T",
            FixingMethod = "진공 흡착 (진공 플레이트)",
            Details = "[시작시간]: 13:00\r\n[종료시간]: 14:30\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 초경 0.15mm 극소경 드릴 마이크로 홀 고속 정밀가공\r\n- 80㎛ 피치 미세 패턴 가공 및 40,000 RPM 스핀들 고정밀 제어",
            X = 1260,
            Y = 80
        };

        var n7 = new WorkflowNode
        {
            OrderId = orderId,
            OrderNumber = orderNo,
            PartName = pName,
            Title = "챔퍼 및 버(Burr) 제거",
            Category = NodeCategory.Collaboration,
            Status = NodeStatus.Ready,
            MachineId = "후공정",
            Assignee = "이서브",
            SubWorkerName = "이서브",
            SubWorkerInstruction = "40X 현미경 하에서 미세 홀 Burr 수작업 전수 제거 요망",
            StartTime = "14:30",
            EndTime = "15:10",
            MaterialSize = "150x120x5T",
            MaterialType = "PEEK 5T",
            FixingMethod = "진공 흡착 (진공 플레이트)",
            Details = "[시작시간]: 14:30\r\n[종료시간]: 15:10\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 40배율 광학 현미경 하에서 미세 홀 Burr 수작업 정밀 제거\r\n- 초음파 엣지 챔퍼링 수작업 마무리 손질",
            X = 1500,
            Y = 80
        };

        var n8 = new WorkflowNode
        {
            OrderId = orderId,
            OrderNumber = orderNo,
            PartName = pName,
            Title = "최종 비전 정밀검사",
            Category = NodeCategory.Inspection,
            Status = NodeStatus.Ready,
            MachineId = "품질팀",
            Assignee = "검사실",
            StartTime = "15:10",
            EndTime = "16:00",
            MaterialSize = "150x120x5T",
            MaterialType = "PEEK 5T",
            FixingMethod = "진공 흡착 (진공 플레이트)",
            Details = "[시작시간]: 15:10\r\n[종료시간]: 16:00\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 비접촉 광학 비전 측정기 전수 피치 및 진원도 검사\r\n- 최종 검사 성적서 발행 및 합격 필증 부착",
            X = 1740,
            Y = 80
        };

        var n9 = new WorkflowNode
        {
            OrderId = orderId,
            OrderNumber = orderNo,
            PartName = pName,
            Title = "초음파 세척 & ESD 포장",
            Category = NodeCategory.PostProcess,
            Status = NodeStatus.Ready,
            MachineId = "세척실",
            Assignee = "이서브",
            StartTime = "16:00",
            EndTime = "16:40",
            MaterialSize = "150x120x5T",
            MaterialType = "PEEK 5T",
            FixingMethod = "진공 흡착 (진공 플레이트)",
            Details = "[시작시간]: 16:00\r\n[종료시간]: 16:40\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 무수에탄올 3단계 초음파 정밀 세척 및 클린 에어 건조\r\n- 대전방지(ESD) 진공 팩킹 및 바코드 라벨링 출고 준비",
            X = 1980,
            Y = 80
        };

        _canvas.AddNode(n1);
        _canvas.AddNode(n2);
        _canvas.AddNode(n3);
        _canvas.AddNode(n4);
        _canvas.AddNode(n5);
        _canvas.AddNode(n6);
        _canvas.AddNode(n7);
        _canvas.AddNode(n8);
        _canvas.AddNode(n9);

        _canvas.AddEdge(n1.Id, n2.Id, EdgeType.Sequence);
        _canvas.AddEdge(n2.Id, n3.Id, EdgeType.Sequence);
        _canvas.AddEdge(n3.Id, n4.Id, EdgeType.Sequence);
        _canvas.AddEdge(n4.Id, n5.Id, EdgeType.Sequence);
        _canvas.AddEdge(n5.Id, n6.Id, EdgeType.Sequence);
        _canvas.AddEdge(n6.Id, n7.Id, EdgeType.Delegation);
        _canvas.AddEdge(n7.Id, n8.Id, EdgeType.Sequence);
        _canvas.AddEdge(n8.Id, n9.Id, EdgeType.Sequence);

        _canvas.ArrangeByTimeline();

        _canvas.SelectedNode = n1;
        _detailPanel.BindNode(n1);

        UpdateWorkflowStats();
        SaveWorkflowDraftLocally(silent: true);
    }

    private void PopulateFallbackOrders()
    {
        _cboOrders.Items.Clear();
        _cachedOrders.Clear();

        string sampleJson = @"[
          {
            ""orderId"": 1,
            ""orderNumber"": ""WO-2026-001"",
            ""customerName"": ""삼성전자 반도체사업부"",
            ""status"": ""진행중"",
            ""parts"": [
              { ""partId"": 1, ""partCode"": ""PRB-001"", ""partName"": ""프로브카드 인터포저"", ""material"": ""PEEK"", ""thickness"": 5.0, ""targetQty"": 2, ""productionType"": ""시제품"" },
              { ""partId"": 2, ""partCode"": ""PRB-002"", ""partName"": ""프로브카드 고정 가이드플레이트"", ""material"": ""SUS304"", ""thickness"": 3.0, ""targetQty"": 4, ""productionType"": ""양산"" },
              { ""partId"": 3, ""partCode"": ""SCK-001"", ""partName"": ""반도체 테스트 소켓 상판"", ""material"": ""AL6061"", ""thickness"": 10.0, ""targetQty"": 2, ""productionType"": ""양산"" }
            ]
          },
          {
            ""orderId"": 2,
            ""orderNumber"": ""WO-2026-002"",
            ""customerName"": ""SK하이닉스"",
            ""status"": ""대기"",
            ""parts"": [
              { ""partId"": 4, ""partCode"": ""PRB-003"", ""partName"": ""프로브카드 베이스 플레이트"", ""material"": ""AL6061"", ""thickness"": 15.0, ""targetQty"": 1, ""productionType"": ""단품"" },
              { ""partId"": 5, ""partCode"": ""JIG-001"", ""partName"": ""정밀 지그 블록"", ""material"": ""SKD11"", ""thickness"": 20.0, ""targetQty"": 2, ""productionType"": ""단품"" }
            ]
          }
        ]";

        try
        {
            using var doc = JsonDocument.Parse(sampleJson);
            _cachedOrders = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
            foreach (var o in _cachedOrders)
            {
                string orderNo = o.GetProperty("orderNumber").GetString() ?? "";
                string customer = o.TryGetProperty("customerName", out var cn) ? cn.GetString() ?? "" : "";
                string status = o.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "대기";
                _cboOrders.Items.Add($"[{orderNo}] {customer} ({status})");
            }
            if (_cboOrders.Items.Count > 0)
            {
                _cboOrders.SelectedIndex = 0;
            }
        }
        catch { }
    }

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
                TitleBoxes = _canvas.TitleBoxes.ToList(),
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
            MessageBox.Show("확정할 워크플로우 공정이 없습니다. 먼저 공정을 생성해주세요.", "공정 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_isPlanConfirmed)
        {
            var reconfirm = MessageBox.Show(
                $"현재 워크플로우 계획이 이미 확정된 상태입니다.\n(확정 시각: {_planConfirmedAt:yyyy-MM-dd HH:mm})\n\n최신 변경사항으로 서버에 재확정 및 공유하시겠습니까?",
                "계획 재확정 확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            if (reconfirm != DialogResult.Yes) return;
        }
        else
        {
            var confirm = MessageBox.Show(
                $"현재 수립된 총 {_canvas.Nodes.Count}개의 공정 계획을 확정하고 중앙 서버 작업관리 시스템에 공유하시겠습니까?\n\n확정 후 화면에 [🔒 계획 확정 완료] 배지가 활성화되며 실시간 작업 현황으로 반영됩니다.",
                "워크플로우 계획 확정",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            if (confirm != DialogResult.Yes) return;
        }

        // 1. Save and Create Immutable Confirmed Snapshot
        SaveWorkflowDraftLocally(silent: true);
        if (_currentJob != null)
        {
            try
            {
                var snapshot = await _storageManager.CreateConfirmedSnapshotAsync(_currentJob, _session.DisplayName);
                System.Diagnostics.Debug.WriteLine($"[Confirmed Snapshot Created] ID={snapshot.SnapshotId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Snapshot Error] {ex.Message}");
            }
        }

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

            // Update machine prompt with confirmed status
            await _api.UpdateMachineStatusAsync(
                _currentMachineId,
                "대기",
                orderNo,
                partName,
                $"[계획확정] {_canvas.Nodes[0].Title} 외 {_canvas.Nodes.Count - 1}공정",
                _session.DisplayName
            );

            // Mark confirmation state visually!
            _isPlanConfirmed = true;
            _planConfirmedAt = DateTime.Now;

            _btnConfirmPlan.Text = "✅ 계획확정 완료";
            _btnConfirmPlan.BackColor = Color.FromArgb(5, 150, 105);
            _btnConfirmPlan.ForeColor = Color.White;

            _canvas.IsWorkflowConfirmed = true;
            _canvas.ConfirmedInfoText = $"{_planConfirmedAt:yyyy-MM-dd HH:mm} ({_session.DisplayName})";
            _canvas.Invalidate();

            if (_lblConfirmedStatusBadge != null)
            {
                _lblConfirmedStatusBadge.Text = $"🔒 [계획확정: {_planConfirmedAt:HH:mm}]";
                _lblConfirmedStatusBadge.Visible = true;
            }

            await LoadHistoryGridsAsync();

            MessageBox.Show(
                $"[워크플로우 계획 확정 및 서버 공유 완료]\n\n작업지시서: {orderNo}\n대상 부품: {partName}\n총 공정 수: {_canvas.Nodes.Count}개\n확정자: {_session.DisplayName}\n확정시각: {_planConfirmedAt:yyyy-MM-dd HH:mm:ss}\n\n화면에 [🔒 계획 확정 완료] 배지가 활성화되었습니다.",
                "계획 확정 완료",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show($"서버 전송 중 오류 발생: {ex.Message}", "전송 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetWorkflowConfirmation()
    {
        _isPlanConfirmed = false;
        _planConfirmedAt = null;

        if (_btnConfirmPlan != null)
        {
            _btnConfirmPlan.Text = "🔒 계획 확정";
            _btnConfirmPlan.BackColor = Color.FromArgb(8, 47, 73);
            _btnConfirmPlan.ForeColor = Palette.AccentSky;
        }

        if (_canvas != null)
        {
            _canvas.IsWorkflowConfirmed = false;
            _canvas.ConfirmedInfoText = "";
            _canvas.Invalidate();
        }

        if (_lblConfirmedStatusBadge != null)
        {
            _lblConfirmedStatusBadge.Visible = false;
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

    private async void OpenIssueDialog(WorkflowNode node)
    {
        AutoSaveWorkflow();
        using var dlg = new IssueDetailDialog(node, _session.DisplayName);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            AutoSaveWorkflow();
            _canvas.Invalidate();
            _detailPanel.BindNode(node);
            UpdateWorkflowStats();

            if (dlg.DataSaved)
            {
                try
                {
                    await _api.CreateDefectAndCountermeasureAsync(
                        string.IsNullOrEmpty(node.PartName) ? node.OrderNumber : node.PartName,
                        string.IsNullOrEmpty(node.MachineId) ? "R01" : node.MachineId,
                        "공정이슈",
                        string.IsNullOrEmpty(node.IssueType) ? "공정 이상" : node.IssueType,
                        node.IssueOccurredDetails,
                        node.IssueRootCause,
                        node.IssueActionTaken,
                        _session.DisplayName
                    );

                    string serverStatus = node.Status switch
                    {
                        NodeStatus.Running => "가동중",
                        NodeStatus.PendingCheck => "측정대기",
                        NodeStatus.Completed => "완료",
                        NodeStatus.Issue => "불량/이슈",
                        _ => "대기"
                    };

                    string prompt = node.Status == NodeStatus.Issue
                        ? $"[이슈발생] {node.Title} - {node.IssueType}"
                        : (node.HasIssueHistory ? $"[조치완료] {node.Title}" : $"[{node.Status}] {node.Title}");

                    await _api.UpdateMachineStatusAsync(
                        node.MachineId,
                        serverStatus,
                        node.OrderNumber,
                        node.PartName,
                        prompt,
                        _session.DisplayName
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[IssueSync] Error: {ex.Message}");
                }
            }
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

        var (firstSize, firstMat, firstFix) = _canvas.GetFirstNodeMaterialInfo();

        var node = new WorkflowNode
        {
            Title = defaultTitle,
            Category = cat,
            OrderNumber = orderNo,
            PartName = partName,
            MachineId = _currentMachineId,
            Assignee = _session.DisplayName,
            MaterialSize = firstSize,
            MaterialType = firstMat,
            FixingMethod = string.IsNullOrEmpty(firstFix) ? "바이스" : firstFix,
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
        var dups = _canvas.DuplicateSelectedNodes();
        if (dups.Count > 0)
        {
            if (dups.Count > 1) _detailPanel.BindNodes(dups);
            else _detailPanel.BindNode(dups[0]);
            AutoSaveWorkflow();
        }
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

            if (newStatus == NodeStatus.Running)
            {
                node.StartedAt = DateTime.Now;
                node.StartTime = DateTime.Now.ToString("HH:mm");
            }

            // When current node is Completed (1차완료 확정), advance next connected steps to Running/Inspection!
            if (newStatus == NodeStatus.Completed)
            {
                node.CompletedAt = DateTime.Now;
                node.EndTime = DateTime.Now.ToString("HH:mm");

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
                        {
                            nextNode.StartedAt = DateTime.Now;
                            nextNode.StartTime = DateTime.Now.ToString("HH:mm");
                        }

                        nextActivatedNode ??= nextNode;
                    }
                }

                // Keep current completed node selected & bound so the worker immediately sees completion and end time!
                _canvas.SelectedNode = node;
                _detailPanel.BindNode(node);
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

            // 0. Record Execution History (Immutable & Persistent across plan modifications/node deletions)
            try
            {
                var execRecord = new ExecutionHistoryRecord
                {
                    JobId = !string.IsNullOrEmpty(node.JobId) ? node.JobId : (_currentJob?.JobId ?? ""),
                    NodeId = node.Id,
                    NodeTitle = node.Title,
                    MachineId = node.MachineId,
                    WorkerName = _session.DisplayName,
                    EventType = newStatus.ToString(),
                    Status = serverStatus,
                    Timestamp = DateTime.Now,
                    ActualStartAt = node.StartedAt,
                    ActualEndAt = node.CompletedAt,
                    InspectionDetails = node.Category == NodeCategory.Inspection ? node.Details : "",
                    IssueDetails = newStatus == NodeStatus.Issue ? (node.IssueOccurredDetails + " " + node.Details) : "",
                    Note = statusPrompt
                };
                await _storageManager.RecordExecutionHistoryAsync(execRecord);
            }
            catch { }

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
                var sbL = new System.Text.StringBuilder();
                foreach (var l in logs)
                {
                    string lid = l.TryGetProperty("id", out var i) ? i.ToString() : "";
                    string ltm = l.TryGetProperty("timestamp", out var t) ? t.GetString() ?? "" : "";
                    string lst = l.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                    sbL.Append(lid).Append(':').Append(ltm).Append(':').Append(lst).Append(';');
                }
                string summary = sbL.ToString();
                if (summary != _lastHistorySummary)
                {
                    _lastHistorySummary = summary;
                    _gridHistory.SuspendLayout();
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
                    _gridHistory.ResumeLayout();
                }
            }

            // 2. Defects
            var defects = await _api.GetAllPermanentEventsAsync();
            if (defects != null)
            {
                var sbD = new System.Text.StringBuilder();
                foreach (var d in defects)
                {
                    string did = d.TryGetProperty("id", out var i) ? i.ToString() : "";
                    string dact = d.TryGetProperty("actionTaken", out var a) ? a.GetString() ?? "" : "";
                    sbD.Append(did).Append(':').Append(dact).Append(';');
                }
                string summary = sbD.ToString();
                if (summary != _lastDefectsSummary)
                {
                    _lastDefectsSummary = summary;
                    _gridDefects.SuspendLayout();
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
                    _gridDefects.ResumeLayout();
                }
            }

            // 3. Sub-worker tasks & Real-time Live Status Dashboard Cards
            var subTasks = await _api.GetSubWorkerTasksAsync();
            if (subTasks != null)
            {
                var sbS = new System.Text.StringBuilder();
                foreach (var st in subTasks)
                {
                    string stid = st.TryGetProperty("id", out var i) ? i.ToString() : "";
                    string stst = st.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                    sbS.Append(stid).Append(':').Append(stst).Append(';');
                }
                string summary = sbS.ToString();
                if (summary != _lastSubTasksSummary)
                {
                    _lastSubTasksSummary = summary;
                    _gridSubTasks.SuspendLayout();
                    _gridSubTasks.Rows.Clear();
                    var subWorkerLatest = new Dictionary<string, (string Loc, string Status, string Task, string Time)>();

                    foreach (var st in subTasks)
                    {
                        string date = st.TryGetProperty("createdAt", out var dt) ? dt.GetString() ?? "" : "";
                        string mach = st.TryGetProperty("machineId", out var m) ? m.GetString() ?? "R01" : "R01";
                        string order = st.TryGetProperty("orderNumber", out var o) ? o.GetString() ?? "" : "";
                        string part = st.TryGetProperty("partName", out var p) ? p.GetString() ?? "" : "";
                        string worker = st.TryGetProperty("subWorkerName", out var sw) ? sw.GetString() ?? "이서브" : "이서브";
                        string instr = st.TryGetProperty("instruction", out var ins) ? ins.GetString() ?? "" : "";
                        string leader = st.TryGetProperty("leaderUsername", out var ld) ? ld.GetString() ?? "-" : "-";
                        string status = st.TryGetProperty("status", out var stt) ? stt.GetString() ?? "지시중" : "지시중";
                        string report = st.TryGetProperty("reportContent", out var rep) ? rep.GetString() ?? "-" : "-";

                        _gridSubTasks.Rows.Add(date, worker, mach, status, instr, $"[{order}] {part}", leader, report);

                        if (!subWorkerLatest.ContainsKey(worker))
                        {
                            subWorkerLatest[worker] = (mach, status, instr, date);
                        }
                    }
                    _gridSubTasks.ResumeLayout();

                    // Update Top Live Cards Bar
                    if (_pnlSubWorkerCards != null)
                    {
                        _pnlSubWorkerCards.SuspendLayout();
                        _pnlSubWorkerCards.Controls.Clear();
                        var allKnownSubs = new string[] { "이서브", "김보조", "박지원", "최도움" };
                        foreach (var name in allKnownSubs)
                        {
                            string loc = subWorkerLatest.TryGetValue(name, out var info) ? info.Loc : "R01";
                            string stat = subWorkerLatest.TryGetValue(name, out info) ? info.Status : "대기중";
                            string taskDesc = subWorkerLatest.TryGetValue(name, out info) ? info.Task : "대기 (지시가능)";

                            Color statCol = stat.Contains("완료") ? Palette.AccentSky : (stat.Contains("가공") || stat.Contains("작업") ? Palette.Green : Palette.Amber);

                            var card = new Button
                            {
                                Text = $"👤 {name}  |  📍 {loc}  |  [{stat}]",
                                BackColor = Color.FromArgb(24, 34, 53),
                                ForeColor = statCol,
                                FlatStyle = FlatStyle.Flat,
                                AutoSize = true,
                                Height = 28,
                                Margin = new Padding(2, 2, 6, 2),
                                Padding = new Padding(6, 2, 6, 2),
                                Font = new Font("맑은 고딕", 8f, FontStyle.Bold),
                                Cursor = Cursors.Hand
                            };
                            card.FlatAppearance.BorderColor = Color.FromArgb(80, statCol);
                            card.FlatAppearance.BorderSize = 1;
                            string capturedName = name;
                            string capturedLoc = loc;
                            card.Click += (s, e) => OpenSubWorkerDispatchModal(capturedName, capturedLoc);
                            _pnlSubWorkerCards.Controls.Add(card);
                        }
                        _pnlSubWorkerCards.ResumeLayout();
                    }
                }
            }

            // 4. Post Process
            var posts = await _api.GetPostProcessReportsAsync();
            if (posts != null)
            {
                var sbP = new System.Text.StringBuilder();
                foreach (var pp in posts)
                {
                    string ppid = pp.TryGetProperty("id", out var i) ? i.ToString() : "";
                    string ppst = pp.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                    sbP.Append(ppid).Append(':').Append(ppst).Append(';');
                }
                string summary = sbP.ToString();
                if (summary != _lastPostProcessSummary)
                {
                    _lastPostProcessSummary = summary;
                    _gridPostProcess.SuspendLayout();
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
                    _gridPostProcess.ResumeLayout();
                }
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
                var sb = new System.Text.StringBuilder();
                foreach (var n in list)
                {
                    string title = n.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    string noticeId = n.TryGetProperty("id", out var idEl) ? idEl.ToString() : title;
                    string acks = n.TryGetProperty("officialAcksJson", out var ak) ? ak.GetString() ?? "" : "";
                    sb.Append(noticeId).Append(':').Append(acks).Append(';');

                    if (_isInitialDataLoaded && !string.IsNullOrEmpty(title) && !_knownNoticeTitles.Contains(noticeId))
                    {
                        ToastNotificationForm.ShowToast(
                            "📢 새 공지사항 알림",
                            $"[공지] {title}",
                            ToastType.Notice,
                            async () => await ShowNoticesDialogAsync()
                        );
                    }
                    if (!string.IsNullOrEmpty(noticeId)) _knownNoticeTitles.Add(noticeId);
                }

                string newSummary = sb.ToString();
                if (newSummary != _lastNoticesSummary)
                {
                    _lastNoticesSummary = newSummary;
                    if (_lstNotices != null)
                    {
                        _lstNotices.BeginUpdate();
                        _lstNotices.Items.Clear();
                    }
                    int unread = 0;
                    string firstNoticeTitle = "";
                    foreach (var n in list)
                    {
                        string title = n.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                        if (string.IsNullOrEmpty(firstNoticeTitle)) firstNoticeTitle = title;
                        string date = n.TryGetProperty("createdAt", out var cd) ? cd.GetString() ?? "" : "";
                        string acks = n.TryGetProperty("officialAcksJson", out var ak) ? ak.GetString() ?? "" : "";
                        bool isAcked = acks.Contains(_session.Username) || acks.Contains(_session.DisplayName);

                        if (DateTime.TryParse(date, out var dt)) date = dt.ToString("MM-dd HH:mm");
                        string badge = isAcked ? "✔" : "🔔";
                        if (!isAcked) unread++;

                        if (_lstNotices != null) _lstNotices.Items.Add($"{badge} {title} ({date})");
                    }
                    if (_lstNotices != null) _lstNotices.EndUpdate();

                    if (_lblNoticeBadge != null)
                    {
                        _lblNoticeBadge.Text = unread > 0 ? $"{unread}건 미확인" : "전원 확인완료";
                        _lblNoticeBadge.ForeColor = unread > 0 ? Palette.Amber : Palette.Green;
                    }
                    if (_btnOpenNoticesTop != null)
                    {
                        _btnOpenNoticesTop.Text = unread > 0 ? $"🔔 공지({unread})" : "📢 공지사항";
                        _btnOpenNoticesTop.ForeColor = unread > 0 ? Palette.Amber : Palette.AccentSky;
                    }
                    if (_lblTopNoticeBanner != null)
                    {
                        _lblTopNoticeBanner.Text = string.IsNullOrEmpty(firstNoticeTitle)
                            ? "📢 등록된 최신 공지가 없습니다."
                            : $"📢 최신: {firstNoticeTitle} (클릭시 전체목록)";
                    }
                }
            }
        }
        catch { }
    }

    private async Task LoadChatAsync()
    {
        try
        {
            var (cType, tKey) = GetCurrentChatTopic();
            var list = await _api.GetChatMessagesByChannelAsync(cType, tKey);
            if (list != null)
            {
                var newItems = new List<string>();
                var sbSummary = new System.Text.StringBuilder();

                foreach (var msg in list)
                {
                    string author = msg.TryGetProperty("author", out var s) ? s.GetString() ?? "" : "시스템";
                    string content = msg.TryGetProperty("content", out var m) ? m.GetString() ?? "" : "";
                    string createdAt = msg.TryGetProperty("createdAt", out var cd) ? cd.GetString() ?? "" : "";
                    string sig = $"{author}_{createdAt}_{content}";
                    sbSummary.Append(sig).Append(';');

                    if (_isInitialDataLoaded && !string.IsNullOrEmpty(content) && !_knownChatSignatures.Contains(sig))
                    {
                        if (!author.Equals(_session.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                            !author.Equals(_session.Username, StringComparison.OrdinalIgnoreCase))
                        {
                            ToastNotificationForm.ShowToast(
                                "💬 새 현장 채팅",
                                $"[{author}] {content}",
                                ToastType.Chat,
                                () =>
                                {
                                    if (_txtChatInput != null && !_txtChatInput.IsDisposed)
                                    {
                                        _txtChatInput.Focus();
                                    }
                                }
                            );
                        }
                    }
                    if (!string.IsNullOrEmpty(sig)) _knownChatSignatures.Add(sig);

                    if (DateTime.TryParse(createdAt, out var dt)) createdAt = dt.ToString("HH:mm");
                    newItems.Add($"[{author}] {content} ({createdAt})");
                }

                string summary = sbSummary.ToString();
                if (summary != _lastChatSummary)
                {
                    _lastChatSummary = summary;
                    _lstChat.BeginUpdate();
                    _lstChat.Items.Clear();
                    foreach (var line in newItems) _lstChat.Items.Add(line);
                    if (_lstChat.Items.Count > 0) _lstChat.TopIndex = _lstChat.Items.Count - 1;
                    _lstChat.EndUpdate();
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

    // =========================================================================
    // REALTIME AUTO-SAVE & RECOVERY SYSTEM (JOB-UNIT & DEBOUNCE BASED)
    // =========================================================================
    private void UpdateSaveBadge(SaveStatusEventArgs e)
    {
        if (_lblAutoSaveBadge == null || _lblAutoSaveBadge.IsDisposed) return;
        switch (e.State)
        {
            case SaveState.Saving:
                _lblAutoSaveBadge.Text = "💾 저장 중...";
                _lblAutoSaveBadge.ForeColor = Palette.AccentSky;
                break;
            case SaveState.Success:
                _lblAutoSaveBadge.Text = e.Message;
                _lblAutoSaveBadge.ForeColor = Palette.Green;
                break;
            case SaveState.Failed:
                _lblAutoSaveBadge.Text = "⚠️ 저장 실패";
                _lblAutoSaveBadge.ForeColor = Palette.Rose;
                break;
        }
    }

    private void AutoSaveWorkflow()
    {
        if (_canvas == null || _canvas.Nodes.Count == 0) return;

        string currentOrder = _cachedOrders.Count > 0 && _cboOrders.SelectedIndex >= 0
            ? _cachedOrders[_cboOrders.SelectedIndex].GetProperty("orderNumber").GetString() ?? "" : "";
        string currentPart = _lstParts.SelectedItem?.ToString() ?? "";

        if (_currentJob == null || (!string.IsNullOrEmpty(currentOrder) && _currentJob.OrderNumber != currentOrder))
        {
            _currentJob = _storageManager.CreateJob(currentOrder, currentPart, author: _session.DisplayName);
        }

        // [요구사항 2] 노드의 작업 소속 유지 및 엣지 교차 참조
        string activeJobId = _currentJob.JobId;
        foreach (var node in _canvas.Nodes)
        {
            if (string.IsNullOrEmpty(node.JobId))
                node.JobId = activeJobId;
        }
        foreach (var edge in _canvas.Edges)
        {
            var fn = _canvas.Nodes.FirstOrDefault(n => n.Id == edge.FromNodeId);
            var tn = _canvas.Nodes.FirstOrDefault(n => n.Id == edge.ToNodeId);
            if (string.IsNullOrEmpty(edge.FromJobId)) edge.FromJobId = fn?.JobId ?? activeJobId;
            if (string.IsNullOrEmpty(edge.ToJobId)) edge.ToJobId = tn?.JobId ?? activeJobId;
        }

        // [요구사항 3] 디바운스(Debounce 600ms) 지연 저장 실행
        _storageManager.RequestDebouncedSave(async () =>
        {
            if (_canvas == null || _currentJob == null) return;

            var snapshotNodes = _canvas.Nodes.ToList();
            var snapshotEdges = _canvas.Edges.ToList();
            var snapshotBoxes = _canvas.TitleBoxes.ToList();

            // [요구사항 4] 계획 버전 감지 및 보존 (노드 위치 이동은 제외, 공정/순서/장비/작업자 변경 시 기록)
            await _storageManager.RecordPlanVersionIfChangedAsync(
                _currentJob,
                snapshotNodes,
                snapshotEdges,
                _session.DisplayName,
                "작업 워크플로우 실시간 공정계획 변경"
            );

            _currentJob.Nodes = snapshotNodes;
            _currentJob.Edges = snapshotEdges;
            _currentJob.TitleBoxes = snapshotBoxes;
            _currentJob.IsConfirmed = _canvas.IsWorkflowConfirmed;
            _currentJob.ConfirmedBy = _canvas.ConfirmedInfoText;

            // 작업 단위 디렉토리에 안전 저장 (임시파일 교체 + .bak 보존)
            await _storageManager.SaveJobAsync(_currentJob, _session.DisplayName);

            // 하위 호환성 레거시 캐시 동기화
            try
            {
                var legacyData = new WorkflowData
                {
                    JobId = _currentJob.JobId,
                    OrderNumber = _currentJob.OrderNumber,
                    PartName = _currentJob.PartName,
                    Nodes = snapshotNodes,
                    Edges = snapshotEdges,
                    TitleBoxes = snapshotBoxes,
                    LastSavedAt = DateTime.Now,
                    IsConfirmed = _canvas.IsWorkflowConfirmed,
                    ConfirmedBy = _canvas.ConfirmedInfoText
                };
                string legacyJson = JsonSerializer.Serialize(legacyData, new JsonSerializerOptions { WriteIndented = true });
                string activeFile = Path.Combine(_localWorkflowDir, "autosave_current_active.json");
                await File.WriteAllTextAsync(activeFile, legacyJson);

                string cacheKey = GetCurrentPartCacheKey();
                if (!string.IsNullOrEmpty(cacheKey))
                {
                    string partFile = Path.Combine(_localWorkflowDir, $"{cacheKey}.json");
                    await File.WriteAllTextAsync(partFile, legacyJson);
                }
            }
            catch { }
        }, delayMs: 600);
    }

    private bool TryRestoreAutoSavedWorkflow()
    {
        try
        {
            // 1. 레거시 파일 마이그레이션 백그라운드 기동
            _ = Task.Run(async () => await _storageManager.MigrateLegacyWorkflowsAsync());

            // 2. 신규 작업 단위 활성 세션 복구 시도
            string? lastActiveJobId = _storageManager.GetLastActiveJobIdAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(lastActiveJobId))
            {
                var job = _storageManager.LoadJobAsync(lastActiveJobId).GetAwaiter().GetResult();
                if (job != null && job.Nodes.Count > 0)
                {
                    _currentJob = job;
                    _canvas.Clear();
                    _canvas.Nodes.AddRange(job.Nodes);
                    _canvas.Edges.AddRange(job.Edges);
                    if (job.TitleBoxes != null) _canvas.TitleBoxes.AddRange(job.TitleBoxes);
                    _canvas.IsWorkflowConfirmed = job.IsConfirmed;
                    _canvas.ConfirmedInfoText = job.ConfirmedBy;
                    _canvas.Invalidate();
                    _canvas.SelectedNode = _canvas.Nodes.FirstOrDefault();
                    _detailPanel.BindNode(_canvas.SelectedNode);
                    UpdateWorkflowStats();

                    if (_lblAutoSaveBadge != null)
                    {
                        _lblAutoSaveBadge.Text = $"📂 작업 복원됨 [{job.JobId[..Math.Min(16, job.JobId.Length)]}...]";
                        _lblAutoSaveBadge.ForeColor = Palette.AccentSky;
                    }
                    return true;
                }
            }

            // 3. 레거시 파일로부터의 복구
            string activeFile = Path.Combine(_localWorkflowDir, "autosave_current_active.json");
            if (File.Exists(activeFile))
            {
                string json = File.ReadAllText(activeFile);
                var data = JsonSerializer.Deserialize<WorkflowData>(json);
                if (data != null && data.Nodes.Count > 0)
                {
                    _canvas.Clear();
                    _canvas.Nodes.AddRange(data.Nodes);
                    _canvas.Edges.AddRange(data.Edges);
                    if (data.TitleBoxes != null) _canvas.TitleBoxes.AddRange(data.TitleBoxes);
                    _canvas.IsWorkflowConfirmed = data.IsConfirmed;
                    _canvas.ConfirmedInfoText = data.ConfirmedBy;
                    _canvas.Invalidate();
                    _canvas.SelectedNode = _canvas.Nodes.FirstOrDefault();
                    _detailPanel.BindNode(_canvas.SelectedNode);
                    UpdateWorkflowStats();

                    _currentJob = _storageManager.CreateJob(data.OrderNumber, data.PartName, data.OrderId, data.PartId, author: _session.DisplayName);
                    _currentJob.Nodes = data.Nodes;
                    _currentJob.Edges = data.Edges;
                    _currentJob.TitleBoxes = data.TitleBoxes;
                    _ = _storageManager.SaveJobAsync(_currentJob, _session.DisplayName);

                    if (_lblAutoSaveBadge != null)
                    {
                        _lblAutoSaveBadge.Text = $"📂 자동복원됨 ({data.LastSavedAt:HH:mm})";
                        _lblAutoSaveBadge.ForeColor = Palette.AccentSky;
                    }
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    // =========================================================================
    // UI BUILDER HELPERS
    // =========================================================================
    private Panel CreateCard(string title, Control bodyContent)
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg900,
            Padding = new Padding(6),
            Margin = new Padding(0, 0, 0, 6)
        };
        pnl.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnl.Width - 1, pnl.Height - 1);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // Dedicated Header Row
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Dedicated Content Body Row

        var lblHeader = new Label
        {
            Text = title,
            Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
            ForeColor = Palette.TextWhite,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };
        layout.Controls.Add(lblHeader, 0, 0);

        bodyContent.Dock = DockStyle.Fill;
        layout.Controls.Add(bodyContent, 0, 1);

        pnl.Controls.Add(layout);
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

    // =========================================================================
    // REWORK (재가공) & HISTORY DIALOG
    // =========================================================================
    private async Task CreateReworkWorkflowActionAsync()
    {
        if (_canvas == null || _canvas.Nodes.Count == 0)
        {
            MessageBox.Show("재가공을 생성할 기준 워크플로우 공정이 없습니다.", "재가공 생성 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string currentOrder = _cachedOrders.Count > 0 && _cboOrders.SelectedIndex >= 0
            ? _cachedOrders[_cboOrders.SelectedIndex].GetProperty("orderNumber").GetString() ?? "WO" : "WO";
        string currentPart = _lstParts.SelectedItem?.ToString() ?? "PART";

        if (_currentJob == null)
        {
            _currentJob = _storageManager.CreateJob(currentOrder, currentPart, author: _session.DisplayName);
            _currentJob.Nodes = _canvas.Nodes.ToList();
            _currentJob.Edges = _canvas.Edges.ToList();
            _currentJob.TitleBoxes = _canvas.TitleBoxes.ToList();
        }

        string reworkReason = SimpleInputDialog.Show("재가공 사유를 입력하세요 (예: 치수 정밀도 보정, 추가 단차 밀링 등):", "재가공 워크플로우 생성", "치수 재가공 요청");
        if (string.IsNullOrWhiteSpace(reworkReason))
            return;

        reworkReason = reworkReason.Trim();

        string qtyStr = SimpleInputDialog.Show("재가공 대상 수량을 입력하세요:", "재가공 수량 입력", "1");
        if (string.IsNullOrWhiteSpace(qtyStr))
            return;

        int reworkQty = int.TryParse(qtyStr.Trim(), out int parsed) ? Math.Max(1, parsed) : 1;

        try
        {
            var reworkJob = await _storageManager.CreateReworkJobAsync(_currentJob, reworkReason, reworkQty, _session.DisplayName);

            _currentJob = reworkJob;
            _canvas.Clear();
            _canvas.Nodes.AddRange(reworkJob.Nodes);
            _canvas.Edges.AddRange(reworkJob.Edges);
            _canvas.TitleBoxes.AddRange(reworkJob.TitleBoxes);
            _canvas.IsWorkflowConfirmed = false;
            _canvas.ConfirmedInfoText = "";
            _canvas.Invalidate();
            _canvas.SelectedNode = _canvas.Nodes.FirstOrDefault();
            _detailPanel.BindNode(_canvas.SelectedNode);
            UpdateWorkflowStats();

            string msg = $"[재가공 워크플로우 생성 완료]\r\n\r\n" +
                         $"원작업: {reworkJob.ParentJobId}\r\n" +
                         $"신규 재가공 ID: {reworkJob.JobId}\r\n" +
                         $"사유: {reworkJob.ReworkReason}\r\n" +
                         $"대상수량: {reworkJob.TargetQuantity}개\r\n\r\n" +
                         $"기존 완료 기록은 안전하게 보존되며, 새 작업은 대기 상태로 초기화되었습니다.";
            MessageBox.Show(msg, "재가공 워크플로우 생성", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"재가공 생성 중 오류가 발생했습니다: {ex.Message}", "생성 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ShowJobHistoryDialogAsync()
    {
        if (_currentJob == null)
        {
            MessageBox.Show("조회할 활성 작업이 없습니다.", "작업 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var executions = await _storageManager.LoadExecutionHistoriesAsync(_currentJob.JobId);
        string jobDir = _storageManager.GetJobDir(_currentJob.JobId);
        string versionsFile = Path.Combine(jobDir, "plan_versions.json");
        List<PlanVersionRecord> versions = new();
        if (File.Exists(versionsFile))
        {
            try
            {
                string json = await File.ReadAllTextAsync(versionsFile);
                versions = JsonSerializer.Deserialize<List<PlanVersionRecord>>(json) ?? new();
            }
            catch { }
        }

        using var dlg = new Form
        {
            Text = $"작업 이력 및 버전 관리 - [{_currentJob.JobId}]",
            Size = new Size(820, 520),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Palette.Bg950,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9f)
        };

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var tabPlan = new TabPage("📋 공정 계획 변경 버전 이력") { BackColor = Palette.Bg900 };
        var tabExec = new TabPage("⚡ 실제 실행/측정/이슈 실적") { BackColor = Palette.Bg900 };

        var txtPlan = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg950,
            ForeColor = Palette.TextWhite,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 9.2f)
        };
        if (versions.Count == 0)
        {
            txtPlan.Text = "기록된 계획 변경 버전이 없습니다. (단순 노드 이동은 버전에 포함되지 않으며, 공정 추가/삭제/장비 변경 시 자동 기록됩니다)";
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            foreach (var v in versions)
            {
                sb.AppendLine($"[버전 {v.VersionNumber}] 일시: {v.ChangedAt:yyyy-MM-dd HH:mm:ss} | 변경자: {v.ChangedBy}");
                sb.AppendLine($"  사유: {v.ChangeReason}");
                sb.AppendLine($"  공정 수: {v.NodesSnapshot.Count}개, 연결선: {v.EdgesSnapshot.Count}개");
                sb.AppendLine(new string('-', 70));
            }
            txtPlan.Text = sb.ToString();
        }
        tabPlan.Controls.Add(txtPlan);

        var txtExec = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg950,
            ForeColor = Palette.TextWhite,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 9.2f)
        };
        if (executions.Count == 0)
        {
            txtExec.Text = "기록된 실제 실행/측정/이슈 실적이 없습니다. (노드 상태 변경 및 작업 진행 시 영구 보존됩니다)";
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ex in executions.OrderByDescending(e => e.Timestamp))
            {
                sb.AppendLine($"[{ex.Timestamp:yyyy-MM-dd HH:mm:ss}] [{ex.EventType}] {ex.NodeTitle} (장비: {ex.MachineId}, 작업자: {ex.WorkerName})");
                if (!string.IsNullOrEmpty(ex.Note)) sb.AppendLine($"  상태 요약: {ex.Note}");
                if (!string.IsNullOrEmpty(ex.InspectionDetails)) sb.AppendLine($"  측정 내용: {ex.InspectionDetails}");
                if (!string.IsNullOrEmpty(ex.IssueDetails)) sb.AppendLine($"  이슈 내용: {ex.IssueDetails}");
                sb.AppendLine(new string('-', 70));
            }
            txtExec.Text = sb.ToString();
        }
        tabExec.Controls.Add(txtExec);

        tabs.TabPages.Add(tabPlan);
        tabs.TabPages.Add(tabExec);
        dlg.Controls.Add(tabs);

        dlg.ShowDialog(this);
    }
}
