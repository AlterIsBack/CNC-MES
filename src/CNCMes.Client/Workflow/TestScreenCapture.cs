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

        // Access canvas and create nodes showing 4-way connection ports (Left, Right, Top, Bottom)
        var canvasField = typeof(WorkflowMainForm).GetField("_canvas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (canvasField?.GetValue(form) is WorkflowCanvasControl canvas)
        {
            canvas.Clear();

            // Create Node 1 (1차 가공)
            var n1 = new WorkflowNode
            {
                Id = "N01",
                Title = "1차 전면 황삭 가공",
                Category = NodeCategory.Machining,
                MachineId = "R01",
                Assignee = "김철수",
                Status = NodeStatus.Completed,
                MaterialSize = "150x120x5T",
                MaterialType = "PEEK",
                FixingMethod = "바이스",
                StartTime = "09:00",
                EndTime = "10:30",
                X = 120,
                Y = 140,
                Width = 240,
                Height = 100
            };

            // Create Node 2 (측정/검사 - 우측 연결)
            var n2 = new WorkflowNode
            {
                Id = "N02",
                Title = "1차 중간 정밀 측정 (CMM)",
                Category = NodeCategory.Inspection,
                MachineId = "측정실",
                Assignee = "박검사",
                Status = NodeStatus.Running,
                StartTime = "10:30",
                EndTime = "11:20",
                X = 480,
                Y = 140,
                Width = 240,
                Height = 100
            };

            // Create Node 3 (서브인원 후공정 - 아래로 연결: Top/Bottom 포트 연결 시연)
            var n3 = new WorkflowNode
            {
                Id = "N03",
                Title = "챔퍼 및 버(Burr) 제거",
                Category = NodeCategory.Collaboration,
                MachineId = "후공정",
                Assignee = "이서브",
                Status = NodeStatus.Ready,
                StartTime = "11:30",
                EndTime = "12:30",
                X = 480,
                Y = 320,
                Width = 240,
                Height = 100
            };

            // Create Node 4 (2차 가공 - 좌측으로 복귀 연결)
            var n4 = new WorkflowNode
            {
                Id = "N04",
                Title = "2차 미세피치 정삭 (홀가공)",
                Category = NodeCategory.Machining,
                MachineId = "R02",
                Assignee = "최가공",
                Status = NodeStatus.Ready,
                StartTime = "13:30",
                EndTime = "15:00",
                X = 120,
                Y = 320,
                Width = 240,
                Height = 100
            };

            canvas.Nodes.AddRange(new[] { n1, n2, n3, n4 });

            // Connect N1 (Right) -> N2 (Left) [수평 연결]
            canvas.AddEdge("N01", "N02", EdgeType.Sequence);

            // Connect N2 (Bottom) -> N3 (Top) [수직 연결: 요청하신 위/아래 노드점 활용!]
            canvas.AddEdge("N02", "N03", EdgeType.Delegation);

            // Connect N3 (Left) -> N4 (Right) [수평 역방향 연결]
            canvas.AddEdge("N03", "N04", EdgeType.Sequence);

            // Title Box for Visual Polish
            canvas.TitleBoxes.Add(new WorkflowTitleBox
            {
                Title = "반도체 프로브카드 4단 멀티공정 (상하좌우 4포트 연결망)",
                ColorHex = "#0284c7",
                X = 80,
                Y = 80,
                Width = 680,
                Height = 380
            });

            canvas.ResetView();
            canvas.Invalidate();
        }

        for (int i = 0; i < 20; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(50);
        }

        // Save v1.5.4 screen capture
        using (var bmp = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, 1920, 1080));
            string path = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\workflow_v154_screen.png";
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine("SAVED_V154_SCREEN: " + path);
        }

        // Also capture node close-up showing all 4 ports (Left, Right, Top, Bottom)
        using (var nodeBmp = new Bitmap(760, 420))
        {
            using var g = Graphics.FromImage(nodeBmp);
            g.Clear(Color.FromArgb(10, 15, 29));
            if (canvasField?.GetValue(form) is WorkflowCanvasControl c)
            {
                using var fullBmp = new Bitmap(1920, 1080);
                form.DrawToBitmap(fullBmp, new Rectangle(0, 0, 1920, 1080));
                g.DrawImage(fullBmp, new Rectangle(0, 0, 760, 420), new Rectangle(400, 110, 760, 420), GraphicsUnit.Pixel);
            }
            string cropPath = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\node_4ports_v154.png";
            nodeBmp.Save(cropPath, ImageFormat.Png);
            Console.WriteLine("SAVED_NODE_4PORTS: " + cropPath);
        }

        form.Close();
    }
}
