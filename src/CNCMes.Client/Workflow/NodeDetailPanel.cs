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
    }

    private WorkflowNode? _currentNode;
    private bool _isBinding = false;

    private TextBox _txtTitle = null!;
    private ComboBox _cboCategory = null!;
    private ComboBox _cboMachine = null!;
    private TextBox _txtAssignee = null!;
    private Label _lblOrderAndPart = null!;
    private TextBox _txtDetails = null!;
    private TextBox _txtNextProcess = null!;
    private Label _lblCurrentStatus = null!;

    // Sub-Worker Dedicated UI
    private TextBox _txtSubWorker = null!;
    private TextBox _txtSubInstruction = null!;
    private Button _btnSendSubWorkerTask = null!;
    private Label _lblSubWorkerTaskStatus = null!;

    private Button _btnStatusReady = null!;
    private Button _btnStatusRunning = null!;
    private Button _btnStatusComplete = null!;
    private Button _btnStatusIssue = null!;
    private Button _btnDeleteNode = null!;
    private Button _btnDuplicateNode = null!;
    private Button _btnSaveMemory = null!;

    public event Action<WorkflowNode, NodeStatus>? StatusChangeRequested;
    public event Action<WorkflowNode>? NodeUpdated;
    public event Action<WorkflowNode>? NodeDeleteRequested;
    public event Action<WorkflowNode>? NodeDuplicateRequested;
    public event Action<WorkflowNode, string, string>? SubWorkerTaskSendRequested;

    public NodeDetailPanel()
    {
        DoubleBuffered = true;
        BackColor = Palette.Bg900;
        Font = new Font("맑은 고딕", 9f);
        ForeColor = Palette.TextWhite;
        Padding = new Padding(10);

        InitializeComponents();
        SetEmptyState();
    }

    private void InitializeComponents()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 10,
            Margin = new Padding(0)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f)); // 0: Header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // 1: Order / Part Summary
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f)); // 2: Title & Category
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f)); // 3: Machine & Assignee
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f)); // 4: Status Quick Buttons
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));  // 5: Details Textbox
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f)); // 6: Next Process
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));  // 7: Sub-worker task box
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // 8: Sub-worker Send Button
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f)); // 9: Actions (Save / Duplicate / Delete)

        // 0: Header
        var pnlHeader = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var lblHeader = new Label
        {
            Text = "⚙ 선택 노드 상세 정보 & 실시간 조작",
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold),
            ForeColor = Palette.TextWhite,
            Dock = DockStyle.Left,
            AutoSize = true
        };
        _lblCurrentStatus = new Label
        {
            Text = "[선택 없음]",
            Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold),
            ForeColor = Palette.TextSlate400,
            Dock = DockStyle.Right,
            AutoSize = true
        };
        pnlHeader.Controls.AddRange(new Control[] { lblHeader, _lblCurrentStatus });
        mainLayout.Controls.Add(pnlHeader, 0, 0);

        // 1: Order / Part Summary
        _lblOrderAndPart = new Label
        {
            Dock = DockStyle.Fill,
            Text = "작지: - | 부품: -",
            Font = new Font("Consolas", 8.2f, FontStyle.Bold),
            ForeColor = Palette.AccentSky,
            BackColor = Palette.Bg850,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0)
        };
        mainLayout.Controls.Add(_lblOrderAndPart, 0, 1);

        // 2: Title & Category (2-col grid)
        var row2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));

        var pnlTitle = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        pnlTitle.Controls.Add(new Label { Text = "공정명", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, AutoSize = true });
        _txtTitle = new TextBox { Width = 195, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        _txtTitle.TextChanged += (s, e) => SaveChangesToNode();
        pnlTitle.Controls.Add(_txtTitle);

        var pnlCat = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        pnlCat.Controls.Add(new Label { Text = "종류", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, AutoSize = true });
        _cboCategory = new ComboBox { Width = 115, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Bg850, ForeColor = Color.White };
        _cboCategory.Items.AddRange(new object[] { "준비", "가공", "검사", "후공정", "협업", "이슈" });
        _cboCategory.SelectedIndexChanged += (s, e) => SaveChangesToNode();
        pnlCat.Controls.Add(_cboCategory);

        row2.Controls.Add(pnlTitle, 0, 0);
        row2.Controls.Add(pnlCat, 1, 0);
        mainLayout.Controls.Add(row2, 0, 2);

        // 3: Machine & Assignee (2-col grid)
        var row3 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        row3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        row3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var pnlMach = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        pnlMach.Controls.Add(new Label { Text = "담당 장비", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, AutoSize = true });
        _cboMachine = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Bg850, ForeColor = Color.White };
        _cboMachine.Items.AddRange(new object[] { "R01", "R02", "R03", "R04", "R05", "K01", "K02", "세척기", "측정실", "포장팀" });
        _cboMachine.SelectedIndexChanged += (s, e) => SaveChangesToNode();
        pnlMach.Controls.Add(_cboMachine);

        var pnlAss = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        pnlAss.Controls.Add(new Label { Text = "담당 작업자", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, AutoSize = true });
        _txtAssignee = new TextBox { Width = 150, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        _txtAssignee.TextChanged += (s, e) => SaveChangesToNode();
        pnlAss.Controls.Add(_txtAssignee);

        row3.Controls.Add(pnlMach, 0, 0);
        row3.Controls.Add(pnlAss, 1, 0);
        mainLayout.Controls.Add(row3, 0, 3);

        // 4: Status Quick Action Buttons (High contrast & readable)
        var row4 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 3, 0, 3) };
        for (int i = 0; i < 4; i++) row4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

        _btnStatusReady = CreateFlatButton("대기중", Color.FromArgb(51, 65, 85), Color.White);
        _btnStatusReady.Click += (s, e) => TriggerStatusChange(NodeStatus.Ready);

        _btnStatusRunning = CreateFlatButton("진행중", Color.FromArgb(5, 150, 105), Color.White);
        _btnStatusRunning.Click += (s, e) => TriggerStatusChange(NodeStatus.Running);

        _btnStatusComplete = CreateFlatButton("완료", Color.FromArgb(2, 132, 199), Color.White);
        _btnStatusComplete.Click += (s, e) => TriggerStatusChange(NodeStatus.Completed);

        _btnStatusIssue = CreateFlatButton("이슈", Color.FromArgb(220, 38, 38), Color.White);
        _btnStatusIssue.Click += (s, e) => TriggerStatusChange(NodeStatus.Issue);

        row4.Controls.Add(_btnStatusReady, 0, 0);
        row4.Controls.Add(_btnStatusRunning, 1, 0);
        row4.Controls.Add(_btnStatusComplete, 2, 0);
        row4.Controls.Add(_btnStatusIssue, 3, 0);
        mainLayout.Controls.Add(row4, 0, 4);

        // 5: Details / Memo Box
        var pnlDetails = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var lblDetPrompt = new Label { Text = "세부 작업 지시 / 가공 조건 / 점검 메모", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Top, Height = 18 };
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
        pnlDetails.Controls.Add(_txtDetails);
        pnlDetails.Controls.Add(lblDetPrompt);
        mainLayout.Controls.Add(pnlDetails, 0, 5);

        // 6: Next Process Input
        var pnlNext = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
        pnlNext.Controls.Add(new Label { Text = "다음 예정 작업:", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, AutoSize = true, Margin = new Padding(0, 5, 4, 0) });
        _txtNextProcess = new TextBox { Width = 220, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        _txtNextProcess.TextChanged += (s, e) => SaveChangesToNode();
        pnlNext.Controls.Add(_txtNextProcess);
        mainLayout.Controls.Add(pnlNext, 0, 6);

        // 7: Sub-worker Dedicated Section
        var pnlSubSection = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var pnlSubHeader = new Panel { Dock = DockStyle.Top, Height = 20 };
        var lblSubTitle = new Label { Text = "👥 서브인원 배정 및 작업 지시 (실제 서버 지시 연동)", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.Purple, Dock = DockStyle.Left, AutoSize = true };
        _lblSubWorkerTaskStatus = new Label { Text = "○ 미발송", Font = new Font("맑은 고딕", 7.5f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Right, AutoSize = true };
        pnlSubHeader.Controls.AddRange(new Control[] { lblSubTitle, _lblSubWorkerTaskStatus });

        var pnlSubBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        pnlSubBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        pnlSubBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var pnlSubNameRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        pnlSubNameRow.Controls.Add(new Label { Text = "서브인원:", Font = new Font("맑은 고딕", 7.8f), ForeColor = Palette.TextSlate400, AutoSize = true, Margin = new Padding(0, 3, 4, 0) });
        _txtSubWorker = new TextBox { Width = 110, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "이서브" };
        _txtSubWorker.TextChanged += (s, e) => SaveChangesToNode();
        pnlSubNameRow.Controls.Add(_txtSubWorker);

        _txtSubInstruction = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 8.5f),
            PlaceholderText = "서브인원에게 전달할 작업 지시 내용 (예: 치구 세팅 및 공구 준비)"
        };
        _txtSubInstruction.TextChanged += (s, e) => SaveChangesToNode();

        pnlSubBody.Controls.Add(pnlSubNameRow, 0, 0);
        pnlSubBody.Controls.Add(_txtSubInstruction, 0, 1);

        pnlSubSection.Controls.Add(pnlSubBody);
        pnlSubSection.Controls.Add(pnlSubHeader);
        mainLayout.Controls.Add(pnlSubSection, 0, 7);

        // 8: Sub-Worker Send Button
        _btnSendSubWorkerTask = CreateFlatButton("📤 서브인원 작업 지시 전송 (서버 지시 발송)", Color.FromArgb(126, 34, 206), Color.White);
        _btnSendSubWorkerTask.Click += (s, e) => OnSendSubWorkerTaskClicked();
        mainLayout.Controls.Add(_btnSendSubWorkerTask, 0, 8);

        // 9: Actions (Save / Duplicate / Delete)
        var row9 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 2, 0, 0) };
        row9.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        row9.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        row9.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));

        _btnSaveMemory = CreateFlatButton("💾 메모 저장", Palette.Bg800, Palette.AccentSky);
        _btnSaveMemory.Click += (s, e) => { SaveChangesToNode(); MessageBox.Show("노드 정보가 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information); };

        _btnDuplicateNode = CreateFlatButton("📋 공정 복제", Palette.Bg800, Color.FromArgb(168, 85, 247));
        _btnDuplicateNode.Click += (s, e) => { if (_currentNode != null) NodeDuplicateRequested?.Invoke(_currentNode); };

        _btnDeleteNode = CreateFlatButton("🗑 노드 삭제", Color.FromArgb(153, 27, 27), Color.White);
        _btnDeleteNode.Click += (s, e) =>
        {
            if (_currentNode != null && MessageBox.Show("선택한 노드를 삭제하시겠습니까?", "노드 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                NodeDeleteRequested?.Invoke(_currentNode);
            }
        };

        row9.Controls.Add(_btnSaveMemory, 0, 0);
        row9.Controls.Add(_btnDuplicateNode, 1, 0);
        row9.Controls.Add(_btnDeleteNode, 2, 0);
        mainLayout.Controls.Add(row9, 0, 9);

        Controls.Add(mainLayout);
    }

    public void BindNode(WorkflowNode? node)
    {
        _currentNode = node;
        if (node == null)
        {
            SetEmptyState();
            return;
        }

        _isBinding = true;
        try
        {
            Enabled = true;
        _lblOrderAndPart.Text = $"작지: {node.OrderNumber} | 부품: {node.PartName}";
        _lblCurrentStatus.Text = $"[{GetStatusText(node.Status)}]";
        _lblCurrentStatus.ForeColor = GetStatusColor(node.Status);

        _txtTitle.Text = node.Title;
        _cboCategory.SelectedIndex = (int)node.Category;

        int machIdx = _cboMachine.Items.IndexOf(node.MachineId);
        _cboMachine.SelectedIndex = machIdx >= 0 ? machIdx : 0;

        _txtAssignee.Text = node.Assignee;
        _txtDetails.Text = node.Details;
        _txtNextProcess.Text = node.NextProcess;

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
        }

        // Highlight active status button
        UpdateStatusButtonsHighlight(node.Status);
        }
        finally
        {
            _isBinding = false;
        }
    }

    private void UpdateStatusButtonsHighlight(NodeStatus status)
    {
        _btnStatusReady.FlatAppearance.BorderSize = (status == NodeStatus.Ready) ? 2 : 0;
        _btnStatusRunning.FlatAppearance.BorderSize = (status == NodeStatus.Running || status == NodeStatus.PendingCheck) ? 2 : 0;
        _btnStatusComplete.FlatAppearance.BorderSize = (status == NodeStatus.Completed) ? 2 : 0;
        _btnStatusIssue.FlatAppearance.BorderSize = (status == NodeStatus.Issue) ? 2 : 0;
    }

    private void SaveChangesToNode()
    {
        if (_isBinding || _currentNode == null) return;

        _currentNode.Title = _txtTitle.Text.Trim();
        _currentNode.Category = (NodeCategory)_cboCategory.SelectedIndex;
        _currentNode.MachineId = _cboMachine.SelectedItem?.ToString() ?? "R01";
        _currentNode.Assignee = _txtAssignee.Text.Trim();
        _currentNode.Details = _txtDetails.Text;
        _currentNode.NextProcess = _txtNextProcess.Text.Trim();
        _currentNode.SubWorkerName = _txtSubWorker.Text.Trim();
        _currentNode.SubWorkerInstruction = _txtSubInstruction.Text.Trim();

        NodeUpdated?.Invoke(_currentNode);
    }

    private void TriggerStatusChange(NodeStatus newStatus)
    {
        if (_currentNode == null) return;
        SaveChangesToNode();
        _currentNode.Status = newStatus;
        if (newStatus == NodeStatus.Running && _currentNode.StartedAt == null)
            _currentNode.StartedAt = DateTime.Now;
        if (newStatus == NodeStatus.Completed && _currentNode.CompletedAt == null)
            _currentNode.CompletedAt = DateTime.Now;

        _lblCurrentStatus.Text = $"[{GetStatusText(newStatus)}]";
        _lblCurrentStatus.ForeColor = GetStatusColor(newStatus);
        UpdateStatusButtonsHighlight(newStatus);

        StatusChangeRequested?.Invoke(_currentNode, newStatus);
    }

    private void OnSendSubWorkerTaskClicked()
    {
        if (_currentNode == null) return;
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
        _isBinding = true;
        try
        {
            Enabled = false;
        _lblOrderAndPart.Text = "작지: [선택 없음] | 부품: [선택 없음]";
        _lblCurrentStatus.Text = "[대기]";
        _lblCurrentStatus.ForeColor = Palette.TextSlate400;

        _txtTitle.Text = "";
        _cboCategory.SelectedIndex = 1;
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
            Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Dock = DockStyle.Fill,
            Margin = new Padding(2)
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
        return btn;
    }

    private static string GetStatusText(NodeStatus s) => s switch
    {
        NodeStatus.Ready => "대기중",
        NodeStatus.Running => "진행중",
        NodeStatus.PendingCheck => "진행중 (측정)",
        NodeStatus.Completed => "완료",
        NodeStatus.Issue => "이슈발생",
        _ => "대기중"
    };

    private static Color GetStatusColor(NodeStatus s) => s switch
    {
        NodeStatus.Ready => Palette.TextSlate400,
        NodeStatus.Running => Palette.Green,
        NodeStatus.PendingCheck => Palette.Amber,
        NodeStatus.Completed => Palette.AccentSky,
        NodeStatus.Issue => Palette.Rose,
        _ => Palette.TextSlate400
    };
}
