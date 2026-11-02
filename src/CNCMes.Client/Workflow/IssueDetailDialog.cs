using System;
using System.Drawing;
using System.Windows.Forms;
using CNCMes.Client.Workflow.Models;
using Palette = CNCMes.Client.Workflow.WorkflowMainForm.Palette;

namespace CNCMes.Client.Workflow;

/// <summary>
/// 공정 이슈 등록, 원인 분석, 대응 및 조치 이력 관리 전용 팝업 폼
/// </summary>
public class IssueDetailDialog : Form
{
    private readonly WorkflowNode _node;
    private readonly string _currentWorker;

    private ComboBox _cboIssueType = null!;
    private TextBox _txtOccurred = null!;
    private TextBox _txtRootCause = null!;
    private TextBox _txtActionTaken = null!;
    private ComboBox _cboResolveStatus = null!;
    private Label _lblStatusInfo = null!;

    public bool WasResolved { get; private set; } = false;
    public bool DataSaved { get; private set; } = false;

    public IssueDetailDialog(WorkflowNode node, string currentWorker = "작업자")
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _currentWorker = string.IsNullOrWhiteSpace(currentWorker) ? "작업자" : currentWorker;

        InitializeComponents();
        LoadNodeIssueData();
    }

    private void InitializeComponents()
    {
        Text = $"🚨 공정 이슈 등록 및 조치 보고서 - [{_node.Title}] (설비: {_node.MachineId})";
        Size = new Size(620, 680);
        MinimumSize = new Size(580, 620);
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
            RowCount = 5,
            Padding = new Padding(14),
            Margin = new Padding(0)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f)); // Header Info Banner
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 32f));  // 1. 발생 내용
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 32f));  // 2. 원인 분석
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 36f));  // 3. 대응 및 조치
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f)); // Action Buttons Footer

        // -------------------------------------------------------------
        // Header Banner
        // -------------------------------------------------------------
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg900,
            Padding = new Padding(10, 6, 10, 6),
            Margin = new Padding(0, 0, 0, 8)
        };
        pnlHeader.Paint += (s, e) =>
        {
            using var pen = new Pen(_node.Status == NodeStatus.Issue ? Palette.Rose : Palette.Amber, 1.5f);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlHeader.Width - 1, pnlHeader.Height - 1);
        };

        var lblTitle = new Label
        {
            Text = $"🛠 [{_node.Title}] 공정 이슈 관리 | 담당설비: {_node.MachineId} | 작지: {_node.OrderNumber}",
            Font = new Font("맑은 고딕", 9.8f, FontStyle.Bold),
            ForeColor = Palette.TextWhite,
            Dock = DockStyle.Top,
            Height = 20
        };

        _lblStatusInfo = new Label
        {
            Text = _node.Status == NodeStatus.Issue
                ? "🔴 현재 상태: 이슈 발생 (가공 중단 또는 조치 대기)"
                : (_node.HasIssueHistory ? "⚠️ 현재 상태: 과거 이슈 조치 완료됨 (이력 보존 상태)" : "⚪ 현재 상태: 신규 이슈 등록"),
            Font = new Font("맑은 고딕", 8.5f),
            ForeColor = _node.Status == NodeStatus.Issue ? Palette.Rose : Palette.Amber,
            Dock = DockStyle.Bottom,
            Height = 18
        };
        pnlHeader.Controls.Add(_lblStatusInfo);
        pnlHeader.Controls.Add(lblTitle);
        mainLayout.Controls.Add(pnlHeader, 0, 0);

        // -------------------------------------------------------------
        // Section 1: 발생 내용 (What happened)
        // -------------------------------------------------------------
        var grpOccurred = CreateSectionGroup("1️⃣ 이슈 발생 내용 (불량 현상 및 발생 상황)");
        var pnlOccurredBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(4)
        };
        pnlOccurredBody.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
        pnlOccurredBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlOccurredBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        pnlOccurredBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var lblType = new Label { Text = "이슈/불량 유형:", ForeColor = Palette.TextMuted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _cboIssueType = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            BackColor = Palette.Bg800,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9f)
        };
        _cboIssueType.Items.AddRange(new object[]
        {
            "치수 오차 / 공차 초과",
            "공구 마모 / 파손 (Tool Broken)",
            "형상 가공 불량 / 단차 발생",
            "표면 스크래치 / 찍힘 (Burr)",
            "CNC 장비 알람 / 서보 이상",
            "가공 프로그램(NC) 좌표 오차",
            "소재 불량 / 변형 / 기포",
            "지그/바이스 클램핑 풀림",
            "기타 작업 이상"
        });
        _cboIssueType.SelectedIndex = 0;

        var lblOccurredDetail = new Label { Text = "발생 상세 내용:", ForeColor = Palette.TextMuted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Padding = new Padding(0, 4, 0, 0) };
        _txtOccurred = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Palette.Bg800,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9f)
        };

        pnlOccurredBody.Controls.Add(lblType, 0, 0);
        pnlOccurredBody.Controls.Add(_cboIssueType, 1, 0);
        pnlOccurredBody.Controls.Add(lblOccurredDetail, 0, 1);
        pnlOccurredBody.Controls.Add(_txtOccurred, 1, 1);
        grpOccurred.Controls.Add(pnlOccurredBody);
        mainLayout.Controls.Add(grpOccurred, 0, 1);

        // -------------------------------------------------------------
        // Section 2: 원인 분석 (Root Cause)
        // -------------------------------------------------------------
        var grpCause = CreateSectionGroup("2️⃣ 원인 분석 (Root Cause)");
        _txtRootCause = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Palette.Bg800,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9f),
            Margin = new Padding(4)
        };
        grpCause.Controls.Add(_txtRootCause);
        mainLayout.Controls.Add(grpCause, 0, 2);

        // -------------------------------------------------------------
        // Section 3: 대응 및 조치 (Countermeasure & Actions Taken)
        // -------------------------------------------------------------
        var grpAction = CreateSectionGroup("3️⃣ 대응 및 조치 내용 (Countermeasure & Actions)");
        _txtActionTaken = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Palette.Bg800,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 9f),
            Margin = new Padding(4)
        };
        grpAction.Controls.Add(_txtActionTaken);
        mainLayout.Controls.Add(grpAction, 0, 3);

        // -------------------------------------------------------------
        // Section 4: Bottom Action Buttons & Status Selector
        // -------------------------------------------------------------
        var pnlBottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0, 6, 0, 0)
        };
        pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f)); // Label
        pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f)); // Combo
        pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));  // Button 1
        pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));  // Button 2
        pnlBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        pnlBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));

        var lblResolveStatus = new Label
        {
            Text = "조치 후 복귀 상태:",
            ForeColor = Palette.TextMuted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("맑은 고딕", 8.5f)
        };
        _cboResolveStatus = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Palette.Bg800,
            ForeColor = Color.White,
            Font = new Font("맑은 고딕", 8.8f)
        };
        _cboResolveStatus.Items.AddRange(new object[] { "대기", "진행중", "측정", "완료" });
        _cboResolveStatus.SelectedIndex = 1; // 기본: 진행중

        var btnSaveOnly = new Button
        {
            Text = "💾 이슈 등록/수정 저장",
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg800,
            ForeColor = Palette.Amber,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(3, 1, 3, 1)
        };
        btnSaveOnly.FlatAppearance.BorderColor = Palette.Amber;
        btnSaveOnly.Click += (s, e) => SaveData(asResolved: false);

        var btnResolve = new Button
        {
            Text = "✅ 조치완료 확정 (이력보존)",
            Dock = DockStyle.Fill,
            BackColor = Palette.Green,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(3, 1, 3, 1)
        };
        btnResolve.FlatAppearance.BorderSize = 0;
        btnResolve.Click += (s, e) => SaveData(asResolved: true);

        var btnClose = new Button
        {
            Text = "닫기 (취소)",
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg900,
            ForeColor = Palette.TextMuted,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 8.5f),
            Cursor = Cursors.Hand,
            Margin = new Padding(3, 1, 3, 1)
        };
        btnClose.FlatAppearance.BorderColor = Palette.Border;
        btnClose.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        pnlBottom.Controls.Add(lblResolveStatus, 0, 0);
        pnlBottom.Controls.Add(_cboResolveStatus, 1, 0);
        pnlBottom.Controls.Add(btnSaveOnly, 2, 0);
        pnlBottom.Controls.Add(btnResolve, 3, 0);

        // Row 2: Close button stretched across right
        pnlBottom.Controls.Add(new Label { Text = $"작성자: {_currentWorker}", ForeColor = Palette.TextMuted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        pnlBottom.SetColumnSpan(pnlBottom.Controls[pnlBottom.Controls.Count - 1], 2);
        pnlBottom.Controls.Add(btnClose, 3, 1);

        mainLayout.Controls.Add(pnlBottom, 0, 4);
        Controls.Add(mainLayout);
    }

    private Panel CreateSectionGroup(string title)
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Bg900,
            Padding = new Padding(8, 26, 8, 8),
            Margin = new Padding(0, 0, 0, 8)
        };
        pnl.Paint += (s, e) =>
        {
            using var pen = new Pen(Palette.Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnl.Width - 1, pnl.Height - 1);

            using var font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
            using var brush = new SolidBrush(Palette.AccentSky);
            e.Graphics.DrawString(title, font, brush, 8, 6);
        };
        return pnl;
    }

    private void LoadNodeIssueData()
    {
        if (!string.IsNullOrEmpty(_node.IssueType))
        {
            _cboIssueType.Text = _node.IssueType;
        }

        _txtOccurred.Text = _node.IssueOccurredDetails;
        _txtRootCause.Text = _node.IssueRootCause;
        _txtActionTaken.Text = _node.IssueActionTaken;

        if (_node.HasIssueHistory && _node.IssueResolvedAt.HasValue)
        {
            _lblStatusInfo.Text = $"✅ 조치 완료됨: {_node.IssueResolvedAt.Value:yyyy-MM-dd HH:mm} (담당: {_node.IssueResolvedBy}) | 이력 수정 가능";
            _lblStatusInfo.ForeColor = Palette.Green;
        }
    }

    private void SaveData(bool asResolved)
    {
        if (string.IsNullOrWhiteSpace(_txtOccurred.Text) && string.IsNullOrWhiteSpace(_txtActionTaken.Text))
        {
            if (MessageBox.Show("발생 내용 또는 조치 내용이 비어있습니다. 그대로 저장하시겠습니까?", "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
        }

        _node.IssueType = _cboIssueType.Text.Trim();
        _node.IssueOccurredDetails = _txtOccurred.Text.Trim();
        _node.IssueRootCause = _txtRootCause.Text.Trim();
        _node.IssueActionTaken = _txtActionTaken.Text.Trim();
        _node.HasIssueHistory = true; // 이슈가 한 번이라도 기록되면 영구적으로 이력이 남음!

        if (!_node.IssueReportedAt.HasValue)
        {
            _node.IssueReportedAt = DateTime.Now;
        }

        if (asResolved)
        {
            _node.IssueResolvedAt = DateTime.Now;
            _node.IssueResolvedBy = _currentWorker;
            WasResolved = true;

            // 선택된 상태로 복귀
            _node.Status = _cboResolveStatus.Text switch
            {
                "대기" => NodeStatus.Ready,
                "진행중" => NodeStatus.Running,
                "측정" => NodeStatus.PendingCheck,
                "완료" => NodeStatus.Completed,
                _ => NodeStatus.Running
            };
        }
        else
        {
            // 이슈 발생 상태 유지
            _node.Status = NodeStatus.Issue;
            WasResolved = false;
        }

        DataSaved = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}
