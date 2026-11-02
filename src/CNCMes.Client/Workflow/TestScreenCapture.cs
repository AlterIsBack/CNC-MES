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

        // Test: Timeline Arrangement & Layout preservation
        var canvasField = typeof(WorkflowMainForm).GetField("_canvas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var detailField = typeof(WorkflowMainForm).GetField("_detailPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (canvasField?.GetValue(form) is WorkflowCanvasControl canvas && canvas.Nodes.Count > 0 && detailField?.GetValue(form) is NodeDetailPanel detail)
        {
            // 사용자의 요청 시나리오: 가공시간이 16시부터 시작하는 공정들
            double curTime = 16.0; // 16:00 시작
            for (int i = 0; i < canvas.Nodes.Count; i++)
            {
                var n = canvas.Nodes[i];
                int sh = (int)curTime;
                int sm = (int)Math.Round((curTime - sh) * 60);
                n.StartTime = $"{sh:D2}:{sm:D2}";

                double endT = curTime + 0.5; // 30분 공정
                int eh = (int)endT;
                int em = (int)Math.Round((endT - eh) * 60);
                n.EndTime = $"{eh:D2}:{em:D2}";

                // 기존 형태 유지 검증: 짝수 노드는 상단(Y=120), 홀수 노드는 하단(Y=280)으로 2열 레이아웃 유지
                n.Y = (i % 2 == 0) ? 120f : 280f;

                curTime += 0.5;
            }

            // 1. Add Title Boxes
            var box1 = new WorkflowTitleBox
            {
                Title = "1차 가공 공정군 (소재 및 황삭 - 16:00 시작)",
                ColorHex = "#0284c7",
                X = 40,
                Y = 60,
                Width = 580,
                Height = 360
            };
            canvas.TitleBoxes.Add(box1);

            // 2. Turn on timeline guides
            canvas.ShowTimelineGuides = true;

            // 3. Perform ArrangeByTimeline (autoFitBaseHour: true)
            // 가공시간 16시에 맞춰 타임라인 기준 시간이 16:00으로 자동 설정되고,
            // 노드들의 상하 Y(120f, 280f) 형태가 보존되고, X는 16:00 눈금선(60px)부터 시간순 정렬되는지 확인!
            canvas.ArrangeByTimeline(autoFitBaseHour: true);

            var baseMethod = typeof(WorkflowMainForm).GetMethod("UpdateTimelineBaseButtonText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            baseMethod?.Invoke(form, null);

            Console.WriteLine($"TIMELINE_TEST: BaseHour={canvas.GetEffectiveTimelineBaseHour()}, Node0(X={canvas.Nodes[0].X}, Y={canvas.Nodes[0].Y}), Node1(X={canvas.Nodes[1].X}, Y={canvas.Nodes[1].Y})");
        }

        using (var bmp = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, 1920, 1080));
            string path = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\workflow_v150_timeline_screen.png";
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine("SAVED_V150_SCREEN: " + path);
        }
        form.Close();
    }
}
