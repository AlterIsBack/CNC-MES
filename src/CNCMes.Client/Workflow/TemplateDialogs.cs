using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client.Workflow;

/// <summary>
/// 현재 캔버스의 공정 워크플로우를 재사용 가능한 템플릿으로 저장하는 대화상자
/// </summary>
public class TemplateSaveDialog : Form
{
    private readonly TextBox _txtName;
    private readonly TextBox _txtDesc;
    private readonly ComboBox _cboFixing;
    private readonly TextBox _txtMaterial;

    public string TemplateName => _txtName.Text.Trim();
    public string TemplateDescription => _txtDesc.Text.Trim();
    public string FixingMethod => _cboFixing.Text.Trim();
    public string TargetMaterial => _txtMaterial.Text.Trim();

    public TemplateSaveDialog(int nodeCount, string defaultMaterial = "PEEK 5T", string defaultFixing = "바이스")
    {
        Text = "💾 공정 워크플로우 템플릿 저장";
        Size = new Size(460, 360);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(11, 19, 35);
        ForeColor = Color.White;
        Font = new Font("맑은 고딕", 9f);

        var pnl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(16)
        };
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f)); // Info
        pnl.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f)); // Name
        pnl.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f)); // Material
        pnl.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f)); // Fixing
        pnl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Desc
        pnl.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f)); // Buttons

        var lblInfo = new Label
        {
            Text = $"현재 캔버스의 {nodeCount}개 공정 노드 및 연결선을 템플릿으로 저장합니다.",
            ForeColor = Color.FromArgb(14, 165, 233),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold)
        };
        pnl.SetColumnSpan(lblInfo, 2);
        pnl.Controls.Add(lblInfo, 0, 0);

        var lblName = new Label { Text = "템플릿 명칭:", ForeColor = Color.FromArgb(203, 213, 225), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _txtName = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 35, 64), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = $"공정 템플릿_{DateTime.Now:yyyyMMdd_HHmm}" };
        pnl.Controls.Add(lblName, 0, 1);
        pnl.Controls.Add(_txtName, 1, 1);

        var lblMat = new Label { Text = "소재 규격:", ForeColor = Color.FromArgb(203, 213, 225), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _txtMaterial = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 35, 64), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = defaultMaterial };
        pnl.Controls.Add(lblMat, 0, 2);
        pnl.Controls.Add(_txtMaterial, 1, 2);

        var lblFix = new Label { Text = "고정 방식:", ForeColor = Color.FromArgb(203, 213, 225), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _cboFixing = new ComboBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 35, 64), ForeColor = Color.White, DropDownStyle = ComboBoxStyle.DropDown };
        _cboFixing.Items.AddRange(new object[] { "바이스 고정", "진공 흡착 (진공 플레이트)", "클램프 고정 (사이드/토글)", "마그네틱 척 고정", "전용 지그 블록", "스크류 볼팅 고정" });
        _cboFixing.Text = defaultFixing;
        pnl.Controls.Add(lblFix, 0, 3);
        pnl.Controls.Add(_cboFixing, 1, 3);

        var lblDesc = new Label { Text = "템플릿 설명:", ForeColor = Color.FromArgb(203, 213, 225), Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Padding = new Padding(0, 6, 0, 0) };
        _txtDesc = new TextBox { Dock = DockStyle.Fill, Multiline = true, BackColor = Color.FromArgb(20, 35, 64), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "사용자 정의 표준 가공 공정 템플릿" };
        pnl.Controls.Add(lblDesc, 0, 4);
        pnl.Controls.Add(_txtDesc, 1, 4);

        var pnlBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0), Padding = new Padding(0) };
        var btnCancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 80, Height = 32, BackColor = Color.FromArgb(30, 45, 70), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var btnSave = new Button { Text = "💾 저장", Width = 90, Height = 32, BackColor = Color.FromArgb(5, 150, 105), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("맑은 고딕", 9f, FontStyle.Bold) };
        btnSave.Click += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
            {
                MessageBox.Show("템플릿 명칭을 입력해주세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtName.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        };

        pnlBtns.Controls.AddRange(new Control[] { btnCancel, btnSave });
        pnl.SetColumnSpan(pnlBtns, 2);
        pnl.Controls.Add(pnlBtns, 0, 5);

        Controls.Add(pnl);
        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }
}

/// <summary>
/// 저장된 표준 및 사용자 공정 템플릿 목록을 조회하고 캔버스에 적용하는 대화상자
/// </summary>
public class TemplateLoadDialog : Form
{
    private readonly ListBox _lstTemplates;
    private readonly Label _lblTitle;
    private readonly Label _lblDesc;
    private readonly Label _lblMeta;
    private readonly ListBox _lstSteps;
    private readonly Button _btnAppend;
    private readonly Button _btnReplace;
    private readonly Button _btnDelete;
    private readonly Label _lblSelectedCount;

    public List<WorkflowTemplateItem> SelectedTemplates { get; } = new();
    public WorkflowTemplateItem? SelectedTemplate => SelectedTemplates.FirstOrDefault();
    public bool IsAppendMode { get; private set; } = false;

    public TemplateLoadDialog()
    {
        Text = "📂 공정 워크플로우 템플릿 불러오기 (다중 템플릿 동시 지원)";
        Size = new Size(940, 620);
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        BackColor = Color.FromArgb(11, 19, 35);
        ForeColor = Color.White;
        Font = new Font("맑은 고딕", 9f);

        // 1. Root Layout: 중앙 Splitter(Fill) + 맨 하단 고정 액션바(Bottom)
        // 화면 해상도나 DPI 배율에 관계없이 하단 버튼이 100% 잘리지 않고 항상 노출되도록 구성
        var pnlRoot = new Panel { Dock = DockStyle.Fill };

        // 2. 맨 하단 전체 고정 버튼 바 (Dock = Bottom)
        var pnlBottomBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(16, 10, 16, 10)
        };

        // 좌측: 선택 삭제 버튼
        _btnDelete = new Button
        {
            Text = "🗑 선택 템플릿 삭제",
            Dock = DockStyle.Left,
            Width = 140,
            Height = 36,
            BackColor = Color.FromArgb(127, 29, 29),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 8.8f),
            Cursor = Cursors.Hand
        };
        _btnDelete.FlatAppearance.BorderSize = 0;
        _btnDelete.Click += (s, e) => DeleteSelectedTemplate();

        // 우측 컨트롤 컨테이너 (FlowLayoutPanel: Right to Left)
        var pnlRightButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        var btnClose = new Button
        {
            Text = "닫기",
            DialogResult = DialogResult.Cancel,
            Width = 85,
            Height = 36,
            BackColor = Color.FromArgb(51, 65, 85),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 9f),
            Margin = new Padding(6, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        btnClose.FlatAppearance.BorderSize = 0;

        _btnReplace = new Button
        {
            Text = "⚡ 새로 대체하여 적용",
            Width = 175,
            Height = 36,
            BackColor = Color.FromArgb(2, 132, 199),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
            Margin = new Padding(6, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        _btnReplace.FlatAppearance.BorderSize = 0;
        _btnReplace.Click += (s, e) => ApplySelectedTemplates(append: false);

        _btnAppend = new Button
        {
            Text = "➕ 기존 화면에 추가",
            Width = 195,
            Height = 36,
            BackColor = Color.FromArgb(5, 150, 105),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 9.2f, FontStyle.Bold),
            Margin = new Padding(6, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        _btnAppend.FlatAppearance.BorderSize = 0;
        _btnAppend.Click += (s, e) => ApplySelectedTemplates(append: true);

        pnlRightButtons.Controls.AddRange(new Control[] { btnClose, _btnReplace, _btnAppend });

        pnlBottomBar.Controls.Add(_btnDelete);
        pnlBottomBar.Controls.Add(pnlRightButtons);

        // 3. 중앙 본문 SplitContainer (좌: 목록, 우: 상세 프리뷰)
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 290,
            SplitterWidth = 6,
            BackColor = Color.FromArgb(30, 41, 59)
        };

        // Left: Template List (다중 선택 MultiExtended 지원)
        var pnlLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Color.FromArgb(11, 19, 35) };
        var lblListTitle = new Label
        {
            Text = "📋 등록된 공정 템플릿 목록 (Ctrl/Shift 다중 선택 가능)",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(14, 165, 233)
        };

        _lstTemplates = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 27, 49),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 9f),
            ItemHeight = 24,
            SelectionMode = SelectionMode.MultiExtended
        };
        _lstTemplates.SelectedIndexChanged += (s, e) => OnTemplateSelected();

        _lblSelectedCount = new Label
        {
            Text = "선택된 템플릿: 1개",
            Dock = DockStyle.Bottom,
            Height = 24,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("맑은 고딕", 8.2f)
        };

        pnlLeft.Controls.Add(_lstTemplates);
        pnlLeft.Controls.Add(lblListTitle);
        pnlLeft.Controls.Add(_lblSelectedCount);
        split.Panel1.Controls.Add(pnlLeft);

        // Right: Template Preview
        var pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Color.FromArgb(11, 19, 35) };
        _lblTitle = new Label { Text = "템플릿을 선택하세요", Dock = DockStyle.Top, Height = 30, Font = new Font("맑은 고딕", 11.5f, FontStyle.Bold), ForeColor = Color.FromArgb(245, 158, 11) };
        _lblDesc = new Label { Text = "-", Dock = DockStyle.Top, Height = 36, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("맑은 고딕", 8.5f) };
        _lblMeta = new Label { Text = "", Dock = DockStyle.Top, Height = 24, ForeColor = Color.FromArgb(168, 85, 247), Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold) };

        var lblStepsTitle = new Label { Text = "공정 단계 흐름도 (상세):", Dock = DockStyle.Top, Height = 24, Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold), ForeColor = Color.FromArgb(203, 213, 225) };
        _lstSteps = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 27, 49),
            ForeColor = Color.FromArgb(240, 240, 240),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 8.8f),
            ItemHeight = 22
        };

        pnlRight.Controls.Add(_lstSteps);
        pnlRight.Controls.Add(lblStepsTitle);
        pnlRight.Controls.Add(_lblMeta);
        pnlRight.Controls.Add(_lblDesc);
        pnlRight.Controls.Add(_lblTitle);
        split.Panel2.Controls.Add(pnlRight);

        pnlRoot.Controls.Add(split);
        pnlRoot.Controls.Add(pnlBottomBar);

        Controls.Add(pnlRoot);

        RefreshTemplateList();
    }

    private void RefreshTemplateList()
    {
        _lstTemplates.Items.Clear();
        var all = TemplateManager.GetAllTemplates();
        foreach (var t in all)
        {
            string tag = t.IsBuiltIn ? "[표준]" : "[사용자]";
            _lstTemplates.Items.Add($"{tag} {t.Name} ({t.Nodes.Count}단계)");
        }
        if (_lstTemplates.Items.Count > 0)
        {
            _lstTemplates.SelectedIndex = 0;
        }
    }

    private void OnTemplateSelected()
    {
        SelectedTemplates.Clear();
        var all = TemplateManager.GetAllTemplates();

        if (_lstTemplates.SelectedIndices.Count > 0)
        {
            foreach (int idx in _lstTemplates.SelectedIndices)
            {
                if (idx >= 0 && idx < all.Count)
                {
                    SelectedTemplates.Add(all[idx]);
                }
            }
        }

        int count = SelectedTemplates.Count;
        _lblSelectedCount.Text = $"선택된 템플릿: {count}개";

        if (count == 0) return;

        if (count == 1)
        {
            var single = SelectedTemplates[0];
            _lblTitle.Text = single.Name;
            _lblDesc.Text = single.Description;
            _lblMeta.Text = $"소재: {single.TargetMaterial}  |  고정방식: {single.FixingMethod}  |  단계 수: {single.Nodes.Count}개";

            _lstSteps.Items.Clear();
            int step = 1;
            foreach (var n in single.Nodes)
            {
                string timeStr = !string.IsNullOrEmpty(n.StartTime) ? $" [{n.StartTime}~{n.EndTime}]" : "";
                _lstSteps.Items.Add($"{step:D2}. [{n.MachineId}] {n.Title}{timeStr}");
                step++;
            }

            _btnDelete.Visible = !single.IsBuiltIn;
            _btnAppend.Text = "➕ 기존 화면에 추가";
            _btnReplace.Text = "⚡ 새로 대체하여 적용";
        }
        else
        {
            _lblTitle.Text = $"선택된 템플릿 {count}개 동시 불러오기";
            _lblDesc.Text = "선택된 여러 템플릿이 캔버스에 각각 타이틀 박스로 구분되어 상하/나란히 함께 배치됩니다.";
            int totalSteps = SelectedTemplates.Sum(t => t.Nodes.Count);
            _lblMeta.Text = $"총 공정 단계 수: {totalSteps}개  |  포함 템플릿: {count}개";

            _lstSteps.Items.Clear();
            foreach (var t in SelectedTemplates)
            {
                _lstSteps.Items.Add($"📦 [{t.Name}] - {t.Nodes.Count}개 공정 ({t.TargetMaterial})");
                int step = 1;
                foreach (var n in t.Nodes)
                {
                    _lstSteps.Items.Add($"    └ {step:D2}. [{n.MachineId}] {n.Title}");
                    step++;
                }
            }

            _btnDelete.Visible = SelectedTemplates.Any(t => !t.IsBuiltIn);
            _btnAppend.Text = $"➕ 선택한 {count}개 템플릿 기존 화면에 추가";
            _btnReplace.Text = $"⚡ 선택한 {count}개 템플릿으로 새로 대체";
        }
    }

    private void ApplySelectedTemplates(bool append)
    {
        if (SelectedTemplates.Count == 0)
        {
            MessageBox.Show("적용할 템플릿을 목록에서 하나 이상 선택해주세요.", "선택 필요", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        IsAppendMode = append;
        DialogResult = DialogResult.OK;
    }

    private void DeleteSelectedTemplate()
    {
        var customTemplates = SelectedTemplates.Where(t => !t.IsBuiltIn).ToList();
        if (customTemplates.Count == 0)
        {
            MessageBox.Show("표준 내장 템플릿은 삭제할 수 없습니다.", "삭제 불가", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string names = string.Join(", ", customTemplates.Select(t => $"'{t.Name}'"));
        if (MessageBox.Show($"선택한 템플릿({names})을 삭제하시겠습니까?", "템플릿 삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            foreach (var t in customTemplates)
            {
                TemplateManager.DeleteTemplate(t.Name);
            }
            RefreshTemplateList();
        }
    }
}
