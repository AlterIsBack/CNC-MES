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
        var api = new ApiClient();
        var session = new UserSession
        {
            Username = "a",
            DisplayName = "김철수 (작업자)",
            Role = "Worker",
            MachineId = "R01",
            SelectedMachines = new System.Collections.Generic.List<string> { "R01", "R02" }
        };

        string baseDir = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6";

        // =========================================================================
        // 1. 1920x1080: 타임라인 시작 위치 수평 일치 & 상하좌우 노드점 포트 지정 연결 화면
        // =========================================================================
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

        var canvasField = typeof(WorkflowMainForm).GetField("_canvas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var detailPanelField = typeof(WorkflowMainForm).GetField("_detailPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (canvasField?.GetValue(form) is WorkflowCanvasControl canvas)
        {
            canvas.Clear();

            var n1 = new WorkflowNode
            {
                Id = "N01",
                Title = "1차 전면 황삭 가공",
                Category = NodeCategory.Machining,
                MachineId = "R01",
                Assignee = "김철수",
                Status = NodeStatus.Completed,
                MaterialSize = "150x120x5T",
                MaterialType = "PEEK 5T",
                FixingMethod = "진공 흡착 (진공 플레이트)",
                StartTime = "09:30",
                EndTime = "10:10",
                X = 140,
                Y = 160,
                Width = 240,
                Height = 100
            };

            var n2 = new WorkflowNode
            {
                Id = "N02",
                Title = "1차 중간측정 (CMM 정밀)",
                Category = NodeCategory.Inspection,
                MachineId = "측정실",
                Assignee = "박측정",
                Status = NodeStatus.Running,
                MaterialSize = "150x120x5T",
                MaterialType = "PEEK 5T",
                FixingMethod = "진공 흡착 (진공 플레이트)",
                StartTime = "10:20",
                EndTime = "11:00",
                X = 460,
                Y = 160,
                Width = 240,
                Height = 100
            };

            var n3 = new WorkflowNode
            {
                Id = "N03",
                Title = "2차 미세피치 정삭 (홀가공)",
                Category = NodeCategory.Machining,
                MachineId = "R02",
                Assignee = "최가공",
                Status = NodeStatus.Ready,
                MaterialSize = "150x120x5T",
                MaterialType = "PEEK 5T",
                FixingMethod = "진공 흡착 (진공 플레이트)",
                StartTime = "11:20",
                EndTime = "13:00",
                X = 460,
                Y = 360,
                Width = 240,
                Height = 100
            };

            var n4 = new WorkflowNode
            {
                Id = "N04",
                Title = "챔퍼 및 버(Burr) 제거",
                Category = NodeCategory.Collaboration,
                MachineId = "후공정",
                Assignee = "이서브",
                Status = NodeStatus.Ready,
                MaterialSize = "150x120x5T",
                MaterialType = "PEEK 5T",
                FixingMethod = "바이스",
                StartTime = "13:30",
                EndTime = "14:30",
                X = 140,
                Y = 360,
                Width = 240,
                Height = 100
            };

            canvas.Nodes.AddRange(new[] { n1, n2, n3, n4 });

            // 포트 지정 임의 연결:
            // N1(Right) -> N2(Left)
            canvas.AddEdge("N01", "N02", EdgeType.Sequence, "", "Right", "Left");
            // N2(Bottom) -> N3(Top) (사용자 요청: 아래-위 노드점 연결 지원!)
            canvas.AddEdge("N02", "N03", EdgeType.Sequence, "", "Bottom", "Top");
            // N3(Left) -> N4(Right) (포트 지정 연결)
            canvas.AddEdge("N03", "N04", EdgeType.Delegation, "", "Left", "Right");

            // N1 선택 상태로 세팅
            canvas.SetSelectedNodes(new[] { n1 });
            if (detailPanelField?.GetValue(form) is NodeDetailPanel detailPanel)
            {
                detailPanel.BindNode(n1);
            }

            canvas.ResetView();
            canvas.Invalidate();
        }

        for (int i = 0; i < 20; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(50);
        }

        // Capture 1: Aligned Screen
        using (var bmp1 = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmp1, new Rectangle(0, 0, 1920, 1080));
            string path1 = System.IO.Path.Combine(baseDir, "workflow_v160_aligned_screen.png");
            bmp1.Save(path1, ImageFormat.Png);
            Console.WriteLine("SAVED_ALIGNED: " + path1);
        }

        // Toggle Right Sidebar (숨기기/접기 테스트)
        var toggleMethod = typeof(WorkflowMainForm).GetMethod("ToggleRightSidebar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        toggleMethod?.Invoke(form, null);

        for (int i = 0; i < 20; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(50);
        }

        // Capture 2: Collapsed Screen
        using (var bmp2 = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmp2, new Rectangle(0, 0, 1920, 1080));
            string path2 = System.IO.Path.Combine(baseDir, "workflow_v160_collapsed_screen.png");
            bmp2.Save(path2, ImageFormat.Png);
            Console.WriteLine("SAVED_COLLAPSED: " + path2);
        }

        form.Close();

        // =========================================================================
        // 2. 1366x768 저해상도 노트북 환경 캡처 (버튼 잘림 없음 검증)
        // =========================================================================
        var formLow = new WorkflowMainForm(api, session);
        formLow.Width = 1366;
        formLow.Height = 768;
        formLow.FormBorderStyle = FormBorderStyle.None;
        formLow.StartPosition = FormStartPosition.Manual;
        formLow.Location = new Point(0, 0);

        formLow.Show();
        for (int i = 0; i < 20; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(50);
        }

        using (var bmp3 = new Bitmap(1366, 768))
        {
            formLow.DrawToBitmap(bmp3, new Rectangle(0, 0, 1366, 768));
            string path3 = System.IO.Path.Combine(baseDir, "workflow_v160_lowres_1366_screen.png");
            bmp3.Save(path3, ImageFormat.Png);
            Console.WriteLine("SAVED_LOWRES: " + path3);
        }

        formLow.Close();
    }
}
