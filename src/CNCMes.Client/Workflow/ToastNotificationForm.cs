using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CNCMes.Client.Workflow;

public enum ToastType
{
    WorkOrder,
    Notice,
    Chat,
    Info,
    Success
}

/// <summary>
/// 화면 우측 하단에 뜨는 모던한 실시간 토스트 알림 팝업
/// </summary>
public class ToastNotificationForm : Form
{
    private readonly System.Windows.Forms.Timer _dismissTimer;
    private readonly System.Windows.Forms.Timer _animTimer;
    private double _targetOpacity = 0.95;
    private bool _isClosing = false;
    private readonly Action? _onClickAction;

    public ToastNotificationForm(string title, string message, ToastType type = ToastType.Info, Action? onClickAction = null)
    {
        _onClickAction = onClickAction;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(360, 84);
        Opacity = 0.0;
        Cursor = Cursors.Hand;

        // 색상 테마
        Color accentColor = type switch
        {
            ToastType.WorkOrder => Color.FromArgb(14, 165, 233), // 하늘색 (작업지시)
            ToastType.Notice => Color.FromArgb(245, 158, 11),    // 주황색 (공지사항)
            ToastType.Chat => Color.FromArgb(16, 185, 129),      // 녹색 (채팅)
            ToastType.Success => Color.FromArgb(16, 185, 129),
            _ => Color.FromArgb(99, 102, 241)
        };

        string iconText = type switch
        {
            ToastType.WorkOrder => "📋",
            ToastType.Notice => "📢",
            ToastType.Chat => "💬",
            ToastType.Success => "✔",
            _ => "🔔"
        };

        BackColor = Color.FromArgb(15, 23, 42);
        DoubleBuffered = true;

        // UI 구성
        var lblIcon = new Label
        {
            Text = iconText,
            Font = new Font("Segoe UI Emoji", 18f),
            ForeColor = accentColor,
            Location = new Point(12, 16),
            Size = new Size(36, 48),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var lblTitle = new Label
        {
            Text = title,
            Font = new Font("맑은 고딕", 9.2f, FontStyle.Bold),
            ForeColor = accentColor,
            Location = new Point(52, 12),
            Size = new Size(270, 20),
            AutoEllipsis = true
        };

        var lblMessage = new Label
        {
            Text = message,
            Font = new Font("맑은 고딕", 8.5f),
            ForeColor = Color.FromArgb(226, 232, 240),
            Location = new Point(52, 34),
            Size = new Size(270, 38),
            AutoEllipsis = true
        };

        var btnClose = new Label
        {
            Text = "✕",
            Font = new Font("맑은 고딕", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(330, 8),
            Size = new Size(22, 22),
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        btnClose.Click += (s, e) => CloseToast();
        btnClose.MouseEnter += (s, e) => btnClose.ForeColor = Color.White;
        btnClose.MouseLeave += (s, e) => btnClose.ForeColor = Color.FromArgb(148, 163, 184);

        // 클릭 시 콜백
        void HandleClick(object? s, EventArgs e)
        {
            _onClickAction?.Invoke();
            CloseToast();
        }

        Click += HandleClick;
        lblIcon.Click += HandleClick;
        lblTitle.Click += HandleClick;
        lblMessage.Click += HandleClick;

        Controls.AddRange(new Control[] { lblIcon, lblTitle, lblMessage, btnClose });

        // 화면 우측 하단 위치 계산
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
        Location = new Point(workArea.Right - Width - 24, workArea.Bottom - Height - 24);

        // 애니메이션 타이머 (Fade In / Fade Out)
        _animTimer = new System.Windows.Forms.Timer { Interval = 25 };
        _animTimer.Tick += (s, e) =>
        {
            if (!_isClosing)
            {
                if (Opacity < _targetOpacity)
                    Opacity = Math.Min(_targetOpacity, Opacity + 0.1);
                else
                    _animTimer.Stop();
            }
            else
            {
                if (Opacity > 0.05)
                {
                    Opacity -= 0.12;
                }
                else
                {
                    _animTimer.Stop();
                    Close();
                    Dispose();
                }
            }
        };

        // 자동 닫힘 타이머 (4초)
        _dismissTimer = new System.Windows.Forms.Timer { Interval = 4200 };
        _dismissTimer.Tick += (s, e) =>
        {
            _dismissTimer.Stop();
            CloseToast();
        };

        Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var borderPen = new Pen(accentColor, 1.5f);
            using var leftBarBrush = new SolidBrush(accentColor);

            // 좌측 강조 바
            e.Graphics.FillRectangle(leftBarBrush, 0, 0, 5, Height);

            // 외곽 테두리
            e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _animTimer.Start();
        _dismissTimer.Start();
    }

    private void CloseToast()
    {
        if (_isClosing) return;
        _isClosing = true;
        _dismissTimer.Stop();
        _animTimer.Start();
    }

    public static void ShowToast(string title, string message, ToastType type = ToastType.Info, Action? onClick = null)
    {
        try
        {
            var toast = new ToastNotificationForm(title, message, type, onClick);
            toast.Show();
        }
        catch { }
    }
}
