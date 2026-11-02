using System.Drawing.Imaging;
using CNCMes.Client.Workflow;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client;

public static class TestScreenCapture
{
    public static void CaptureForm()
    {
        ApplicationConfiguration.Initialize();
        var api = new ApiClient();
        var session = new UserSession
        {
            Username = "a",
            DisplayName = "김철수 (작업자)",
            Role = "Worker",
            MachineId = "R01",
            SelectedMachines = new System.Collections.Generic.List<string> { "R01", "R02" }
        };

        var form = new WorkflowMainForm(api, session);
        form.Width = 1920;
        form.Height = 1080;
        form.FormBorderStyle = FormBorderStyle.None;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);

        form.Show();
        for (int i = 0; i < 20; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(50);
        }

        // Apply probe card template
        form.ApplyProbecardTemplate("프로브카드 인터포저 (PEEK 5T)");

        for (int i = 0; i < 25; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(50);
        }

        // Print debug
        var panelField = typeof(WorkflowMainForm).GetField("_detailPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (panelField?.GetValue(form) is NodeDetailPanel dp)
        {
            Console.WriteLine($"DEBUG_TAGS: Bounds={dp._pnlTagPresets.Bounds}, Visible={dp._pnlTagPresets.Visible}, Controls={dp._pnlTagPresets.Controls.Count}, Parent={dp._pnlTagPresets.Parent?.Name ?? dp._pnlTagPresets.Parent?.GetType().Name}");
            foreach (Control c in dp._pnlTagPresets.Controls)
            {
                Console.WriteLine($"  BTN: text='{c.Text}', bounds={c.Bounds}, visible={c.Visible}");
            }
        }

        using (var bmp = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, 1920, 1080));
            string path = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\workflow_v147_screen.png";
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine("SAVED_V147_SCREEN: " + path);
        }
        form.Close();
    }
}
