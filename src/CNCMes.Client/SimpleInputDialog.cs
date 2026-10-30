namespace CNCMes.Client;

public static class SimpleInputDialog
{
    public static string Show(string prompt, string title, string defaultValue = "")
    {
        using var form = new Form
        {
            Width = 460,
            Height = 200,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = new Font("맑은 고딕", 9.5f)
        };

        var lbl = new Label { Left = 20, Top = 20, Text = prompt, Width = 400, AutoSize = true, ForeColor = Color.FromArgb(56, 189, 248) };
        var txt = new TextBox { Left = 20, Top = 55, Width = 400, Text = defaultValue, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var btnOk = new Button { Text = "확인", Left = 230, Width = 90, Top = 100, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(56, 189, 248), ForeColor = Color.FromArgb(15, 23, 42), FlatStyle = FlatStyle.Flat };
        var btnCancel = new Button { Text = "취소", Left = 330, Width = 90, Top = 100, DialogResult = DialogResult.Cancel, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };

        form.Controls.Add(lbl);
        form.Controls.Add(txt);
        form.Controls.Add(btnOk);
        form.Controls.Add(btnCancel);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? txt.Text : "";
    }
}
