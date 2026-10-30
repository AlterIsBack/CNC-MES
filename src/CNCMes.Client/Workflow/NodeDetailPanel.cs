using System;
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
        public static readonly Color FocusBorder = Color.FromArgb(56, 189, 248);
    }

    private WorkflowNode? _currentNode;
    private bool _isBinding;

    // Controls
    private Label _lblOrderAndPart = null!;
    private Label _lblCurrentStatus = null!;
    private TextBox _txtTitle = null!;
    private ComboBox _cboCategory = null!;
    private ComboBox _cboMachine = null!;
    private TextBox _txtAssignee = null!;
    private TextBox _txtDetails = null!;
    private TextBox _txtNextProcess = null!;
    private Label _lblSubWorkerTaskStatus = null!;
    private TextBox _txtSubWorker = null!;
    private TextBox _txtSubInstruction = null!;

    // 5 Status Buttons (Image 2 Exact Match: 대기, ▶가공, 📐측정, ⏹완료, 🚨이슈)
    private Button _btnStatusReady = null!;
    private Button _btnStatusRunning = null!;
    private Button _btnStatusPendingCheck = null!;
    private Button _btnStatusComplete = null!;
    private Button _btnStatusIssue = null!;

    // Action Buttons
    private Button _btnSaveDetails = null!;
    private Button _btnDuplicate = null!;
    private Button _btnDelete = null!;
    private Button _btnSendSubTask = null!;

    public event Action<WorkflowNode, NodeStatus>? StatusChangeRequested;
    public event Action<WorkflowNode>? NodeUpdated;
    public event Action<WorkflowNode>? NodeDeleteRequested;
    public event Action<WorkflowNode>? NodeDuplicateRequested;
    public event Action<WorkflowNode, string, string>? SubWorkerTaskSendRequested;

    public NodeDetailPanel()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        BackColor = Palette.Bg900;
        ForeColor = Palette.TextWhite;
        Padding = new Padding(12);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));  // 0: Header (Title & Status)
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));  // 1: Work Order & Part Info
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));  // 2: Title & Category Row
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));  // 3: Machine & Assignee Row
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));  // 4: 5 Status Quick Buttons
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35f));   // 5: Details / Memo Box
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));  // 6: Next Process Input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));   // 7: Sub-worker Dedicated Section
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));  // 8: Action Buttons (Save/Duplicate/Delete)

        // 0: Header
        var pnlHeader = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var lblHeader = new Label
        {
            Text = "⚙ 선택 노드 상세 정보 실시간 조작",
            Font = new Font("맑은 고딕", 10f, FontStyle.Bold),
            ForeColor = Palette.TextWhite,
            Dock = DockStyle.Left,
            AutoSize = true
        };
        _lblCurrentStatus = new Label
        {
            Text = "[대기]",
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold),
            ForeColor = Palette.TextSlate400,
            Dock = DockStyle.Right,
            AutoSize = true
        };
        pnlHeader.Controls.AddRange(new Control[] { lblHeader, _lblCurrentStatus });
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

        // 2: Title & Category
        var row2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65f));
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));

        var pnlTitle = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        pnlTitle.Controls.Add(new Label { Text = "공정명 (타이틀)", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, AutoSize = true });
        _txtTitle = new TextBox { Width = 205, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("맑은 고딕", 9.2f) };
        _txtTitle.TextChanged += (s, e) => SaveChangesToNode();
        pnlTitle.Controls.Add(_txtTitle);

        var pnlCat = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        pnlCat.Controls.Add(new Label { Text = "공정분류", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, AutoSize = true });
        _cboCategory = new ComboBox { Width = 115, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Bg850, ForeColor = Color.White };
        _cboCategory.Items.AddRange(new object[] { "준비", "가공", "검사", "후공정", "협업", "이슈" });
        _cboCategory.SelectedIndexChanged += (s, e) => SaveChangesToNode();
        pnlCat.Controls.Add(_cboCategory);

        row2.Controls.Add(pnlTitle, 0, 0);
        row2.Controls.Add(pnlCat, 1, 0);
        mainLayout.Controls.Add(row2, 0, 2);

        // 3: Machine & Assignee
        var row3 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        row3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        row3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var pnlMach = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        pnlMach.Controls.Add(new Label { Text = "담당 설비 (장비)", Font = new Font("맑은 고딕", 7.8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, AutoSize = true });
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

        // 4: 5 Status Quick Action Buttons (Exact Image 2 Match: 대기, ▶가공, 📐측정, ⏹완료, 🚨이슈)
        var row4 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Margin = new Padding(0, 2, 0, 2) };
        for (int i = 0; i < 5; i++) row4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

        _btnStatusReady = CreateFlatButton("대기", Color.FromArgb(42, 59, 76), Color.White);
        _btnStatusReady.Click += (s, e) => TriggerStatusChange(NodeStatus.Ready);

        _btnStatusRunning = CreateFlatButton("▶가공", Color.FromArgb(5, 150, 105), Color.White);
        _btnStatusRunning.Click += (s, e) => TriggerStatusChange(NodeStatus.Running);

        _btnStatusPendingCheck = CreateFlatButton("📐측정", Color.FromArgb(217, 119, 6), Color.White);
        _btnStatusPendingCheck.Click += (s, e) => TriggerStatusChange(NodeStatus.PendingCheck);

        _btnStatusComplete = CreateFlatButton("⏹완료", Color.FromArgb(2, 132, 199), Color.White);
        _btnStatusComplete.Click += (s, e) => TriggerStatusChange(NodeStatus.Completed);

        _btnStatusIssue = CreateFlatButton("🚨이슈", Color.FromArgb(220, 38, 38), Color.White);
        _btnStatusIssue.Click += (s, e) => TriggerStatusChange(NodeStatus.Issue);

        row4.Controls.Add(_btnStatusReady, 0, 0);
        row4.Controls.Add(_btnStatusRunning, 1, 0);
        row4.Controls.Add(_btnStatusPendingCheck, 2, 0);
        row4.Controls.Add(_btnStatusComplete, 3, 0);
        row4.Controls.Add(_btnStatusIssue, 4, 0);
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
        mainLayout.Controls.Add(pnlSubSection, 0, 7);

        // 8: Action Buttons (Save / Duplicate / Delete)
        var row8 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 3, 0, 0) };
        row8.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));
        row8.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));
        row8.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));

        _btnSaveDetails = CreateFlatButton("💾 메모 저장", Palette.Bg800, Color.White);
        _btnSaveDetails.Click += (s, e) =>
        {
            if (_currentNode == null)
            {
                MessageBox.Show("편집할 작업 노드를 캔버스에서 먼저 선택해 주세요.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SaveChangesToNode();
            MessageBox.Show("노드 정보와 작업 지시 메모가 반영되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        _btnDuplicate = CreateFlatButton("📋 공정 복제", Palette.Bg800, Color.White);
        _btnDuplicate.Click += (s, e) =>
        {
            if (_currentNode == null)
            {
                MessageBox.Show("복제할 작업 노드를 캔버스에서 먼저 선택해 주세요.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            NodeDuplicateRequested?.Invoke(_currentNode);
        };

        _btnDelete = CreateFlatButton("🗑 노드 삭제", Color.FromArgb(185, 28, 28), Color.White);
        _btnDelete.Click += (s, e) =>
        {
            if (_currentNode == null)
            {
                MessageBox.Show("삭제할 작업 노드를 캔버스에서 먼저 선택해 주세요.", "안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show($"'{_currentNode.Title}' 공정 노드를 삭제하시겠습니까?", "노드 삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                NodeDeleteRequested?.Invoke(_currentNode);
                SetEmptyState();
            }
        };

        row8.Controls.Add(_btnSaveDetails, 0, 0);
        row8.Controls.Add(_btnDuplicate, 1, 0);
        row8.Controls.Add(_btnDelete, 2, 0);
        mainLayout.Controls.Add(row8, 0, 8);

        Controls.Add(mainLayout);
        SetEmptyState();
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
            _lblOrderAndPart.Text = $"작지: {node.OrderNumber} | 부품: {node.PartName}";
            _lblCurrentStatus.Text = $"[{GetStatusText(node.Status)}]";
            _lblCurrentStatus.ForeColor = GetStatusColor(node.Status);

            _txtTitle.Text = node.Title;
            _cboCategory.SelectedIndex = Math.Clamp((int)node.Category, 0, 5);

            int machIdx = _cboMachine.Items.IndexOf(node.MachineId);
            if (machIdx >= 0) _cboMachine.SelectedIndex = machIdx;
            else if (_cboMachine.Items.Count > 0) _cboMachine.SelectedIndex = 0;

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

            UpdateStatusButtonsHighlight(node.Status);
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
        if (_currentNode == null)
        {
            MessageBox.Show("편집할 작업 노드를 캔버스에서 먼저 선택해 주세요.", "노드 미선택", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
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
        _isBinding = true;
        try
        {
            // KEEP ENABLED SO TEXT AND BUTTONS ARE ALWAYS FULLY READABLE IN DARK THEME!
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
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            Margin = new Padding(2),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private string GetStatusText(NodeStatus s) => s switch
    {
        NodeStatus.Ready => "대기",
        NodeStatus.Running => "가공중",
        NodeStatus.PendingCheck => "측정대기",
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
