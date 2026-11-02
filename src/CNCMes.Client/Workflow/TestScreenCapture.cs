using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
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

        // Test: Title Box addition & Timeline guides
        var canvasField = typeof(WorkflowMainForm).GetField("_canvas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var detailField = typeof(WorkflowMainForm).GetField("_detailPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (canvasField?.GetValue(form) is WorkflowCanvasControl canvas && canvas.Nodes.Count > 0 && detailField?.GetValue(form) is NodeDetailPanel detail)
        {
            var n1 = canvas.Nodes[0];
            canvas.SelectedNode = n1;
            detail.BindNode(n1);

            // 1. Add Title Boxes
            var box1 = new WorkflowTitleBox
            {
                Title = "1차 가공 공정군 (소재 및 황삭)",
                ColorHex = "#0284c7",
                X = 60,
                Y = 60,
                Width = 560,
                Height = 320
            };
            var box2 = new WorkflowTitleBox
            {
                Title = "2차 정밀 가공 및 검사 공정군",
                ColorHex = "#10b981",
                X = 660,
                Y = 60,
                Width = 560,
                Height = 320
            };
            canvas.TitleBoxes.Add(box1);
            canvas.TitleBoxes.Add(box2);

            // 2. Turn on timeline guides
            canvas.ShowTimelineGuides = true;

            // 3. Test node duplicate selection
            canvas.SelectedNodes.Clear();
            canvas.SelectedNodes.Add(n1);
            int prevCount = canvas.Nodes.Count;
            canvas.DuplicateSelectedNodes();
            Console.WriteLine($"DUPLICATE_TEST: Prev={prevCount}, After={canvas.Nodes.Count}, SelectedCount={canvas.SelectedNodes.Count}");

            // 4. Test SubWorkerDispatchDialog rendering
            using var subDlg = new SubWorkerDispatchDialog("이서브", "R01", "WO-2026-001", "프로브카드 인터포저");
            subDlg.StartPosition = FormStartPosition.Manual;
            subDlg.Location = new Point(120, 120);
            subDlg.Show();
            for (int k = 0; k < 10; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
            using (var dlgBmp = new Bitmap(subDlg.Width, subDlg.Height))
            {
                subDlg.DrawToBitmap(dlgBmp, new Rectangle(0, 0, subDlg.Width, subDlg.Height));
                string dlgPath = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\subworker_dispatch_dialog_v149.png";
                dlgBmp.Save(dlgPath, ImageFormat.Png);
                Console.WriteLine("SAVED_SUBWORKER_DIALOG_SCREEN: " + dlgPath);
            }
            subDlg.Close();
        }

        using (var bmp = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, 1920, 1080));
            string path = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\workflow_v149_screen.png";
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine("SAVED_V149_SCREEN: " + path);
        }
        form.Close();
    }
}
