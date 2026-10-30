using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client.Workflow;

public class NodeDetailPanel : UserControl
{
    private static class Palette
    {
        public static readonly Color Bg900 = Color.FromArgb(11, 19, 35);
        public static readonly Color Bg850 = Color.FromArgb(15, 27, 49);
        public static readonly Color Bg800 = Color.FromArgb(20, 35, 64);
        public static readonly Color Border = Color.FromArgb(29, 46, 78);
        public static readonly Color AccentSky = Color.FromArgb(14, 165, 233);
        public static readonly Color Green = Color.FromArgb(16, 185, 129);
        public static readonly Color Amber = Color.FromArgb(245, 158, 11);
        public static readonly Color Rose = Color.FromArgb(239, 68, 68);
        public static readonly Color Purple = Color.FromArgb(168, 85, 247);
        public static readonly Color TextWhite = Color.FromArgb(248, 250, 252);
        public static readonly Color TextSlate400 = Color.FromArgb(148, 163, 184);
    }

    private WorkflowNode? _currentNode;

    private TextBox _txtTitle = null!;
    private ComboBox _cboCategory = null!;
    private ComboBox _cboMachine = null!;
    private TextBox _txtAssignee = null!;
    private Label _lblOrderAndPart = null!;
    private TextBox _txtDetails = null!;
    private Label _lblCurrentStatus = null!;

    private Button _btnStatusReady = null!;
    private Button _btnStatusStart = null!;
    private Button _btnStatusInspect = null!;
    private Button _btnStatusComplete = null!;
    private Button _btnStatusIssue = null!;
    private Button _btnDeleteNode = null!;
    private Button _btnDuplicateNode = null!;

    public event Action<WorkflowNode, NodeStatus>? StatusChangeRequested;
    public event Action<WorkflowNode>? NodeUpdated;
    public event Action<WorkflowNode>? NodeDeleteRequested;
    public event Action<WorkflowNode>? NodeDuplicateRequested;

    public NodeDetailPanel()
    {
        DoubleBuffered = true;
        BackColor = Palette.Bg900;
        Font = new Font("맑은 고딕", 9f);
        ForeColor = Palette.TextWhite;
        Padding = new Padding(12);

        InitializeComponents();
        SetEmptyState();
    }

    private void InitializeComponents()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Margin = new Padding(0)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f)); // Order / Part Summary
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f)); // Title & Category
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f)); // Machine & Assignee
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f)); // Status Actions
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Details Textbox
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f)); // Apply & Actions
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f)); // Delete / Duplicate

        // 0: Header
        var pnlHeader = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var lblHeader = new Label
        {
            Text = "⚙ 선택 노드 상세 정보 & 실시간 조작",
            Font = new Font("맑은 고딕", 9.8f, FontStyle.Bold),
            ForeColor = Palette.TextWhite,
            Dock = DockStyle.Left,
            AutoSize = true
        };
        _lblCurrentStatus = new Label
        {
            Text = "[대기]",
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
            Font = new Font("Consolas", 8.5f),
            ForeColor = Palette.AccentSky,
            BackColor = Palette.Bg850,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0)
        };
        mainLayout.Controls.Add(_lblOrderAndPart, 0, 1);

        // 2: Title & Category (2-col grid)
        var row2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));

        var pnlTitle = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        pnlTitle.Controls.Add(new Label { Text = "공정명", Font = new Font("맑은 고딕", 7.8f), ForeColor = Palette.TextSlate400, AutoSize = true });
        _txtTitle = new TextBox { Width = 180, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        _txtTitle.TextChanged += (s, e) => SaveChangesToNode();
        pnlTitle.Controls.Add(_txtTitle);

        var pnlCat = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        pnlCat.Controls.Add(new Label { Text = "종류", Font = new Font("맑은 고딕", 7.8f), ForeColor = Palette.TextSlate400, AutoSize = true });
        _cboCategory = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Bg850, ForeColor = Color.White };
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
        pnlMach.Controls.Add(new Label { Text = "담당 장비", Font = new Font("맑은 고딕", 7.8f), ForeColor = Palette.TextSlate400, AutoSize = true });
        _cboMachine = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Bg850, ForeColor = Color.White };
        _cboMachine.Items.AddRange(new object[] { "R01", "R02", "R03", "R04", "R05", "K01", "K02" });
        _cboMachine.SelectedIndexChanged += (s, e) => SaveChangesToNode();
        pnlMach.Controls.Add(_cboMachine);

        var pnlAss = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        pnlAss.Controls.Add(new Label { Text = "담당자 (서브인원)", Font = new Font("맑은 고딕", 7.8f), ForeColor = Palette.TextSlate400, AutoSize = true });
        _txtAssignee = new TextBox { Width = 140, BackColor = Palette.Bg850, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        _txtAssignee.TextChanged += (s, e) => SaveChangesToNode();
        pnlAss.Controls.Add(_txtAssignee);

        row3.Controls.Add(pnlMach, 0, 0);
        row3.Controls.Add(pnlAss, 1, 0);
        mainLayout.Controls.Add(row3, 0, 3);

        // 4: Status Quick Action Buttons (5 buttons)
        var row4 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Margin = new Padding(0, 4, 0, 4) };
        for (int i = 0; i < 5; i++) row4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

        _btnStatusReady = CreateFlatButton("대기", Palette.Bg800, Palette.TextSlate400);
        _btnStatusReady.Click += (s, e) => TriggerStatusChange(NodeStatus.Ready);

        _btnStatusStart = CreateFlatButton("▶가공", Palette.Green, Color.White);
        _btnStatusStart.Click += (s, e) => TriggerStatusChange(NodeStatus.Running);

        _btnStatusInspect = CreateFlatButton("📐측정", Palette.Amber, Palette.Bg900);
        _btnStatusInspect.Click += (s, e) => TriggerStatusChange(NodeStatus.PendingCheck);

        _btnStatusComplete = CreateFlatButton("✅완료", Color.FromArgb(2, 132, 199), Color.White);
        _btnStatusComplete.Click += (s, e) => TriggerStatusChange(NodeStatus.Completed);

        _btnStatusIssue = CreateFlatButton("🚨이슈", Palette.Rose, Color.White);
        _btnStatusIssue.Click += (s, e) => TriggerStatusChange(NodeStatus.Issue);

        row4.Controls.AddRange(new Control[] { _btnStatusReady, _btnStatusStart, _btnStatusInspect, _btnStatusComplete, _btnStatusIssue });
        mainLayout.Controls.Add(row4, 0, 4);

        // 5: Details / Instructions Memo
        var pnlDetails = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var lblDet = new Label { Text = "세부 작업 지시 / 가공 조건 / 점검 메모", Dock = DockStyle.Top, Height = 18, ForeColor = Palette.TextSlate400, Font = new Font("맑은 고딕", 7.8f) };
        _txtDetails = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            BackColor = Palette.Bg850,
            ForeColor = Palette.TextWhite,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical
        };
        _txtDetails.TextChanged += (s, e) => SaveChangesToNode();
        pnlDetails.Controls.Add(_txtDetails);
        pnlDetails.Controls.Add(lblDet);
        mainLayout.Controls.Add(pnlDetails, 0, 5);

        // 6: Action Buttons (Apply / Refresh)
        var row6 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 4, 0, 0) };
        row6.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        row6.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var btnSave = CreateFlatButton("💾 메모 및 변경 저장", Palette.Bg800, Palette.AccentSky);
        btnSave.Click += (s, e) => { SaveChangesToNode(); if (_currentNode != null) NodeUpdated?.Invoke(_currentNode); };
        
        _btnDuplicateNode = CreateFlatButton("📋 공정 복제 (타 부품 재사용)", Palette.Bg800, Color.FromArgb(168, 85, 247));
        _btnDuplicateNode.Click += (s, e) => { if (_currentNode != null) NodeDuplicateRequested?.Invoke(_currentNode); };

        row6.Controls.Add(btnSave, 0, 0);
        row6.Controls.Add(_btnDuplicateNode, 1, 0);
        mainLayout.Controls.Add(row6, 0, 6);

        // 7: Delete Button
        _btnDeleteNode = CreateFlatButton("🗑 선택 노드 삭제", Color.FromArgb(153, 27, 27), Color.White);
        _btnDeleteNode.Dock = DockStyle.Fill;
        _btnDeleteNode.Click += (s, e) =>
        {
            if (_currentNode != null && MessageBox.Show($"'{_currentNode.Title}' 공정 노드를 삭제하시겠습니까?", "노드 삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                NodeDeleteRequested?.Invoke(_currentNode);
            }
        };
        mainLayout.Controls.Add(_btnDeleteNode, 0, 7);

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

        Enabled = true;
        _txtTitle.Text = node.Title;
        _cboCategory.SelectedIndex = (int)node.Category;
        
        int machIdx = _cboMachine.Items.IndexOf(node.MachineId);
        _cboMachine.SelectedIndex = machIdx >= 0 ? machIdx : 0;
        
        _txtAssignee.Text = node.Assignee;
        _lblOrderAndPart.Text = $"작지: {(string.IsNullOrEmpty(node.OrderNumber) ? "-" : node.OrderNumber)} | 부품: {(string.IsNullOrEmpty(node.PartName) ? "-" : node.PartName)}";
        _txtDetails.Text = node.Details;

        UpdateStatusBadge(node.Status);
    }

    private void SetEmptyState()
    {
        Enabled = false;
        _txtTitle.Text = "";
        _cboCategory.SelectedIndex = 1;
        _cboMachine.SelectedIndex = 0;
        _txtAssignee.Text = "";
        _lblOrderAndPart.Text = "캔버스에서 노드를 선택하십시오.";
        _txtDetails.Text = "";
        _lblCurrentStatus.Text = "[선택 없음]";
        _lblCurrentStatus.ForeColor = Palette.TextSlate400;
    }

    private void UpdateStatusBadge(NodeStatus status)
    {
        switch (status)
        {
            case NodeStatus.Running:
                _lblCurrentStatus.Text = "[▶ 가공 진행중]";
                _lblCurrentStatus.ForeColor = Palette.Green;
                break;
            case NodeStatus.PendingCheck:
                _lblCurrentStatus.Text = "[📐 중간측정 대기]";
                _lblCurrentStatus.ForeColor = Palette.Amber;
                break;
            case NodeStatus.Completed:
                _lblCurrentStatus.Text = "[✅ 공정 완료]";
                _lblCurrentStatus.ForeColor = Color.FromArgb(56, 189, 248);
                break;
            case NodeStatus.Issue:
                _lblCurrentStatus.Text = "[🚨 긴급 이슈 발생]";
                _lblCurrentStatus.ForeColor = Palette.Rose;
                break;
            default:
                _lblCurrentStatus.Text = "[대기 / 준비]";
                _lblCurrentStatus.ForeColor = Palette.TextSlate400;
                break;
        }
    }

    private void TriggerStatusChange(NodeStatus newStatus)
    {
        if (_currentNode == null) return;
        _currentNode.Status = newStatus;
        if (newStatus == NodeStatus.Running && _currentNode.StartedAt == null)
            _currentNode.StartedAt = DateTime.Now;
        if (newStatus == NodeStatus.Completed)
            _currentNode.CompletedAt = DateTime.Now;

        UpdateStatusBadge(newStatus);
        StatusChangeRequested?.Invoke(_currentNode, newStatus);
        NodeUpdated?.Invoke(_currentNode);
    }

    private void SaveChangesToNode()
    {
        if (_currentNode == null) return;
        _currentNode.Title = _txtTitle.Text;
        if (_cboCategory.SelectedIndex >= 0)
            _currentNode.Category = (NodeCategory)_cboCategory.SelectedIndex;
        if (_cboMachine.SelectedItem != null)
            _currentNode.MachineId = _cboMachine.SelectedItem.ToString() ?? "R01";
        _currentNode.Assignee = _txtAssignee.Text;
        _currentNode.Details = _txtDetails.Text;
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
            Dock = DockStyle.Fill,
            Margin = new Padding(2),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }
}
