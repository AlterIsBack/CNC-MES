using System;
using System.Drawing;
using System.Windows.Forms;
using CNCMes.Client.Workflow.Models;
using Palette = CNCMes.Client.Workflow.WorkflowMainForm.Palette;

namespace CNCMes.Client.Workflow;

/// <summary>
/// 공정 노드 더블클릭 시 표시되는 전용 정보 조회 및 수정 팝업 다이얼로그 (정보창)
/// </summary>
public class NodeEditDialog : Form
{
    private readonly WorkflowNode _node;
    private readonly string _currentWorker;
    private readonly NodeStatus _initialStatus;

    // Controls
    private ComboBox _cboCategory = null!;
    private ComboBox _cboTitle = null!;
    private ComboBox _cboMachine = null!;
    private TextBox _txtAssignee = null!;

    private Button _btnStatusReady = null!;
    private Button _btnStatusRunning = null!;
    private Button _btnStatusPendingCheck = null!;
    private Button _btnStatusComplete = null!;
    private Button _btnStatusIssue = null!;
    private Label _lblStatusText = null!;
    private NodeStatus _selectedStatus;

    private ComboBox _cboStartTime = null!;
    private ComboBox _cboEndTime = null!;
    private TextBox _txtMaterialSize = null!;
    private ComboBox _cboMaterialType = null!;
    private ComboBox _cboFixingMethod = null!;

    private TextBox _txtDetails = null!;
    private TextBox _txtNextProcess = null!;
    private TextBox _txtSubWorker = null!;
    private TextBox _txtSubInstruction = null!;

    public bool StatusChanged => _selectedStatus != _initialStatus;

    public NodeEditDialog(WorkflowNode node, string currentWorker = "작업자")
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _currentWorker = string.IsNullOrWhiteSpace(currentWorker) ? "작업자" : currentWorker;
        _initialStatus = node.Status;
        _selectedStatus = node.Status;

        InitializeComponents();
        LoadNodeData();
    }

    private void InitializeComponents()
    {
        Text = $"⚙ 공정 노드 상세 정보 수정 - [{_node.Title}] (설비: {_node.MachineId})";
        Size = new Size(680, 820);
        MinimumSize = new Size(620, 740);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Palette.Bg950;
        ForeColor = Palette.TextWhite;
        Font = new Font("맑은 고딕", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(12),
            Margin = new Padding(0)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));  // 0: Header Info Banner
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));  // 1: Title, Category, Machine, Assignee
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));  // 2: Status Quick Buttons
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 95f));  // 3: Fixed 5 Fields (Start, End, Size, Material, Fixing)
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 4: Details & Tag Presets
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));  // 5: Next Process & Sub-worker
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));  // 6: Bottom Buttons (Save / Cancel)

        // -------------------------------------------------------------
        // 0. Header Info Banner
        // -------------------------------------------------------------
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg900,
            Padding = new Padding(12, 6, 12, 6)
        };
        var lblBannerOrder = new Label
        {
            Text = $"작지: {_node.OrderNumber}  |  부품: {_node.PartName}",
            ForeColor = Palette.AccentSky,
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold),
            Dock = DockStyle.Left,
            AutoSize = true
        };
        _lblStatusText = new Label
        {
            Text = $"상태: [{GetStatusName(_node.Status)}]",
            ForeColor = GetStatusColor(_node.Status),
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold),
            Dock = DockStyle.Right,
            AutoSize = true
        };
        pnlHeader.Controls.Add(lblBannerOrder);
        pnlHeader.Controls.Add(_lblStatusText);
        mainLayout.Controls.Add(pnlHeader, 0, 0);

        // -------------------------------------------------------------
        // 1. Process Title, Category, Machine, Assignee (2x2 Grid)
        // -------------------------------------------------------------
        var gridBasic = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0, 2, 0, 2)
        };
        gridBasic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f)); // Title
        gridBasic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f)); // Category
        gridBasic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f)); // Machine
        gridBasic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23f)); // Assignee

        gridBasic.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        gridBasic.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        gridBasic.Controls.Add(new Label { Text = "공정명 (타이틀)", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 0, 0);
        gridBasic.Controls.Add(new Label { Text = "공정분류", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 1, 0);
        gridBasic.Controls.Add(new Label { Text = "담당 설비 (장비)", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 2, 0);
        gridBasic.Controls.Add(new Label { Text = "담당 작업자", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 3, 0);

        _cboTitle = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 8.8f), DropDownStyle = ComboBoxStyle.DropDown };
        _cboCategory = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 8.8f), DropDownStyle = ComboBoxStyle.DropDownList };
        _cboCategory.Items.AddRange(new object[] { "준비", "가공", "검사", "서브인원", "후공정", "이슈/통로" });
        _cboCategory.SelectedIndexChanged += (s, e) => PopulateTitlePresets();

        _cboMachine = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 8.8f), DropDownStyle = ComboBoxStyle.DropDownList };
        _cboMachine.Items.AddRange(new object[] { "R01", "R02", "R03", "R04", "R05", "K01", "K02", "세척기", "측정실", "포장팀" });

        _txtAssignee = new TextBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 8.8f), BorderStyle = BorderStyle.FixedSingle };

        gridBasic.Controls.Add(_cboTitle, 0, 1);
        gridBasic.Controls.Add(_cboCategory, 1, 1);
        gridBasic.Controls.Add(_cboMachine, 2, 1);
        gridBasic.Controls.Add(_txtAssignee, 3, 1);
        mainLayout.Controls.Add(gridBasic, 0, 1);

        // -------------------------------------------------------------
        // 2. Status Quick Buttons (대기, 진행중, 측정, 완료, 이슈)
        // -------------------------------------------------------------
        var rowStatus = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 4)
        };
        for (int i = 0; i < 5; i++) rowStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

        _btnStatusReady = CreateButton("대기", Color.FromArgb(42, 59, 76), Color.White);
        _btnStatusReady.Click += (s, e) => SetSelectedStatus(NodeStatus.Ready);

        _btnStatusRunning = CreateButton("진행중", Color.FromArgb(5, 150, 105), Color.White);
        _btnStatusRunning.Click += (s, e) =>
        {
            SetSelectedStatus(NodeStatus.Running);
            // 현재시간 자동 시작시간 입력!
            string nowTime = DateTime.Now.ToString("HH:mm");
            _cboStartTime.Text = nowTime;
        };

        _btnStatusPendingCheck = CreateButton("측정", Color.FromArgb(217, 119, 6), Color.White);
        _btnStatusPendingCheck.Click += (s, e) => SetSelectedStatus(NodeStatus.PendingCheck);

        _btnStatusComplete = CreateButton("완료", Color.FromArgb(2, 132, 199), Color.White);
        _btnStatusComplete.Click += (s, e) =>
        {
            SetSelectedStatus(NodeStatus.Completed);
            // 현재시간 자동 종료시간 입력!
            string nowTime = DateTime.Now.ToString("HH:mm");
            _cboEndTime.Text = nowTime;
        };

        _btnStatusIssue = CreateButton("이슈", Color.FromArgb(220, 38, 38), Color.White);
        _btnStatusIssue.Click += (s, e) => SetSelectedStatus(NodeStatus.Issue);

        rowStatus.Controls.Add(_btnStatusReady, 0, 0);
        rowStatus.Controls.Add(_btnStatusRunning, 1, 0);
        rowStatus.Controls.Add(_btnStatusPendingCheck, 2, 0);
        rowStatus.Controls.Add(_btnStatusComplete, 3, 0);
        rowStatus.Controls.Add(_btnStatusIssue, 4, 0);
        mainLayout.Controls.Add(rowStatus, 0, 2);

        // -------------------------------------------------------------
        // 3. Fixed 5 Fields (시작, 종료, Size, 재질, 고정방식)
        // -------------------------------------------------------------
        var pnlFields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            Margin = new Padding(0, 2, 0, 2)
        };
        pnlFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65f)); // Label 1
        pnlFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));  // Input 1
        pnlFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60f)); // Label 2
        pnlFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));  // Input 2
        pnlFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        pnlFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        pnlFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

        var timePresets = new object[] { "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "13:00", "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30" };
        var matPresets = new object[] { "PEEK 5T", "AL6061-T6", "SUS304", "베이크라이트", "MC나일론", "초경합금", "황동(C3604)", "SKD11" };
        var fixPresets = new object[] { "바이스 고정", "진공 흡착 (진공 플레이트)", "클램프 고정 (사이드/토글)", "마그네틱 척 고정", "전용 지그 블록", "스크류 볼팅 고정", "양면 테이프 고정" };

        var lblStart = new Label { Text = "시작시간:", ForeColor = Palette.AccentSky, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 8f, FontStyle.Bold) };
        _cboStartTime = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("Consolas", 9f), DropDownStyle = ComboBoxStyle.DropDown };
        _cboStartTime.Items.AddRange(timePresets);

        var lblEnd = new Label { Text = "종료시간:", ForeColor = Palette.AccentSky, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 8f, FontStyle.Bold) };
        _cboEndTime = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("Consolas", 9f), DropDownStyle = ComboBoxStyle.DropDown };
        _cboEndTime.Items.AddRange(timePresets);

        var lblSize = new Label { Text = "Size:", ForeColor = Palette.Amber, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 8f, FontStyle.Bold) };
        _txtMaterialSize = new TextBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 8.8f), BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "예: 150x120x5T" };

        var lblMat = new Label { Text = "재질:", ForeColor = Palette.Amber, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 8f, FontStyle.Bold) };
        _cboMaterialType = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 8.8f), DropDownStyle = ComboBoxStyle.DropDown };
        _cboMaterialType.Items.AddRange(matPresets);

        var lblFix = new Label { Text = "고정방식:", ForeColor = Color.FromArgb(168, 85, 247), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 8f, FontStyle.Bold) };
        _cboFixingMethod = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 8.8f), DropDownStyle = ComboBoxStyle.DropDown };
        _cboFixingMethod.Items.AddRange(fixPresets);

        pnlFields.Controls.Add(lblStart, 0, 0);
        pnlFields.Controls.Add(_cboStartTime, 1, 0);
        pnlFields.Controls.Add(lblEnd, 2, 0);
        pnlFields.Controls.Add(_cboEndTime, 3, 0);

        pnlFields.Controls.Add(lblSize, 0, 1);
        pnlFields.Controls.Add(_txtMaterialSize, 1, 1);
        pnlFields.Controls.Add(lblMat, 2, 1);
        pnlFields.Controls.Add(_cboMaterialType, 3, 1);

        pnlFields.Controls.Add(lblFix, 0, 2);
        pnlFields.Controls.Add(_cboFixingMethod, 1, 2);
        pnlFields.SetColumnSpan(_cboFixingMethod, 3);

        mainLayout.Controls.Add(pnlFields, 0, 3);

        // -------------------------------------------------------------
        // 4. Details / Memo Box with Auto-Sizing Tag Buttons
        // -------------------------------------------------------------
        var pnlDetails = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0)
        };
        pnlDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlDetails.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pnlDetails.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        pnlDetails.Controls.Add(new Label { Text = "세부 작업 지시 / 가공 조건 / 점검 메모 (태그 클릭 시 자동 삽입)", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 0, 0);

        var pnlTagPresets = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
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
                Padding = new Padding(6, 2, 6, 2),
                Margin = new Padding(2, 2, 2, 2),
                Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold)
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
            pnlTagPresets.Controls.Add(btn);
        }

        AddTagBtn("+공구", "[공구]");
        AddTagBtn("+가공조건", "[조건]");
        AddTagBtn("+공작물원점", "[원점]");
        AddTagBtn("+주의사항", "[주의]");
        AddTagBtn("+검사기준", "[검사]");
        AddTagBtn("+직접추가", "[사용자지정]");

        pnlDetails.Controls.Add(pnlTagPresets, 0, 1);

        _txtDetails = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.8f)
        };
        pnlDetails.Controls.Add(_txtDetails, 0, 2);
        mainLayout.Controls.Add(pnlDetails, 0, 4);

        // -------------------------------------------------------------
        // 5. Next Process & Sub-worker Section
        // -------------------------------------------------------------
        var pnlBottomMeta = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 2, 0, 2)
        };
        pnlBottomMeta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        pnlBottomMeta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        pnlBottomMeta.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlBottomMeta.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        pnlBottomMeta.Controls.Add(new Label { Text = "다음 예정 작업:", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 0, 0);
        pnlBottomMeta.Controls.Add(new Label { Text = "👥 서브인원 배정:", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.Purple, Dock = DockStyle.Fill }, 1, 0);

        _txtNextProcess = new TextBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("맑은 고딕", 8.8f) };
        pnlBottomMeta.Controls.Add(_txtNextProcess, 0, 1);

        var pnlSub = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        pnlSub.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
        pnlSub.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _txtSubWorker = new TextBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("맑은 고딕", 8.8f), PlaceholderText = "이름" };
        _txtSubInstruction = new TextBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("맑은 고딕", 8.8f), PlaceholderText = "지시내용" };
        pnlSub.Controls.Add(_txtSubWorker, 0, 0);
        pnlSub.Controls.Add(_txtSubInstruction, 1, 0);
        pnlBottomMeta.Controls.Add(pnlSub, 1, 1);

        mainLayout.Controls.Add(pnlBottomMeta, 0, 5);

        // -------------------------------------------------------------
        // 6. Action Buttons Footer (Save & Close)
        // -------------------------------------------------------------
        var pnlActions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 0)
        };
        pnlActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        pnlActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));

        var btnSave = CreateButton("💾 저장 및 캔버스 적용", Color.FromArgb(5, 150, 105), Color.White);
        btnSave.Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold);
        btnSave.Click += (s, e) => SaveAndApply();

        var btnCancel = CreateButton("✕ 닫기 (취소)", Palette.Bg800, Palette.TextSlate300);
        btnCancel.Font = new Font("맑은 고딕", 9f);
        btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

        pnlActions.Controls.Add(btnSave, 0, 0);
        pnlActions.Controls.Add(btnCancel, 1, 0);
        mainLayout.Controls.Add(pnlActions, 0, 6);

        Controls.Add(mainLayout);
    }

    private void PopulateTitlePresets()
    {
        string currentTitle = _cboTitle.Text;
        _cboTitle.Items.Clear();

        var cat = (NodeCategory)Math.Clamp(_cboCategory.SelectedIndex, 0, 5);
        var presets = cat switch
        {
            NodeCategory.Preparation => new string[]
            {
                "준비작업 (도면 & NC 확인)",
                "소재작업 (치수 & 평면도 검수)",
                "베이스판 교체 & 원점 세팅",
                "지그 및 진공 플레이트 장착",
                "소재 장착 및 Z축 공구 세팅"
            },
            NodeCategory.Machining => new string[]
            {
                "1차 전면 황삭 가공 진행",
                "1차 전면 중삭/정삭 가공 진행",
                "2차 배면(반전) 황삭 가공 진행",
                "2차 배면(반전) 정삭 가공 진행",
                "마이크로 홀 드릴링 정밀 가공",
                "포켓 및 내경 윤곽 밀링 가공",
                "외곽 윤곽 및 측면 정밀 가공",
                "챔퍼(면취) 및 버(Burr) 제거 가공",
                "탭(Tap) / 나사산 가공 진행",
                "고속 정밀 피니싱 가공 진행",
                "치수 보정 후 재가공(수정가공) 진행"
            },
            NodeCategory.Inspection => new string[]
            {
                "1차 중간측정 (CMM 정밀측정)",
                "최종 전수 3차원 측정 검사",
                "현미경 외관 및 미세 크랙 검사",
                "단차 및 평면도 정밀 검사"
            },
            NodeCategory.Collaboration => new string[]
            {
                "서브인원 버(Burr) 제거 작업",
                "서브인원 에어 세척 및 블로우",
                "부품 가조립 및 홀 핀 점검"
            },
            NodeCategory.PostProcess => new string[]
            {
                "초음파 정밀 세척 및 건조",
                "레이저 마킹 및 시리얼 각인",
                "방청 도포 및 진공 포장 출하"
            },
            _ => new string[] { "일반 공정 작업 진행" }
        };

        _cboTitle.Items.AddRange(presets);
        _cboTitle.Text = string.IsNullOrEmpty(currentTitle) ? presets[0] : currentTitle;
    }

    private void LoadNodeData()
    {
        _cboCategory.SelectedIndex = Math.Clamp((int)_node.Category, 0, 5);
        PopulateTitlePresets();
        _cboTitle.Text = _node.Title;

        int machIdx = _cboMachine.Items.IndexOf(_node.MachineId);
        if (machIdx >= 0) _cboMachine.SelectedIndex = machIdx;
        else if (_cboMachine.Items.Count > 0) _cboMachine.SelectedIndex = 0;

        _txtAssignee.Text = string.IsNullOrEmpty(_node.Assignee) ? _currentWorker : _node.Assignee;
        _cboStartTime.Text = _node.StartTime;
        _cboEndTime.Text = _node.EndTime;
        _txtMaterialSize.Text = _node.MaterialSize;
        _cboMaterialType.Text = _node.MaterialType;
        _cboFixingMethod.Text = string.IsNullOrEmpty(_node.FixingMethod) ? "바이스 고정" : _node.FixingMethod;
        _txtDetails.Text = _node.Details;
        _txtNextProcess.Text = _node.NextProcess;
        _txtSubWorker.Text = string.IsNullOrEmpty(_node.SubWorkerName) ? "이서브" : _node.SubWorkerName;
        _txtSubInstruction.Text = _node.SubWorkerInstruction;

        SetSelectedStatus(_node.Status);
    }

    private void SetSelectedStatus(NodeStatus status)
    {
        _selectedStatus = status;
        _lblStatusText.Text = $"상태: [{GetStatusName(status)}]";
        _lblStatusText.ForeColor = GetStatusColor(status);

        void Highlight(Button btn, bool active)
        {
            if (active)
            {
                btn.FlatAppearance.BorderColor = Palette.AccentSky;
                btn.FlatAppearance.BorderSize = 2;
            }
            else
            {
                btn.FlatAppearance.BorderSize = 0;
            }
        }

        Highlight(_btnStatusReady, status == NodeStatus.Ready);
        Highlight(_btnStatusRunning, status == NodeStatus.Running);
        Highlight(_btnStatusPendingCheck, status == NodeStatus.PendingCheck);
        Highlight(_btnStatusComplete, status == NodeStatus.Completed);
        Highlight(_btnStatusIssue, status == NodeStatus.Issue);
    }

    private void SaveAndApply()
    {
        _node.Title = _cboTitle.Text.Trim();
        _node.Category = (NodeCategory)Math.Clamp(_cboCategory.SelectedIndex, 0, 5);
        _node.MachineId = _cboMachine.SelectedItem?.ToString() ?? "R01";
        _node.Assignee = _txtAssignee.Text.Trim();

        _node.StartTime = _cboStartTime.Text.Trim();
        _node.EndTime = _cboEndTime.Text.Trim();
        _node.MaterialSize = _txtMaterialSize.Text.Trim();
        _node.MaterialType = _cboMaterialType.Text.Trim();
        _node.FixingMethod = _cboFixingMethod.Text.Trim();

        _node.Details = _txtDetails.Text;
        _node.NextProcess = _txtNextProcess.Text.Trim();
        _node.SubWorkerName = _txtSubWorker.Text.Trim();
        _node.SubWorkerInstruction = _txtSubInstruction.Text.Trim();

        _node.Status = _selectedStatus;
        if (_selectedStatus == NodeStatus.Running)
        {
            _node.StartedAt ??= DateTime.Now;
            if (string.IsNullOrWhiteSpace(_node.StartTime))
                _node.StartTime = DateTime.Now.ToString("HH:mm");
        }
        else if (_selectedStatus == NodeStatus.Completed)
        {
            _node.CompletedAt ??= DateTime.Now;
            if (string.IsNullOrWhiteSpace(_node.EndTime))
                _node.EndTime = DateTime.Now.ToString("HH:mm");
        }

        DialogResult = DialogResult.OK;
    }

    private static Button CreateButton(string text, Color bg, Color fg)
    {
        var btn = new Button
        {
            Text = text,
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Fill,
            Cursor = Cursors.Hand,
            Margin = new Padding(2)
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private static string GetStatusName(NodeStatus st) => st switch
    {
        NodeStatus.Ready => "대기",
        NodeStatus.Running => "진행중",
        NodeStatus.PendingCheck => "측정",
        NodeStatus.Completed => "완료",
        NodeStatus.Issue => "이슈",
        _ => "대기"
    };

    private static Color GetStatusColor(NodeStatus st) => st switch
    {
        NodeStatus.Ready => Color.FromArgb(148, 163, 184),
        NodeStatus.Running => Palette.Green,
        NodeStatus.PendingCheck => Palette.Amber,
        NodeStatus.Completed => Palette.AccentSky,
        NodeStatus.Issue => Palette.Rose,
        _ => Palette.TextWhite
    };
}
