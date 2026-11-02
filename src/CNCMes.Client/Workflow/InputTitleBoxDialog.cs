using System;
using System.Drawing;
using System.Windows.Forms;
using Palette = CNCMes.Client.Workflow.WorkflowMainForm.Palette;

namespace CNCMes.Client.Workflow;

/// <summary>
/// 타이틀 그룹 박스 이름 및 테마 색상 설정 팝업
/// </summary>
public class InputTitleBoxDialog : Form
{
    private TextBox _txtTitle = null!;
    private ComboBox _cboColor = null!;

    public string ResultTitle => _txtTitle.Text.Trim();
    public string BoxTitle => ResultTitle;
    public string BoxColorHex => ResultColor;
    public string ResultColor => _cboColor.SelectedItem switch
    {
        "녹색 (Green)" => "#10b981",
        "주황색 (Amber)" => "#f59e0b",
        "보라색 (Purple)" => "#a855f7",
        "로즈 (Rose)" => "#ef4444",
        _ => "#0284c7" // 하늘색 (Sky Blue)
    };

    public InputTitleBoxDialog(string initialTitle = "1차 가공 공정군", string initialColor = "#0284c7")
    {
        InitializeComponents(initialTitle, initialColor);
    }

    private void InitializeComponents(string initialTitle, string initialColor)
    {
        Text = "📦 타이틀 그룹 박스 설정";
        Size = new Size(460, 260);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Palette.Bg950;
        ForeColor = Palette.TextWhite;
        Font = new Font("맑은 고딕", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16),
            Margin = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f)); // Title input
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f)); // Color input
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Spacer
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f)); // Action buttons

        // 1. Title Input
        var pnlTitle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        pnlTitle.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlTitle.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlTitle.Controls.Add(new Label { Text = "🏷 타이틀 그룹 명칭 (헤더에 표시될 공정 그룹명)", Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold), ForeColor = Color.FromArgb(148, 163, 184), Dock = DockStyle.Fill }, 0, 0);
        _txtTitle = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.5f),
            BorderStyle = BorderStyle.FixedSingle,
            Text = initialTitle
        };
        pnlTitle.Controls.Add(_txtTitle, 0, 1);
        layout.Controls.Add(pnlTitle, 0, 0);

        // 2. Color Input
        var pnlColor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        pnlColor.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlColor.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlColor.Controls.Add(new Label { Text = "🎨 그룹 박스 테마 색상", Font = new Font("맑은 고딕", 8.2f, FontStyle.Bold), ForeColor = Color.FromArgb(148, 163, 184), Dock = DockStyle.Fill }, 0, 0);
        _cboColor = new ComboBox
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.2f),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cboColor.Items.AddRange(new object[] { "하늘색 (Sky Blue)", "녹색 (Green)", "주황색 (Amber)", "보라색 (Purple)", "로즈 (Rose)" });
        _cboColor.SelectedIndex = initialColor switch
        {
            "#10b981" => 1,
            "#f59e0b" => 2,
            "#a855f7" => 3,
            "#ef4444" => 4,
            _ => 0
        };
        pnlColor.Controls.Add(_cboColor, 0, 1);
        layout.Controls.Add(pnlColor, 0, 1);

        // 3. Action Buttons
        var rowActions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        rowActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        rowActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));

        var btnOk = new Button
        {
            Text = "💾 적용",
            BackColor = Color.FromArgb(5, 150, 105),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Fill,
            Font = new Font("맑은 고딕", 9.2f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnOk.FlatAppearance.BorderSize = 0;
        btnOk.Click += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(_txtTitle.Text))
            {
                MessageBox.Show("타이틀 그룹 박스의 명칭을 입력해주세요.", "입력 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtTitle.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        };

        var btnCancel = new Button
        {
            Text = "✕ 취소",
            BackColor = Palette.Bg800,
            ForeColor = Color.FromArgb(203, 213, 225),
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Fill,
            Font = new Font("맑은 고딕", 9f),
            Cursor = Cursors.Hand
        };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

        rowActions.Controls.Add(btnOk, 0, 0);
        rowActions.Controls.Add(btnCancel, 1, 0);
        layout.Controls.Add(rowActions, 0, 3);

        Controls.Add(layout);
    }
}
