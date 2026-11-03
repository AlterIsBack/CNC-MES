using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client.Workflow;

public class NodeDetailPanel : UserControl
{
    public static class Palette
    {
        public static readonly Color Bg900 = Color.FromArgb(11, 19, 35);
        public static readonly Color Bg850 = Color.FromArgb(15, 27, 49);
        public static readonly Color Bg800 = Color.FromArgb(20, 35, 64);
        public static readonly Color Border = Color.FromArgb(35, 55, 95);
        public static readonly Color AccentSky = Color.FromArgb(14, 165, 233);
        public static readonly Color Green = Color.FromArgb(16, 185, 129);
        public static readonly Color Amber = Color.FromArgb(245, 158, 11);
        public static readonly Color Rose = Color.FromArgb(239, 68, 68);
        public static readonly Color Purple = Color.FromArgb(168, 85, 247);
        public static readonly Color TextWhite = Color.FromArgb(248, 250, 252);
        public static readonly Color TextSlate400 = Color.FromArgb(148, 163, 184);
        public static readonly Color TextSlate300 = Color.FromArgb(203, 213, 225);
        public static readonly Color CatInsp = Color.FromArgb(245, 158, 11);
        public static readonly Color FocusBorder = Color.FromArgb(56, 189, 248);
    }

    private static readonly Dictionary<NodeCategory, string[]> ClassicProcessPresets = new()
    {
        [NodeCategory.Machining] = new[]
        {
            "1차 전면 황삭 가공 진행",
            "1차 전면 중삭/정삭 가공 진행",
            "2차 후면(반전) 황삭 가공 진행",
            "2차 후면(반전) 정삭 가공 진행",
            "포켓 및 내경 윤곽 밀링 가공",
            "마이크로 홀 드릴링 정밀 가공",
            "챔퍼(면취) 및 버(Burr) 제거 가공",
            "고속 정밀 피니싱 가공 진행"
        },
        [NodeCategory.Preparation] = new[]
        {
            "소재 장착 및 바이스/치구 세팅",
            "G54 공작물 원점(X/Y/Z) 세팅",
            "도면/공정도 및 NC 프로그램 검증",
            "공구 장착 및 공구길이(H) 세팅",
            "초품(첫물) 시가공 및 세팅 확인"
        },
        [NodeCategory.Inspection] = new[]
        {
            "초물 치수 중간측정 검사",
            "1차 가공 후 버니어/마이크로 측정",
            "정삭 후 포켓 깊이/두께 측정",
            "외경/전장 주요 공차 적합성 확인",
            "최종 가공완료 후 출하 전 치수 확인"
        },
        [NodeCategory.PostProcess] = new[]
        {
            "초음파 세척 및 에어건 건조",
            "레이저 마킹 및 핀 압입 조립",
            "표면 아노다이징 및 후처리 의뢰",
            "최종 방청 포장 및 납품 대기"
        },
        [NodeCategory.Collaboration] = new[]
        {
            "서브인원 소재이송 및 2차세팅 지원",
            "서브인원 면취 및 버(Burr) 제거 위임",
            "가공 완료품 세척실 운반 및 인계"
        },
        [NodeCategory.Issue] = new[]
        {
            "공구 마모 및 치수 불량 점검",
            "장비 알람 발생 및 긴급 점검",
            "원점 틀어짐 확인 및 재설정",
            "소재 불량 및 설계변경 확인"
        }
    };

    private WorkflowNode? _currentNode;
    private readonly List<WorkflowNode> _currentNodes = new();
    private bool _isBinding;

    // Controls
    private Label _lblHeader = null!;
    private Label _lblOrderAndPart = null!;
    private Label _lblCurrentStatus = null!;
    private ComboBox _cboTitle = null!;
    private ComboBox _cboCategory = null!;
    private ComboBox _cboMachine = null!;
    private TextBox _txtAssignee = null!;
    // Common Custom Detail Fields ([시작시간], [종료시간], [소재사이즈], [소재종류])
    private ComboBox _cboStartTime = null!;
    private ComboBox _cboEndTime = null!;
    private TextBox _txtMaterialSize = null!;
    private ComboBox _cboMaterialType = null!;
    private ComboBox _cboFixingMethod = null!;
    internal FlowLayoutPanel _pnlTagPresets = null!;

    private TextBox _txtDetails = null!;
    private TextBox _txtNextProcess = null!;
    private Label _lblSubWorkerTaskStatus = null!;
    private TextBox _txtSubWorker = null!;
    private TextBox _txtSubInstruction = null!;

    // 5 Status Buttons (대기, 진행중, 측정, 완료, 이슈)
    private Button _btnStatusReady = null!;
    private Button _btnStatusRunning = null!;
    private Button _btnStatusPendingCheck = null!;
    private Button _btnStatusComplete = null!;
    private Button _btnStatusIssue = null!;
    private Button _btnViewIssueHistory = null!;

    public event Action<WorkflowNode>? IssueDialogRequested;

    // Action Buttons
    private Button _btnSaveDetails = null!;
    private Button _btnDuplicate = null!;
    private Button _btnDisconnect = null!;
    private Button _btnDelete = null!;
    private Button _btnSendSubTask = null!;

    public event Action<WorkflowNode, NodeStatus>? StatusChangeRequested;
    public event Action<List<WorkflowNode>, NodeStatus>? BatchStatusChangeRequested;
    public event Action<WorkflowNode>? NodeUpdated;
    public event Action<List<WorkflowNode>>? NodesUpdated;
    public event Action<WorkflowNode>? NodeDeleteRequested;
    public event Action<List<WorkflowNode>>? BatchNodesDeleteRequested;
    public event Action<WorkflowNode>? NodeDuplicateRequested;
    public event Action<WorkflowNode>? NodeDisconnectRequested;
    public event Action<WorkflowNode, string, string>? SubWorkerTaskSendRequested;
    public Func<WorkflowNode, bool>? IsFirstNodePredicate { get; set; }
    public Func<(string Size, string Material, string Fixing)>? GetFirstNodeMaterialProvider { get; set; }
    public Action<string, string, string>? FirstNodeMaterialChanged { get; set; }
    public event Action? CollapseRequested;

    public NodeDetailPanel()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        BackColor = Palette.Bg900;
        ForeColor = Palette.TextWhite;
        Padding = new Padding(8);
        AutoScroll = true; // Never clip controls on any screen resolution!

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            AutoScrollMinSize = new Size(330, 520),
            ColumnCount = 1,
            RowCount = 10,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f)); // 0: Header (Title & Status)
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f)); // 1: Work Order & Part Info
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f)); // 2: Title & Category Row
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f)); // 3: Machine & Assignee Row
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // 4: 5 Status Quick Buttons
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86f)); // 5: Fixed 5 Fields (Start/End/Size/Material/Fixing)
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));  // 6: Details / Memo Box
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f)); // 7: Next Process Input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));  // 8: Sub-worker Dedicated Section
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f)); // 9: Action Buttons

        // 0: Header
        var pnlHeader = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _lblHeader = new Label
        {
            Text = "⚙ 선택 노드 상세 정보 실시간 조작",
            Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
            ForeColor = Palette.TextWhite,
            Dock = DockStyle.Left,
            AutoSize = true
        };
        var btnCollapse = new Button
        {
            Text = "접기 ➔",
            Font = new Font("맑은 고딕", 7.5f, FontStyle.Bold),
            ForeColor = Palette.AccentSky,
            BackColor = Palette.Bg800,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Right,
            Width = 52,
            Height = 22,
            Margin = new Padding(3, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        btnCollapse.FlatAppearance.BorderSize = 1;
        btnCollapse.FlatAppearance.BorderColor = Palette.Border;
        btnCollapse.Click += (s, e) => CollapseRequested?.Invoke();

        _lblCurrentStatus = new Label
        {
            Text = "[대기]",
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold),
            ForeColor = Palette.TextSlate400,
            Dock = DockStyle.Right,
            AutoSize = true
        };
        pnlHeader.Controls.AddRange(new Control[] { _lblHeader, btnCollapse, _lblCurrentStatus });
        mainLayout.Controls.Add(pnlHeader, 0, 0);

        // 1: Work Order & Part
        _lblOrderAndPart = new Label
        {
            Dock = DockStyle.Fill,
            Text = "작지: [선택 없음] | 부품: [선택 없음]",
            Font = new Font("맑은 고딕", 8f),
            ForeColor = Palette.AccentSky,
            AutoEllipsis = true
        };
        mainLayout.Controls.Add(_lblOrderAndPart, 0, 1);

        // 2: Title (DropDown with Classic Presets) & Category (Pixel-perfect 2-col TableLayout)
        var row2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65f));
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));

        var pnlTitle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 3, 0) };
        pnlTitle.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlTitle.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlTitle.Controls.Add(new Label { Text = "공정명 (선택 / 직접 입력)", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 0, 0);

        _cboTitle = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9f),
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        _cboTitle.TextChanged += (s, e) => SaveChangesToNode();
        _cboTitle.SelectedIndexChanged += (s, e) => SaveChangesToNode();
        pnlTitle.Controls.Add(_cboTitle, 0, 1);

        var pnlCat = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(3, 0, 0, 0) };
        pnlCat.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlCat.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlCat.Controls.Add(new Label { Text = "공정분류", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 0, 0);

        _cboCategory = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9f)
        };
        _cboCategory.Items.AddRange(new object[] { "준비", "가공", "검사", "후공정", "협업", "이슈" });
        _cboCategory.SelectedIndexChanged += (s, e) =>
        {
            if (!_isBinding && _cboCategory.SelectedIndex >= 0)
            {
                var cat = (NodeCategory)_cboCategory.SelectedIndex;
                PopulateTitlePresets(cat, _cboTitle.Text);
                SaveChangesToNode();
            }
        };
        pnlCat.Controls.Add(_cboCategory, 0, 1);

        row2.Controls.Add(pnlTitle, 0, 0);
        row2.Controls.Add(pnlCat, 1, 0);
        mainLayout.Controls.Add(row2, 0, 2);

        // 3: Machine & Assignee (Pixel-perfect 2-col TableLayout 50/50)
        var row3 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        row3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        row3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var pnlMach = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 3, 0) };
        pnlMach.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlMach.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlMach.Controls.Add(new Label { Text = "담당 설비 (장비)", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 0, 0);

        _cboMachine = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9f)
        };
        _cboMachine.Items.AddRange(new object[] { "R01", "R02", "R03", "R04", "R05", "K01", "K02", "세척기", "측정실", "포장팀" });
        _cboMachine.SelectedIndexChanged += (s, e) => SaveChangesToNode();
        pnlMach.Controls.Add(_cboMachine, 0, 1);

        var pnlAss = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(3, 0, 0, 0) };
        pnlAss.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlAss.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlAss.Controls.Add(new Label { Text = "담당 작업자", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 0, 0);

        _txtAssignee = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 9f)
        };
        _txtAssignee.TextChanged += (s, e) => SaveChangesToNode();
        pnlAss.Controls.Add(_txtAssignee, 0, 1);

        row3.Controls.Add(pnlMach, 0, 0);
        row3.Controls.Add(pnlAss, 1, 0);
        mainLayout.Controls.Add(row3, 0, 3);

        // 4: 5 Status Quick Action Buttons (대기, 진행중, 측정, 완료, 이슈)
        var row4 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Margin = new Padding(0, 2, 0, 2) };
        for (int i = 0; i < 5; i++) row4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

        _btnStatusReady = CreateFlatButton("대기", Color.FromArgb(42, 59, 76), Color.White);
        _btnStatusReady.Click += (s, e) => TriggerStatusChange(NodeStatus.Ready);

        _btnStatusRunning = CreateFlatButton("진행중", Color.FromArgb(5, 150, 105), Color.White);
        _btnStatusRunning.Click += (s, e) => TriggerStatusChange(NodeStatus.Running);

        _btnStatusPendingCheck = CreateFlatButton("측정", Color.FromArgb(217, 119, 6), Color.White);
        _btnStatusPendingCheck.Click += (s, e) => TriggerStatusChange(NodeStatus.PendingCheck);

        _btnStatusComplete = CreateFlatButton("완료", Color.FromArgb(2, 132, 199), Color.White);
        _btnStatusComplete.Click += (s, e) => TriggerStatusChange(NodeStatus.Completed);

        _btnStatusIssue = CreateFlatButton("이슈", Color.FromArgb(220, 38, 38), Color.White);
        _btnStatusIssue.Click += (s, e) =>
        {
            if (_currentNode != null)
            {
                IssueDialogRequested?.Invoke(_currentNode);
            }
            else
            {
                TriggerStatusChange(NodeStatus.Issue);
            }
        };

        _btnViewIssueHistory = CreateFlatButton("⚠️ 이슈 조치 이력 조회 / 수정", Color.FromArgb(60, 245, 158, 11), Palette.Amber);
        _btnViewIssueHistory.Visible = false;
        _btnViewIssueHistory.Click += (s, e) =>
        {
            if (_currentNode != null)
            {
                IssueDialogRequested?.Invoke(_currentNode);
            }
        };

        row4.Controls.Add(_btnStatusReady, 0, 0);
        row4.Controls.Add(_btnStatusRunning, 1, 0);
        row4.Controls.Add(_btnStatusPendingCheck, 2, 0);
        row4.Controls.Add(_btnStatusComplete, 3, 0);
        row4.Controls.Add(_btnStatusIssue, 4, 0);
        mainLayout.Controls.Add(row4, 0, 4);

        // 5: Common Custom Fields Section ([시작시간] / [종료시간] / [소재사이즈] / [소재종류] / 태그프리셋)
        var pnlCommonFields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            Margin = new Padding(0, 1, 0, 1),
            Padding = new Padding(0)
        };
        pnlCommonFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55f)); // Label 1
        pnlCommonFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));  // Input 1
        pnlCommonFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50f)); // Label 2
        pnlCommonFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));  // Input 2
        pnlCommonFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f)); // Row 0: 시작 / 종료
        pnlCommonFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f)); // Row 1: Size / 재질
        pnlCommonFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f)); // Row 2: 고정방식

        var timePresets = new object[] { "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30" };
        var matPresets = new object[] { "PEEK 5T", "AL6061-T6", "SUS304", "베이크라이트", "MC나일론", "초경합금", "황동(C3604)", "SKD11" };
        var fixPresets = new object[] { "바이스 고정", "진공 흡착 (진공 플레이트)", "클램프 고정 (사이드/토글)", "마그네틱 척 고정", "전용 지그 블록", "스크류 볼팅 고정", "양면 테이프 고정" };

        var lblStart = new Label { Text = "시작:", ForeColor = Palette.AccentSky, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 8f, FontStyle.Bold) };
        _cboStartTime = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("Consolas", 8.5f), DropDownStyle = ComboBoxStyle.DropDown };
        _cboStartTime.Items.AddRange(timePresets);
        _cboStartTime.TextChanged += (s, e) => SaveChangesToNode();

        var lblEnd = new Label { Text = "종료:", ForeColor = Palette.AccentSky, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 8f, FontStyle.Bold) };
        _cboEndTime = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("Consolas", 8.5f), DropDownStyle = ComboBoxStyle.DropDown };
        _cboEndTime.Items.AddRange(timePresets);
        _cboEndTime.TextChanged += (s, e) => SaveChangesToNode();

        var lblMatSize = new Label { Text = "Size:", ForeColor = Palette.CatInsp, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 8f, FontStyle.Bold) };
        _txtMaterialSize = new TextBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 8.2f), BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "예: 150x120x5T" };
        _txtMaterialSize.TextChanged += (s, e) => SaveChangesToNode();

        var lblMatType = new Label { Text = "재질:", ForeColor = Palette.CatInsp, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 8f, FontStyle.Bold) };
        _cboMaterialType = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 8.2f), DropDownStyle = ComboBoxStyle.DropDown };
        _cboMaterialType.Items.AddRange(matPresets);
        _cboMaterialType.TextChanged += (s, e) => SaveChangesToNode();

        var lblFixMethod = new Label { Text = "고정방식:", ForeColor = Color.FromArgb(168, 85, 247), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 7.5f, FontStyle.Bold) };
        _cboFixingMethod = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 8.2f), DropDownStyle = ComboBoxStyle.DropDown };
        _cboFixingMethod.Items.AddRange(fixPresets);
        _cboFixingMethod.Text = "바이스 고정";
        _cboFixingMethod.TextChanged += (s, e) => SaveChangesToNode();

        pnlCommonFields.Controls.Add(lblStart, 0, 0);
        pnlCommonFields.Controls.Add(_cboStartTime, 1, 0);
        pnlCommonFields.Controls.Add(lblEnd, 2, 0);
        pnlCommonFields.Controls.Add(_cboEndTime, 3, 0);

        pnlCommonFields.Controls.Add(lblMatSize, 0, 1);
        pnlCommonFields.Controls.Add(_txtMaterialSize, 1, 1);
        pnlCommonFields.Controls.Add(lblMatType, 2, 1);
        pnlCommonFields.Controls.Add(_cboMaterialType, 3, 1);

        pnlCommonFields.Controls.Add(lblFixMethod, 0, 2);
        pnlCommonFields.Controls.Add(_cboFixingMethod, 1, 2);
        pnlCommonFields.SetColumnSpan(_cboFixingMethod, 3);

        mainLayout.Controls.Add(pnlCommonFields, 0, 5);

        // 6: Details / Memo Box with Auto-Sizing Preset Tag Buttons (DPI & Resolution Adaptive)
        var pnlDetails = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        pnlDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f)); // 0: Prompt Label
        pnlDetails.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 1: Tag Buttons (Auto height for word wrap)
        pnlDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f)); // 2: Issue Button
        pnlDetails.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 3: Details TextBox

        var lblDetPrompt = new Label { Text = "세부 작업 지시 / 가공 조건 / 점검 메모 (클릭 시 태그 자동 추가)", Font = new Font("맑은 고딕", 7.6f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill };

        _pnlTagPresets = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,  // Auto wrap onto 2nd row on narrow resolutions
            Margin = new Padding(0),
            Padding = new Padding(0, 2, 0, 2)
        };

        void AddTagBtn(string title, string tagFormat)
        {
            var btn = new Button
            {
                Text = title,
                BackColor = Palette.Bg800,
                ForeColor = Palette.TextSlate300,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(5, 1, 5, 1),
                Margin = new Padding(2, 2, 2, 2),
                Font = new Font("맑은 고딕", 7.2f, FontStyle.Bold)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) =>
            {
                if (!_txtDetails.Text.Contains(tagFormat))
                {
                    _txtDetails.AppendText((string.IsNullOrEmpty(_txtDetails.Text) ? "" : "\r\n") + tagFormat + ": ");
                    _txtDetails.Focus();
                }
            };
            _pnlTagPresets.Controls.Add(btn);
        }

        AddTagBtn("+공구", "[사용공구]");
        AddTagBtn("+조건", "[가공조건]");
        AddTagBtn("+원점", "[원점좌표]");
        AddTagBtn("+주의", "[주의사항]");
        AddTagBtn("+검사", "[검사항목]");

        var btnAddCustom = new Button
        {
            Text = "+직접추가",
            BackColor = Color.FromArgb(40, 14, 165, 233),
            ForeColor = Palette.AccentSky,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(5, 1, 5, 1),
            Margin = new Padding(2, 2, 2, 2),
            Font = new Font("맑은 고딕", 7.2f, FontStyle.Bold)
        };
        btnAddCustom.FlatAppearance.BorderSize = 0;
        btnAddCustom.Click += (s, e) =>
        {
            using var promptForm = new Form { Text = "커스텀 항목 추가", Size = new Size(320, 140), StartPosition = FormStartPosition.CenterParent, BackColor = Palette.Bg900, ForeColor = Color.White, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
            var txtTag = new TextBox { Text = "클램핑압력", Location = new Point(16, 20), Width = 270, BackColor = Palette.Bg850, ForeColor = Color.White };
            var btnOk = new Button { Text = "추가", DialogResult = DialogResult.OK, Location = new Point(206, 56), Width = 80, BackColor = Palette.AccentSky, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            promptForm.Controls.AddRange(new Control[] { txtTag, btnOk });
            promptForm.AcceptButton = btnOk;
            if (promptForm.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(txtTag.Text))
            {
                string tag = $"[{txtTag.Text.Trim()}]";
                _txtDetails.AppendText((string.IsNullOrEmpty(_txtDetails.Text) ? "" : "\r\n") + tag + ": ");
                AddTagBtn($"+{txtTag.Text.Trim()}", tag);
            }
        };
        _pnlTagPresets.Controls.Add(btnAddCustom);

        _btnViewIssueHistory.Dock = DockStyle.Fill;
        _txtDetails = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 8.8f)
        };
        _txtDetails.TextChanged += (s, e) => SaveChangesToNode();

        pnlDetails.Controls.Add(lblDetPrompt, 0, 0);
        pnlDetails.Controls.Add(_pnlTagPresets, 0, 1);
        pnlDetails.Controls.Add(_btnViewIssueHistory, 0, 2);
        pnlDetails.Controls.Add(_txtDetails, 0, 3);
        mainLayout.Controls.Add(pnlDetails, 0, 6);

        // 7: Next Process Input
        var pnlNext = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 2, 0, 2) };
        pnlNext.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95f));
        pnlNext.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlNext.Controls.Add(new Label { Text = "다음 예정 작업:", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _txtNextProcess = new TextBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("맑은 고딕", 8.8f) };
        _txtNextProcess.TextChanged += (s, e) => SaveChangesToNode();
        pnlNext.Controls.Add(_txtNextProcess, 1, 0);
        mainLayout.Controls.Add(pnlNext, 0, 7);

        // 8: Sub-worker Dedicated Section
        var pnlSubSection = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var pnlSubHeader = new Panel { Dock = DockStyle.Top, Height = 20 };
        var lblSubTitle = new Label { Text = "👥 서브인원 배정 및 작업 지시 (실제 서버 지시 연동)", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.Purple, Dock = DockStyle.Left, AutoSize = true };
        _lblSubWorkerTaskStatus = new Label { Text = "○ 미발송", Font = new Font("맑은 고딕", 7.5f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Right, AutoSize = true };
        pnlSubHeader.Controls.AddRange(new Control[] { lblSubTitle, _lblSubWorkerTaskStatus });

        var pnlSubBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        pnlSubBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        pnlSubBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var pnlSubNameRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        pnlSubNameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65f));
        pnlSubNameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlSubNameRow.Controls.Add(new Label { Text = "서브인원:", Font = new Font("맑은 고딕", 7.8f), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _txtSubWorker = new TextBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "이서브", Font = new Font("맑은 고딕", 8.8f) };
        _txtSubWorker.TextChanged += (s, e) => SaveChangesToNode();
        pnlSubNameRow.Controls.Add(_txtSubWorker, 1, 0);

        _txtSubInstruction = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 8.2f)
        };
        _txtSubInstruction.TextChanged += (s, e) => SaveChangesToNode();

        pnlSubBody.Controls.Add(pnlSubNameRow, 0, 0);
        pnlSubBody.Controls.Add(_txtSubInstruction, 0, 1);

        _btnSendSubTask = CreateFlatButton("⚡ 서브인원 작업 지시 전송 (서버 지시 발송)", Palette.Purple, Color.White);
        _btnSendSubTask.Height = 26;
        _btnSendSubTask.Dock = DockStyle.Bottom;
        _btnSendSubTask.Click += (s, e) => OnSendSubWorkerTaskClicked();

        pnlSubSection.Controls.Add(pnlSubBody);
        pnlSubSection.Controls.Add(_btnSendSubTask);
        pnlSubSection.Controls.Add(pnlSubHeader);
        mainLayout.Controls.Add(pnlSubSection, 0, 8);

        // 8: Action Buttons (Save / Duplicate / Disconnect / Delete)
        var row8 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 3, 0, 0) };
        row8.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // Save
        row8.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // Duplicate
        row8.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // Disconnect
        row8.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // Delete

        _btnSaveDetails = CreateFlatButton("💾 저장", Palette.Bg800, Color.White);
        _btnSaveDetails.Click += (s, e) =>
        {
            if (_currentNodes.Count == 0 && _currentNode == null)
            {
                MessageBox.Show("편집할 작업 노드를 캔버스에서 먼저 선택해 주세요.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SaveChangesToNode();
            string msg = _currentNodes.Count > 1
                ? $"{_currentNodes.Count}개 공정 노드의 설정(설비/공정 등)이 일괄 저장되었습니다."
                : $"'{_currentNode?.Title}' 노드의 세부 내용이 저장되었습니다.";
            MessageBox.Show(msg, "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        _btnDuplicate = CreateFlatButton("📋 공정 복제", Palette.Bg800, Color.White);
        _btnDuplicate.Click += (s, e) =>
        {
            if (_currentNodes.Count > 1)
            {
                foreach (var n in _currentNodes.ToList())
                    NodeDuplicateRequested?.Invoke(n);
                return;
            }
            if (_currentNode != null) NodeDuplicateRequested?.Invoke(_currentNode);
        };

        _btnDisconnect = CreateFlatButton("✂ 연결 끊기", Palette.Bg800, Palette.Amber);
        _btnDisconnect.Click += (s, e) =>
        {
            if (_currentNodes.Count > 1)
            {
                foreach (var n in _currentNodes.ToList())
                    NodeDisconnectRequested?.Invoke(n);
                return;
            }
            if (_currentNode != null) NodeDisconnectRequested?.Invoke(_currentNode);
        };

        _btnDelete = CreateFlatButton("🗑 노드 삭제", Color.FromArgb(127, 29, 29), Color.White);
        _btnDelete.Click += (s, e) =>
        {
            if (_currentNodes.Count > 1)
            {
                if (MessageBox.Show(
                    $"현재 다중 선택된 총 {_currentNodes.Count}개의 공정 노드를 모두 삭제하시겠습니까?",
                    "일괄 삭제 확인",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    var targets = _currentNodes.ToList();
                    BatchNodesDeleteRequested?.Invoke(targets);
                    SetEmptyState();
                }
                return;
            }

            if (_currentNode != null)
            {
                if (MessageBox.Show(
                    $"'{_currentNode.Title}' 노드를 삭제하시겠습니까?",
                    "노드 삭제 확인",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    NodeDeleteRequested?.Invoke(_currentNode);
                    SetEmptyState();
                }
            }
        };

        row8.Controls.Add(_btnSaveDetails, 0, 0);
        row8.Controls.Add(_btnDuplicate, 1, 0);
        row8.Controls.Add(_btnDisconnect, 2, 0);
        row8.Controls.Add(_btnDelete, 3, 0);
        mainLayout.Controls.Add(row8, 0, 9);

        Controls.Add(mainLayout);
        SetEmptyState();
    }

    private void PopulateTitlePresets(NodeCategory category, string? preserveText = null)
    {
        string currentText = preserveText ?? _cboTitle.Text;
        _cboTitle.Items.Clear();

        if (ClassicProcessPresets.TryGetValue(category, out var presets))
        {
            _cboTitle.Items.AddRange(presets);
        }
        else if (ClassicProcessPresets.TryGetValue(NodeCategory.Machining, out var machPresets))
        {
            _cboTitle.Items.AddRange(machPresets);
        }

        if (!string.IsNullOrEmpty(currentText))
        {
            _cboTitle.Text = currentText;
        }
        else if (_cboTitle.Items.Count > 0)
        {
            _cboTitle.SelectedIndex = 0;
        }
    }

    public void BindNode(WorkflowNode? node)
    {
        if (node == null)
            BindNodes(Array.Empty<WorkflowNode>());
        else
            BindNodes(new[] { node });
    }

    public void BindNodes(IReadOnlyList<WorkflowNode> nodes)
    {
        _currentNodes.Clear();
        _currentNodes.AddRange(nodes);
        _currentNode = nodes.FirstOrDefault();

        if (nodes.Count == 0)
        {
            SetEmptyState();
            return;
        }

        _isBinding = true;
        try
        {
            if (nodes.Count == 1)
            {
                var node = nodes[0];
                _lblHeader.Text = "⚙ 선택 노드 상세 정보 실시간 조작";
                _lblHeader.ForeColor = Palette.TextWhite;
                _lblOrderAndPart.Text = $"작지: {node.OrderNumber} | 부품: {node.PartName}";
                _lblCurrentStatus.Text = $"[{GetStatusText(node.Status)}]";
                _lblCurrentStatus.ForeColor = GetStatusColor(node.Status);

                _cboCategory.SelectedIndex = Math.Clamp((int)node.Category, 0, 5);
                PopulateTitlePresets(node.Category, node.Title);
                _cboTitle.Text = node.Title;

                int machIdx = _cboMachine.Items.IndexOf(node.MachineId);
                if (machIdx >= 0) _cboMachine.SelectedIndex = machIdx;
                else if (_cboMachine.Items.Count > 0) _cboMachine.SelectedIndex = 0;

                _txtAssignee.Text = node.Assignee;
                _txtDetails.Text = node.Details;
                _txtNextProcess.Text = node.NextProcess;
                // 후속 노드 바인딩 시, 작업자가 별도로 수동 지정하지 않았고 비어있으면 첫 번째 노드의 소재 정보를 자동 계승
                if (IsFirstNodePredicate?.Invoke(node) != true && !node.IsMaterialCustomized)
                {
                    var firstMat = GetFirstNodeMaterialProvider?.Invoke();
                    if (firstMat.HasValue && (!string.IsNullOrEmpty(firstMat.Value.Size) || !string.IsNullOrEmpty(firstMat.Value.Material) || !string.IsNullOrEmpty(firstMat.Value.Fixing)))
                    {
                        if (string.IsNullOrEmpty(node.MaterialSize)) node.MaterialSize = firstMat.Value.Size;
                        if (string.IsNullOrEmpty(node.MaterialType)) node.MaterialType = firstMat.Value.Material;
                        if (string.IsNullOrEmpty(node.FixingMethod) || node.FixingMethod == "바이스") 
                            node.FixingMethod = string.IsNullOrEmpty(firstMat.Value.Fixing) ? "바이스" : firstMat.Value.Fixing;
                    }
                }

                _cboStartTime.Text = node.StartTime;
                _cboEndTime.Text = node.EndTime;
                _txtMaterialSize.Text = node.MaterialSize;
                _cboMaterialType.Text = node.MaterialType;
                if (_cboFixingMethod != null) _cboFixingMethod.Text = string.IsNullOrEmpty(node.FixingMethod) ? "바이스 고정" : node.FixingMethod;
                _txtSubWorker.Text = string.IsNullOrEmpty(node.SubWorkerName) ? "이서브" : node.SubWorkerName;
                _txtSubInstruction.Text = node.SubWorkerInstruction;

                if (node.IsSubWorkerTaskSent)
                {
                    _lblSubWorkerTaskStatus.Text = "● 지시 전송됨";
                    _lblSubWorkerTaskStatus.ForeColor = Palette.Green;
                }
                else
                {
                    _lblSubWorkerTaskStatus.Text = "○ 미발송";
                    _lblSubWorkerTaskStatus.ForeColor = Palette.TextSlate400;
            if (_btnViewIssueHistory != null) _btnViewIssueHistory.Visible = false;
                }

                _btnDelete.Text = "🗑 노드 삭제";
                _btnSaveDetails.Text = "💾 저장";
                UpdateStatusButtonsHighlight(node.Status);

                _btnViewIssueHistory.Visible = node.HasIssueHistory;
                if (node.HasIssueHistory)
                {
                    _btnViewIssueHistory.Text = node.Status == NodeStatus.Issue
                        ? "🚨 이슈 작성/원인·조치 수정 (클릭)"
                        : "⚠️ 이슈 조치 이력 조회/수정 (클릭)";
                }
            }
            else
            {
                // Multi-node Batch Edit Mode!
                _lblHeader.Text = $"⚡ 선택 노드 일괄 편집 ({nodes.Count}개 선택됨)";
                _lblHeader.ForeColor = Palette.FocusBorder;
                _lblOrderAndPart.Text = $"[다중 선택 모드] {nodes.Count}개 공정 노드 일괄 설정 대상";

                // Status check: if all have same status, show that, else [다중 상태]
                bool sameStatus = nodes.All(n => n.Status == nodes[0].Status);
                if (sameStatus)
                {
                    _lblCurrentStatus.Text = $"[{GetStatusText(nodes[0].Status)}]";
                    _lblCurrentStatus.ForeColor = GetStatusColor(nodes[0].Status);
                    UpdateStatusButtonsHighlight(nodes[0].Status);
                }
                else
                {
                    _lblCurrentStatus.Text = "[다중 상태]";
                    _lblCurrentStatus.ForeColor = Palette.Amber;
                    UpdateStatusButtonsHighlight((NodeStatus)(-1)); // clear all
                }

                // Category check
                bool sameCategory = nodes.All(n => n.Category == nodes[0].Category);
                if (sameCategory)
                {
                    _cboCategory.SelectedIndex = Math.Clamp((int)nodes[0].Category, 0, 5);
                    PopulateTitlePresets(nodes[0].Category, "(선택 노드 일괄 적용)");
                }
                else
                {
                    _cboCategory.SelectedIndex = 1; // 가공
                    PopulateTitlePresets(NodeCategory.Machining, "(선택 노드 일괄 적용)");
                }

                // Machine check: if all have same machine, select that machine!
                bool sameMachine = nodes.All(n => string.Equals(n.MachineId, nodes[0].MachineId, StringComparison.OrdinalIgnoreCase));
                if (sameMachine && !string.IsNullOrEmpty(nodes[0].MachineId))
                {
                    int machIdx = _cboMachine.Items.IndexOf(nodes[0].MachineId);
                    if (machIdx >= 0) _cboMachine.SelectedIndex = machIdx;
                    else _cboMachine.SelectedIndex = 0;
                }
                else
                {
                    int machIdx = _cboMachine.Items.IndexOf(nodes[0].MachineId);
                    if (machIdx >= 0) _cboMachine.SelectedIndex = machIdx;
                    else _cboMachine.SelectedIndex = 0;
                }

                _txtAssignee.Text = nodes.All(n => n.Assignee == nodes[0].Assignee) ? nodes[0].Assignee : "";
                _txtDetails.Text = "";
                _txtNextProcess.Text = "";
            if (_cboStartTime != null) _cboStartTime.Text = "";
            if (_cboEndTime != null) _cboEndTime.Text = "";
            if (_txtMaterialSize != null) _txtMaterialSize.Text = "";
            if (_cboMaterialType != null) _cboMaterialType.Text = "";
        if (_cboFixingMethod != null) _cboFixingMethod.Text = "";
                _txtSubWorker.Text = "";
                _txtSubInstruction.Text = "";
                _lblSubWorkerTaskStatus.Text = $"○ {nodes.Count}개 노드";
                _lblSubWorkerTaskStatus.ForeColor = Palette.TextSlate400;

                _btnDelete.Text = $"🗑 일괄 삭제 ({nodes.Count})";
                _btnSaveDetails.Text = "💾 일괄 저장";
            }
        }
        finally
        {
            _isBinding = false;
        }
    }

    private void UpdateStatusButtonsHighlight(NodeStatus status)
    {
        void SetHighlight(Button btn, bool active)
        {
            if (active)
            {
                btn.FlatAppearance.BorderColor = Palette.FocusBorder;
                btn.FlatAppearance.BorderSize = 2;
            }
            else
            {
                btn.FlatAppearance.BorderSize = 0;
            }
        }

        SetHighlight(_btnStatusReady, status == NodeStatus.Ready);
        SetHighlight(_btnStatusRunning, status == NodeStatus.Running);
        SetHighlight(_btnStatusPendingCheck, status == NodeStatus.PendingCheck);
        SetHighlight(_btnStatusComplete, status == NodeStatus.Completed);
        SetHighlight(_btnStatusIssue, status == NodeStatus.Issue);
    }

    private void SaveChangesToNode()
    {
        if (_isBinding) return;

        if (_currentNodes.Count > 1)
        {
            string selectedMach = _cboMachine.SelectedItem?.ToString() ?? "R01";
            var selectedCat = (NodeCategory)Math.Clamp(_cboCategory.SelectedIndex, 0, 5);
            string assignee = _txtAssignee.Text.Trim();
            string details = _txtDetails.Text;
            string sTime = _cboStartTime?.Text.Trim() ?? "";
            string eTime = _cboEndTime?.Text.Trim() ?? "";
            string batchMSize = _txtMaterialSize?.Text.Trim() ?? "";
            string batchMType = _cboMaterialType?.Text.Trim() ?? "";
            string batchFixMethod = _cboFixingMethod?.Text.Trim() ?? "";

            foreach (var node in _currentNodes)
            {
                node.MachineId = selectedMach;
                if (!string.IsNullOrWhiteSpace(assignee)) node.Assignee = assignee;
                if (!string.IsNullOrWhiteSpace(details)) node.Details = details;
                if (!string.IsNullOrWhiteSpace(sTime)) node.StartTime = sTime;
                if (!string.IsNullOrWhiteSpace(eTime)) node.EndTime = eTime;
                if (!string.IsNullOrWhiteSpace(batchMSize)) node.MaterialSize = batchMSize;
                if (!string.IsNullOrWhiteSpace(batchMType)) node.MaterialType = batchMType;
                if (!string.IsNullOrWhiteSpace(batchFixMethod)) node.FixingMethod = batchFixMethod;
            }

            NodesUpdated?.Invoke(_currentNodes);
            return;
        }

        if (_currentNode == null) return;

        _currentNode.Title = _cboTitle.Text.Trim();
        _currentNode.Category = (NodeCategory)_cboCategory.SelectedIndex;
        _currentNode.MachineId = _cboMachine.SelectedItem?.ToString() ?? "R01";
        _currentNode.Assignee = _txtAssignee.Text.Trim();
        _currentNode.Details = _txtDetails.Text;
        _currentNode.NextProcess = _txtNextProcess.Text.Trim();
        _currentNode.SubWorkerName = _txtSubWorker.Text.Trim();
        _currentNode.SubWorkerInstruction = _txtSubInstruction.Text.Trim();
        if (_cboStartTime != null) _currentNode.StartTime = _cboStartTime.Text.Trim();
        if (_cboEndTime != null) _currentNode.EndTime = _cboEndTime.Text.Trim();

        string mSize = _txtMaterialSize?.Text.Trim() ?? "";
        string mType = _cboMaterialType?.Text.Trim() ?? "";
        string fixMethod = _cboFixingMethod?.Text.Trim() ?? "";

        bool isFirst = IsFirstNodePredicate?.Invoke(_currentNode) == true;
        if (isFirst)
        {
            bool matChanged = _currentNode.MaterialSize != mSize || _currentNode.MaterialType != mType || _currentNode.FixingMethod != fixMethod;
            _currentNode.MaterialSize = mSize;
            _currentNode.MaterialType = mType;
            _currentNode.FixingMethod = fixMethod;

            if (matChanged && (!string.IsNullOrEmpty(mSize) || !string.IsNullOrEmpty(mType) || !string.IsNullOrEmpty(fixMethod)))
            {
                FirstNodeMaterialChanged?.Invoke(mSize, mType, fixMethod);
            }
        }
        else
        {
            var firstMat = GetFirstNodeMaterialProvider?.Invoke();
            if (firstMat.HasValue && (!string.IsNullOrEmpty(firstMat.Value.Size) || !string.IsNullOrEmpty(firstMat.Value.Material) || !string.IsNullOrEmpty(firstMat.Value.Fixing)))
            {
                if (mSize != firstMat.Value.Size || mType != firstMat.Value.Material || fixMethod != firstMat.Value.Fixing)
                {
                    _currentNode.IsMaterialCustomized = true;
                }
            }
            _currentNode.MaterialSize = mSize;
            _currentNode.MaterialType = mType;
            _currentNode.FixingMethod = fixMethod;
        }

        NodeUpdated?.Invoke(_currentNode);
    }

    private void TriggerStatusChange(NodeStatus newStatus)
    {
        if (_currentNodes.Count == 0 && _currentNode == null)
        {
            MessageBox.Show("편집할 작업 노드를 캔버스에서 먼저 선택해 주세요.", "노드 미선택", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string nowTimeStr = DateTime.Now.ToString("HH:mm");

        if (_currentNodes.Count > 1)
        {
            SaveChangesToNode();
            foreach (var node in _currentNodes)
            {
                node.Status = newStatus;
                if (newStatus == NodeStatus.Running)
                {
                    node.StartedAt = DateTime.Now;
                    if (string.IsNullOrWhiteSpace(node.StartTime) || node == _currentNode)
                        node.StartTime = nowTimeStr;
                }
                else if (newStatus == NodeStatus.Completed)
                {
                    node.CompletedAt = DateTime.Now;
                    node.EndTime = nowTimeStr;
                }
            }

            if (_currentNode != null)
            {
                if (newStatus == NodeStatus.Running && _cboStartTime != null) _cboStartTime.Text = nowTimeStr;
                if (newStatus == NodeStatus.Completed && _cboEndTime != null) _cboEndTime.Text = nowTimeStr;
            }

            _lblCurrentStatus.Text = $"[일괄 {GetStatusText(newStatus)}]";
            _lblCurrentStatus.ForeColor = GetStatusColor(newStatus);
            UpdateStatusButtonsHighlight(newStatus);

            BatchStatusChangeRequested?.Invoke(_currentNodes, newStatus);
            NodesUpdated?.Invoke(_currentNodes);
            return;
        }

        if (_currentNode != null)
        {
            SaveChangesToNode();
            _currentNode.Status = newStatus;
            if (newStatus == NodeStatus.Running)
            {
                _currentNode.StartedAt = DateTime.Now;
                _currentNode.StartTime = nowTimeStr;
                if (_cboStartTime != null) _cboStartTime.Text = nowTimeStr;
            }
            else if (newStatus == NodeStatus.Completed)
            {
                _currentNode.CompletedAt = DateTime.Now;
                _currentNode.EndTime = nowTimeStr;
                if (_cboEndTime != null) _cboEndTime.Text = nowTimeStr;
            }

            _lblCurrentStatus.Text = $"[{GetStatusText(newStatus)}]";
            _lblCurrentStatus.ForeColor = GetStatusColor(newStatus);
            UpdateStatusButtonsHighlight(newStatus);

            NodeUpdated?.Invoke(_currentNode);
            StatusChangeRequested?.Invoke(_currentNode, newStatus);
        }
    }

    private void OnSendSubWorkerTaskClicked()
    {
        if (_currentNode == null)
        {
            MessageBox.Show("작업 지시를 전송할 노드를 캔버스에서 먼저 선택해 주세요.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SaveChangesToNode();

        string subWorker = _txtSubWorker.Text.Trim();
        string instruction = _txtSubInstruction.Text.Trim();

        if (string.IsNullOrEmpty(subWorker))
        {
            MessageBox.Show("작업을 지시할 서브인원의 이름을 입력해주세요.", "서브인원 미지정", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtSubWorker.Focus();
            return;
        }

        if (string.IsNullOrEmpty(instruction))
        {
            instruction = $"{_currentNode.Title} 준비 및 보조 작업 지원 요망";
            _txtSubInstruction.Text = instruction;
        }

        SubWorkerTaskSendRequested?.Invoke(_currentNode, subWorker, instruction);
    }

    public void MarkSubWorkerTaskSent()
    {
        if (_currentNode != null)
        {
            _currentNode.IsSubWorkerTaskSent = true;
            _lblSubWorkerTaskStatus.Text = "● 지시 전송됨";
            _lblSubWorkerTaskStatus.ForeColor = Palette.Green;
        }
    }

    public void SetEmptyState()
    {
        _currentNode = null;
        _currentNodes.Clear();
        _isBinding = true;
        try
        {
            if (_lblHeader != null)
            {
                _lblHeader.Text = "⚙ 선택 노드 상세 정보 실시간 조작";
                _lblHeader.ForeColor = Palette.TextWhite;
            }
            if (_btnDelete != null) _btnDelete.Text = "🗑 노드 삭제";
            if (_btnSaveDetails != null) _btnSaveDetails.Text = "💾 저장";
            _lblOrderAndPart.Text = "작지: [선택 없음] | 부품: [선택 없음]";
            _lblCurrentStatus.Text = "[대기]";
            _lblCurrentStatus.ForeColor = Palette.TextSlate400;

            _cboTitle.Text = "";
            _cboCategory.SelectedIndex = 1;
            PopulateTitlePresets(NodeCategory.Machining, "");

            _cboMachine.SelectedIndex = 0;
            _txtAssignee.Text = "";
            _txtDetails.Text = "";
            _txtNextProcess.Text = "";
            _txtSubWorker.Text = "";
            _txtSubInstruction.Text = "";
            _lblSubWorkerTaskStatus.Text = "○ 미발송";
            _lblSubWorkerTaskStatus.ForeColor = Palette.TextSlate400;
            UpdateStatusButtonsHighlight(NodeStatus.Ready);
        }
        finally
        {
            _isBinding = false;
        }
    }

    private Button CreateFlatButton(string text, Color bg, Color fg)
    {
        var btn = new Button
        {
            Text = text,
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            Margin = new Padding(2),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    public static string GetStatusText(NodeStatus s) => s switch
    {
        NodeStatus.Ready => "대기",
        NodeStatus.Running => "진행중",
        NodeStatus.PendingCheck => "측정",
        NodeStatus.Completed => "완료",
        NodeStatus.Issue => "이슈",
        _ => "대기"
    };

    private Color GetStatusColor(NodeStatus s) => s switch
    {
        NodeStatus.Ready => Palette.TextSlate400,
        NodeStatus.Running => Palette.Green,
        NodeStatus.PendingCheck => Palette.Amber,
        NodeStatus.Completed => Palette.AccentSky,
        NodeStatus.Issue => Palette.Rose,
        _ => Palette.TextSlate400
    };
}
