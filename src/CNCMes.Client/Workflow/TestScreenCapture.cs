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

        // 1. Capture Empty Canvas Guide with the new [✕ 닫기] button
        using (var bmpEmpty = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmpEmpty, new Rectangle(0, 0, 1920, 1080));
            string emptyPath = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\empty_workflow_guide_v151.png";
            bmpEmpty.Save(emptyPath, ImageFormat.Png);
            Console.WriteLine("SAVED_EMPTY_GUIDE_SCREEN: " + emptyPath);
        }

        // 2. Apply probe card template
        form.ApplyProbecardTemplate("프로브카드 인터포저 (PEEK 5T)");

        for (int i = 0; i < 25; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(50);
        }

        var canvasField = typeof(WorkflowMainForm).GetField("_canvas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var detailField = typeof(WorkflowMainForm).GetField("_detailPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (canvasField?.GetValue(form) is WorkflowCanvasControl canvas && canvas.Nodes.Count > 0 && detailField?.GetValue(form) is NodeDetailPanel detail)
        {
            // 사용자의 요청 3 검증:
            // "한공정의 흐름에 첫번째 소재의 재질과, 사이즈, 고정방식은 첫번째 노드를 그대로 따라가게 해. 그리고 특별히 다른건 작업자가 수동으로 바꾸게하고."
            // 첫 번째 노드를 선택하여 Size: 50*50*5, 재질: SUS304, 고정방식: 바이스 로 변경
            canvas.SelectedNode = canvas.Nodes[0];
            detail.BindNode(canvas.Nodes[0]);

            canvas.Nodes[0].MaterialSize = "50*50*5";
            canvas.Nodes[0].MaterialType = "SUS304";
            canvas.Nodes[0].FixingMethod = "바이스";
            canvas.Nodes[0].StartTime = "14:00";
            canvas.Nodes[0].EndTime = "14:40";

            // 첫 번째 노드의 소재 정보를 후속 공정 노드들에 전파
            canvas.PropagateFirstNodeMaterial("50*50*5", "SUS304", "바이스");

            // 후속 노드 중 3번째 노드(index 2)는 작업자가 수동으로 특별히 '황동', '50*50*3T'로 변경했다고 가정 (IsMaterialCustomized = true)
            if (canvas.Nodes.Count > 2)
            {
                canvas.Nodes[2].MaterialSize = "50*50*3T";
                canvas.Nodes[2].MaterialType = "황동 (Brass)";
                canvas.Nodes[2].IsMaterialCustomized = true;
            }

            // 첫 번째 노드가 다시 변경되어도 3번째 노드는 수동 변경값이 유지되는지 검증
            canvas.PropagateFirstNodeMaterial("50*50*5", "SUS304", "바이스");

            Console.WriteLine($"MATERIAL_INHERIT_TEST: Node0({canvas.Nodes[0].MaterialSize}, {canvas.Nodes[0].MaterialType}), Node1({canvas.Nodes[1].MaterialSize}, {canvas.Nodes[1].MaterialType}), Node2_Custom({canvas.Nodes[2].MaterialSize}, {canvas.Nodes[2].MaterialType})");

            // 사용자의 요청 1 검증: 타임라인 간격 조절 및 14:00 자동 정렬
            canvas.SetTimelineInterval(280f);
            canvas.ArrangeByTimeline(autoFitBaseHour: true);

            var baseMethod = typeof(WorkflowMainForm).GetMethod("UpdateTimelineBaseButtonText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            baseMethod?.Invoke(form, null);

            var intMethod = typeof(WorkflowMainForm).GetMethod("UpdateTimelineIntervalButtonText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            intMethod?.Invoke(form, null);

            // 첫 번째 노드 바인딩하여 상세 패널 표시
            detail.BindNode(canvas.Nodes[0]);
        }

        using (var bmp = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, 1920, 1080));
            string path = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\workflow_v151_screen.png";
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine("SAVED_V151_SCREEN: " + path);
        }
        form.Close();
    }
}
