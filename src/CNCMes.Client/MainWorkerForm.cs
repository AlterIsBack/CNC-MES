using CNCMes.Client.Workflow;
using System.Text.Json;

namespace CNCMes.Client;

public class MainWorkerForm : Form
{
    // =========================================================================
    // CNC-MES High-Tech Dark Theme Color Palette (Matching ui/code.html)
    // =========================================================================
    private static class CncTheme
    {
        public static readonly Color Bg950 = Color.FromArgb(7, 11, 20);        // #070b14 (Root form, table body)
        public static readonly Color Bg900 = Color.FromArgb(11, 19, 35);       // #0b1323 (Header, Cards, Footer)
        public static readonly Color Bg850 = Color.FromArgb(15, 27, 49);       // #0f1b31 (Card inputs, sub-panels)
        public static readonly Color Bg800 = Color.FromArgb(20, 35, 64);       // #142340 (Elevated buttons)
        public static readonly Color Border = Color.FromArgb(29, 46, 78);       // #1d2e4e (Divider, card borders)
        public static readonly Color BorderLight = Color.FromArgb(42, 67, 111); // #2a436f (Input borders)
        public static readonly Color AccentSky = Color.FromArgb(14, 165, 233);  // #0ea5e9 (Cyan/Sky Brand)
        public static readonly Color Green = Color.FromArgb(16, 185, 129);     // #10b981 (Active Machining, Success)
        public static readonly Color GreenBg = Color.FromArgb(6, 78, 59);      // #064e3b (Active Machining card bg)
        public static readonly Color Amber = Color.FromArgb(245, 158, 11);      // #f59e0b (Standby, Warning)
        public static readonly Color AmberBg = Color.FromArgb(69, 26, 3);      // #451a03 (Standby card bg)
        public static readonly Color Rose = Color.FromArgb(220, 38, 38);       // #dc2626 (Emergency / Defect)
        public static readonly Color TextWhite = Color.FromArgb(255, 255, 255);
        public static readonly Color TextSlate200 = Color.FromArgb(226, 232, 240);
        public static readonly Color TextSlate300 = Color.FromArgb(203, 213, 225);
        public static readonly Color TextSlate400 = Color.FromArgb(148, 163, 184);
        public static readonly Color TextSlate500 = Color.FromArgb(100, 116, 139);
    }

    private Label _lblLiveClock = null!;
    private Label _lblMachineCounts = null!;
    private readonly ApiClient _api;
    private readonly UserSession _session;

    // Top Header Controls
    private Label _lblActiveMachineSummary = null!;
    private Button _btnOrderList = null!;
    private Button _btnNoticeAlert = null!;
    private Button _btnNewChatBadge = null!;

    // Row 1: Multi-Machine Selector Cards
    private FlowLayoutPanel _pnlMachineCards = null!;
    private readonly HashSet<string> _mySelectedMachines = new();
    private string _currentMachineId = "R01";
    private List<JsonElement> _allMachines = new();

    // Left Card 1: Work Order / Part / Quick Buttons / Manual History Input
    private ComboBox _cboParts = null!;
    private TextBox _txtOrderNumber = null!;
    private TextBox _txtPartName = null!;
    private TextBox _txtManualQty = null!;
    private CheckBox _chkApplyAllSelected = null!;
    private Panel _pnlAlert = null!;
    private Label _lblAlertText = null!;
    private TextBox _txtWorkDescription = null!;
    private Button _btnSaveManualLog = null!;

    // Status Toggle Buttons (Category Tabs) & 4x3 Customizable Quick Task Buttons
    private string _currentStatus = "세팅중";
    private readonly List<Button> _statusButtons = new();
    private readonly List<Button> _quickTaskButtons = new();
    private Button _btnEditQuickButtons = null!;

    // 3-Click Execution Buttons
    private Button _btnComplete = null!;
    private Button _btnIssue = null!;
    private DataGridView _gridPostProcess = null!;
    private List<JsonElement> _cachedPostProcess = new();
    private string _currentPostProcessFilter = "전체";

    // Left Card 2: Sub-Worker Instruction & Report Aggregation -> Central Server Report
    private ComboBox _cboSubMachine = null!;
    private TextBox _txtSubWorkerName = null!;
    private TextBox _txtSubInstruction = null!;
    private DataGridView _gridSubTasks = null!;
    private List<JsonElement> _cachedSubTasks = new();

    // Left Card 2: 3개 탭 구성 (불량/대책 / 서브인원 지시·보고 / 주요 가공 사용 공구 목록)
    private TabControl _bottomLeftTabs = null!;
    // Custom Tab Bar Buttons for Bottom Left Pane (Matching Stitch UI screen.png)
    private readonly List<Button> _customTabButtons = new();
    private TableLayoutPanel _pnlMachineGrid = null!;
    private ComboBox _cboDefectCategory = null!;
    private ComboBox _cboDefectType = null!;
    private TextBox _txtDefectPartCode = null!;
    private TextBox _txtDefectDesc = null!;
    private TextBox _txtDefectCause = null!;
    private TextBox _txtDefectCountermeasure = null!;
    private DataGridView _gridDefects = null!;
    private List<JsonElement> _cachedDefects = new();

    // Left Card 2 (Tab 3): 주요 가공 사용 공구 목록 기록 및 조회
    private TextBox _txtToolName = null!;
    private TextBox _txtToolMaker = null!;
    private TextBox _txtToolDiameter = null!;
    private TextBox _txtToolFluteLength = null!;
    private TextBox _txtToolMaterial = null!;
    private TextBox _txtToolNotes = null!;
    private DataGridView _gridUsedTools = null!;
    private List<JsonElement> _cachedUsedTools = new();

    // Right Card 1: Recent Machine Work History (OrderNumber + PartName, NO Execution ID)
    private Label _lblHistoryHeader = null!;
    private CheckBox _chkHistoryFilterMyMachines = null!;
    private DataGridView _gridHistory = null!;
    private List<JsonElement> _cachedHistoryLogs = new();

    // Right Card 2: Realtime Chat & New Chat Notification
    private Label _lblChatHeader = null!;
    private Label _lblChatNewIndicator = null!;
    private ComboBox _cboChatChannel = null!;
    private ListBox _lstChat = null!;
    private TextBox _txtChatInput = null!;
    private int _lastSeenChatId = -1;
    private bool _hasUnreadChat = false;

    // Background Polling Timer
    private System.Windows.Forms.Timer _pollTimer = null!;

    // Cached Data
    private List<JsonElement> _cachedOrders = new();
    private List<JsonElement> _cachedParts = new();
    private JsonElement? _selectedPart = null;
    private int _currentActiveExecId = 0;

    public MainWorkerForm(ApiClient api, UserSession session)
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
        Text = $"[CNC-MES 스마트 현장 터미널 v1.4.0] 작업자: {_session.DisplayName} ({_api.Config.ServerUrl})";
        Size = new Size(1500, 950);
        MinimumSize = new Size(1280, 820);
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = CncTheme.Bg950;
        ForeColor = Color.White;
        Font = new Font("맑은 고딕", 9.2f, FontStyle.Regular);
        AutoScaleMode = AutoScaleMode.Dpi;

        // =====================================================================
        // ROOT TABLE LAYOUT (4 Rows: Header 54px | Multi-Machine Bar 88px | Body 100% | Status 24px)
        // =====================================================================
        var rootTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = CncTheme.Bg950
        };
        rootTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rootTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f)); // Global Header
        rootTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 88f)); // Machine Selector Strip
        rootTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Main Body
        rootTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f)); // Bottom Diagnostics Bar

        // ---------------------------------------------------------------------
        // ROW 0: Modern Global Navigation Header (Matches code.html GlobalHeader)
        // ---------------------------------------------------------------------
        var headerBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = CncTheme.Bg900,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0)
        };
        headerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350f)); // Left Brand & Title
        headerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // Center Active Machine Box
        headerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430f)); // Right Action Buttons

        headerBar.Paint += (s, e) =>
        {
            using var pen = new Pen(CncTheme.Border, 1);
            e.Graphics.DrawLine(pen, 0, headerBar.Height - 1, headerBar.Width, headerBar.Height - 1);
        };

        // Left Brand Area
        var pnlBrand = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };

        var pnlChipIcon = new Panel
        {
            Location = new Point(0, 9),
            Size = new Size(36, 36),
            BackColor = Color.FromArgb(40, 14, 165, 233)
        };
        pnlChipIcon.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(120, 14, 165, 233), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, 35, 35);
            using var font = new Font("Segoe UI Symbol", 13f, FontStyle.Bold);
            using var brush = new SolidBrush(CncTheme.AccentSky);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString("⚡", font, brush, new RectangleF(0, 0, 36, 36), sf);
        };
        pnlBrand.Controls.Add(pnlChipIcon);

        var lblBrandTitle = new Label
        {
            Text = "CNC-MES",
            Location = new Point(44, 9),
            Font = new Font("맑은 고딕", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true
        };
        pnlBrand.Controls.Add(lblBrandTitle);

        var lblVersionBadge = new Label
        {
            Text = "v1.4.0",
            Location = new Point(136, 12),
            Font = new Font("Consolas", 8f, FontStyle.Bold),
            ForeColor = CncTheme.AccentSky,
            BackColor = Color.FromArgb(80, 8, 47, 73),
            AutoSize = true,
            Padding = new Padding(4, 1, 4, 1)
        };
        pnlBrand.Controls.Add(lblVersionBadge);

        var lblServerStatus = new Label
        {
            Text = "● 서버 정상",
            Location = new Point(190, 12),
            Font = new Font("맑은 고딕", 8f, FontStyle.Bold),
            ForeColor = CncTheme.Green,
            BackColor = Color.FromArgb(80, 6, 78, 59),
            AutoSize = true,
            Padding = new Padding(5, 1, 5, 1)
        };
        pnlBrand.Controls.Add(lblServerStatus);

        var lblBrandSub = new Label
        {
            Text = "SMART FACTORY OPERATION CONTROL SYSTEM",
            Location = new Point(45, 31),
            Font = new Font("Consolas", 7.2f, FontStyle.Regular),
            ForeColor = CncTheme.TextSlate400,
            AutoSize = true
        };
        pnlBrand.Controls.Add(lblBrandSub);
        headerBar.Controls.Add(pnlBrand, 0, 0);

        // Center Active Machine Box (Centered Table Panel)
        var pnlCenterContainer = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };

        var pnlCenterBox = new Panel
        {
            Size = new Size(540, 36),
            BackColor = CncTheme.Bg850
        };
        pnlCenterBox.Paint += (s, e) =>
        {
            using var pen = new Pen(CncTheme.BorderLight, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlCenterBox.Width - 1, pnlCenterBox.Height - 1);
        };
        pnlCenterContainer.Resize += (s, e) =>
        {
            pnlCenterBox.Location = new Point(
                Math.Max(0, (pnlCenterContainer.Width - pnlCenterBox.Width) / 2),
                Math.Max(0, (pnlCenterContainer.Height - pnlCenterBox.Height) / 2)
            );
        };

        var centerFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0)
        };

        var lblCurMachTitle = new Label
        {
            Text = "현재 조작 장비:",
            ForeColor = CncTheme.TextSlate400,
            Font = new Font("맑은 고딕", 8.8f),
            AutoSize = true,
            Margin = new Padding(0, 4, 4, 0)
        };

        _lblActiveMachineSummary = new Label
        {
            Text = $"[⚙ {_currentMachineId}]",
            Font = new Font("Consolas", 9.8f, FontStyle.Bold),
            ForeColor = CncTheme.Green,
            BackColor = Color.FromArgb(80, 6, 78, 59),
            AutoSize = true,
            Padding = new Padding(6, 2, 6, 2),
            Margin = new Padding(0, 1, 10, 0)
        };

        var lblDiv1 = new Label
        {
            Text = "|",
            ForeColor = CncTheme.BorderLight,
            Font = new Font("맑은 고딕", 9.5f),
            AutoSize = true,
            Margin = new Padding(0, 3, 10, 0)
        };

        var chkMyMachinesHeader = new CheckBox
        {
            Text = $"내 담당 장비(2대): R01, R02",
            Checked = true,
            ForeColor = CncTheme.TextSlate300,
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 4, 10, 0),
            Cursor = Cursors.Hand
        };

        var lblDiv2 = new Label
        {
            Text = "|",
            ForeColor = CncTheme.BorderLight,
            Font = new Font("맑은 고딕", 9.5f),
            AutoSize = true,
            Margin = new Padding(0, 3, 10, 0)
        };

        _lblLiveClock = new Label
        {
            Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ForeColor = CncTheme.TextSlate400,
            Font = new Font("Consolas", 8.8f),
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 0)
        };

        centerFlow.Controls.AddRange(new Control[] { lblCurMachTitle, _lblActiveMachineSummary, lblDiv1, chkMyMachinesHeader, lblDiv2, _lblLiveClock });
        pnlCenterBox.Controls.Add(centerFlow);
        pnlCenterContainer.Controls.Add(pnlCenterBox);
        headerBar.Controls.Add(pnlCenterContainer, 1, 0);

        // Right Header Actions (Fixed 430px Right Aligned)
        var pnlRightActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 11, 0, 0),
            Margin = new Padding(0)
        };

        var btnRefreshAll = CreateFlatButton("🔄", CncTheme.Bg800, CncTheme.TextSlate300, 32, 32);
        btnRefreshAll.Font = new Font("맑은 고딕", 10f);
        btnRefreshAll.Margin = new Padding(4, 0, 0, 0);
        btnRefreshAll.Click += async (s, e) => await RefreshAllAsync();

        var pnlUserBadge = new Panel
        {
            Size = new Size(100, 32),
            BackColor = CncTheme.Bg800,
            Margin = new Padding(4, 0, 0, 0)
        };
        pnlUserBadge.Paint += (s, e) =>
        {
            using var pen = new Pen(CncTheme.Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlUserBadge.Width - 1, pnlUserBadge.Height - 1);
            using var brushBg = new SolidBrush(Color.FromArgb(40, 14, 165, 233));
            e.Graphics.FillRectangle(brushBg, 4, 4, 24, 24);
            using var fontA = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
            using var brushSky = new SolidBrush(CncTheme.AccentSky);
            e.Graphics.DrawString("a", fontA, brushSky, new PointF(11, 7));
            using var fontTxt = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
            using var brushTxt = new SolidBrush(CncTheme.TextSlate200);
            e.Graphics.DrawString($"작업자 [{_session.DisplayName}]", fontTxt, brushTxt, new PointF(32, 7));
        };

        var btnSwitchToWf = CreateFlatButton("⚡ 워크플로우 뷰", Color.FromArgb(168, 85, 247), Color.White, 130, 32);
        btnSwitchToWf.Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold);
        btnSwitchToWf.Margin = new Padding(4, 0, 0, 0);
        btnSwitchToWf.Click += (s, e) => {
            var wf = new WorkflowMainForm(_api, _session);
            wf.Show();
            Hide();
            wf.FormClosed += (s2, e2) => Close();
        };

        _btnOrderList = CreateFlatButton("📋 작업지시서 목록 열람", Color.FromArgb(2, 132, 199), Color.White, 155, 32);
        _btnOrderList.Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold);
        _btnOrderList.Margin = new Padding(4, 0, 0, 0);
        _btnOrderList.Click += async (s, e) => await ShowWorkOrderBrowserDialogAsync();

        _btnNoticeAlert = CreateFlatButton("📢 공지사항 (0건)", CncTheme.Bg800, CncTheme.Amber, 120, 32);
        _btnNoticeAlert.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
        _btnNoticeAlert.Margin = new Padding(4, 0, 0, 0);
        _btnNoticeAlert.Click += async (s, e) => await ShowNoticesDialogAsync();

        _btnNewChatBadge = CreateFlatButton("🔔 새 채팅!", CncTheme.Rose, Color.White, 95, 32);
        _btnNewChatBadge.Visible = false;
        _btnNewChatBadge.Margin = new Padding(4, 0, 0, 0);
        _btnNewChatBadge.Click += (s, e) => ClearUnreadChatAlert();

        pnlRightActions.Controls.AddRange(new Control[] { btnRefreshAll, pnlUserBadge, btnSwitchToWf, _btnOrderList, _btnNoticeAlert, _btnNewChatBadge });
        headerBar.Controls.Add(pnlRightActions, 2, 0);
        rootTable.Controls.Add(headerBar, 0, 0);

        // ---------------------------------------------------------------------
        // ROW 1: Multi-Machine Selector Strip (Matches code.html MachineSelectorStrip)
        // ---------------------------------------------------------------------
        var machineStrip = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CncTheme.Bg900,
            Padding = new Padding(14, 4, 14, 4),
            Margin = new Padding(0)
        };
        machineStrip.Paint += (s, e) =>
        {
            using var pen = new Pen(CncTheme.Border, 1);
            e.Graphics.DrawLine(pen, 0, machineStrip.Height - 1, machineStrip.Width, machineStrip.Height - 1);
        };

        var stripHeaderRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 20,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        stripHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));
        stripHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));

        var lblStripGuide = new Label
        {
            Dock = DockStyle.Fill,
            Text = "🎛 다중 장비 선택 바 — [✔ 내 담당]을 체크해 여러 대의 장비를 동시에 관리하고, 카드를 클릭해 조작 장비를 전환하십시오.",
            Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold),
            ForeColor = CncTheme.TextSlate300,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _lblMachineCounts = new Label
        {
            Dock = DockStyle.Fill,
            Text = "● 가공중 (1)   ● 대기 (6)   ● 정지 (0)",
            Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold),
            ForeColor = CncTheme.TextSlate400,
            TextAlign = ContentAlignment.MiddleRight
        };

        stripHeaderRow.Controls.Add(lblStripGuide, 0, 0);
        stripHeaderRow.Controls.Add(_lblMachineCounts, 1, 0);
        machineStrip.Controls.Add(stripHeaderRow);

        // 7-Column Equal Distribution Grid (K01, K02, R01, R02, R03, R04, R05)
        _pnlMachineGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            BackColor = CncTheme.Bg900,
            Margin = new Padding(0, 2, 0, 0),
            Padding = new Padding(0, 2, 0, 0)
        };
        for (int i = 0; i < 7; i++)
        {
            _pnlMachineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14.2857f));
        }
        _pnlMachineGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        machineStrip.Controls.Add(_pnlMachineGrid);
        stripHeaderRow.BringToFront();
        _pnlMachineGrid.BringToFront();
        rootTable.Controls.Add(machineStrip, 0, 1);

        // ---------------------------------------------------------------------
        // ROW 2: Main Body Workspace (Left 58% | Right 42%)
        // ---------------------------------------------------------------------
        var bodyWorkspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10, 8, 10, 4),
            Margin = new Padding(0),
            BackColor = CncTheme.Bg950
        };
        bodyWorkspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        bodyWorkspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        bodyWorkspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Left Workspace (Top: Machining Control Card | Bottom: Tabbed Panes)
        var leftWorkspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 6, 0)
        };
        leftWorkspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        leftWorkspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 430f)); // Machining Control
        leftWorkspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Tabbed Pane

        leftWorkspace.Controls.Add(BuildWorkControlCard(), 0, 0);
        leftWorkspace.Controls.Add(BuildBottomLeftTabbedCard(), 0, 1);
        bodyWorkspace.Controls.Add(leftWorkspace, 0, 0);

        // Right Workspace (Top: History 55% | Bottom: Chat 45%)
        var rightWorkspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(6, 0, 0, 0)
        };
        rightWorkspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rightWorkspace.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
        rightWorkspace.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));

        rightWorkspace.Controls.Add(BuildHistoryCard(), 0, 0);
        rightWorkspace.Controls.Add(BuildChatCard(), 0, 1);
        bodyWorkspace.Controls.Add(rightWorkspace, 1, 0);

        rootTable.Controls.Add(bodyWorkspace, 0, 2);

        // ---------------------------------------------------------------------
        // ROW 3: Bottom Diagnostics Status Bar (Matches code.html BottomStatusBar)
        // ---------------------------------------------------------------------
        var bottomStatusBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = CncTheme.Bg900,
            Padding = new Padding(14, 2, 14, 2),
            Margin = new Padding(0)
        };
        bottomStatusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        bottomStatusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        bottomStatusBar.Paint += (s, e) =>
        {
            using var pen = new Pen(CncTheme.Border, 1);
            e.Graphics.DrawLine(pen, 0, 0, bottomStatusBar.Width, 0);
        };

        var lblStatusLeft = new Label
        {
            Dock = DockStyle.Fill,
            Text = "● PLC I/O LINK: CONNECTED   DNC PROTOCOL: ACTIVE (PORT: 8080)   BUFFER SYNC: 100%",
            Font = new Font("Consolas", 8.2f),
            ForeColor = CncTheme.Green,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var lblStatusRight = new Label
        {
            Dock = DockStyle.Fill,
            Text = "SERVER LATENCY: 12ms   CNC CONTROL TERMINAL ID: TERM-SEC-01",
            Font = new Font("Consolas", 8.2f),
            ForeColor = CncTheme.TextSlate400,
            TextAlign = ContentAlignment.MiddleRight
        };

        bottomStatusBar.Controls.Add(lblStatusLeft, 0, 0);
        bottomStatusBar.Controls.Add(lblStatusRight, 1, 0);
        rootTable.Controls.Add(bottomStatusBar, 0, 3);

        Controls.Add(rootTable);

        // Setup 1-second live clock & 3.5-second polling timer
        var clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        clockTimer.Tick += (s, e) =>
        {
            if (_lblLiveClock != null && !_lblLiveClock.IsDisposed)
            {
                _lblLiveClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
        };
        clockTimer.Start();

        _pollTimer = new System.Windows.Forms.Timer { Interval = 3500 };
        _pollTimer.Tick += async (s, e) => await PollRealtimeUpdatesAsync();
        _pollTimer.Start();
    }

        private Panel BuildWorkControlCard()
    {
        var card = CreateCardPanel(new Padding(0, 0, 0, 6));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12, 8, 12, 8),
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));  // Row 0: Header & Settings
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));  // Row 1: Unified Work Order Spec Box (2 lines)
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));  // Row 2: 5 Status Mode Action Buttons
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Row 3: 3x4 / 4x3 Inspection Matrix Buttons
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));  // Row 4: Manual Work Log Entry Input Row
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));  // Row 5: Action / Emergency Buttons

        // Row 0: Header + Edit Button
        var headerRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblCardTitle = new Label
        {
            Text = "🎛 장비 작업 제어 및 탭별 작업버튼",
            Font = new Font("맑은 고딕", 9.8f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        var lblSubNote = new Label
        {
            Text = " (클릭=즉시보고 / 우클릭·편집=내용수정)",
            Font = new Font("맑은 고딕", 8.2f),
            ForeColor = CncTheme.TextSlate400,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(4, 2, 0, 0)
        };

        var titleFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        titleFlow.Controls.Add(lblCardTitle);
        titleFlow.Controls.Add(lblSubNote);

        _pnlAlert = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(127, 29, 29), Visible = false, Margin = new Padding(8, 0, 8, 0) };
        _lblAlertText = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(254, 202, 202),
            Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0),
            AutoEllipsis = true
        };
        _pnlAlert.Controls.Add(_lblAlertText);

        _btnEditQuickButtons = CreateFlatButton("⚙️ [일시대기] 버튼 편집", CncTheme.Bg800, CncTheme.AccentSky, 155, 26);
        _btnEditQuickButtons.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        _btnEditQuickButtons.Anchor = AnchorStyles.Right;
        _btnEditQuickButtons.Margin = new Padding(4, 0, 0, 0);
        _btnEditQuickButtons.Click += (s, e) => ShowEditQuickButtonsDialog(_currentStatus);

        headerRow.Controls.Add(titleFlow, 0, 0);
        headerRow.Controls.Add(_pnlAlert, 1, 0);
        headerRow.Controls.Add(_btnEditQuickButtons, 2, 0);
        layout.Controls.Add(headerRow, 0, 0);

        // Row 1: Unified Work Order Spec Box (Matching code.html Work Order Bar & Item Specifications)
        var pnlSpecBox = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CncTheme.Bg850,
            Padding = new Padding(8, 6, 8, 6),
            Margin = new Padding(0, 2, 0, 4)
        };
        pnlSpecBox.Paint += (s, e) =>
        {
            using var pen = new Pen(CncTheme.BorderLight, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlSpecBox.Width - 1, pnlSpecBox.Height - 1);
        };

        var specLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        specLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        specLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        // SubRow 1: Work Order Dropdown + Browse Button
        var orderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        orderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85f));
        orderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        orderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135f));

        var lblOrderSelect = new Label { Text = "작업지시서:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = CncTheme.TextSlate300, Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold) };
        _cboParts = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = CncTheme.Bg950,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9f),
            Margin = new Padding(0, 1, 8, 1)
        };
        _cboParts.SelectedIndexChanged += async (s, e) => await OnPartSelectedAsync();

        var btnPickOrder = CreateFlatButton("📑 지시서 상세선택", Color.FromArgb(3, 105, 161), Color.White, 130, 26);
        btnPickOrder.Click += async (s, e) => await ShowWorkOrderBrowserDialogAsync();

        orderRow.Controls.Add(lblOrderSelect, 0, 0);
        orderRow.Controls.Add(_cboParts, 1, 0);
        orderRow.Controls.Add(btnPickOrder, 2, 0);
        specLayout.Controls.Add(orderRow, 0, 0);

        // SubRow 2: OrderNo / PartName / Qty / Apply All Check
        var manualFieldsRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, Margin = new Padding(0) };
        manualFieldsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65f));  // 작지번호:
        manualFieldsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f)); // txtOrderNumber
        manualFieldsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65f));  // 가공품목:
        manualFieldsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // txtPartName
        manualFieldsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));  // 수량:
        manualFieldsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50f));  // txtManualQty
        manualFieldsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // chkApplyAllSelected

        manualFieldsRow.Controls.Add(new Label { Text = "작지번호:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = CncTheme.Amber, Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold) }, 0, 0);
        _txtOrderNumber = CreateDarkTextBox("0177-NST2610-SOCKE");
        manualFieldsRow.Controls.Add(_txtOrderNumber, 1, 0);

        manualFieldsRow.Controls.Add(new Label { Text = "가공품목:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = CncTheme.Green, Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold), Padding = new Padding(4, 0, 0, 0) }, 2, 0);
        _txtPartName = CreateDarkTextBox("0177-NST2610-SOCKET-RG750P2");
        manualFieldsRow.Controls.Add(_txtPartName, 3, 0);

        manualFieldsRow.Controls.Add(new Label { Text = "수량:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = CncTheme.TextSlate400, Font = new Font("맑은 고딕", 8.8f) }, 4, 0);
        _txtManualQty = CreateDarkTextBox("1");
        _txtManualQty.TextAlign = HorizontalAlignment.Center;
        manualFieldsRow.Controls.Add(_txtManualQty, 5, 0);

        _chkApplyAllSelected = new CheckBox
        {
            Text = "담당장비 일괄적용",
            AutoSize = true,
            ForeColor = CncTheme.TextSlate300,
            Font = new Font("맑은 고딕", 8.5f),
            Dock = DockStyle.Fill,
            Checked = true,
            Margin = new Padding(8, 3, 0, 0),
            Cursor = Cursors.Hand
        };
        manualFieldsRow.Controls.Add(_chkApplyAllSelected, 6, 0);
        specLayout.Controls.Add(manualFieldsRow, 0, 1);

        pnlSpecBox.Controls.Add(specLayout);
        layout.Controls.Add(pnlSpecBox, 0, 1);

        // Row 2: 5 Major CNC Core Mode Action Buttons (code.html primary-action-mode-buttons)
        var statusTabsRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new Padding(0, 2, 0, 2) };
        for (int c = 0; c < 5; c++) statusTabsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

        var statusList = new (string key, string label, Color color)[]
        {
            ("세팅중", "🔧 세팅/단도리", Color.FromArgb(56, 189, 248)),
            ("가공중", "⚡ 본가공/절삭", CncTheme.Green),
            ("측정중", "📏 중간측정", Color.FromArgb(52, 211, 153)),
            ("공구교체", "🛠 공구교체", CncTheme.Amber),
            ("대기", "⏸ 일시대기 🚨", Color.FromArgb(244, 63, 94))
        };

        _statusButtons.Clear();
        var tabToolTip = new ToolTip();
        foreach (var item in statusList)
        {
            var btn = new Button
            {
                Dock = DockStyle.Fill,
                Text = item.label,
                BackColor = CncTheme.Bg800,
                ForeColor = CncTheme.TextSlate200,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(2)
            };
            btn.Tag = (item.key, item.color);
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = CncTheme.Border;
            btn.Click += (s, e) => SetCurrentStatus(item.key);
            tabToolTip.SetToolTip(btn, $"[{item.label}] 탭 작업버튼 12개로 전환");
            _statusButtons.Add(btn);
            statusTabsRow.Controls.Add(btn);
        }
        layout.Controls.Add(statusTabsRow, 0, 2);

        // Row 3: 12-Item Field Inspection & Status Notification Matrix Buttons (3 cols x 4 rows)
        var quickGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Margin = new Padding(0, 2, 0, 2),
            BackColor = Color.Transparent
        };
        for (int c = 0; c < 3; c++) quickGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        for (int r = 0; r < 4; r++) quickGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

        _quickTaskButtons.Clear();
        for (int idx = 0; idx < 12; idx++)
        {
            int capturedIdx = idx;
            var qBtn = new Button
            {
                Dock = DockStyle.Fill,
                Text = $"작업 {idx + 1}",
                BackColor = CncTheme.Bg850,
                ForeColor = CncTheme.TextSlate300,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("맑은 고딕", 8.8f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Margin = new Padding(2),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };
            qBtn.FlatAppearance.BorderSize = 1;
            qBtn.FlatAppearance.BorderColor = CncTheme.Border;
            qBtn.Click += async (s, e) =>
            {
                // Strip leading icon if present when setting manual text
                string cleanText = qBtn.Text.Replace("☑", "").Trim();
                _txtWorkDescription.Text = cleanText;
                await SaveManualWorkHistoryAsync(silentPopup: true);
            };
            qBtn.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    EditSingleQuickButton(_currentStatus, capturedIdx);
                }
            };
            tabToolTip.SetToolTip(qBtn, "클릭: 이 작업이력을 서버에 즉시 보고  |  우클릭: 이 버튼 내용 수정");
            _quickTaskButtons.Add(qBtn);
            quickGrid.Controls.Add(qBtn, idx % 3, idx / 3);
        }
        SetCurrentStatus("가공중"); // Default active tab matching screen.png
        layout.Controls.Add(quickGrid, 0, 3);

        // Row 4: Manual Work Description Input + Save Button
        var manualInputRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 2, 0, 2) };
        manualInputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        manualInputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 195f));

        _txtWorkDescription = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = CncTheme.Bg950,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.5f),
            BorderStyle = BorderStyle.FixedSingle,
            Text = "라우터",
            Margin = new Padding(0, 2, 6, 2)
        };
        _txtWorkDescription.KeyDown += async (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) await SaveManualWorkHistoryAsync(silentPopup: false);
        };

        _btnSaveManualLog = CreateFlatButton("💾 수동 작업이력 기록·보고", CncTheme.Bg800, Color.White, 190, 32);
        _btnSaveManualLog.Dock = DockStyle.Fill;
        _btnSaveManualLog.Margin = new Padding(0, 1, 0, 1);
        _btnSaveManualLog.Click += async (s, e) => await SaveManualWorkHistoryAsync(silentPopup: false);

        manualInputRow.Controls.Add(_txtWorkDescription, 0, 0);
        manualInputRow.Controls.Add(_btnSaveManualLog, 1, 0);
        layout.Controls.Add(manualInputRow, 0, 4);

        // Row 5: Action & Emergency Buttons (2-column layout matching screen.png)
        // Row 5: Emergency Alert Critical Wide Button (100% width, matching Stitch UI code.html / screen.png)
        _btnIssue = CreateFlatButton("⚠️ 긴급 이슈 · 공구 파손 · 불량 등록 및 서버 즉시 전송 🚨", CncTheme.Rose, Color.White, 100, 38);
        _btnIssue.Dock = DockStyle.Fill;
        _btnIssue.Font = new Font("맑은 고딕", 10f, FontStyle.Bold);
        _btnIssue.Margin = new Padding(0, 2, 0, 0);
        _btnIssue.Click += async (s, e) => await ReportIssueAsync();

        // _btnComplete는 후속공정 탭이나 빠른 버튼에서 동작하도록 유지
        _btnComplete = CreateFlatButton("가공 완료", CncTheme.Green, Color.White, 1, 1);
        _btnComplete.Visible = false;
        _btnComplete.Click += async (s, e) => await ShowCompleteAndPostProcessDialogAsync();

        layout.Controls.Add(_btnIssue, 0, 5);

        card.Controls.Add(layout);
        return card;
    }

    private Panel BuildBottomLeftTabbedCard()
    {
        var card = CreateCardPanel(new Padding(0));

        var tabContainer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        tabContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        tabContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // Custom Tab Header Bar
        tabContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Tab Content Area

        // Custom Modern Dark Tab Header Bar (Matches code.html Sub-Tabs Navigation)
        var pnlCustomTabBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = CncTheme.Bg900,
            Padding = new Padding(6, 2, 6, 0),
            Margin = new Padding(0)
        };
        pnlCustomTabBar.Paint += (s, e) =>
        {
            using var pen = new Pen(CncTheme.Border, 1);
            e.Graphics.DrawLine(pen, 0, pnlCustomTabBar.Height - 1, pnlCustomTabBar.Width, pnlCustomTabBar.Height - 1);
        };

        _bottomLeftTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("맑은 고딕", 9.2f, FontStyle.Regular),
            Appearance = TabAppearance.FlatButtons,
            ItemSize = new Size(0, 1),
            SizeMode = TabSizeMode.Fixed
        };

        var tabDefect = new TabPage { BackColor = CncTheme.Bg900, Padding = new Padding(4) };
        tabDefect.Controls.Add(BuildDefectAndCountermeasurePanel());

        var tabUsedTool = new TabPage { BackColor = CncTheme.Bg900, Padding = new Padding(4) };
        tabUsedTool.Controls.Add(BuildUsedToolsPanel());

        var tabSubWorker = new TabPage { BackColor = CncTheme.Bg900, Padding = new Padding(4) };
        tabSubWorker.Controls.Add(BuildSubWorkerPanel());

        var tabPostProcess = new TabPage { BackColor = CncTheme.Bg900, Padding = new Padding(4) };
        tabPostProcess.Controls.Add(BuildPostProcessPanel());

        _bottomLeftTabs.TabPages.AddRange(new[] { tabDefect, tabUsedTool, tabSubWorker, tabPostProcess });

        var tabNames = new[]
        {
            "💥 불량·수정·대책 히스토리 기록 및 조회",
            "🛠 주요 가공 사용 공구 목록",
            "📋 서브인원 작업 지시 및 보고 취합",
            "⚙ 후공정(핀삽입/각인/후처리) 추적"
        };

        _customTabButtons.Clear();
        for (int i = 0; i < tabNames.Length; i++)
        {
            int tabIdx = i;
            var btn = new Button
            {
                Text = tabNames[i],
                Height = 30,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 4, 0),
                Padding = new Padding(10, 0, 10, 0)
            };
            btn.FlatAppearance.BorderSize = 0;

            btn.Paint += (s, e) =>
            {
                bool isSelected = _bottomLeftTabs.SelectedIndex == tabIdx;
                btn.BackColor = isSelected ? CncTheme.Bg800 : Color.Transparent;
                btn.ForeColor = isSelected ? CncTheme.AccentSky : CncTheme.TextSlate400;

                if (isSelected)
                {
                    using var brushSky = new SolidBrush(CncTheme.AccentSky);
                    e.Graphics.FillRectangle(brushSky, 0, btn.Height - 3, btn.Width, 3);
                }
            };

            btn.Click += (s, e) =>
            {
                _bottomLeftTabs.SelectedIndex = tabIdx;
                foreach (var b in _customTabButtons) b.Invalidate();
            };

            _customTabButtons.Add(btn);
            pnlCustomTabBar.Controls.Add(btn);
        }

        tabContainer.Controls.Add(pnlCustomTabBar, 0, 0);
        tabContainer.Controls.Add(_bottomLeftTabs, 0, 1);
        card.Controls.Add(tabContainer);
        return card;
    }

    private Panel BuildDefectAndCountermeasurePanel()
    {
        var pnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(19, 28, 49) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(4, 2, 4, 2)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Row 0: Category / Type / PartCode / Save Button
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Row 1: 불량·수정 내용 + 발생 원인
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Row 2: 재발방지 대책·조치 내용 + 수정 버튼
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Row 3: History Grid

        // Row 0: 구분 / 유형 / 대상품목 / 등록 버튼
        var r0 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));  // 주기:
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85f));  // cboDefectCategory
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));  // 유형:
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115f)); // cboDefectType
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));  // 작지/품목:
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // txtDefectPartCode
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175f)); // btnSaveDefect

        r0.Controls.Add(new Label { Text = "구분:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("맑은 고딕", 8.8f) }, 0, 0);
        _cboDefectCategory = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, Font = new Font("맑은 고딕", 8.8f) };
        _cboDefectCategory.Items.AddRange(new object[] { "가공중", "가공후", "세팅/수정", "납품후" });
        _cboDefectCategory.SelectedIndex = 0;
        r0.Controls.Add(_cboDefectCategory, 1, 0);

        r0.Controls.Add(new Label { Text = "유형:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("맑은 고딕", 8.8f) }, 2, 0);
        _cboDefectType = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, Font = new Font("맑은 고딕", 8.8f) };
        _cboDefectType.Items.AddRange(new object[] { "치수불량", "가공불량/찍힘", "공구파손/마모", "NC프로그램수정", "세팅/치구수정", "원점/옵셋보정", "도면/설계변경" });
        _cboDefectType.SelectedIndex = 0;
        r0.Controls.Add(_cboDefectType, 3, 0);

        r0.Controls.Add(new Label { Text = "작지/품목:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(251, 191, 36), Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold) }, 4, 0);
        _txtDefectPartCode = CreateDarkTextBox("WO-2026-LIVE01 / 소켓 상판");
        r0.Controls.Add(_txtDefectPartCode, 5, 0);

        var btnSaveDefect = CreateFlatButton("💾 불량·수정·대책 등록", Color.FromArgb(220, 38, 38), Color.White, 170, 26);
        btnSaveDefect.Dock = DockStyle.Fill;
        btnSaveDefect.Margin = new Padding(4, 2, 0, 2);
        btnSaveDefect.Click += async (s, e) => await SaveDefectAndCountermeasureAsync();
        r0.Controls.Add(btnSaveDefect, 6, 0);
        layout.Controls.Add(r0, 0, 0);

        // Row 1: 불량·수정 내용 + 원인
        var r1 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95f));  // 불량/수정내용:
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));   // txtDefectDesc
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68f));  // 발생원인:
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));   // txtDefectCause

        r1.Controls.Add(new Label { Text = "불량/수정내용:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(248, 113, 113), Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold) }, 0, 0);
        _txtDefectDesc = CreateDarkTextBox("포켓 깊이 +0.02mm 편차 발생 및 프로그램 수정");
        r1.Controls.Add(_txtDefectDesc, 1, 0);

        r1.Controls.Add(new Label { Text = "발생원인:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(203, 213, 225), Font = new Font("맑은 고딕", 8.8f) }, 2, 0);
        _txtDefectCause = CreateDarkTextBox("엔드밀 공구 마모 및 Z축 열변위");
        r1.Controls.Add(_txtDefectCause, 3, 0);
        layout.Controls.Add(r1, 0, 1);

        // Row 2: 조치 및 재발방지 대책 + 선택 건 수정 버튼
        var r2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 1, 0, 2) };
        r2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95f));  // 조치/대책내용:
        r2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // txtDefectCountermeasure
        r2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175f)); // btnEditDefect

        r2.Controls.Add(new Label { Text = "조치/재발대책:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(52, 211, 153), Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold) }, 0, 0);
        _txtDefectCountermeasure = CreateDarkTextBox("신품 공구 교체 후 Z축 옵셋 -0.02mm 보정, 5EA마다 중간측정 실시");
        r2.Controls.Add(_txtDefectCountermeasure, 1, 0);

        var btnEditDefect = CreateFlatButton("✏️ 선택 히스토리 수정", Color.FromArgb(2, 132, 199), Color.White, 170, 26);
        btnEditDefect.Dock = DockStyle.Fill;
        btnEditDefect.Margin = new Padding(4, 2, 0, 2);
        btnEditDefect.Click += async (s, e) => await EditSelectedDefectHistoryAsync();
        r2.Controls.Add(btnEditDefect, 2, 0);
        layout.Controls.Add(r2, 0, 2);

        // Row 3: Defect & Countermeasure History Grid
        _gridDefects = CreateModernGrid();
        _gridDefects.Columns.Add("EventId", "No");
        _gridDefects.Columns["EventId"]!.FillWeight = 32;
        _gridDefects.Columns.Add("Machine", "장비");
        _gridDefects.Columns["Machine"]!.FillWeight = 42;
        _gridDefects.Columns.Add("PartCode", "작지/품목");
        _gridDefects.Columns["PartCode"]!.FillWeight = 85;
        _gridDefects.Columns.Add("Type", "유형");
        _gridDefects.Columns["Type"]!.FillWeight = 68;
        _gridDefects.Columns.Add("Desc", "불량 및 수정 내용");
        _gridDefects.Columns["Desc"]!.FillWeight = 120;
        _gridDefects.Columns.Add("Cause", "발생 원인");
        _gridDefects.Columns["Cause"]!.FillWeight = 85;
        _gridDefects.Columns.Add("Action", "조치 및 재발방지 대책");
        _gridDefects.Columns["Action"]!.FillWeight = 130;
        _gridDefects.Columns.Add("AuthorTime", "작성자/일시");
        _gridDefects.Columns["AuthorTime"]!.FillWeight = 75;
        _gridDefects.CellDoubleClick += async (s, e) =>
        {
            if (e.RowIndex >= 0) await EditSelectedDefectHistoryAsync();
        };

        layout.Controls.Add(_gridDefects, 0, 3);
        pnl.Controls.Add(layout);
        return pnl;
    }

    private Panel BuildSubWorkerPanel()
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(19, 28, 49) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6, 4, 6, 4),
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // Header + Report Button
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // Input Bar
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Sub-Worker Tasks Grid

        // Row 0: Header + Action button
        var topRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblTitle = new Label
        {
            Text = "👥 서브인원 작업 지시 및 보고 취합 (더블클릭 시 서브보고 입력 -> 중앙서버 보고)",
            Font = new Font("맑은 고딕", 9.2f, FontStyle.Bold),
            ForeColor = Color.FromArgb(251, 191, 36),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var btnReportToCentral = CreateFlatButton("📥 선택 건 서브보고 입력 & 🚀 중앙서버 보고", Color.FromArgb(16, 185, 129), Color.White, 265, 25);
        btnReportToCentral.Font = new Font("맑은 고딕", 8.6f, FontStyle.Bold);
        btnReportToCentral.Click += async (s, e) => await ReportSubWorkerTaskToCentralAsync();

        topRow.Controls.Add(lblTitle, 0, 0);
        topRow.Controls.Add(btnReportToCentral, 1, 0);
        layout.Controls.Add(topRow, 0, 0);

        // Row 1: Sub-worker Instruction Input Bar
        var inputRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, Margin = new Padding(0, 2, 0, 4) };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));  // 장비:
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));  // cboSubMachine
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65f));  // 서브인원:
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95f));  // txtSubWorkerName
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65f));  // 지시내용:
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // txtSubInstruction
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115f)); // btnAssign

        inputRow.Controls.Add(new Label { Text = "장비:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("맑은 고딕", 8.8f) }, 0, 0);
        _cboSubMachine = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9f),
            Margin = new Padding(0, 3, 4, 2)
        };
        inputRow.Controls.Add(_cboSubMachine, 1, 0);

        inputRow.Controls.Add(new Label { Text = "서브인원:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("맑은 고딕", 8.8f) }, 2, 0);
        _txtSubWorkerName = CreateDarkTextBox("이서브");
        inputRow.Controls.Add(_txtSubWorkerName, 3, 0);

        inputRow.Controls.Add(new Label { Text = "지시내용:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("맑은 고딕", 8.8f) }, 4, 0);
        _txtSubInstruction = CreateDarkTextBox("소재 10EA 면취 및 1차 세팅 진행");
        inputRow.Controls.Add(_txtSubInstruction, 5, 0);

        var btnAssignSub = CreateFlatButton("📤 작업 지시", Color.FromArgb(37, 99, 235), Color.White, 110, 26);
        btnAssignSub.Dock = DockStyle.Fill;
        btnAssignSub.Margin = new Padding(4, 2, 0, 2);
        btnAssignSub.Click += async (s, e) => await CreateSubWorkerTaskAsync();
        inputRow.Controls.Add(btnAssignSub, 6, 0);

        layout.Controls.Add(inputRow, 0, 1);

        // Row 2: Sub-Worker Tasks DataGridView
        _gridSubTasks = CreateModernGrid();
        _gridSubTasks.Columns.Add("TaskId", "No");
        _gridSubTasks.Columns["TaskId"]!.FillWeight = 35;
        _gridSubTasks.Columns.Add("Machine", "장비");
        _gridSubTasks.Columns["Machine"]!.FillWeight = 48;
        _gridSubTasks.Columns.Add("SubWorker", "서브인원");
        _gridSubTasks.Columns["SubWorker"]!.FillWeight = 65;
        _gridSubTasks.Columns.Add("OrderPart", "작지번호 / 품목");
        _gridSubTasks.Columns["OrderPart"]!.FillWeight = 110;
        _gridSubTasks.Columns.Add("Instruction", "작업 지시 내용");
        _gridSubTasks.Columns["Instruction"]!.FillWeight = 120;
        _gridSubTasks.Columns.Add("Report", "서브인원 보고 내용");
        _gridSubTasks.Columns["Report"]!.FillWeight = 120;
        _gridSubTasks.Columns.Add("Status", "상태");
        _gridSubTasks.Columns["Status"]!.FillWeight = 68;
        _gridSubTasks.CellDoubleClick += async (s, e) =>
        {
            if (e.RowIndex >= 0) await ReportSubWorkerTaskToCentralAsync();
        };

        layout.Controls.Add(_gridSubTasks, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    // =========================================================================
    // LEFT CARD 2 (Tab 2): 주요 가공 사용 공구 목록 기록 및 조회 패널
    // =========================================================================
    private Panel BuildUsedToolsPanel()
    {
        var pnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(19, 28, 49) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(4, 2, 4, 2)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Row 0: 공구명, 메이커, 공구경, 등록버튼
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Row 1: 날장, 재질, 기타 비고/메모, 삭제버튼
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Row 2: Grid

        // Row 0: 공구명(황삭/정삭/드릴 등) / 메이커 / 공구경(Ø) / 등록버튼
        var r0 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52f));  // 공구명:
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));  // _txtToolName
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52f));  // 메이커:
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));  // _txtToolMaker
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52f));  // 공구경:
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));  // _txtToolDiameter
        r0.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f)); // btnAddTool

        r0.Controls.Add(new Label { Text = "공구명:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(56, 189, 248), Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold) }, 0, 0);
        _txtToolName = CreateDarkTextBox("");
        _txtToolName.PlaceholderText = "예: 황삭 엔드밀, 마이크로 드릴, 챔퍼밀";
        r0.Controls.Add(_txtToolName, 1, 0);

        r0.Controls.Add(new Label { Text = "메이커:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("맑은 고딕", 8.8f) }, 2, 0);
        _txtToolMaker = CreateDarkTextBox("");
        _txtToolMaker.PlaceholderText = "예: OSG, YG-1, Korloy";
        r0.Controls.Add(_txtToolMaker, 3, 0);

        r0.Controls.Add(new Label { Text = "공구경:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("맑은 고딕", 8.8f) }, 4, 0);
        _txtToolDiameter = CreateDarkTextBox("");
        _txtToolDiameter.PlaceholderText = "예: Ø10.0, Ø3.0, Ø0.5";
        r0.Controls.Add(_txtToolDiameter, 5, 0);

        var btnAddTool = CreateFlatButton("🧰 사용 공구 등록", Color.FromArgb(16, 185, 129), Color.White, 155, 28);
        btnAddTool.Dock = DockStyle.Fill;
        btnAddTool.Click += async (s, e) => await SaveUsedToolAsync();
        r0.Controls.Add(btnAddTool, 6, 0);
        layout.Controls.Add(r0, 0, 0);

        // Row 1: 날장 / 재질 / 기타 비고 / 삭제버튼
        var r1 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, Margin = new Padding(0, 1, 0, 1) };
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52f));  // 날장:
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));  // _txtToolFluteLength
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52f));  // 재질:
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));  // _txtToolMaterial
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52f));  // 기타:
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));  // _txtToolNotes
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f)); // btnDeleteTool

        r1.Controls.Add(new Label { Text = "날장:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("맑은 고딕", 8.8f) }, 0, 0);
        _txtToolFluteLength = CreateDarkTextBox("");
        _txtToolFluteLength.PlaceholderText = "예: 25L, 12L";
        r1.Controls.Add(_txtToolFluteLength, 1, 0);

        r1.Controls.Add(new Label { Text = "재질:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("맑은 고딕", 8.8f) }, 2, 0);
        _txtToolMaterial = CreateDarkTextBox("");
        _txtToolMaterial.PlaceholderText = "예: 초경, HSS, PCD, 코팅초경";
        r1.Controls.Add(_txtToolMaterial, 3, 0);

        r1.Controls.Add(new Label { Text = "기타:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("맑은 고딕", 8.8f) }, 4, 0);
        _txtToolNotes = CreateDarkTextBox("");
        _txtToolNotes.PlaceholderText = "예: 4날/마모양호/신품장착";
        r1.Controls.Add(_txtToolNotes, 5, 0);

        var btnDeleteTool = CreateFlatButton("🗑️ 선택 공구 삭제", Color.FromArgb(239, 68, 68), Color.White, 155, 28);
        btnDeleteTool.Dock = DockStyle.Fill;
        btnDeleteTool.Click += async (s, e) => await DeleteSelectedUsedToolAsync();
        r1.Controls.Add(btnDeleteTool, 6, 0);
        layout.Controls.Add(r1, 0, 1);

        // Row 2: Used Tools Grid
        _gridUsedTools = CreateModernGrid();
        _gridUsedTools.Columns.Add("ToolLogId", "ID");
        _gridUsedTools.Columns["ToolLogId"]!.Visible = false;
        _gridUsedTools.Columns.Add("Machine", "장비");
        _gridUsedTools.Columns["Machine"]!.FillWeight = 45;
        _gridUsedTools.Columns.Add("OrderPart", "작지/품목");
        _gridUsedTools.Columns["OrderPart"]!.FillWeight = 90;
        _gridUsedTools.Columns.Add("ToolName", "공구명");
        _gridUsedTools.Columns["ToolName"]!.FillWeight = 110;
        _gridUsedTools.Columns.Add("Maker", "메이커");
        _gridUsedTools.Columns["Maker"]!.FillWeight = 70;
        _gridUsedTools.Columns.Add("Diameter", "공구경(Ø)");
        _gridUsedTools.Columns["Diameter"]!.FillWeight = 65;
        _gridUsedTools.Columns.Add("FluteLength", "날장");
        _gridUsedTools.Columns["FluteLength"]!.FillWeight = 55;
        _gridUsedTools.Columns.Add("Material", "재질");
        _gridUsedTools.Columns["Material"]!.FillWeight = 65;
        _gridUsedTools.Columns.Add("Notes", "기타 비고");
        _gridUsedTools.Columns["Notes"]!.FillWeight = 110;
        _gridUsedTools.Columns.Add("WorkerTime", "작업자 / 등록시각");
        _gridUsedTools.Columns["WorkerTime"]!.FillWeight = 85;

        layout.Controls.Add(_gridUsedTools, 0, 2);
        pnl.Controls.Add(layout);
        return pnl;
    }

    // =========================================================================
    // RIGHT CARD 1: 장비 최근 작업 이력 (오클릭 수정·삭제 및 서버 로그 기록 지원)
    // =========================================================================
        private Panel BuildHistoryCard()
    {
        var card = CreateCardPanel(new Padding(0, 0, 0, 6));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12, 8, 12, 8),
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var topRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var headerFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _lblHistoryHeader = new Label
        {
            Text = "⚡ 실시간 설비별 가공 현황",
            Font = new Font("맑은 고딕", 9.8f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        var lblHistNote = new Label
        {
            Text = " (현재 작지번호 및 진행상황)",
            Font = new Font("맑은 고딕", 8.2f),
            ForeColor = CncTheme.TextSlate400,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(2, 2, 0, 0)
        };
        headerFlow.Controls.Add(_lblHistoryHeader);
        headerFlow.Controls.Add(lblHistNote);

        var btnRefreshHistory = CreateFlatButton("⟳ 새로고침", Color.FromArgb(3, 105, 161), Color.White, 85, 26);
        btnRefreshHistory.Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold);
        btnRefreshHistory.Click += async (s, e) => { await LoadMachinesAsync(); await LoadWorkHistoryGridAsync(); };

        _chkHistoryFilterMyMachines = new CheckBox
        {
            Text = "내 담당만",
            Checked = true,
            AutoSize = true,
            ForeColor = CncTheme.TextSlate300,
            Font = new Font("맑은 고딕", 8.5f),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(6, 4, 0, 0),
            Cursor = Cursors.Hand
        };
        _chkHistoryFilterMyMachines.CheckedChanged += async (s, e) => await LoadWorkHistoryGridAsync();

        topRow.Controls.Add(headerFlow, 0, 0);
        topRow.Controls.Add(btnRefreshHistory, 1, 0);
        topRow.Controls.Add(_chkHistoryFilterMyMachines, 2, 0);
        layout.Controls.Add(topRow, 0, 0);

        _gridHistory = CreateModernGrid();
        _gridHistory.Columns.Add("MachineId", "장비");
        _gridHistory.Columns.Add("Status", "상태");
        _gridHistory.Columns.Add("OrderNumber", "작지번호");
        _gridHistory.Columns.Add("PartName", "가공품목");
        _gridHistory.Columns.Add("WorkDetail", "현재 진행상황 / 세부공정");
        _gridHistory.Columns.Add("WorkerName", "담당자");
        _gridHistory.Columns.Add("RpmFeed", "회전/이송");
        _gridHistory.Columns.Add("RunTime", "가동시간");

        _gridHistory.Columns["MachineId"].Width = 55;
        _gridHistory.Columns["MachineId"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _gridHistory.Columns["Status"].Width = 70;
        _gridHistory.Columns["Status"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _gridHistory.Columns["OrderNumber"].Width = 125;
        _gridHistory.Columns["PartName"].Width = 135;
        _gridHistory.Columns["WorkDetail"].Width = 160;
        _gridHistory.Columns["WorkerName"].Width = 65;
        _gridHistory.Columns["WorkerName"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _gridHistory.Columns["RpmFeed"].Width = 85;
        _gridHistory.Columns["RpmFeed"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _gridHistory.Columns["RunTime"].Width = 65;
        _gridHistory.Columns["RunTime"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        // Double-click row to select machine
        _gridHistory.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < _gridHistory.Rows.Count)
            {
                var mid = _gridHistory.Rows[e.RowIndex].Cells["MachineId"].Value?.ToString();
                if (!string.IsNullOrEmpty(mid))
                {
                    _currentMachineId = mid;
                    UpdateHeaderMachineSummary();
                }
            }
        };

        layout.Controls.Add(_gridHistory, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Panel BuildChatCard()
    {
        var card = CreateCardPanel(new Padding(0));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12, 8, 12, 8),
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));

        var topRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var chatTitleFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _lblChatHeader = new Label
        {
            Text = "💬 현장 실시간 소통 채팅",
            Font = new Font("맑은 고딕", 9.8f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        var lblChatLogBadge = new Label
        {
            Text = "서버 로그 자동저장",
            Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold),
            ForeColor = CncTheme.AccentSky,
            BackColor = Color.FromArgb(80, 8, 47, 73),
            AutoSize = true,
            Padding = new Padding(5, 2, 5, 2),
            Margin = new Padding(8, 2, 0, 0)
        };
        chatTitleFlow.Controls.Add(_lblChatHeader);
        chatTitleFlow.Controls.Add(lblChatLogBadge);

        _lblChatNewIndicator = new Label
        {
            Text = "🔔 새 글",
            Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold),
            ForeColor = CncTheme.Rose,
            Visible = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(6, 6, 0, 0)
        };

        var channelFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Anchor = AnchorStyles.Right };
        var lblChannelIcon = new Label { Text = "📡", AutoSize = true, Margin = new Padding(0, 5, 2, 0) };
        _cboChatChannel = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = CncTheme.Bg850,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 8.8f),
            Width = 150,
            Margin = new Padding(0, 2, 0, 0)
        };
        _cboChatChannel.Items.AddRange(new object[] { "🌐 전체 공통 채널", "라인 A (R01~R05)", "라인 B (K01~K04)", "품질관리실 / 측정실" });
        _cboChatChannel.SelectedIndex = 0;
        _cboChatChannel.SelectedIndexChanged += async (s, e) =>
        {
            await LoadChatMessagesAsync();
        };
        channelFlow.Controls.Add(lblChannelIcon);
        channelFlow.Controls.Add(_cboChatChannel);

        topRow.Controls.Add(chatTitleFlow, 0, 0);
        topRow.Controls.Add(_lblChatNewIndicator, 1, 0);
        topRow.Controls.Add(channelFlow, 2, 0);
        layout.Controls.Add(topRow, 0, 0);

        _lstChat = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = CncTheme.Bg950,
            ForeColor = CncTheme.TextSlate200,
            Font = new Font("맑은 고딕", 9f),
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            Margin = new Padding(0, 2, 0, 4)
        };
        layout.Controls.Add(_lstChat, 0, 1);

        var sendRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        sendRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        sendRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));

        _txtChatInput = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = CncTheme.Bg950,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 9.2f),
            Margin = new Padding(0, 3, 6, 3)
        };
        _txtChatInput.KeyDown += async (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) await SendChatAsync();
        };

        var btnSend = CreateFlatButton("전송 ✈", Color.FromArgb(2, 132, 199), Color.White, 85, 30);
        btnSend.Dock = DockStyle.Fill;
        btnSend.Margin = new Padding(0, 2, 0, 2);
        btnSend.Click += async (s, e) => await SendChatAsync();

        sendRow.Controls.Add(_txtChatInput, 0, 0);
        sendRow.Controls.Add(btnSend, 1, 0);
        layout.Controls.Add(sendRow, 0, 2);

        card.Controls.Add(layout);
        return card;
    }

    private static Panel CreateCardPanel(Padding margin)
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CncTheme.Bg900,
            Margin = margin
        };
        pnl.Paint += (s, e) =>
        {
            using var pen = new Pen(CncTheme.Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnl.Width - 1, pnl.Height - 1);
        };
        return pnl;
    }

    private static Button CreateFlatButton(string text, Color bg, Color fg, int width, int height)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(width, height),
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(3, 0, 3, 0)
        };
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = CncTheme.BorderLight;
        return btn;
    }

    private static TextBox CreateDarkTextBox(string text)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Text = text,
            BackColor = CncTheme.Bg950,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f),
            Margin = new Padding(0, 2, 4, 2)
        };
    }

    private static DataGridView CreateModernGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = CncTheme.Bg950,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            Font = new Font("맑은 고딕", 8.8f),
            RowTemplate = { Height = 28 }
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = CncTheme.Bg850;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = CncTheme.TextSlate300;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold);
        grid.ColumnHeadersHeight = 30;
        grid.DefaultCellStyle.BackColor = CncTheme.Bg950;
        grid.DefaultCellStyle.ForeColor = CncTheme.TextSlate200;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(12, 74, 110);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.GridColor = CncTheme.Border;
        return grid;
    }

    private static string GetCategoryDisplayLabel(string statusKey) => statusKey switch
    {
        "세팅중" => "🔧 세팅/단도리",
        "가공중" => "⚡ 본가공/절삭",
        "측정중" => "🔬 중간측정",
        "공구교체" => "🛠️ 공구교체",
        "대기" => "⏸️ 일시대기",
        "알람" => "🚨 알람/이상",
        _ => statusKey
    };

        private void SetCurrentStatus(string statusKey)
    {
        _currentStatus = statusKey;
        Color activeTabColor = CncTheme.AccentSky;

        foreach (var btn in _statusButtons)
        {
            if (btn.Tag is ValueTuple<string, Color> info)
            {
                bool active = info.Item1 == statusKey;
                if (active) activeTabColor = info.Item2;
                btn.BackColor = active ? info.Item2 : CncTheme.Bg800;
                btn.ForeColor = active ? Color.White : CncTheme.TextSlate400;
                btn.FlatAppearance.BorderColor = active ? Color.White : CncTheme.Border;
                btn.FlatAppearance.BorderSize = active ? 2 : 1;
            }
        }

        if (_btnEditQuickButtons != null && !_btnEditQuickButtons.IsDisposed)
        {
            _btnEditQuickButtons.Text = $"⚙️ [{GetCategoryDisplayLabel(statusKey)}] 버튼 편집";
        }

        RefreshQuickTaskButtonsForTab(statusKey, activeTabColor);
    }

    private void RefreshQuickTaskButtonsForTab(string statusKey, Color tabColor)
    {
        if (_quickTaskButtons.Count == 0) return;
        var labels = _api.GetQuickButtons(statusKey);

        for (int i = 0; i < _quickTaskButtons.Count; i++)
        {
            string label = (i < labels.Count && !string.IsNullOrWhiteSpace(labels[i])) ? labels[i] : $"작업 {i + 1}";
            // Display clean checkbox icon like code.html
            string displayLabel = label.StartsWith("☑") ? label : $"☑  {label}";
            _quickTaskButtons[i].Text = displayLabel;
            _quickTaskButtons[i].BackColor = CncTheme.Bg850;
            _quickTaskButtons[i].ForeColor = CncTheme.TextSlate300;
            _quickTaskButtons[i].FlatAppearance.BorderColor = CncTheme.Border;
        }
    }

    private void EditSingleQuickButton(string statusKey, int buttonIndex)
    {
        var currentList = new List<string>(_api.GetQuickButtons(statusKey));
        string currentText = buttonIndex < currentList.Count ? currentList[buttonIndex] : "";
        var defaults = ApiClient.GetDefaultQuickButtonsForCategory(statusKey);
        string defaultText = buttonIndex < defaults.Count ? defaults[buttonIndex] : "";

        using var dlg = new Form
        {
            Text = $"✏️ [{GetCategoryDisplayLabel(statusKey)}] #{buttonIndex + 1}번 작업버튼 내용 수정",
            Size = new Size(520, 235),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.5f)
        };

        var lblGuide = new Label
        {
            Text = $"[{GetCategoryDisplayLabel(statusKey)}] 탭의 {buttonIndex + 1}번째 버튼 이름(기록될 작업이력 내용)을 수정합니다.\n작업자의 현장 판단에 맞게 원하는 문구로 자유롭게 변경하세요:",
            Location = new Point(18, 16),
            Size = new Size(470, 42),
            ForeColor = Color.FromArgb(125, 211, 252),
            Font = new Font("맑은 고딕", 9f, FontStyle.Bold)
        };

        var txtInput = new TextBox
        {
            Text = currentText,
            Location = new Point(18, 66),
            Size = new Size(468, 32),
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 11f, FontStyle.Bold)
        };

        var lblDefault = new Label
        {
            Text = $"기본값: {defaultText}",
            Location = new Point(18, 104),
            Size = new Size(468, 22),
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("맑은 고딕", 8.5f)
        };

        var btnResetOne = CreateFlatButton("🔄 기본값 복원", Color.FromArgb(51, 65, 85), Color.White, 120, 36);
        btnResetOne.Location = new Point(18, 140);
        btnResetOne.Click += (s, e) => txtInput.Text = defaultText;

        var btnSave = CreateFlatButton("💾 저장 및 적용", Color.FromArgb(16, 185, 129), Color.White, 150, 36);
        btnSave.Location = new Point(220, 140);
        btnSave.Click += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(txtInput.Text))
            {
                MessageBox.Show("버튼에 표시할 작업 내용을 입력하세요.", "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            currentList[buttonIndex] = txtInput.Text.Trim();
            _api.SaveQuickButtonsForCategory(statusKey, currentList);
            SetCurrentStatus(statusKey);
            _txtWorkDescription.Text = currentList[buttonIndex];
            dlg.DialogResult = DialogResult.OK;
            dlg.Close();
        };

        var btnCancel = CreateFlatButton("취소", Color.FromArgb(71, 85, 105), Color.White, 105, 36);
        btnCancel.Location = new Point(380, 140);
        btnCancel.Click += (s, e) => dlg.Close();

        txtInput.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) btnSave.PerformClick();
        };

        dlg.Controls.AddRange(new Control[] { lblGuide, txtInput, lblDefault, btnResetOne, btnSave, btnCancel });
        dlg.Shown += (s, e) => { txtInput.Focus(); txtInput.SelectAll(); };
        dlg.ShowDialog(this);
    }

    private void ShowEditQuickButtonsDialog(string initialCategoryKey)
    {
        using var dlg = new Form
        {
            Text = "⚙️ 탭별 작업이력 입력버튼(12개) 사용자 정의 편집",
            Size = new Size(860, 560),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.5f)
        };

        string editingKey = initialCategoryKey;
        var textBoxes = new List<TextBox>();

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(16, 10, 16, 6), BackColor = Color.FromArgb(19, 28, 49) };
        var lblTitle = new Label
        {
            Text = "💡 상단 카테고리 탭을 선택하고, 해당 탭에 표시될 12개 작업이력 버튼의 이름/내용을 자유롭게 수정하세요.",
            Dock = DockStyle.Top,
            Height = 26,
            ForeColor = Color.FromArgb(125, 211, 252),
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold)
        };

        var tabSelectorGrid = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 44, ColumnCount = 6, RowCount = 1 };
        for (int i = 0; i < 6; i++) tabSelectorGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66f));

        (string label, string key, Color col)[] cats =
        {
            ("🔧 세팅/단도리", "세팅중", Color.FromArgb(245, 158, 11)),
            ("⚡ 본가공/절삭", "가공중", Color.FromArgb(16, 185, 129)),
            ("🔬 중간측정", "측정중", Color.FromArgb(14, 165, 233)),
            ("🛠️ 공구교체", "공구교체", Color.FromArgb(168, 85, 247)),
            ("⏸️ 일시대기", "대기", Color.FromArgb(100, 116, 139)),
            ("🚨 알람/이상", "알람", Color.FromArgb(239, 68, 68))
        };

        var catButtons = new List<Button>();

        var centerGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(16, 14, 16, 10)
        };
        centerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        centerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        for (int r = 0; r < 6; r++) centerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 16.66f));

        for (int i = 0; i < 12; i++)
        {
            var itemPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(6, 4, 6, 4) };
            itemPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68f));
            itemPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var lblNo = new Label
            {
                Text = $"버튼 #{i + 1}",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(251, 191, 36),
                Font = new Font("맑은 고딕", 9f, FontStyle.Bold)
            };

            var tb = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("맑은 고딕", 10f, FontStyle.Bold),
                Margin = new Padding(0, 4, 0, 2)
            };
            textBoxes.Add(tb);

            itemPanel.Controls.Add(lblNo, 0, 0);
            itemPanel.Controls.Add(tb, 1, 0);
            centerGrid.Controls.Add(itemPanel, i % 2, i / 2);
        }

        void LoadCategoryIntoEditor(string key)
        {
            editingKey = key;
            var list = _api.GetQuickButtons(key);
            for (int i = 0; i < 12; i++)
            {
                textBoxes[i].Text = i < list.Count ? list[i] : "";
            }
            foreach (var b in catButtons)
            {
                if (b.Tag is ValueTuple<string, Color> tagInfo)
                {
                    bool active = tagInfo.Item1 == key;
                    b.BackColor = active ? tagInfo.Item2 : Color.FromArgb(30, 41, 59);
                    b.ForeColor = active ? (key == "세팅중" ? Color.FromArgb(15, 23, 42) : Color.White) : Color.FromArgb(148, 163, 184);
                    b.FlatAppearance.BorderColor = active ? Color.White : Color.FromArgb(51, 65, 85);
                }
            }
        }

        void SaveCurrentEditorInputs()
        {
            var updated = textBoxes.Select(t => t.Text.Trim()).ToList();
            _api.SaveQuickButtonsForCategory(editingKey, updated);
        }

        for (int i = 0; i < cats.Length; i++)
        {
            var c = cats[i];
            var b = new Button
            {
                Dock = DockStyle.Fill,
                Text = c.label,
                Tag = (c.key, c.col),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(2)
            };
            b.Click += (s, e) =>
            {
                SaveCurrentEditorInputs();
                LoadCategoryIntoEditor(c.key);
            };
            catButtons.Add(b);
            tabSelectorGrid.Controls.Add(b, i, 0);
        }

        topPanel.Controls.Add(tabSelectorGrid);
        topPanel.Controls.Add(lblTitle);

        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(16, 10, 16, 10), BackColor = Color.FromArgb(19, 28, 49) };

        var btnResetTab = CreateFlatButton("🔄 현재 탭 기본값으로 초기화", Color.FromArgb(71, 85, 105), Color.White, 210, 36);
        btnResetTab.Dock = DockStyle.Left;
        btnResetTab.Click += (s, e) =>
        {
            if (MessageBox.Show($"[{GetCategoryDisplayLabel(editingKey)}] 탭의 12개 버튼을 초기 기본값으로 되돌리시겠습니까?", "기본값 복원 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _api.ResetQuickButtonsForCategory(editingKey);
                LoadCategoryIntoEditor(editingKey);
                SetCurrentStatus(editingKey);
            }
        };

        var btnSaveAll = CreateFlatButton("💾 변경내용 저장 및 즉시 반영", Color.FromArgb(16, 185, 129), Color.White, 230, 36);
        btnSaveAll.Dock = DockStyle.Right;
        btnSaveAll.Click += (s, e) =>
        {
            SaveCurrentEditorInputs();
            SetCurrentStatus(editingKey);
            MessageBox.Show($"[{GetCategoryDisplayLabel(editingKey)}] 탭의 12개 작업버튼 내용이 저장되어 화면에 즉시 반영되었습니다.", "작업버튼 편집 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            dlg.DialogResult = DialogResult.OK;
            dlg.Close();
        };

        bottomBar.Controls.Add(btnResetTab);
        bottomBar.Controls.Add(btnSaveAll);

        dlg.Controls.Add(centerGrid);
        dlg.Controls.Add(topPanel);
        dlg.Controls.Add(bottomBar);

        LoadCategoryIntoEditor(initialCategoryKey);
        dlg.ShowDialog(this);
    }

    // =========================================================================
    // Multi-Machine Cards Rendering & Selection
    // =========================================================================
        private void RenderMachineCards()
    {
        if (_pnlMachineGrid == null) return;
        _pnlMachineGrid.SuspendLayout();
        _pnlMachineGrid.Controls.Clear();

        int workingCnt = 0;
        int standbyCnt = 0;
        int stopCnt = 0;

        // Default 7 machines matching Stitch UI code.html / screen.png
        var machineOrder = new[] { "K01", "K02", "R01", "R02", "R03", "R04", "R05" };
        var machineMap = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _allMachines)
        {
            var mid = m.GetProperty("machineId").GetString() ?? "";
            machineMap[mid] = m;
        }

        for (int col = 0; col < machineOrder.Length; col++)
        {
            var id = machineOrder[col];
            string name = id;
            string status = "대기";
            string jobNo = "-";
            string partNm = "-";

            if (machineMap.TryGetValue(id, out var m))
            {
                name = m.GetProperty("machineName").GetString() ?? id;
                status = m.GetProperty("status").GetString() ?? "대기";
                jobNo = m.TryGetProperty("activeJobNumber", out var jn) && jn.ValueKind == JsonValueKind.String ? jn.GetString() : "-";
                partNm = m.TryGetProperty("activePartName", out var pn) && pn.ValueKind == JsonValueKind.String ? pn.GetString() : "-";
            }

            if (status.Contains("가공")) workingCnt++;
            else if (status.Contains("정지") || status.Contains("알람")) stopCnt++;
            else standbyCnt++;

            bool isChecked = _mySelectedMachines.Contains(id);
            bool isFocused = id == _currentMachineId;

            // Card panel filling column 100%
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = isFocused 
                    ? Color.FromArgb(40, 6, 78, 59) 
                    : (isChecked && status.Contains("가공") ? Color.FromArgb(25, 6, 78, 59) : CncTheme.Bg850),
                Margin = new Padding(3, 1, 3, 1),
                Padding = new Padding(6, 4, 6, 4),
                Cursor = Cursors.Hand
            };

            string cardStatus = status;
            card.Paint += (s, e) =>
            {
                Color borderClr = isFocused 
                    ? CncTheme.Green 
                    : (isChecked ? (cardStatus.Contains("가공") ? CncTheme.Green : CncTheme.Amber) : CncTheme.Border);
                int borderW = isFocused ? 2 : 1;
                using var pen = new Pen(borderClr, borderW);
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);

                if (isFocused)
                {
                    // Active top-right corner marker
                    using var brushGreen = new SolidBrush(CncTheme.Green);
                    e.Graphics.FillPolygon(brushGreen, new[] {
                        new Point(card.Width - 8, 0),
                        new Point(card.Width, 0),
                        new Point(card.Width, 8)
                    });
                }
            };

            // Top Row: Checkbox + Machine Name + Status Badge
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = Color.Transparent };
            var chk = new CheckBox
            {
                Text = id,
                Checked = isChecked,
                Dock = DockStyle.Left,
                Width = 65,
                Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
                ForeColor = isFocused ? CncTheme.Green : (isChecked ? (status.Contains("가공") ? CncTheme.Green : CncTheme.Amber) : CncTheme.TextSlate300),
                Cursor = Cursors.Hand
            };
            chk.CheckedChanged += (s, e) =>
            {
                if (chk.Checked) _mySelectedMachines.Add(id);
                else _mySelectedMachines.Remove(id);
                SaveSelectedMachinesConfig();
                UpdateHeaderMachineSummary();
                card.Invalidate();
            };

            var badgeText = status.Contains("가공") ? "가공중" : (status.Contains("정지") ? "정지" : "대기");
            Color badgeBg = status.Contains("가공") ? CncTheme.Green : (status.Contains("정지") ? Color.FromArgb(70, 159, 18, 57) : Color.FromArgb(30, 41, 59));
            Color badgeFg = status.Contains("가공") ? CncTheme.Bg950 : (status.Contains("정지") ? Color.FromArgb(253, 164, 175) : CncTheme.TextSlate400);

            var lblStatus = new Label
            {
                Text = badgeText,
                Dock = DockStyle.Right,
                AutoSize = true,
                Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold),
                ForeColor = badgeFg,
                BackColor = badgeBg,
                Padding = new Padding(4, 1, 4, 1),
                Margin = new Padding(0, 1, 0, 0)
            };

            topPanel.Controls.Add(lblStatus);
            topPanel.Controls.Add(chk);

            // Bottom Row: Job / Part Name Label
            string jobDisplay = "- | -";
            if (jobNo != "-" && !string.IsNullOrEmpty(jobNo))
            {
                jobDisplay = jobNo;
            }
            else if (isFocused)
            {
                jobDisplay = "0177-NST2610-SOCKET-RG750P...";
            }

            var lblJob = new Label
            {
                Text = jobDisplay,
                Dock = DockStyle.Bottom,
                Height = 18,
                Font = new Font("Consolas", 7.5f, FontStyle.Regular),
                ForeColor = isFocused ? Color.FromArgb(167, 243, 208) : CncTheme.TextSlate500,
                AutoEllipsis = true,
                BackColor = Color.Transparent
            };

            var selectMachineHandler = new EventHandler(async (s, e) =>
            {
                _currentMachineId = id;
                UpdateHeaderMachineSummary();
                RenderMachineCards();
                await LoadWorkHistoryGridAsync();
            });

            card.Click += selectMachineHandler;
            lblJob.Click += selectMachineHandler;
            topPanel.Click += selectMachineHandler;

            card.Controls.Add(lblJob);
            card.Controls.Add(topPanel);
            _pnlMachineGrid.Controls.Add(card, col, 0);
        }

        if (_lblMachineCounts != null)
        {
            _lblMachineCounts.Text = $"● 가공중 ({workingCnt})   ● 대기 ({standbyCnt})   ● 정지 ({stopCnt})";
        }

        _pnlMachineGrid.ResumeLayout();
    }

    private void SaveSelectedMachinesConfig()
    {
        _api.Config.LastMachineId = _currentMachineId;
        _api.Config.SelectedMachines = _mySelectedMachines.ToList();
        _api.SaveConfig();
    }

    private void UpdateHeaderMachineSummary()
    {
        if (_lblActiveMachineSummary != null && !_lblActiveMachineSummary.IsDisposed)
        {
            _lblActiveMachineSummary.Text = $"[⚙ {_currentMachineId}]";
        }
    }

    // =========================================================================
    // Data Initialization & Refresh
    // =========================================================================
    private async Task InitializeDataAsync()
    {
        await RefreshAllAsync();
        await LoadChatMessagesAsync(initialLoad: true);
        await CheckNoticesBadgeAsync();
    }

    private async Task RefreshAllAsync()
    {
        await LoadMachinesAsync();
        await LoadWorkOrdersAsync();
        await LoadWorkHistoryGridAsync();
        await LoadDefectHistoryGridAsync();
        await LoadUsedToolsGridAsync();
        await LoadSubWorkerTasksAsync();
        await LoadPostProcessReportsAsync();
        await CheckNoticesBadgeAsync();
    }

    private async Task LoadMachinesAsync()
    {
        _allMachines = await _api.GetMachinesAsync();
        if (_allMachines.Count > 0)
        {
            var validIds = _allMachines.Select(x => x.GetProperty("machineId").GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (!validIds.Contains(_currentMachineId) && validIds.Count > 0)
                _currentMachineId = validIds[0];

            // Sync dropdowns
            var prevSub = _cboSubMachine.SelectedItem?.ToString();
            _cboSubMachine.Items.Clear();
            foreach (var id in validIds) _cboSubMachine.Items.Add(id);
            if (!string.IsNullOrEmpty(prevSub) && validIds.Contains(prevSub)) _cboSubMachine.SelectedItem = prevSub;
            else if (validIds.Contains(_currentMachineId)) _cboSubMachine.SelectedItem = _currentMachineId;

            var prevChat = _cboChatChannel.SelectedItem?.ToString();
            _cboChatChannel.Items.Clear();
            _cboChatChannel.Items.Add("🌐 전체 공통 채널");
            foreach (var id in validIds) _cboChatChannel.Items.Add($"⚙️ {id} 장비 채널");
            if (!string.IsNullOrEmpty(prevChat) && _cboChatChannel.Items.Contains(prevChat))
                _cboChatChannel.SelectedItem = prevChat;
            else
                _cboChatChannel.SelectedIndex = 0;
        }
        UpdateHeaderMachineSummary();
        RenderMachineCards();
    }

    private async Task LoadWorkOrdersAsync()
    {
        _cachedOrders = await _api.GetWorkOrdersAsync();
        _cachedParts.Clear();
        _cboParts.Items.Clear();

        foreach (var o in _cachedOrders)
        {
            if (o.TryGetProperty("isHidden", out var ih) && ih.GetBoolean()) continue;
            var orderNum = o.GetProperty("orderNumber").GetString() ?? "";
            if (o.TryGetProperty("parts", out var parts))
            {
                foreach (var p in parts.EnumerateArray())
                {
                    _cachedParts.Add(p);
                    var pName = p.GetProperty("partName").GetString();
                    var pCode = p.GetProperty("partCode").GetString();
                    var pQty = p.GetProperty("targetQty").GetInt32();
                    var pStatus = p.GetProperty("status").GetString();
                    _cboParts.Items.Add($"[{orderNum}] {pName} ({pCode}) - {pQty}EA [{pStatus}]");
                }
            }
        }

        if (_cboParts.Items.Count > 0 && _cboParts.SelectedIndex < 0)
        {
            _cboParts.SelectedIndex = 0;
        }
    }

    private async Task OnPartSelectedAsync()
    {
        var idx = _cboParts.SelectedIndex;
        if (idx < 0 || idx >= _cachedParts.Count) return;

        _selectedPart = _cachedParts[idx];
        var p = _selectedPart.Value;

        var ordNo = p.TryGetProperty("orderNumber", out var on) ? on.GetString() ?? "" : "";
        var pName = p.GetProperty("partName").GetString() ?? "";
        var pCode = p.GetProperty("partCode").GetString() ?? "";
        var qty = p.GetProperty("targetQty").GetInt32();

        _txtOrderNumber.Text = ordNo;
        _txtPartName.Text = pName;
        _txtManualQty.Text = qty.ToString();
        if (_txtDefectPartCode != null)
        {
            _txtDefectPartCode.Text = $"{ordNo} / {pName} ({pCode})";
        }

        // Check permanent history for pre-warning
        var hist = await _api.GetPartHistoryAsync(pCode);
        if (hist.HasValue && hist.Value.TryGetProperty("totalIssueCount", out var count) && count.GetInt32() > 0)
        {
            var firstIssue = hist.Value.GetProperty("history")[0];
            var type = firstIssue.GetProperty("eventType").GetString();
            var desc = firstIssue.GetProperty("description").GetString();
            _lblAlertText.Text = $"🚨 과거 불량 주의: [{type}] {desc}";
            _pnlAlert.Visible = true;
        }
        else
        {
            _pnlAlert.Visible = false;
        }
    }

    // =========================================================================
    // Requirement #3 & #5: 수동 작업이력 기록 & 장비 최근 이력 (작지번호/품목 표시)
    // =========================================================================
    private async Task SaveManualWorkHistoryAsync(bool silentPopup)
    {
        var orderNo = _txtOrderNumber.Text.Trim();
        var partName = _txtPartName.Text.Trim();
        var detail = _txtWorkDescription.Text.Trim();
        int.TryParse(_txtManualQty.Text.Trim(), out int qty);

        if (string.IsNullOrEmpty(detail))
        {
            MessageBox.Show("작업 내용을 입력해주세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var targetMachines = _chkApplyAllSelected.Checked ? _mySelectedMachines.ToList() : new List<string> { _currentMachineId };
        _btnSaveManualLog.Enabled = false;

        int successCount = 0;
        foreach (var mid in targetMachines)
        {
            bool ok = await _api.AddManualWorkLogAsync(mid, orderNo, partName, detail, _currentStatus, _session.DisplayName, null, qty);
            if (ok) successCount++;
        }

        _btnSaveManualLog.Enabled = true;
        await LoadMachinesAsync();
        await LoadWorkHistoryGridAsync();

        if (!silentPopup && successCount > 0)
        {
            MessageBox.Show(
                $"장비 [{string.Join(", ", targetMachines)}]의 작업 이력이 중앙 서버에 기록되었습니다!\n\n" +
                $"• 작지번호: {orderNo}\n" +
                $"• 가공품목: {partName}\n" +
                $"• 작업내용: {detail}\n" +
                $"• 장비상태: {_currentStatus}",
                "작업 이력 기록 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task LoadWorkHistoryGridAsync()
    {
        try
        {
            if (_allMachines.Count == 0)
            {
                _allMachines = await _api.GetMachinesAsync();
            }

            _gridHistory.Rows.Clear();

            foreach (var m in _allMachines)
            {
                string mid = m.TryGetProperty("machineId", out var midEl) ? midEl.GetString() ?? "" : "";
                if (_chkHistoryFilterMyMachines.Checked && !_mySelectedMachines.Contains(mid))
                    continue;

                string st = m.TryGetProperty("status", out var sEl) ? sEl.GetString() ?? "대기" : "대기";
                string ord = m.TryGetProperty("activeJobNumber", out var oEl) ? oEl.GetString() ?? "-" : "-";
                string prt = m.TryGetProperty("activePartName", out var pEl) ? pEl.GetString() ?? "-" : "-";
                string prog = m.TryGetProperty("currentProgram", out var prgEl) ? prgEl.GetString() ?? "-" : "-";
                string worker = m.TryGetProperty("activeWorker", out var wEl) ? wEl.GetString() ?? "-" : "-";
                int rpm = m.TryGetProperty("spindleRpm", out var rpmEl) ? rpmEl.GetInt32() : 0;
                double feed = m.TryGetProperty("feedRate", out var fEl) ? fEl.GetDouble() : 0;
                double runMin = m.TryGetProperty("runTimeMinutes", out var rEl) ? rEl.GetDouble() : 0;

                string rpmFeed = (rpm > 0 || feed > 0) ? $"{rpm}rpm/{feed:F0}" : "-";
                string runTime = runMin > 0 ? $"{runMin:F0}분" : "-";

                int rIdx = _gridHistory.Rows.Add(mid, st, ord, prt, prog, worker, rpmFeed, runTime);
                var row = _gridHistory.Rows[rIdx];

                row.Cells["MachineId"].Style.ForeColor = CncTheme.AccentSky;
                row.Cells["MachineId"].Style.Font = new Font("Consolas", 8.8f, FontStyle.Bold);
                row.Cells["WorkDetail"].Style.ForeColor = Color.FromArgb(252, 211, 77);
                row.Cells["WorkDetail"].Style.Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold);

                if (st.Contains("가공"))
                {
                    row.Cells["Status"].Style.ForeColor = CncTheme.Green;
                    row.Cells["Status"].Style.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
                }
                else if (st.Contains("대기"))
                {
                    row.Cells["Status"].Style.ForeColor = CncTheme.Amber;
                    row.Cells["Status"].Style.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
                }
                else
                {
                    row.Cells["Status"].Style.ForeColor = CncTheme.Rose;
                    row.Cells["Status"].Style.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
                }
            }
        }
        catch { }
    }

    private async Task EditSelectedWorkHistoryAsync()
    {
        if (_gridHistory.SelectedRows.Count == 0)
        {
            MessageBox.Show("수정할 작업 이력을 먼저 표에서 선택해주세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var row = _gridHistory.SelectedRows[0];
        int logId = Convert.ToInt32(row.Cells["LogId"].Value);
        string curMid = row.Cells["Machine"].Value?.ToString() ?? _currentMachineId;
        string curOrd = row.Cells["OrderNo"].Value?.ToString() ?? "-";
        string curPrt = row.Cells["PartName"].Value?.ToString() ?? "-";
        string curDtl = row.Cells["WorkDetail"].Value?.ToString() ?? "";
        string curWkr = row.Cells["Worker"].Value?.ToString() ?? _session.DisplayName;
        string curSt = row.Cells["Status"].Value?.ToString() ?? "가공중";

        using var dlg = new Form
        {
            Text = $"✏️ 장비 최근 작업 이력 수정 (로그 ID #{logId} — 수정 내역 서버 로그 자동 기록)",
            Size = new Size(560, 410),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.5f)
        };

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(18, 14, 18, 14)
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < 6; i++) tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var cboMid = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White };
        foreach (var m in _allMachines) cboMid.Items.Add(m.GetProperty("machineId").GetString() ?? "");
        if (!cboMid.Items.Contains(curMid)) cboMid.Items.Add(curMid);
        cboMid.SelectedItem = curMid;

        var txtOrd = CreateDarkTextBox(curOrd == "-" ? _txtOrderNumber.Text : curOrd);
        var txtPrt = CreateDarkTextBox(curPrt == "-" ? _txtPartName.Text : curPrt);
        var txtDtl = CreateDarkTextBox(curDtl);

        var cboSt = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White };
        cboSt.Items.AddRange(new object[] { "세팅중", "가공중", "측정중", "공구교체", "대기", "알람", "가공완료", "작업지시" });
        if (!cboSt.Items.Contains(curSt)) cboSt.Items.Add(curSt);
        cboSt.SelectedItem = curSt;

        var txtReason = CreateDarkTextBox("버튼 오클릭 / 작지·품목 오입력 수정");

        tbl.Controls.Add(new Label { Text = "장비:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184) }, 0, 0);
        tbl.Controls.Add(cboMid, 1, 0);
        tbl.Controls.Add(new Label { Text = "작지번호:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(251, 191, 36), Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold) }, 0, 1);
        tbl.Controls.Add(txtOrd, 1, 1);
        tbl.Controls.Add(new Label { Text = "품목(품명):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(52, 211, 153), Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold) }, 0, 2);
        tbl.Controls.Add(txtPrt, 1, 2);
        tbl.Controls.Add(new Label { Text = "세부 작업내용:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(125, 211, 252), Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold) }, 0, 3);
        tbl.Controls.Add(txtDtl, 1, 3);
        tbl.Controls.Add(new Label { Text = "가공 상태:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184) }, 0, 4);
        tbl.Controls.Add(cboSt, 1, 4);
        tbl.Controls.Add(new Label { Text = "수정 사유:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184) }, 0, 5);
        tbl.Controls.Add(txtReason, 1, 5);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
        var btnSave = CreateFlatButton("✅ 수정 저장 (서버 로그 기록)", Color.FromArgb(16, 185, 129), Color.White, 220, 36);
        var btnCancel = CreateFlatButton("취소", Color.FromArgb(51, 65, 85), Color.White, 90, 36);
        btnCancel.Click += (s, e) => dlg.Close();

        btnSave.Click += async (s, e) =>
        {
            var newMid = cboMid.SelectedItem?.ToString() ?? curMid;
            var newOrd = txtOrd.Text.Trim();
            var newPrt = txtPrt.Text.Trim();
            var newDtl = txtDtl.Text.Trim();
            var newSt = cboSt.SelectedItem?.ToString() ?? curSt;
            var reason = txtReason.Text.Trim();

            bool ok = await _api.UpdateMachineWorkLogAsync(
                logId,
                newMid,
                newOrd,
                newPrt,
                newDtl,
                newSt,
                curWkr,
                1,
                $"{_session.DisplayName}(사유:{reason})");

            if (ok)
            {
                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
                await LoadWorkHistoryGridAsync();
                await LoadMachinesAsync();
                MessageBox.Show(
                    $"선택하신 장비 작업 이력이 수정되었으며,\n중앙 서버 로그 폴더(logs/work_history_edit_*.txt)에 변경 전·후 기록이 저장되었습니다.",
                    "이력 수정 및 로그 기록 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("이력 수정에 실패했습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnCancel);
        tbl.Controls.Add(btnPanel, 0, 6);
        tbl.SetColumnSpan(btnPanel, 2);

        dlg.Controls.Add(tbl);
        dlg.ShowDialog(this);
    }

    // =========================================================================
    // 주요 가공 사용 공구 목록 등록 및 조회 로직
    // =========================================================================
    private async Task SaveUsedToolAsync()
    {
        var toolName = _txtToolName.Text.Trim();
        if (string.IsNullOrWhiteSpace(toolName))
        {
            MessageBox.Show("공구명을 입력해주세요 (예: 황삭 엔드밀, 드릴, 챔퍼밀 등)", "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtToolName.Focus();
            return;
        }

        var maker = _txtToolMaker.Text.Trim();
        var dia = _txtToolDiameter.Text.Trim();
        var flute = _txtToolFluteLength.Text.Trim();
        var mat = _txtToolMaterial.Text.Trim();
        var notes = _txtToolNotes.Text.Trim();
        var ordNo = _txtOrderNumber.Text.Trim();
        var partName = _txtPartName.Text.Trim();

        var ok = await _api.AddUsedToolAsync(_currentMachineId, ordNo, partName, toolName, maker, dia, flute, mat, notes, _session.DisplayName);
        if (ok)
        {
            _txtToolName.Text = "";
            _txtToolMaker.Text = "";
            _txtToolDiameter.Text = "";
            _txtToolFluteLength.Text = "";
            _txtToolMaterial.Text = "";
            _txtToolNotes.Text = "";

            await LoadUsedToolsGridAsync();
            MessageBox.Show($"[{_currentMachineId}] 장비의 사용 공구 [{toolName}] 정보가 서버에 등록되었습니다.", "사용 공구 등록 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show("사용 공구 등록 중 서버 오류가 발생했습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task LoadUsedToolsGridAsync()
    {
        try
        {
            _cachedUsedTools = await _api.GetUsedToolsAsync(_currentMachineId);
            _gridUsedTools.Rows.Clear();

            foreach (var t in _cachedUsedTools)
            {
                int tid = t.GetProperty("toolLogId").GetInt32();
                string mid = t.GetProperty("machineId").GetString() ?? "-";
                string ord = t.TryGetProperty("orderNumber", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() ?? "-" : "-";
                string prt = t.TryGetProperty("partName", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "-" : "-";
                string tName = t.GetProperty("toolName").GetString() ?? "-";
                string maker = t.TryGetProperty("maker", out var mk) && mk.ValueKind == JsonValueKind.String ? mk.GetString() ?? "-" : "-";
                string dia = t.TryGetProperty("toolDiameter", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "-" : "-";
                string fl = t.TryGetProperty("fluteLength", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() ?? "-" : "-";
                string mat = t.TryGetProperty("material", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "-" : "-";
                string notes = t.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "-" : "-";
                string worker = t.GetProperty("workerName").GetString() ?? "-";
                string created = t.GetProperty("createdAt").GetString() ?? "";
                string shortTime = created.Length >= 16 ? created.Substring(5, 11) : created;

                _gridUsedTools.Rows.Add(tid, mid, $"{ord} / {prt}", tName, maker, dia, fl, mat, notes, $"{worker} ({shortTime})");
            }
        }
        catch { }
    }

    private async Task DeleteSelectedUsedToolAsync()
    {
        if (_gridUsedTools.SelectedRows.Count == 0)
        {
            MessageBox.Show("삭제할 사용 공구 행을 표에서 선택해주세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var row = _gridUsedTools.SelectedRows[0];
        int tid = Convert.ToInt32(row.Cells["ToolLogId"].Value);
        string tName = row.Cells["ToolName"].Value?.ToString() ?? "공구";

        if (MessageBox.Show($"선택한 [{tName}] 공구 기록을 삭제하시겠습니까?", "공구 기록 삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            var ok = await _api.DeleteUsedToolAsync(tid, _session.DisplayName);
            if (ok)
            {
                await LoadUsedToolsGridAsync();
                MessageBox.Show("공구 기록이 삭제되었습니다.", "삭제 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("삭제 처리 중 오류가 발생했습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private async Task DeleteSelectedWorkHistoryAsync()
    {
        if (_gridHistory.SelectedRows.Count == 0)
        {
            MessageBox.Show("삭제할 작업 이력을 먼저 표에서 선택해주세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var row = _gridHistory.SelectedRows[0];
        int logId = Convert.ToInt32(row.Cells["LogId"].Value);
        string curMid = row.Cells["Machine"].Value?.ToString() ?? "";
        string curDtl = row.Cells["WorkDetail"].Value?.ToString() ?? "";

        var confirm = MessageBox.Show(
            $"잘못 클릭하여 입력된 아래 작업 이력을 삭제하시겠습니까?\n(삭제 내역은 중앙 서버 로그에 자동 기록됩니다)\n\n• 장비: {curMid}\n• 작업내용: {curDtl}",
            "🗑️ 오클릭 이력 삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes) return;

        bool ok = await _api.DeleteMachineWorkLogAsync(logId, _session.DisplayName);
        if (ok)
        {
            await LoadWorkHistoryGridAsync();
            await LoadMachinesAsync();
            MessageBox.Show("잘못 입력된 이력이 삭제되었으며 서버 로그에 기록되었습니다.", "삭제 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // =========================================================================
    // 불량 · 수정 · 대책 히스토리 등록 및 수정 (서버 DB & 로그 파일 영구 기록)
    // =========================================================================
    private async Task SaveDefectAndCountermeasureAsync()
    {
        var category = _cboDefectCategory.SelectedItem?.ToString() ?? "가공중";
        var type = _cboDefectType.SelectedItem?.ToString() ?? "치수불량";
        var partCode = _txtDefectPartCode.Text.Trim();
        if (string.IsNullOrEmpty(partCode))
        {
            partCode = $"{_txtOrderNumber.Text.Trim()} / {_txtPartName.Text.Trim()}";
        }
        var desc = _txtDefectDesc.Text.Trim();
        var cause = _txtDefectCause.Text.Trim();
        var action = _txtDefectCountermeasure.Text.Trim();

        if (string.IsNullOrEmpty(desc) || string.IsNullOrEmpty(action))
        {
            MessageBox.Show("불량/수정 내용과 조치/재발방지 대책을 입력해주세요.", "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool ok = await _api.CreateDefectAndCountermeasureAsync(
            partCode,
            _currentMachineId,
            category,
            type,
            desc,
            cause,
            action,
            _session.DisplayName);

        if (ok)
        {
            // Also record in machine work history so it appears in recent machine logs
            await _api.AddManualWorkLogAsync(
                _currentMachineId,
                _txtOrderNumber.Text.Trim(),
                _txtPartName.Text.Trim(),
                $"[{type}] {desc} => 대책: {action}",
                _currentStatus,
                _session.DisplayName,
                null,
                0);

            await LoadDefectHistoryGridAsync();
            await LoadWorkHistoryGridAsync();
            MessageBox.Show(
                $"불량 · 수정 · 대책 히스토리가 중앙 서버 DB 및 로그(logs/defect_countermeasure_*.txt)에 영구 기록되었습니다!\n\n" +
                $"• 대상: {partCode} ({_currentMachineId})\n" +
                $"• 유형: [{category}] {type}\n" +
                $"• 불량/수정: {desc}\n" +
                $"• 원인: {cause}\n" +
                $"• 재발대책: {action}",
                "불량·수정·대책 히스토리 등록 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show("불량·대책 등록 중 오류가 발생했습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task LoadDefectHistoryGridAsync()
    {
        try
        {
            _cachedDefects = await _api.GetAllPermanentEventsAsync();
            _gridDefects.Rows.Clear();

            foreach (var ev in _cachedDefects)
            {
                int eid = ev.GetProperty("eventId").GetInt32();
                string mid = ev.TryGetProperty("machineId", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "-" : "-";
                string pCode = ev.GetProperty("partCode").GetString() ?? "-";
                string cat = ev.GetProperty("eventCategory").GetString() ?? "";
                string type = ev.GetProperty("eventType").GetString() ?? "";
                string desc = ev.GetProperty("description").GetString() ?? "";
                string cause = ev.TryGetProperty("cause", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "-" : "-";
                string act = ev.TryGetProperty("actionTaken", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "-" : "-";
                string author = ev.GetProperty("authorUsername").GetString() ?? "";
                string time = ev.GetProperty("createdAt").GetString() ?? "";
                string shortTime = time.Length >= 16 ? time.Substring(5, 11) : time;

                _gridDefects.Rows.Add(eid, mid, pCode, $"{cat}/{type}", desc, cause, act, $"{author} ({shortTime})");
            }
        }
        catch { }
    }

    private async Task EditSelectedDefectHistoryAsync()
    {
        if (_gridDefects.SelectedRows.Count == 0)
        {
            MessageBox.Show("수정할 불량·대책 히스토리를 아래 표에서 선택해주세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var row = _gridDefects.SelectedRows[0];
        int eid = Convert.ToInt32(row.Cells["EventId"].Value);
        string curMid = row.Cells["Machine"].Value?.ToString() ?? _currentMachineId;
        string curPart = row.Cells["PartCode"].Value?.ToString() ?? "";
        string curCatType = row.Cells["Type"].Value?.ToString() ?? "가공중/치수불량";
        string curDesc = row.Cells["Desc"].Value?.ToString() ?? "";
        string curCause = row.Cells["Cause"].Value?.ToString() ?? "";
        string curAct = row.Cells["Action"].Value?.ToString() ?? "";

        var parts = curCatType.Split('/');
        string curCat = parts.Length > 0 ? parts[0] : "가공중";
        string curType = parts.Length > 1 ? parts[1] : curCatType;

        using var dlg = new Form
        {
            Text = $"✏️ 불량 · 수정 · 대책 히스토리 수정 (No #{eid})",
            Size = new Size(600, 420),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.5f)
        };

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(18, 14, 18, 14) };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < 5; i++) tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var txtPart = CreateDarkTextBox(curPart);
        var txtType = CreateDarkTextBox(curType);
        var txtDesc = CreateDarkTextBox(curDesc);
        var txtCause = CreateDarkTextBox(curCause);
        var txtAct = CreateDarkTextBox(curAct);

        tbl.Controls.Add(new Label { Text = "작지/품목:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(251, 191, 36) }, 0, 0);
        tbl.Controls.Add(txtPart, 1, 0);
        tbl.Controls.Add(new Label { Text = "유형:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(148, 163, 184) }, 0, 1);
        tbl.Controls.Add(txtType, 1, 1);
        tbl.Controls.Add(new Label { Text = "불량/수정 내용:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(248, 113, 113), Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold) }, 0, 2);
        tbl.Controls.Add(txtDesc, 1, 2);
        tbl.Controls.Add(new Label { Text = "발생 원인:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(203, 213, 225) }, 0, 3);
        tbl.Controls.Add(txtCause, 1, 3);
        tbl.Controls.Add(new Label { Text = "조치/재발대책:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(52, 211, 153), Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold) }, 0, 4);
        tbl.Controls.Add(txtAct, 1, 4);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
        var btnSave = CreateFlatButton("✅ 수정 저장 (서버 로그 기록)", Color.FromArgb(16, 185, 129), Color.White, 220, 36);
        var btnCancel = CreateFlatButton("취소", Color.FromArgb(51, 65, 85), Color.White, 90, 36);
        btnCancel.Click += (s, e) => dlg.Close();

        btnSave.Click += async (s, e) =>
        {
            bool ok = await _api.UpdateDefectAndCountermeasureAsync(
                eid,
                txtPart.Text.Trim(),
                curMid,
                curCat,
                txtType.Text.Trim(),
                txtDesc.Text.Trim(),
                txtCause.Text.Trim(),
                txtAct.Text.Trim(),
                _session.DisplayName);

            if (ok)
            {
                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
                await LoadDefectHistoryGridAsync();
                MessageBox.Show("불량·수정·대책 히스토리가 업데이트되었으며 중앙 서버 로그에 기록되었습니다.", "수정 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        btnPanel.Controls.Add(btnSave);
        btnPanel.Controls.Add(btnCancel);
        tbl.Controls.Add(btnPanel, 0, 5);
        tbl.SetColumnSpan(btnPanel, 2);

        dlg.Controls.Add(tbl);
        dlg.ShowDialog(this);
    }



    // =========================================================================
    // Requirement #2: 서브인원 작업 지시 및 보고 취합 -> 중앙서버 보고
    // =========================================================================
    private async Task CreateSubWorkerTaskAsync()
    {
        var mid = _cboSubMachine.SelectedItem?.ToString() ?? _currentMachineId;
        var subName = _txtSubWorkerName.Text.Trim();
        var inst = _txtSubInstruction.Text.Trim();
        var ordNo = _txtOrderNumber.Text.Trim();
        var prtName = _txtPartName.Text.Trim();

        if (string.IsNullOrEmpty(subName) || string.IsNullOrEmpty(inst))
        {
            MessageBox.Show("서브인원 이름과 작업 지시 내용을 입력해주세요.", "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool ok = await _api.CreateSubWorkerTaskAsync(mid, ordNo, prtName, _session.DisplayName, subName, inst);
        if (ok)
        {
            _txtSubInstruction.Clear();
            await LoadSubWorkerTasksAsync();
            await LoadWorkHistoryGridAsync();
            MessageBox.Show($"서브인원 [{subName}]에게 [{mid}] 장비 작업 지시가 등록되었습니다.", "작업 지시 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task LoadSubWorkerTasksAsync()
    {
        _cachedSubTasks = await _api.GetSubWorkerTasksAsync();
        _gridSubTasks.Rows.Clear();

        foreach (var t in _cachedSubTasks)
        {
            var tid = t.GetProperty("taskId").GetInt32();
            var mid = t.GetProperty("machineId").GetString() ?? "";
            var ord = t.GetProperty("orderNumber").GetString() ?? "-";
            var prt = t.GetProperty("partName").GetString() ?? "-";
            var sub = t.GetProperty("subWorkerName").GetString() ?? "";
            var inst = t.GetProperty("instruction").GetString() ?? "";
            var rep = t.TryGetProperty("reportContent", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : "(보고 대기중)";
            var st = t.GetProperty("status").GetString() ?? "";

            _gridSubTasks.Rows.Add(tid, mid, sub, $"{ord} / {prt}", inst, rep, st);
        }
    }

    private async Task ReportSubWorkerTaskToCentralAsync()
    {
        if (_gridSubTasks.SelectedRows.Count == 0)
        {
            MessageBox.Show("아래 표에서 서브인원의 보고를 입력할 작업 지시 건을 선택해주세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var row = _gridSubTasks.SelectedRows[0];
        int taskId = Convert.ToInt32(row.Cells["TaskId"].Value);
        string subWorker = row.Cells["SubWorker"].Value?.ToString() ?? "";
        string inst = row.Cells["Instruction"].Value?.ToString() ?? "";

        var repContent = SimpleInputDialog.Show($"[{subWorker}] 인원에게 보고받은 작업 결과 내용을 입력하세요:\n(지시내용: {inst})", "📥 서브인원 보고내용 입력 -> 중앙서버 보고", $"{inst} 완료");
        if (string.IsNullOrWhiteSpace(repContent)) return;

        var qtyStr = SimpleInputDialog.Show("서브인원이 작업 완료한 수량(EA)을 입력하세요:", "완료 수량 입력", _txtManualQty.Text);
        int.TryParse(qtyStr, out int qty);

        bool ok = await _api.ReportSubWorkerTaskAsync(taskId, repContent, qty, "가공중");
        if (ok)
        {
            await LoadSubWorkerTasksAsync();
            await LoadMachinesAsync();
            await LoadWorkHistoryGridAsync();
            MessageBox.Show($"서브인원 [{subWorker}]의 작업 보고 내용이 취합되어 중앙 서버 대시보드 및 장비 이력에 공식 보고되었습니다!", "중앙서버 보고 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // =========================================================================
    // 3-Click Execution Control
    // =========================================================================
    private async Task StartMachiningAsync()
    {
        SetCurrentStatus("가공중");
        var orderNo = _txtOrderNumber.Text.Trim();
        var partName = _txtPartName.Text.Trim();
        var workDesc = $"{partName} 본가공 진행 중";
        _txtWorkDescription.Text = workDesc;

        if (_selectedPart.HasValue && _selectedPart.Value.TryGetProperty("routingSteps", out var steps))
        {
            var curSeq = _selectedPart.Value.GetProperty("currentStepSequence").GetInt32();
            var curStep = steps.EnumerateArray().FirstOrDefault(s => s.GetProperty("stepSequence").GetInt32() == curSeq);
            if (curStep.ValueKind != JsonValueKind.Undefined)
            {
                try
                {
                    var stepId = curStep.GetProperty("stepId").GetInt32();
                    _currentActiveExecId = await _api.StartExecutionAsync(stepId, _currentMachineId, _session.DisplayName);
                }
                catch { }
            }
        }

        await SaveManualWorkHistoryAsync(silentPopup: true);
        MessageBox.Show($"[가공 시작] 장비 [{_currentMachineId}]에서 [{orderNo} / {partName}] 가공이 시작되었습니다!", "본가공 시작", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task FinishMachiningAsync()
    {
        var input = SimpleInputDialog.Show("실제 가공 완료된 수량을 입력하세요:", "⏹️ 가공 완료 및 측정대기 전이", _txtManualQty.Text);
        if (!int.TryParse(input, out var qty) || qty <= 0) return;

        if (_currentActiveExecId > 0)
        {
            try
            {
                await _api.FinishExecutionAsync(_currentActiveExecId, qty);
                _currentActiveExecId = 0;
            }
            catch { }
        }

        SetCurrentStatus("측정중");
        _txtManualQty.Text = qty.ToString();
        _txtWorkDescription.Text = $"가공 완료 ({qty}EA) -> 품질 측정 대기";
        await SaveManualWorkHistoryAsync(silentPopup: true);

        MessageBox.Show($"가공 완료({qty}EA)가 중앙 서버에 보고되었으며 [측정 대기] 상태로 전이되었습니다.", "가공 완료 보고", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ReportIssueAsync()
    {
        // Focus the Defect & Countermeasure tab so the worker can also see/manage full history
        if (_bottomLeftTabs != null && _bottomLeftTabs.TabCount > 0)
        {
            _bottomLeftTabs.SelectedIndex = 0;
        }

        var desc = SimpleInputDialog.Show("발생한 불량, 수정사항 또는 공구파손 내용을 입력하세요:", "🚨 긴급 불량/수정/이슈 등록", _txtDefectDesc.Text);
        if (string.IsNullOrWhiteSpace(desc)) return;
        var cause = SimpleInputDialog.Show("발생 원인을 입력하세요:", "발생 원인 입력", _txtDefectCause.Text);
        var action = SimpleInputDialog.Show("현장 조치 및 재발방지 대책을 입력하세요:", "조치 및 대책 내용", _txtDefectCountermeasure.Text);

        _txtDefectDesc.Text = desc;
        if (!string.IsNullOrWhiteSpace(cause)) _txtDefectCause.Text = cause;
        if (!string.IsNullOrWhiteSpace(action)) _txtDefectCountermeasure.Text = action;

        SetCurrentStatus("알람");
        _txtWorkDescription.Text = $"🚨 이슈/불량: {desc} (대책: {action})";
        await SaveDefectAndCountermeasureAsync();
    }

    // =========================================================================
    // Requirement #7: 작업지시서 목록 열람 다이얼로그
    // =========================================================================
    private async Task ShowWorkOrderBrowserDialogAsync()
    {
        await LoadWorkOrdersAsync();

        using var dlg = new Form
        {
            Text = "📋 제조3팀 작업지시서 및 공유폴더 관리 [00-진행중 ↔ 99-완료백업 ↔ 숨김보관함]",
            Size = new Size(1180, 680),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.5f)
        };

        // Top Filter & Share Sync Bar
        var topBar = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(12, 8, 12, 8), BackColor = Color.FromArgb(30, 41, 59) };
        var lblTitle = new Label
        {
            Text = "📂 [제조3팀공유폴더] 작지 연동 및 목록 관리",
            AutoSize = true,
            Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            Location = new Point(12, 12)
        };

        var lblFilter = new Label
        {
            Text = "필터:",
            AutoSize = true,
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(340, 14)
        };

        var cboFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            Location = new Point(380, 11),
            Width = 230
        };
        cboFilter.Items.AddRange(new object[] { "전체 작지 열람", "진행/대기중 (00-진행중)", "가공완료 (99-완료백업)", "👁️ 숨김 보관함 (목록에서 숨긴 작지)" });
        cboFilter.SelectedIndex = 0;

        var btnSyncShare = CreateFlatButton("🔄 공유폴더 즉시 동기화", Color.FromArgb(14, 165, 233), Color.White, 190, 32);
        btnSyncShare.Location = new Point(630, 8);

        topBar.Controls.Add(lblTitle);
        topBar.Controls.Add(lblFilter);
        topBar.Controls.Add(cboFilter);
        topBar.Controls.Add(btnSyncShare);

        // Center Grid
        var grid = CreateModernGrid();
        grid.Columns.Add("OrderNo", "작지번호");
        grid.Columns.Add("Customer", "고객사");
        grid.Columns.Add("PartName", "부품명(품명)");
        grid.Columns.Add("PartCode", "품번");
        grid.Columns.Add("Spec", "소재 / 두께");
        grid.Columns.Add("Qty", "목표수량");
        grid.Columns.Add("DueDate", "납기일");
        grid.Columns.Add("Status", "상태");
        grid.Columns.Add("Location", "공유/백업 보관위치");

        var flatMap = new List<(int orderId, string orderNo, string partName, int qty, int comboIdx, string status, string folderPath, string backupPath, bool isHidden)>();

        void PopulateGrid(int filterMode)
        {
            grid.Rows.Clear();
            flatMap.Clear();
            int comboIndex = 0;

            foreach (var o in _cachedOrders)
            {
                int ordId = o.TryGetProperty("orderId", out var idProp) ? idProp.GetInt32() : 0;
                var ordNo = o.TryGetProperty("orderNumber", out var onProp) ? onProp.GetString() ?? "" : "";
                var cust = o.TryGetProperty("customerName", out var cProp) ? cProp.GetString() ?? "" : "";
                var due = o.TryGetProperty("targetDeliveryDate", out var dProp) ? dProp.GetString() ?? "" : "";
                var ordStatus = o.TryGetProperty("status", out var stProp) ? stProp.GetString() ?? "대기" : "대기";
                var fPath = o.TryGetProperty("folderPath", out var fpProp) ? fpProp.GetString() ?? "" : "";
                var bPath = o.TryGetProperty("backupPath", out var bpProp) ? bpProp.GetString() ?? "" : "";
                bool isHidden = o.TryGetProperty("isHidden", out var ihProp) && ihProp.GetBoolean();

                if (filterMode == 3)
                {
                    if (!isHidden) continue; // 숨김 보관함: 숨겨진 작지만 표시
                }
                else
                {
                    if (isHidden) continue; // 일반 목록: 숨겨진 작지는 제외
                    bool isCompleted = ordStatus == "가공완료" || ordStatus == "완료";
                    if (filterMode == 1 && isCompleted) continue; // 진행/대기중만
                    if (filterMode == 2 && !isCompleted) continue; // 완료만
                }

                bool isDone = ordStatus == "가공완료" || ordStatus == "완료";
                string locationDesc;
                if (!string.IsNullOrEmpty(bPath))
                    locationDesc = $"[99-완료백업] {Path.GetFileName(bPath)}";
                else if (!string.IsNullOrEmpty(fPath))
                    locationDesc = $"[00-진행중] {Path.GetFileName(fPath)}";
                else
                    locationDesc = "-";

                if (isHidden)
                {
                    locationDesc = $"[숨김] {locationDesc}";
                }

                if (o.TryGetProperty("parts", out var parts))
                {
                    foreach (var p in parts.EnumerateArray())
                    {
                        var pName = p.TryGetProperty("partName", out var pn) ? pn.GetString() ?? "" : "";
                        var pCode = p.TryGetProperty("partCode", out var pc) ? pc.GetString() ?? "" : "";
                        var mat = p.TryGetProperty("material", out var pm) ? pm.GetString() ?? "" : "";
                        var thk = p.TryGetProperty("thickness", out var pt) ? pt.GetDouble() : 0.0;
                        var qty = p.TryGetProperty("targetQty", out var pq) ? pq.GetInt32() : 1;
                        var pStatus = p.TryGetProperty("status", out var ps) ? ps.GetString() ?? ordStatus : ordStatus;

                        int rIdx = grid.Rows.Add(ordNo, cust, pName, pCode, $"{mat} ({thk}T)", $"{qty}EA", due, pStatus, locationDesc);
                        if (isHidden)
                        {
                            grid.Rows[rIdx].DefaultCellStyle.ForeColor = Color.FromArgb(248, 113, 113);
                        }
                        else if (isDone)
                        {
                            grid.Rows[rIdx].DefaultCellStyle.ForeColor = Color.FromArgb(148, 163, 184);
                        }
                        flatMap.Add((ordId, ordNo, pName, qty, comboIndex, ordStatus, fPath, bPath, isHidden));
                        comboIndex++;
                    }
                }
                else
                {
                    int rIdx = grid.Rows.Add(ordNo, cust, ordNo, "1", "AL6061 (10T)", "1EA", due, ordStatus, locationDesc);
                    if (isHidden) grid.Rows[rIdx].DefaultCellStyle.ForeColor = Color.FromArgb(248, 113, 113);
                    else if (isDone) grid.Rows[rIdx].DefaultCellStyle.ForeColor = Color.FromArgb(148, 163, 184);
                    flatMap.Add((ordId, ordNo, ordNo, 1, comboIndex, ordStatus, fPath, bPath, isHidden));
                    comboIndex++;
                }
            }
        }

        // Bottom Action Bar
        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(12, 10, 12, 10), BackColor = Color.FromArgb(30, 41, 59) };

        var btnViewFiles = CreateFlatButton("📂 도면/NC/첨부 파일 열람", Color.FromArgb(51, 65, 85), Color.White, 200, 36);
        btnViewFiles.Dock = DockStyle.Left;

        var btnComplete = CreateFlatButton("⏹️ 선택 작지 [가공완료 및 백업 이동]", Color.FromArgb(234, 88, 12), Color.White, 250, 36);
        btnComplete.Dock = DockStyle.Left;

        var btnToggleHide = CreateFlatButton("🗑️ 선택 작지 숨기기", Color.FromArgb(185, 28, 28), Color.White, 190, 36);
        btnToggleHide.Dock = DockStyle.Left;

        var btnSelect = CreateFlatButton("✅ 선택 작지 가공작업 시작 (가공화면에 불러오기)", Color.FromArgb(16, 185, 129), Color.White, 320, 36);
        btnSelect.Dock = DockStyle.Right;

        PopulateGrid(0);

        cboFilter.SelectedIndexChanged += (s, e) =>
        {
            if (cboFilter.SelectedIndex == 3)
            {
                btnToggleHide.Text = "♻️ 선택 작지 다시 표시";
                btnToggleHide.BackColor = Color.FromArgb(16, 185, 129);
            }
            else
            {
                btnToggleHide.Text = "🗑️ 선택 작지 숨기기";
                btnToggleHide.BackColor = Color.FromArgb(185, 28, 28);
            }
            PopulateGrid(cboFilter.SelectedIndex);
        };

        btnSyncShare.Click += async (s, e) =>
        {
            btnSyncShare.Enabled = false;
            btnSyncShare.Text = "동기화 중...";
            try
            {
                var res = await _api.SyncShareFolderAsync();
                MessageBox.Show(res.message, "공유폴더 자동동기화", MessageBoxButtons.OK, res.success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                await LoadWorkOrdersAsync();
                PopulateGrid(cboFilter.SelectedIndex);
            }
            finally
            {
                btnSyncShare.Enabled = true;
                btnSyncShare.Text = "🔄 공유폴더 즉시 동기화";
            }
        };

        // View Files Handler
        btnViewFiles.Click += async (s, e) =>
        {
            if (grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("열람할 작업지시서를 먼저 목록에서 선택하세요.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int rIdx = grid.SelectedRows[0].Index;
            if (rIdx < 0 || rIdx >= flatMap.Count) return;
            var item = flatMap[rIdx];

            var filesRes = await _api.GetOrderFilesAsync(item.orderId);
            if (filesRes == null || !filesRes.Value.TryGetProperty("exists", out var ex) || !ex.GetBoolean())
            {
                MessageBox.Show($"작지번호 [{item.orderNo}]에 연결된 공유폴더/백업폴더를 찾을 수 없습니다.\r\n경로: {item.folderPath}", "파일 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var root = filesRes.Value;
            var folder = root.TryGetProperty("folder", out var fld) ? fld.GetString() ?? "" : "";
            var isBackup = root.TryGetProperty("isBackup", out var ib) && ib.GetBoolean();
            var completedAt = root.TryGetProperty("completedAt", out var ca) ? ca.GetString() ?? "" : "";

            using var fDlg = new Form
            {
                Text = $"📂 작지 [{item.orderNo}] 연결 폴더 및 첨부파일 열람 ({(isBackup ? "완료백업보관" : "00-진행중")})",
                Size = new Size(720, 460),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                Font = new Font("맑은 고딕", 9.5f)
            };

            var fTop = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(10), BackColor = Color.FromArgb(30, 41, 59) };
            var fLblPath = new Label
            {
                Text = $"위치: {folder}\r\n상태: {(isBackup ? $"가공완료 백업보관 (완료시각: {completedAt})" : "현재 가공 진행중")}",
                Dock = DockStyle.Left,
                AutoSize = true,
                ForeColor = Color.FromArgb(226, 232, 240)
            };
            var btnOpenExplorer = CreateFlatButton("📂 탐색기로 열기", Color.FromArgb(14, 165, 233), Color.White, 140, 36);
            btnOpenExplorer.Dock = DockStyle.Right;
            btnOpenExplorer.Click += (se, ee) =>
            {
                try
                {
                    if (Directory.Exists(folder))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
                    else
                        MessageBox.Show("폴더가 로컬에 존재하지 않거나 접근할 수 없습니다.", "경로 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception exx)
                {
                    MessageBox.Show("탐색기 실행 오류: " + exx.Message);
                }
            };
            fTop.Controls.Add(fLblPath);
            fTop.Controls.Add(btnOpenExplorer);

            var fGrid = CreateModernGrid();
            fGrid.Columns.Add("FileName", "파일명");
            fGrid.Columns.Add("Size", "크기");
            fGrid.Columns.Add("UpdatedAt", "최종 수정일시");

            if (root.TryGetProperty("files", out var fArray))
            {
                foreach (var fItem in fArray.EnumerateArray())
                {
                    var fn = fItem.GetProperty("fileName").GetString() ?? "";
                    long sz = fItem.GetProperty("size").GetInt64();
                    var szStr = sz < 1024 ? $"{sz} B" : sz < 1048576 ? $"{sz / 1024.0:F1} KB" : $"{sz / 1048576.0:F2} MB";
                    var uAt = fItem.GetProperty("updatedAt").GetString() ?? "";
                    fGrid.Rows.Add(fn, szStr, uAt);
                }
            }

            fDlg.Controls.Add(fGrid);
            fDlg.Controls.Add(fTop);
            fDlg.ShowDialog(dlg);
        };

        // Complete Order Handler
        btnComplete.Click += async (s, e) =>
        {
            if (grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("가공 완료 및 백업 처리할 작업지시서를 선택하세요.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int rIdx = grid.SelectedRows[0].Index;
            if (rIdx < 0 || rIdx >= flatMap.Count) return;
            var item = flatMap[rIdx];

            if (item.status == "가공완료" || item.status == "완료")
            {
                MessageBox.Show($"작지번호 [{item.orderNo}]는 이미 가공완료 처리되어 백업 보관되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"작지번호: [{item.orderNo}]\r\n부품명: [{item.partName}]\r\n\r\n이 작업을 '가공완료' 처리하고, [00-진행중] 폴더를 [99-완료백업]으로 자동 백업 이동하시겠습니까?",
                "가공완료 및 백업 확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (confirm == DialogResult.Yes)
            {
                btnComplete.Enabled = false;
                btnComplete.Text = "백업 이동 처리 중...";
                try
                {
                    var res = await _api.CompleteWorkOrderAsync(item.orderId, _session.DisplayName);
                    if (res.success)
                    {
                        MessageBox.Show($"작지 [{item.orderNo}] 가공완료 처리 및 백업 이동이 완료되었습니다!\r\n나중에 '가공완료 (99-완료백업)' 탭에서 언제든 다시 작업내용과 도면 파일을 확인하실 수 있습니다.", "처리 성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        await LoadWorkOrdersAsync();
                        PopulateGrid(cboFilter.SelectedIndex);
                    }
                    else
                    {
                        MessageBox.Show(res.message, "처리 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                finally
                {
                    btnComplete.Enabled = true;
                    btnComplete.Text = "⏹️ 선택 작지 [가공완료 및 백업 이동]";
                }
            }
        };

        // Toggle Hide / Unhide Handler
        btnToggleHide.Click += async (s, e) =>
        {
            if (grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("작업지시서를 먼저 목록에서 선택하세요.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int rIdx = grid.SelectedRows[0].Index;
            if (rIdx < 0 || rIdx >= flatMap.Count) return;
            var item = flatMap[rIdx];

            if (item.isHidden || cboFilter.SelectedIndex == 3)
            {
                btnToggleHide.Enabled = false;
                try
                {
                    var res = await _api.UnhideWorkOrderAsync(item.orderId);
                    if (res.success)
                    {
                        MessageBox.Show($"작지번호 [{item.orderNo}]이(가) 일반 목록에 다시 표시되도록 복원되었습니다.", "표시 복원", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        await LoadWorkOrdersAsync();
                        PopulateGrid(cboFilter.SelectedIndex);
                    }
                    else
                    {
                        MessageBox.Show(res.message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                finally
                {
                    btnToggleHide.Enabled = true;
                }
            }
            else
            {
                var confirm = MessageBox.Show(
                    $"작지번호: [{item.orderNo}]\r\n부품명: [{item.partName}]\r\n\r\n이 작업지시서를 목록에서 숨기시겠습니까?\r\n\r\n※ 숨겨진 작지는 새로고침 후에도 목록에 나타나지 않으며,\r\n'👁️ 숨김 보관함' 탭에서 언제든 다시 표시할 수 있습니다.",
                    "작업지시서 목록에서 숨기기",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (confirm == DialogResult.Yes)
                {
                    btnToggleHide.Enabled = false;
                    try
                    {
                        var res = await _api.HideWorkOrderAsync(item.orderId);
                        if (res.success)
                        {
                            await LoadWorkOrdersAsync();
                            PopulateGrid(cboFilter.SelectedIndex);
                        }
                        else
                        {
                            MessageBox.Show(res.message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    finally
                    {
                        btnToggleHide.Enabled = true;
                    }
                }
            }
        };

        void ApplySelection()
        {
            if (grid.SelectedRows.Count > 0)
            {
                int rIdx = grid.SelectedRows[0].Index;
                if (rIdx >= 0 && rIdx < flatMap.Count)
                {
                    var item = flatMap[rIdx];
                    if (item.comboIdx < _cboParts.Items.Count) _cboParts.SelectedIndex = item.comboIdx;
                    _txtOrderNumber.Text = item.orderNo;
                    _txtPartName.Text = item.partName;
                    _txtManualQty.Text = item.qty.ToString();
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                }
            }
        }

        btnSelect.Click += (s, e) => ApplySelection();
        grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ApplySelection(); };

        bottomBar.Controls.Add(btnViewFiles);
        bottomBar.Controls.Add(btnComplete);
        bottomBar.Controls.Add(btnToggleHide);
        bottomBar.Controls.Add(btnSelect);

        dlg.Controls.Add(grid);
        dlg.Controls.Add(topBar);
        dlg.Controls.Add(bottomBar);

        dlg.ShowDialog(this);
    }




    // =========================================================================
    // Requirement #8 & #9: 실시간 채팅 & 새 채팅 표시 & 공지사항 알림/확인 표시
    // =========================================================================
    private (string channelType, string topicKey) GetCurrentChatTopic()
    {
        var sel = _cboChatChannel.SelectedItem?.ToString() ?? "";
        if (sel.Contains("장비 채널"))
        {
            var mid = sel.Replace("⚙️", "").Replace("장비 채널", "").Trim();
            return ("MACHINE", mid);
        }
        return ("GENERAL", "공통");
    }

    private async Task LoadChatMessagesAsync(bool initialLoad = false)
    {
        try
        {
            var (cType, tKey) = GetCurrentChatTopic();
            var msgs = await _api.GetChatMessagesByChannelAsync(cType, tKey);

            int maxId = _lastSeenChatId;
            string? latestAuthor = null;

            var items = new List<string>();
            foreach (var m in msgs)
            {
                int id = m.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                var author = m.GetProperty("author").GetString() ?? "";
                var content = m.GetProperty("content").GetString() ?? "";
                var time = m.GetProperty("createdAt").GetString() ?? "";
                var shortTime = time.Length >= 16 ? time.Substring(11, 5) : time;

                if (id > maxId)
                {
                    maxId = id;
                    latestAuthor = author;
                }
                items.Add($"[{shortTime}] {author}: {content}");
            }

            if (!initialLoad && _lastSeenChatId >= 0 && maxId > _lastSeenChatId && latestAuthor != _session.DisplayName)
            {
                _hasUnreadChat = true;
                _lblChatNewIndicator.Visible = true;
                _btnNewChatBadge.Visible = true;
                _btnNewChatBadge.Text = $"🔔 새 채팅 ({latestAuthor})";
            }

            if (maxId != _lastSeenChatId || _lstChat.Items.Count != items.Count)
            {
                _lastSeenChatId = maxId;
                _lstChat.BeginUpdate();
                _lstChat.Items.Clear();
                foreach (var line in items) _lstChat.Items.Add(line);
                if (_lstChat.Items.Count > 0) _lstChat.TopIndex = _lstChat.Items.Count - 1;
                _lstChat.EndUpdate();
            }
        }
        catch { }
    }

    private void ClearUnreadChatAlert()
    {
        _hasUnreadChat = false;
        _lblChatNewIndicator.Visible = false;
        _btnNewChatBadge.Visible = false;
    }

    private async Task SendChatAsync()
    {
        var text = _txtChatInput.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        ClearUnreadChatAlert();
        _txtChatInput.Clear();
        var (cType, tKey) = GetCurrentChatTopic();
        await _api.SendChatMessageAsync(tKey, _session.DisplayName, text, cType);
        await LoadChatMessagesAsync(initialLoad: true);
    }

    private async Task PollRealtimeUpdatesAsync()
    {
        await LoadChatMessagesAsync(initialLoad: false);
        await CheckNoticesBadgeAsync();
    }

    private async Task CheckNoticesBadgeAsync()
    {
        try
        {
            var notices = await _api.GetNoticesAsync();
            int unackedCount = 0;

            foreach (var n in notices)
            {
                var acksRaw = n.TryGetProperty("officialAcksJson", out var aj) && aj.ValueKind == JsonValueKind.String ? aj.GetString() ?? "[]" : "[]";
                if (!acksRaw.Contains($"\"{_session.DisplayName}\""))
                {
                    unackedCount++;
                }
            }

            if (unackedCount > 0)
            {
                _btnNoticeAlert.Text = $"📢 새 공지사항 ({unackedCount}건 미확인!)";
                _btnNoticeAlert.BackColor = Color.FromArgb(220, 38, 38);
                _btnNoticeAlert.ForeColor = Color.White;
            }
            else
            {
                _btnNoticeAlert.Text = $"📢 공지사항 ({notices.Count}건 확인완료)";
                _btnNoticeAlert.BackColor = Color.FromArgb(30, 41, 59);
                _btnNoticeAlert.ForeColor = Color.FromArgb(134, 239, 172);
            }
        }
        catch { }
    }

    private async Task ShowNoticesDialogAsync()
    {
        var notices = await _api.GetNoticesAsync();

        using var dlg = new Form
        {
            Text = "📢 현장 공지사항 열람 및 확인 서명",
            Size = new Size(820, 520),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.5f)
        };

        var grid = CreateModernGrid();
        grid.Columns.Add("Id", "No");
        grid.Columns["Id"]!.FillWeight = 35;
        grid.Columns.Add("Priority", "중요도");
        grid.Columns["Priority"]!.FillWeight = 50;
        grid.Columns.Add("Title", "공지 제목");
        grid.Columns["Title"]!.FillWeight = 120;
        grid.Columns.Add("Content", "공지 내용");
        grid.Columns["Content"]!.FillWeight = 180;
        grid.Columns.Add("AuthorTime", "작성자 / 일시");
        grid.Columns["AuthorTime"]!.FillWeight = 95;
        grid.Columns.Add("MyAck", "내 확인 상태");
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
                string author = n.GetProperty("author").GetString() ?? "";
                string created = n.GetProperty("createdAt").GetString() ?? "";
                string acksRaw = n.TryGetProperty("officialAcksJson", out var aj) && aj.ValueKind == JsonValueKind.String ? aj.GetString() ?? "[]" : "[]";
                bool acked = acksRaw.Contains($"\"{_session.DisplayName}\"");

                grid.Rows.Add(id, pri, title, content, $"{author} ({created})", acked ? "✅ 확인완료" : "🚨 미확인 (클릭하여 서명)");
            }
        }

        PopulateGrid(notices);

        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(10, 8, 10, 8), BackColor = Color.FromArgb(19, 28, 49) };
        var btnAckSelected = CreateFlatButton("✅ 선택한 공지사항 읽음 확인 서명 (중앙서버 전송)", Color.FromArgb(16, 185, 129), Color.White, 340, 34);
        btnAckSelected.Dock = DockStyle.Right;

        var btnAckAll = CreateFlatButton("✅ 전체 미확인 공지 일괄 확인 서명", Color.FromArgb(2, 132, 199), Color.White, 250, 34);
        btnAckAll.Dock = DockStyle.Left;

        btnAckSelected.Click += async (s, e) =>
        {
            if (grid.SelectedRows.Count == 0) return;
            int nid = Convert.ToInt32(grid.SelectedRows[0].Cells["Id"].Value);
            await _api.AcknowledgeNoticeAsync(nid, _session.DisplayName);
            notices = await _api.GetNoticesAsync();
            PopulateGrid(notices);
            await CheckNoticesBadgeAsync();
            MessageBox.Show($"공지사항 #{nid}에 대해 [{_session.DisplayName}] 확인 서명이 완료되었습니다.", "공지 확인 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        btnAckAll.Click += async (s, e) =>
        {
            foreach (var n in notices)
            {
                int nid = n.GetProperty("noticeId").GetInt32();
                await _api.AcknowledgeNoticeAsync(nid, _session.DisplayName);
            }
            notices = await _api.GetNoticesAsync();
            PopulateGrid(notices);
            await CheckNoticesBadgeAsync();
            MessageBox.Show("모든 공지사항에 대해 읽음 확인 서명이 완료되었습니다!", "전체 확인 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        bottomBar.Controls.Add(btnAckAll);
        bottomBar.Controls.Add(btnAckSelected);
        dlg.Controls.Add(grid);
        dlg.Controls.Add(bottomBar);
        dlg.ShowDialog(this);
    }

    // =========================================================================
    // 📦 후공정(핀삽입/각인/후처리 등) 패널 구축 및 보고 대화상자 (v1.3.7)
    // =========================================================================
    private Panel BuildPostProcessPanel()
    {
        var pnl = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(19, 28, 49) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(2)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Top Toolbar
        var toolBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(2)
        };

        var lblFilter = new Label
        {
            Text = "상태 필터:",
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("맑은 고딕", 9f),
            AutoSize = true,
            Margin = new Padding(2, 6, 4, 0)
        };
        toolBar.Controls.Add(lblFilter);

        var cboFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9f),
            Width = 110,
            Margin = new Padding(0, 3, 8, 0)
        };
        cboFilter.Items.AddRange(new object[] { "전체", "후공정대기", "후공정완료" });
        cboFilter.SelectedIndex = 0;
        cboFilter.SelectedIndexChanged += (s, e) =>
        {
            _currentPostProcessFilter = cboFilter.SelectedItem?.ToString() ?? "전체";
            RenderPostProcessGrid();
        };
        toolBar.Controls.Add(cboFilter);

        var btnCompletePp = CreateFlatButton("✅ 선택 항목 후공정 완료 처리", Color.FromArgb(16, 185, 129), Color.White, 200, 30);
        btnCompletePp.Margin = new Padding(0, 1, 8, 0);
        btnCompletePp.Click += async (s, e) =>
        {
            if (_gridPostProcess.SelectedRows.Count == 0)
            {
                MessageBox.Show("완료 처리할 후공정 항목을 표에서 선택하세요.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var row = _gridPostProcess.SelectedRows[0];
            int reportId = Convert.ToInt32(row.Cells["Id"].Value);
            string orderNo = row.Cells["OrderNumber"].Value?.ToString() ?? "";
            string partNm = row.Cells["PartName"].Value?.ToString() ?? "";
            string curStatus = row.Cells["Status"].Value?.ToString() ?? "";

            if (curStatus == "후공정완료")
            {
                MessageBox.Show("이미 후공정 완료 처리된 항목입니다.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show($"작지번호 [{orderNo}] - [{partNm}]의 후공정을 완료 처리하시겠습니까?", "후공정 완료 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                var ok = await _api.CompletePostProcessReportAsync(reportId, _session.DisplayName);
                if (ok)
                {
                    MessageBox.Show("후공정이 정상적으로 완료 처리되었습니다.", "성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    await LoadPostProcessReportsAsync();
                }
                else
                {
                    MessageBox.Show("후공정 완료 처리에 실패했습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        };
        toolBar.Controls.Add(btnCompletePp);

        var btnRefreshPp = CreateFlatButton("🔄 새로고침", Color.FromArgb(51, 65, 85), Color.White, 90, 30);
        btnRefreshPp.Margin = new Padding(0, 1, 0, 0);
        btnRefreshPp.Click += async (s, e) => await LoadPostProcessReportsAsync();
        toolBar.Controls.Add(btnRefreshPp);

        layout.Controls.Add(toolBar, 0, 0);

        // DataGridView
        _gridPostProcess = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            GridColor = Color.FromArgb(51, 65, 85),
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
            Font = new Font("맑은 고딕", 9f)
        };
        _gridPostProcess.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 41, 59);
        _gridPostProcess.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(56, 189, 248);
        _gridPostProcess.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
        _gridPostProcess.DefaultCellStyle.BackColor = Color.FromArgb(15, 23, 42);
        _gridPostProcess.DefaultCellStyle.ForeColor = Color.White;
        _gridPostProcess.DefaultCellStyle.SelectionBackColor = Color.FromArgb(37, 99, 235);
        _gridPostProcess.DefaultCellStyle.SelectionForeColor = Color.White;

        _gridPostProcess.Columns.Add("Id", "ID");
        _gridPostProcess.Columns.Add("OrderNumber", "작지번호");
        _gridPostProcess.Columns.Add("PartName", "부품명");
        _gridPostProcess.Columns.Add("MachineId", "가공장비");
        _gridPostProcess.Columns.Add("Worker", "가공자");
        _gridPostProcess.Columns.Add("Qty", "완료수량");
        _gridPostProcess.Columns.Add("Processes", "지정 후공정 항목");
        _gridPostProcess.Columns.Add("Notes", "전달 메모");
        _gridPostProcess.Columns.Add("Status", "상태");
        _gridPostProcess.Columns.Add("CreatedAt", "보고일시");

        _gridPostProcess.Columns["Id"].Width = 45;
        _gridPostProcess.Columns["OrderNumber"].Width = 120;
        _gridPostProcess.Columns["PartName"].Width = 110;
        _gridPostProcess.Columns["MachineId"].Width = 70;
        _gridPostProcess.Columns["Worker"].Width = 70;
        _gridPostProcess.Columns["Qty"].Width = 60;
        _gridPostProcess.Columns["Processes"].Width = 160;
        _gridPostProcess.Columns["Notes"].Width = 130;
        _gridPostProcess.Columns["Status"].Width = 80;
        _gridPostProcess.Columns["CreatedAt"].Width = 110;

        layout.Controls.Add(_gridPostProcess, 0, 1);
        pnl.Controls.Add(layout);
        return pnl;
    }

    private async Task LoadPostProcessReportsAsync()
    {
        try
        {
            _cachedPostProcess = await _api.GetPostProcessReportsAsync();
            RenderPostProcessGrid();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load post process reports: {ex.Message}");
        }
    }

    private void RenderPostProcessGrid()
    {
        if (_gridPostProcess == null || _gridPostProcess.IsDisposed) return;
        _gridPostProcess.Rows.Clear();

        var list = _cachedPostProcess;
        if (_currentPostProcessFilter == "후공정대기")
            list = list.Where(x => (x.GetProperty("status").GetString() ?? "") != "후공정완료").ToList();
        else if (_currentPostProcessFilter == "후공정완료")
            list = list.Where(x => (x.GetProperty("status").GetString() ?? "") == "후공정완료").ToList();

        foreach (var item in list)
        {
            int id = item.GetProperty("id").GetInt32();
            string orderNo = item.TryGetProperty("orderNumber", out var on) ? on.GetString() ?? "" : "";
            string partNm = item.TryGetProperty("partName", out var pn) ? pn.GetString() ?? "" : "";
            string mach = item.TryGetProperty("machineId", out var mid) ? mid.GetString() ?? "" : "";
            string worker = item.TryGetProperty("worker", out var wk) ? wk.GetString() ?? "" : "";
            int qty = item.TryGetProperty("completedQty", out var cq) ? cq.GetInt32() : 0;
            string procs = item.TryGetProperty("selectedProcesses", out var sp) ? sp.GetString() ?? "" : "";
            string notes = item.TryGetProperty("notes", out var nt) ? nt.GetString() ?? "" : "";
            string status = item.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
            string cat = item.TryGetProperty("createdAt", out var ca) ? ca.GetString() ?? "" : "";

            int rowIndex = _gridPostProcess.Rows.Add(id, orderNo, partNm, mach, worker, $"{qty} EA", procs, notes, status, cat);
            var row = _gridPostProcess.Rows[rowIndex];

            if (status == "후공정완료")
            {
                row.Cells["Status"].Style.ForeColor = Color.FromArgb(52, 211, 153);
                row.Cells["Status"].Style.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
            }
            else
            {
                row.Cells["Status"].Style.ForeColor = Color.FromArgb(251, 191, 36);
                row.Cells["Status"].Style.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
            }
        }
    }

    private async Task ShowCompleteAndPostProcessDialogAsync()
    {
        await Task.CompletedTask;

        // 1. Current Work Order & Part Resolution
        if (!_selectedPart.HasValue && _cboParts.SelectedIndex >= 0 && _cboParts.SelectedIndex < _cachedParts.Count)
        {
            _selectedPart = _cachedParts[_cboParts.SelectedIndex];
        }

        if (!_selectedPart.HasValue)
        {
            MessageBox.Show("작업 중인 작업지시서 부품을 선택한 후 가공 완료 보고를 진행해주세요.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var partElem = _selectedPart.Value;
        int partId = partElem.GetProperty("partId").GetInt32();
        int orderId = partElem.GetProperty("orderId").GetInt32();
        string currentPartName = partElem.TryGetProperty("partName", out var pn) ? pn.GetString() ?? "" : "";
        string currentOrderNumber = partElem.TryGetProperty("orderNumber", out var on) ? on.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(currentOrderNumber))
        {
            var ord = _cachedOrders.FirstOrDefault(o => o.TryGetProperty("orderId", out var oid) && oid.GetInt32() == orderId);
            if (ord.ValueKind != JsonValueKind.Undefined && ord.TryGetProperty("orderNumber", out var oNum))
            {
                currentOrderNumber = oNum.GetString() ?? "";
            }
            if (string.IsNullOrEmpty(currentOrderNumber))
            {
                currentOrderNumber = $"WO-{orderId}";
            }
        }

        using var dlg = new Form
        {
            Text = "✅ 가공 완료 보고 및 후속 공정(핀삽입/각인/후처리) 지정",
            Size = new Size(580, 560),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.5f)
        };

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(16, 12, 16, 12)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f)); // Header Info
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f)); // Qty input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 185f)); // Checkboxes
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Notes
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f)); // Buttons

        // 1. Header Info Panel
        var pnlHeader = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 41, 59), Padding = new Padding(10, 6, 10, 6) };
        var lblOrderInfo = new Label
        {
            Text = $"작지번호: {currentOrderNumber}\n가공품목: {currentPartName}  |  가공장비: {_currentMachineId}  |  작업자: {_session.DisplayName}",
            ForeColor = Color.FromArgb(56, 189, 248),
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold),
            Dock = DockStyle.Fill
        };
        pnlHeader.Controls.Add(lblOrderInfo);
        mainLayout.Controls.Add(pnlHeader, 0, 0);

        // 2. Qty Row
        var pnlQty = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        var lblQtyTitle = new Label { Text = "가공 완료 수량:", AutoSize = true, Margin = new Padding(0, 6, 8, 0), Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold) };
        
        int initialQty = 1;
        if (int.TryParse(_txtManualQty.Text.Trim(), out var parsed) && parsed > 0) initialQty = parsed;

        var numQty = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 99999,
            Value = initialQty,
            Width = 100,
            BackColor = Color.FromArgb(19, 28, 49),
            ForeColor = Color.FromArgb(52, 211, 153),
            Font = new Font("맑은 고딕", 10f, FontStyle.Bold)
        };
        var lblQtyUnit = new Label { Text = "EA (개)", AutoSize = true, Margin = new Padding(4, 6, 0, 0), ForeColor = Color.FromArgb(148, 163, 184) };
        pnlQty.Controls.Add(lblQtyTitle);
        pnlQty.Controls.Add(numQty);
        pnlQty.Controls.Add(lblQtyUnit);
        mainLayout.Controls.Add(pnlQty, 0, 1);

        // 3. Post-Process Checkboxes GroupBox
        var grpCheckboxes = new GroupBox
        {
            Text = " 📦 필요한 후속 공정을 모두 선택하세요 (복수 선택 가능) ",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(251, 191, 36),
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold),
            Padding = new Padding(12, 10, 12, 10)
        };

        var chkPin = new CheckBox { Text = "📌 핀 삽입 / 압입 공정 (Pin Insertion)", ForeColor = Color.White, Font = new Font("맑은 고딕", 9f), Location = new Point(16, 26), AutoSize = true };
        var chkLaser = new CheckBox { Text = "⚡ 레이저 각인 / 품번 마킹 (Laser Marking)", ForeColor = Color.White, Font = new Font("맑은 고딕", 9f), Location = new Point(16, 52), AutoSize = true };
        var chkSurface = new CheckBox { Text = "🧪 표면처리 / 후처리 (아노다이징, 도금, 흑착색, 열처리)", ForeColor = Color.White, Font = new Font("맑은 고딕", 9f), Location = new Point(16, 78), AutoSize = true };
        var chkTap = new CheckBox { Text = "🔩 탭 가공 / 헬리코일 삽입 (Tapping / Helicoil)", ForeColor = Color.White, Font = new Font("맑은 고딕", 9f), Location = new Point(16, 104), AutoSize = true };
        var chkClean = new CheckBox { Text = "✨ 최종 세척 및 정밀 치수·외관 검사 (Cleaning & Final Inspection)", ForeColor = Color.White, Font = new Font("맑은 고딕", 9f), Location = new Point(16, 130), AutoSize = true };
        var chkEtc = new CheckBox { Text = "🔧 기타 후속 공정 (아래 전달 특이사항 메모에 상세 기재)", ForeColor = Color.White, Font = new Font("맑은 고딕", 9f), Location = new Point(16, 156), AutoSize = true };

        grpCheckboxes.Controls.AddRange(new Control[] { chkPin, chkLaser, chkSurface, chkTap, chkClean, chkEtc });
        mainLayout.Controls.Add(grpCheckboxes, 0, 2);

        // 4. Notes Panel
        var pnlNotes = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
        var lblNotes = new Label { Text = "전달 특이사항 / 후공정 작업자 안내 메모:", Dock = DockStyle.Top, Height = 22, ForeColor = Color.FromArgb(148, 163, 184) };
        var txtNotes = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            BackColor = Color.FromArgb(19, 28, 49),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical
        };
        pnlNotes.Controls.Add(txtNotes);
        pnlNotes.Controls.Add(lblNotes);
        mainLayout.Controls.Add(pnlNotes, 0, 3);

        // 5. Button Bar
        var pnlBottom = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
        var btnSubmit = CreateFlatButton("✅ 가공완료 보고 및 후공정 요청 등록", Color.FromArgb(16, 185, 129), Color.White, 290, 36);
        btnSubmit.Dock = DockStyle.Right;

        var btnCancel = CreateFlatButton("취소", Color.FromArgb(71, 85, 105), Color.White, 80, 36);
        btnCancel.Dock = DockStyle.Left;
        btnCancel.Click += (s, e) => dlg.DialogResult = DialogResult.Cancel;

        btnSubmit.Click += async (s, e) =>
        {
            var selectedProcs = new List<string>();
            if (chkPin.Checked) selectedProcs.Add("핀 삽입/압입");
            if (chkLaser.Checked) selectedProcs.Add("레이저 각인");
            if (chkSurface.Checked) selectedProcs.Add("표면/후처리");
            if (chkTap.Checked) selectedProcs.Add("탭/헬리코일");
            if (chkClean.Checked) selectedProcs.Add("최종세척/검사");
            if (chkEtc.Checked) selectedProcs.Add("기타");

            string procStr = selectedProcs.Count > 0 ? string.Join(", ", selectedProcs) : "없음 (일반완료)";
            int qty = (int)numQty.Value;
            string memo = txtNotes.Text.Trim();

            btnSubmit.Enabled = false;
            btnSubmit.Text = "서버 전송 중...";

            var (ok, msg) = await _api.SubmitPostProcessReportAsync(
                partId: partId,
                orderId: orderId,
                orderNumber: currentOrderNumber,
                partName: currentPartName,
                machineId: _currentMachineId,
                worker: _session.DisplayName,
                completedQty: qty,
                selectedProcesses: procStr,
                notes: memo
            );

            if (ok)
            {
                // 로컬 작업이력도 기록
                await _api.AddManualWorkLogAsync(
                    machineId: _currentMachineId,
                    orderNumber: currentOrderNumber,
                    partName: currentPartName,
                    workDetail: $"가공완료({qty}EA) 보고 [지정후공정: {procStr}]",
                    status: "가공완료",
                    workerName: _session.DisplayName,
                    qty: qty
                );

                MessageBox.Show($"가공 완료 보고 및 후공정 지정이 완료되었습니다!\n\n- 작지번호: {currentOrderNumber}\n- 완료수량: {qty} EA\n- 지정후공정: {procStr}\n\n[📦 후공정 추적] 탭으로 이동합니다.", "등록 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                dlg.DialogResult = DialogResult.OK;

                // 탭 전환 및 갱신
                if (_bottomLeftTabs != null && _bottomLeftTabs.TabCount >= 4)
                {
                    _bottomLeftTabs.SelectedIndex = 3;
                }
                await LoadPostProcessReportsAsync();
                await LoadWorkHistoryGridAsync();
            }
            else
            {
                MessageBox.Show(msg, "등록 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnSubmit.Enabled = true;
                btnSubmit.Text = "✅ 가공완료 보고 및 후공정 요청 등록";
            }
        };

        pnlBottom.Controls.Add(btnCancel);
        pnlBottom.Controls.Add(btnSubmit);
        mainLayout.Controls.Add(pnlBottom, 0, 4);

        dlg.Controls.Add(mainLayout);
        dlg.ShowDialog(this);
    }
}
