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

        // 1. Capture TemplateLoadDialog rendering (하단 버튼 바 가시성 완벽 검증)
        using (var dlg = new TemplateLoadDialog())
        {
            dlg.StartPosition = FormStartPosition.Manual;
            dlg.Location = new Point(100, 100);
            dlg.Show();
            for (int k = 0; k < 10; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
            using (var dlgBmp = new Bitmap(dlg.Width, dlg.Height))
            {
                dlg.DrawToBitmap(dlgBmp, new Rectangle(0, 0, dlg.Width, dlg.Height));
                string dlgPath = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\template_load_dialog_v152.png";
                dlgBmp.Save(dlgPath, ImageFormat.Png);
                Console.WriteLine("SAVED_TEMPLATE_DIALOG_SCREEN: " + dlgPath);
            }
            dlg.Close();
        }

        // 2. 다중 템플릿 한 화면 불러오기 검증 (프로브카드 표준 9공정 + 반도체 소켓 7공정 동시 배치)
        var allTemplates = TemplateManager.GetAllTemplates();
        if (allTemplates.Count >= 2)
        {
            var t1 = allTemplates[0]; // 프로브카드
            var t2 = allTemplates[1]; // 반도체 소켓

            var canvasField = typeof(WorkflowMainForm).GetField("_canvas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (canvasField?.GetValue(form) is WorkflowCanvasControl canvas)
            {
                canvas.Clear();

                // 템플릿 1 추가
                var (n1, e1, b1) = TemplateManager.InstantiateTemplate(t1, "WO-2026-001", t1.Name);
                if (b1.Count == 0 && n1.Count > 0)
                {
                    b1.Add(new WorkflowTitleBox
                    {
                        Title = $"[1공정군] {t1.Name} ({t1.TargetMaterial})",
                        ColorHex = "#0284c7",
                        X = 40,
                        Y = 60,
                        Width = 600,
                        Height = 320
                    });
                }
                canvas.Nodes.AddRange(n1);
                canvas.Edges.AddRange(e1);
                canvas.TitleBoxes.AddRange(b1);

                // 템플릿 2 추가 (Y축 아래로 오프셋)
                var (n2, e2, b2) = TemplateManager.InstantiateTemplate(t2, "WO-2026-002", t2.Name);
                float curMaxY = canvas.Nodes.Max(n => n.Y + n.Height);
                float minNewY = n2.Min(n => n.Y);
                float offsetY = (curMaxY + 120f) - minNewY;

                foreach (var n in n2) n.Y += offsetY;
                foreach (var b in b2) b.Y += offsetY;

                if (b2.Count == 0 && n2.Count > 0)
                {
                    b2.Add(new WorkflowTitleBox
                    {
                        Title = $"[2공정군] {t2.Name} ({t2.TargetMaterial})",
                        ColorHex = "#10b981",
                        X = 40,
                        Y = offsetY + 60,
                        Width = 600,
                        Height = 320
                    });
                }

                canvas.Nodes.AddRange(n2);
                canvas.Edges.AddRange(e2);
                canvas.TitleBoxes.AddRange(b2);

                canvas.ArrangeByTimeline(autoFitBaseHour: false);
                canvas.ResetView();

                Console.WriteLine($"MULTI_TEMPLATE_TEST: CanvasNodes={canvas.Nodes.Count}, TitleBoxes={canvas.TitleBoxes.Count}");
            }
        }

        for (int i = 0; i < 20; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(50);
        }

        using (var bmp = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, 1920, 1080));
            string path = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\workflow_multi_template_v152.png";
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine("SAVED_V152_SCREEN: " + path);
        }
        form.Close();
    }
}
