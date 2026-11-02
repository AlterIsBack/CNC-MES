using System;
using System.Drawing;
using System.Windows.Forms;
using Palette = CNCMes.Client.Workflow.WorkflowMainForm.Palette;

namespace CNCMes.Client.Workflow;

/// <summary>
/// 작업자간 유동적인 서브인원 지시 및 위치 배치를 위한 실시간 다이얼로그
/// </summary>
public class SubWorkerDispatchDialog : Form
{
    private ComboBox _cboSubWorker = null!;
    private ComboBox _cboLocation = null!;
    private TextBox _txtOrderNumber = null!;
    private TextBox _txtPartName = null!;
    private TextBox _txtInstruction = null!;
    private ComboBox _cboTaskPreset = null!;

    public string SelectedSubWorker => _cboSubWorker.Text.Trim();
    public string SelectedLocation => _cboLocation.Text.Trim();
    public string OrderNumber => _txtOrderNumber.Text.Trim();
    public string PartName => _txtPartName.Text.Trim();
    public string Instruction => _txtInstruction.Text.Trim();

    public SubWorkerDispatchDialog(string defaultSubWorker = "이서브", string defaultLocation = "R01", string defaultOrder = "", string defaultPart = "")
    {
        InitializeComponents(defaultSubWorker, defaultLocation, defaultOrder, defaultPart);
    }

    private void InitializeComponents(string defaultSubWorker, string defaultLocation, string defaultOrder, string defaultPart)
    {
        Text = "👥 서브인원 실시간 작업 지시 및 위치 이동 지정";
        Size = new Size(580, 520);
        MinimumSize = new Size(520, 480);
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
            RowCount = 6,
            Padding = new Padding(16),
            Margin = new Padding(0)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));  // 0: Header Banner
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));  // 1: SubWorker & Location
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));  // 2: Order & Part
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35f));  // 3: Quick Preset
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 4: Instruction
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));  // 5: Action Buttons

        // 0. Header
        var pnlHeader = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg900, Padding = new Padding(10, 6, 10, 6) };
        var lblHeader = new Label
        {
            Text = "📢 현장 작업자간 실시간 서브인원 공유 및 유동적 지시 전송",
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold),
            ForeColor = Palette.Purple,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        pnlHeader.Controls.Add(lblHeader);
        mainLayout.Controls.Add(pnlHeader, 0, 0);

        // 1. SubWorker & Location
        var row1 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 3, 0, 3) };
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var pnlSub = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        pnlSub.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlSub.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlSub.Controls.Add(new Label { Text = "👤 대상 서브인원", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 0, 0);
        _cboSubWorker = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 9.2f), DropDownStyle = ComboBoxStyle.DropDown };
        _cboSubWorker.Items.AddRange(new object[] { "이서브", "김보조", "박지원", "최도움" });
        _cboSubWorker.Text = string.IsNullOrEmpty(defaultSubWorker) ? "이서브" : defaultSubWorker;
        pnlSub.Controls.Add(_cboSubWorker, 0, 1);

        var pnlLoc = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        pnlLoc.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlLoc.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlLoc.Controls.Add(new Label { Text = "📍 이동/작업 위치 (설비/구역)", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 0, 0);
        _cboLocation = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 9.2f), DropDownStyle = ComboBoxStyle.DropDown };
        _cboLocation.Items.AddRange(new object[] { "R01", "R02", "R03", "R04", "R05", "K01", "K02", "측정실", "세척실", "포장팀", "원자재실" });
        _cboLocation.Text = string.IsNullOrEmpty(defaultLocation) ? "R01" : defaultLocation;
        pnlLoc.Controls.Add(_cboLocation, 0, 1);

        row1.Controls.Add(pnlSub, 0, 0);
        row1.Controls.Add(pnlLoc, 1, 0);
        mainLayout.Controls.Add(row1, 0, 1);

        // 2. Order Number & Part Name
        var row2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 3, 0, 3) };
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var pnlOrd = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        pnlOrd.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlOrd.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlOrd.Controls.Add(new Label { Text = "작업지시번호", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 0, 0);
        _txtOrderNumber = new TextBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 9f), BorderStyle = BorderStyle.FixedSingle, Text = defaultOrder };
        pnlOrd.Controls.Add(_txtOrderNumber, 0, 1);

        var pnlPrt = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        pnlPrt.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlPrt.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlPrt.Controls.Add(new Label { Text = "품목(부품명)", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 0, 0);
        _txtPartName = new TextBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 9f), BorderStyle = BorderStyle.FixedSingle, Text = defaultPart };
        pnlPrt.Controls.Add(_txtPartName, 0, 1);

        row2.Controls.Add(pnlOrd, 0, 0);
        row2.Controls.Add(pnlPrt, 1, 0);
        mainLayout.Controls.Add(row2, 0, 2);

        // 3. Quick Preset Dropdown
        var row3 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 2, 0, 2) };
        row3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
        row3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row3.Controls.Add(new Label { Text = "⚡ 빠른 지시 템플릿:", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.AccentSky, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _cboTaskPreset = new ComboBox { Dock = DockStyle.Fill, BackColor = Palette.Bg850, ForeColor = Color.White, Font = new Font("맑은 고딕", 8.8f), DropDownStyle = ComboBoxStyle.DropDownList };
        _cboTaskPreset.Items.AddRange(new object[] {
            "-- 지시 프리셋 선택 시 아래 내용 자동 기입 --",
            "지정 설비로 즉시 이동하여 가공물 반전 및 클램핑 세팅 보조",
            "40X 현미경 하에서 미세 홀 마이크로 Burr 정밀 제거 및 외관 검수",
            "측정실로 가공품 인계 후 3차원 CMM 치수 측정 대기 및 협조",
            "초음파 정밀 세척기로 이송하여 15분 세척 및 에어 블로우 건조",
            "공구실에서 0.15mm 초경 드릴 신품 수령 후 Z축 옵셋 세팅 보조",
            "긴급 가공 정지 상태 확인 지원 및 칩 배출/절삭유 노즐 점검"
        });
        _cboTaskPreset.SelectedIndex = 0;
        _cboTaskPreset.SelectedIndexChanged += (s, e) =>
        {
            if (_cboTaskPreset.SelectedIndex > 0)
            {
                _txtInstruction.Text = _cboTaskPreset.SelectedItem?.ToString() ?? "";
            }
        };
        row3.Controls.Add(_cboTaskPreset, 1, 0);
        mainLayout.Controls.Add(row3, 0, 3);

        // 4. Instruction TextBox
        var pnlInst = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        pnlInst.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        pnlInst.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlInst.Controls.Add(new Label { Text = "📝 세부 지시 내용 (작업자간 공유 메시지)", Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Palette.TextSlate400, Dock = DockStyle.Fill }, 0, 0);

        _txtInstruction = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Palette.Bg850,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9.2f),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "서브인원에게 전달할 작업 내용, 주의사항, 이동 요청을 입력하세요."
        };
        pnlInst.Controls.Add(_txtInstruction, 0, 1);
        mainLayout.Controls.Add(pnlInst, 0, 4);

        // 5. Action Buttons
        var rowActions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 4, 0, 0) };
        rowActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        rowActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));

        var btnSend = new Button
        {
            Text = "⚡ 서브인원 실시간 지시 및 위치 배정 전송",
            BackColor = Palette.Purple,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Fill,
            Cursor = Cursors.Hand,
            Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold)
        };
        btnSend.FlatAppearance.BorderSize = 0;
        btnSend.Click += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(_txtInstruction.Text))
            {
                MessageBox.Show("서브인원에게 전달할 작업 지시 내용을 입력해주세요.", "입력 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtInstruction.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        };

        var btnCancel = new Button
        {
            Text = "✕ 닫기 (취소)",
            BackColor = Palette.Bg800,
            ForeColor = Palette.TextSlate300,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Fill,
            Cursor = Cursors.Hand,
            Font = new Font("맑은 고딕", 9f)
        };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

        rowActions.Controls.Add(btnSend, 0, 0);
        rowActions.Controls.Add(btnCancel, 1, 0);
        mainLayout.Controls.Add(rowActions, 0, 5);

        Controls.Add(mainLayout);
    }
}
