using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client.Workflow;

/// <summary>
/// 과거 완료 작업 및 전체 작업 다차원 검색 대화상자
/// 품번·품명·작업지시번호로 검색하고 당시 워크플로우를 그대로 열람할 수 있습니다.
/// </summary>
public class JobSearchDialog : Form
{
    private readonly WorkflowStorageManager _storageManager;
    private readonly string? _filterOrderNumber;
    private readonly string? _filterPartName;
    private readonly int _filterOrderId;
    private readonly int _filterPartId;

    private readonly TextBox _txtSearchKeyword;
    private readonly ComboBox _cboStatus;
    private readonly DataGridView _grid;
    private readonly Button _btnOpen;
    private readonly Button _btnClose;
    private readonly Label _lblSummary;

    public WorkUnitJob? SelectedJob { get; private set; }

    public JobSearchDialog(
        WorkflowStorageManager storageManager,
        string? filterOrderNumber = null,
        string? filterPartName = null,
        int filterOrderId = 0,
        int filterPartId = 0)
    {
        _storageManager = storageManager;
        _filterOrderNumber = filterOrderNumber;
        _filterPartName = filterPartName;
        _filterOrderId = filterOrderId;
        _filterPartId = filterPartId;

        bool isSpecificPartMode = !string.IsNullOrEmpty(_filterPartName);
        Text = isSpecificPartMode
            ? $"📜 [{_filterPartName}] 과거 작업 완료 이력 조회"
            : "🔍 CNC-MES 전체 작업 워크플로우 다차원 검색";

        Size = new Size(960, 620);
        MinimumSize = new Size(820, 500);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(11, 19, 35);
        ForeColor = Color.White;
        Font = new Font("맑은 고딕", 9f);

        // Top Filter Bar
        var pnlTop = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.FromArgb(17, 26, 44),
            Padding = new Padding(12, 12, 12, 8)
        };

        var lblPrompt = new Label
        {
            Text = "검색어 (품번/품명/작지번호):",
            Location = new Point(12, 18),
            AutoSize = true,
            ForeColor = Color.FromArgb(203, 213, 225),
            Font = new Font("맑은 고딕", 9f, FontStyle.Bold)
        };

        _txtSearchKeyword = new TextBox
        {
            Location = new Point(190, 15),
            Width = 240,
            BackColor = Color.FromArgb(30, 45, 70),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Text = isSpecificPartMode ? _filterPartName : ""
        };
        _txtSearchKeyword.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) await PerformSearchAsync(); };

        var lblStatus = new Label
        {
            Text = "상태:",
            Location = new Point(445, 18),
            AutoSize = true,
            ForeColor = Color.FromArgb(203, 213, 225)
        };

        _cboStatus = new ComboBox
        {
            Location = new Point(485, 15),
            Width = 110,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(30, 45, 70),
            ForeColor = Color.White
        };
        _cboStatus.Items.AddRange(new object[] { "전체", "완료 (Completed)", "진행중 (Active)", "재가공 (Rework)" });
        _cboStatus.SelectedIndex = isSpecificPartMode ? 1 : 0;
        _cboStatus.SelectedIndexChanged += async (s, e) => await PerformSearchAsync();

        var btnSearch = new Button
        {
            Text = "🔍 검색",
            Location = new Point(610, 13),
            Width = 90,
            Height = 30,
            BackColor = Color.FromArgb(14, 165, 233),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnSearch.FlatAppearance.BorderSize = 0;
        btnSearch.Click += async (s, e) => await PerformSearchAsync();

        var btnShowAll = new Button
        {
            Text = "전체 작업 보기",
            Location = new Point(710, 13),
            Width = 110,
            Height = 30,
            BackColor = Color.FromArgb(40, 60, 90),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnShowAll.FlatAppearance.BorderSize = 0;
        btnShowAll.Click += async (s, e) =>
        {
            _txtSearchKeyword.Clear();
            _cboStatus.SelectedIndex = 0;
            await PerformSearchAsync(ignorePartFilter: true);
        };

        pnlTop.Controls.AddRange(new Control[] { lblPrompt, _txtSearchKeyword, lblStatus, _cboStatus, btnSearch, btnShowAll });

        // Center Grid
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.FromArgb(10, 16, 28),
            ForeColor = Color.White,
            GridColor = Color.FromArgb(30, 45, 70),
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(20, 32, 54);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(56, 189, 248);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
        _grid.ColumnHeadersHeight = 34;
        _grid.RowTemplate.Height = 30;
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(14, 22, 38);
        _grid.DefaultCellStyle.ForeColor = Color.White;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(14, 165, 233);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;

        _grid.Columns.Add("JobId", "고유 식별자 (JobId)");
        _grid.Columns.Add("OrderNumber", "작업지시번호");
        _grid.Columns.Add("PartName", "품번 / 품명");
        _grid.Columns.Add("Status", "진행 상태");
        _grid.Columns.Add("NodeCount", "공정 수");
        _grid.Columns.Add("CompletedAt", "완료 / 저장일시");
        _grid.Columns.Add("CreatedBy", "작업자 / 담당");

        _grid.Columns["JobId"]!.FillWeight = 110;
        _grid.Columns["OrderNumber"]!.FillWeight = 85;
        _grid.Columns["PartName"]!.FillWeight = 130;
        _grid.Columns["Status"]!.FillWeight = 65;
        _grid.Columns["NodeCount"]!.FillWeight = 50;
        _grid.Columns["CompletedAt"]!.FillWeight = 95;
        _grid.Columns["CreatedBy"]!.FillWeight = 75;

        _grid.DoubleClick += (s, e) => OpenSelectedJob();

        // Bottom Action Bar
        var pnlBottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = Color.FromArgb(17, 26, 44),
            Padding = new Padding(16, 8, 16, 8)
        };

        _lblSummary = new Label
        {
            Text = "조회 중...",
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize = true
        };

        _btnClose = new Button
        {
            Text = "닫기",
            Dock = DockStyle.Right,
            Width = 90,
            BackColor = Color.FromArgb(51, 65, 85),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnClose.FlatAppearance.BorderSize = 0;
        _btnClose.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        _btnOpen = new Button
        {
            Text = "📂 선택한 당시 워크플로우 열기 (읽기 전용)",
            Dock = DockStyle.Right,
            Width = 280,
            BackColor = Color.FromArgb(5, 150, 105),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 10, 0)
        };
        _btnOpen.FlatAppearance.BorderSize = 0;
        _btnOpen.Click += (s, e) => OpenSelectedJob();

        var pnlBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        pnlBtns.Controls.AddRange(new Control[] { _btnClose, _btnOpen });

        pnlBottom.Controls.Add(_lblSummary);
        pnlBottom.Controls.Add(pnlBtns);

        Controls.Add(_grid);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);

        Shown += async (s, e) => await PerformSearchAsync();
    }

    private async Task PerformSearchAsync(bool ignorePartFilter = false)
    {
        _grid.Rows.Clear();
        _lblSummary.Text = "작업 내역을 검색하는 중...";

        string kw = _txtSearchKeyword.Text.Trim();
        string statusText = _cboStatus.Text;
        string? statusFilter = statusText switch
        {
            "완료 (Completed)" => "Completed",
            "진행중 (Active)" => "진행중",
            "재가공 (Rework)" => "재가공",
            _ => null
        };

        var allJobs = await _storageManager.LoadAllJobsAsync();

        var query = allJobs.AsEnumerable();

        if (!ignorePartFilter && !string.IsNullOrEmpty(_filterPartName))
        {
            query = query.Where(j =>
                (_filterPartId > 0 && j.PartId == _filterPartId) ||
                j.PartName.Contains(_filterPartName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(kw))
        {
            query = query.Where(j =>
                j.JobId.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                j.OrderNumber.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                j.PartName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                j.CreatedBy.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                j.Nodes.Any(n => n.Title.Contains(kw, StringComparison.OrdinalIgnoreCase) || n.Assignee.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrEmpty(statusFilter))
        {
            if (statusFilter == "Completed")
            {
                query = query.Where(j => j.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) || j.Status.Equals("완료", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                query = query.Where(j => j.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase));
            }
        }

        var results = query
            .OrderByDescending(j => j.CompletedAt ?? j.LastSavedAt)
            .ToList();

        foreach (var job in results)
        {
            string dateStr = (job.CompletedAt ?? job.LastSavedAt).ToString("yyyy-MM-dd HH:mm");
            string statusDisplay = job.Status switch
            {
                "Completed" => "🔒 완료",
                "완료" => "🔒 완료",
                "진행중" => "⚡ 진행중",
                "재가공" => "🔄 재가공",
                _ => job.Status
            };

            int idx = _grid.Rows.Add(
                job.JobId,
                job.OrderNumber,
                job.PartName,
                statusDisplay,
                $"{job.Nodes.Count}개",
                dateStr,
                string.IsNullOrEmpty(job.CreatedBy) ? "-" : job.CreatedBy
            );
            _grid.Rows[idx].Tag = job;
        }

        _lblSummary.Text = $"총 {results.Count}건의 작업 워크플로우가 조회되었습니다. (더블클릭 시 당시 워크플로우를 열람합니다)";
    }

    private void OpenSelectedJob()
    {
        if (_grid.CurrentRow?.Tag is WorkUnitJob job)
        {
            SelectedJob = job;
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            MessageBox.Show("열람할 작업을 목록에서 먼저 선택해 주세요.", "선택 안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
