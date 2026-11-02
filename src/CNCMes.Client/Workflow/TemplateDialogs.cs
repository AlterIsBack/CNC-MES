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
    private readonly Button _btnApply;
    private readonly Button _btnDelete;
    private WorkflowTemplateItem? _selectedTemplate;

    public WorkflowTemplateItem? SelectedTemplate => _selectedTemplate;

    public TemplateLoadDialog()
    {
        Text = "📂 공정 워크플로우 템플릿 불러오기";
        Size = new Size(720, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(11, 19, 35);
        ForeColor = Color.White;
        Font = new Font("맑은 고딕", 9f);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 270,
            SplitterWidth = 5,
            BackColor = Color.FromArgb(35, 55, 95)
        };

        // Left: Template List
        var pnlLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Color.FromArgb(11, 19, 35) };
        var lblListTitle = new Label { Text = "📋 등록된 공정 템플릿 목록", Dock = DockStyle.Top, Height = 26, Font = new Font("맑은 고딕", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(14, 165, 233) };
        _lstTemplates = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 27, 49),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 8.8f),
            ItemHeight = 22
        };
        _lstTemplates.SelectedIndexChanged += (s, e) => OnTemplateSelected();
        pnlLeft.Controls.Add(_lstTemplates);
        pnlLeft.Controls.Add(lblListTitle);
        split.Panel1.Controls.Add(pnlLeft);

        // Right: Template Preview
        var pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Color.FromArgb(11, 19, 35) };
        _lblTitle = new Label { Text = "템플릿을 선택하세요", Dock = DockStyle.Top, Height = 28, Font = new Font("맑은 고딕", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(245, 158, 11) };
        _lblDesc = new Label { Text = "-", Dock = DockStyle.Top, Height = 36, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("맑은 고딕", 8.2f) };
        _lblMeta = new Label { Text = "", Dock = DockStyle.Top, Height = 24, ForeColor = Color.FromArgb(168, 85, 247), Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold) };

        var lblStepsTitle = new Label { Text = "공정 단계 흐름도:", Dock = DockStyle.Top, Height = 22, Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold), ForeColor = Color.FromArgb(203, 213, 225) };
        _lstSteps = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 27, 49),
            ForeColor = Color.FromArgb(240, 240, 240),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 8.5f),
            ItemHeight = 20
        };

        // Bottom Action Bar
        var pnlBottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };

        var btnClose = new Button { Text = "닫기", DialogResult = DialogResult.Cancel, Width = 80, Height = 32, BackColor = Color.FromArgb(30, 45, 70), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        _btnDelete = new Button { Text = "🗑 삭제", Width = 80, Height = 32, BackColor = Color.FromArgb(150, 30, 30), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        _btnDelete.Click += (s, e) => DeleteSelectedTemplate();
        _btnApply = new Button { Text = "🚀 캔버스에 적용", Width = 130, Height = 32, BackColor = Color.FromArgb(5, 150, 105), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("맑은 고딕", 9f, FontStyle.Bold) };
        _btnApply.Click += (s, e) => ApplySelectedTemplate();

        pnlBottom.Controls.AddRange(new Control[] { btnClose, _btnDelete, _btnApply });

        pnlRight.Controls.Add(_lstSteps);
        pnlRight.Controls.Add(lblStepsTitle);
        pnlRight.Controls.Add(_lblMeta);
        pnlRight.Controls.Add(_lblDesc);
        pnlRight.Controls.Add(_lblTitle);
        pnlRight.Controls.Add(pnlBottom);
        split.Panel2.Controls.Add(pnlRight);

        Controls.Add(split);

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
        if (_lstTemplates.SelectedIndex < 0) return;
        var all = TemplateManager.GetAllTemplates();
        if (_lstTemplates.SelectedIndex >= all.Count) return;

        _selectedTemplate = all[_lstTemplates.SelectedIndex];
        _lblTitle.Text = _selectedTemplate.Name;
        _lblDesc.Text = _selectedTemplate.Description;
        _lblMeta.Text = $"소재: {_selectedTemplate.TargetMaterial}  |  고정방식: {_selectedTemplate.FixingMethod}  |  단계 수: {_selectedTemplate.Nodes.Count}개";

        _lstSteps.Items.Clear();
        int step = 1;
        foreach (var n in _selectedTemplate.Nodes)
        {
            string timeStr = !string.IsNullOrEmpty(n.StartTime) ? $" [{n.StartTime}~{n.EndTime}]" : "";
            _lstSteps.Items.Add($"{step:D2}. [{n.MachineId}] {n.Title}{timeStr}");
            step++;
        }

        _btnDelete.Visible = !_selectedTemplate.IsBuiltIn;
    }

    private void ApplySelectedTemplate()
    {
        if (_selectedTemplate == null)
        {
            MessageBox.Show("적용할 템플릿을 선택해주세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DialogResult = DialogResult.OK;
    }

    private void DeleteSelectedTemplate()
    {
        if (_selectedTemplate == null || _selectedTemplate.IsBuiltIn) return;

        if (MessageBox.Show($"'{_selectedTemplate.Name}' 템플릿을 삭제하시겠습니까?", "템플릿 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            TemplateManager.DeleteTemplate(_selectedTemplate.Name);
            RefreshTemplateList();
        }
    }
}
