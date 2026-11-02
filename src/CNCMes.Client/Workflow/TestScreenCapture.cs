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

        // Test: Select first node and test running/completed auto-times
        var canvasField = typeof(WorkflowMainForm).GetField("_canvas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var detailField = typeof(WorkflowMainForm).GetField("_detailPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (canvasField?.GetValue(form) is WorkflowCanvasControl canvas && canvas.Nodes.Count > 0 && detailField?.GetValue(form) is NodeDetailPanel detail)
        {
            var n1 = canvas.Nodes[0];
            canvas.SelectedNode = n1;
            detail.BindNode(n1);

            // 1. Simulate clicking 진행중
            var btnRun = typeof(NodeDetailPanel).GetField("_btnStatusRunning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(detail) as Button;
            btnRun?.PerformClick();
            Console.WriteLine($"RUN_CLICKED: Node StartTime='{n1.StartTime}', Status={n1.Status}");

            // 2. Simulate clicking 완료
            var btnComp = typeof(NodeDetailPanel).GetField("_btnStatusComplete", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(detail) as Button;
            btnComp?.PerformClick();
            Console.WriteLine($"COMPLETE_CLICKED: Node EndTime='{n1.EndTime}', Status={n1.Status}");

            // 3. Test NodeEditDialog rendering
            using var dlg = new NodeEditDialog(n1, "김철수 (작업자)");
            dlg.StartPosition = FormStartPosition.Manual;
            dlg.Location = new Point(100, 100);
            dlg.Show();
            for (int k = 0; k < 10; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
            using (var dlgBmp = new Bitmap(dlg.Width, dlg.Height))
            {
                dlg.DrawToBitmap(dlgBmp, new Rectangle(0, 0, dlg.Width, dlg.Height));
                string dlgPath = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\node_edit_dialog_v148.png";
                dlgBmp.Save(dlgPath, ImageFormat.Png);
                Console.WriteLine("SAVED_DIALOG_SCREEN: " + dlgPath);
            }
            dlg.Close();
        }

        using (var bmp = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, 1920, 1080));
            string path = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\workflow_v148_screen.png";
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine("SAVED_V148_SCREEN: " + path);
        }
        form.Close();
    }
}
