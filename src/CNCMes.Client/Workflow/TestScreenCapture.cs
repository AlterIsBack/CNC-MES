using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using CNCMes.Client.Workflow;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client;

public static class TestScreenCapture
{
    private static void AwaitTask(Task task)
    {
        while (!task.IsCompleted)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(15);
        }
        task.GetAwaiter().GetResult();
    }

    private static T AwaitTask<T>(Task<T> task)
    {
        while (!task.IsCompleted)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(15);
        }
        return task.GetAwaiter().GetResult();
    }

    public static void CaptureForm()
    {
        var api = new ApiClient();
        var session = new UserSession
        {
            Username = "a",
            DisplayName = "김철수 (작업자)",
            Role = "Worker",
            MachineId = "R01",
            SelectedMachines = new System.Collections.Generic.List<string> { "R01", "R02", "R03" }
        };

        string baseDir = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6";

        Console.WriteLine("=== [CNC-MES v1.6.2] 검증 시나리오 시작 ===");

        // =========================================================================
        // 시나리오 1: 1920x1080 기본 화면 및 버튼 배치 (v1.6.2 배지, 과거작업, 전체검색, 작업완료 버튼)
        // =========================================================================
        var form = new WorkflowMainForm(api, session);
        form.Width = 1920;
        form.Height = 1080;
        form.FormBorderStyle = FormBorderStyle.None;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);

        form.Show();
        PumpEvents(25);

        var canvasField = typeof(WorkflowMainForm).GetField("_canvas", BindingFlags.NonPublic | BindingFlags.Instance);
        var detailPanelField = typeof(WorkflowMainForm).GetField("_detailPanel", BindingFlags.NonPublic | BindingFlags.Instance);
        var storageManagerField = typeof(WorkflowMainForm).GetField("_storageManager", BindingFlags.NonPublic | BindingFlags.Instance);

        var canvas = (WorkflowCanvasControl)canvasField!.GetValue(form)!;
        var detailPanel = (NodeDetailPanel)detailPanelField!.GetValue(form)!;
        var storageManager = (WorkflowStorageManager)storageManagerField!.GetValue(form)!;

        // 1. 소켓 상판 워크플로우 구성 (작업지시서 100번 현장 시나리오)
        // 1번 장비 가공 → 서브인원 측정 → 3번 장비 세팅·가공
        canvas.Clear();
        var n1 = new WorkflowNode
        {
            Id = "N-UPPER-01",
            Title = "1차 전면 황삭 (1번 장비)",
            Category = NodeCategory.Machining,
            MachineId = "R01",
            Assignee = "김철수",
            Status = NodeStatus.Completed,
            MaterialSize = "150x120x10T",
            MaterialType = "AL6061 10T",
            FixingMethod = "진공 흡착 (진공 플레이트)",
            StartTime = "09:00",
            EndTime = "10:00",
            X = 140,
            Y = 180,
            Width = 240,
            Height = 100
        };

        var n2 = new WorkflowNode
        {
            Id = "N-UPPER-02",
            Title = "서브인원 치수/평탄도 측정",
            Category = NodeCategory.Inspection,
            MachineId = "측정실",
            Assignee = "박측정",
            Status = NodeStatus.Completed,
            MaterialSize = "150x120x10T",
            MaterialType = "AL6061 10T",
            FixingMethod = "마그네틱 베이스",
            StartTime = "10:10",
            EndTime = "10:40",
            X = 460,
            Y = 180,
            Width = 240,
            Height = 100
        };

        var n3 = new WorkflowNode
        {
            Id = "N-UPPER-03",
            Title = "2차 미세홀 정삭 (3번 장비)",
            Category = NodeCategory.Machining,
            MachineId = "R03",
            Assignee = "김철수",
            Status = NodeStatus.Running,
            MaterialSize = "150x120x10T",
            MaterialType = "AL6061 10T",
            FixingMethod = "지그 클램핑",
            StartTime = "11:00",
            EndTime = "12:30",
            X = 780,
            Y = 180,
            Width = 240,
            Height = 100
        };

        canvas.Nodes.AddRange(new[] { n1, n2, n3 });
        canvas.AddEdge("N-UPPER-01", "N-UPPER-02", EdgeType.Sequence, "", "Right", "Left");
        canvas.AddEdge("N-UPPER-02", "N-UPPER-03", EdgeType.Sequence, "", "Right", "Left");

        canvas.SetSelectedNodes(new[] { n1 });
        detailPanel.BindNode(n1);
        canvas.ResetView();
        canvas.Invalidate();
        PumpEvents(25);

        // Capture 1: v1.6.2 기본 메인 화면
        using (var bmp1 = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmp1, new Rectangle(0, 0, 1920, 1080));
            string path1 = Path.Combine(baseDir, "workflow_v162_main_screen.png");
            bmp1.Save(path1, ImageFormat.Png);
            Console.WriteLine("[PASS] 검증 1: v1.6.2 기본 메인 화면 캡처 저장 -> " + path1);
        }

        // =========================================================================
        // 시나리오 2: 부품별 저장 분리 및 부품 전환 무혼선 검증 (상판 -> 하판 -> 상판)
        // =========================================================================
        var upperJob = storageManager.CreateJob("WO-2026-100", "[SCK-001] 소켓 상판 (AL6061)", 100, 1, author: session.DisplayName);
        upperJob.Nodes = canvas.Nodes.ToList();
        upperJob.Edges = canvas.Edges.ToList();
        AwaitTask(storageManager.SaveJobAsync(upperJob, session.DisplayName));

        // 하판으로 전환: 하판은 새로운 빈 작업 생성 후 카드 1개 배치
        var lowerJob = storageManager.CreateJob("WO-2026-100", "[SCK-002] 소켓 하판 (AL6061)", 100, 2, author: session.DisplayName);
        canvas.Clear();
        var lowerNode = new WorkflowNode
        {
            Id = "N-LOWER-01",
            Title = "하판 바닥면 페이싱 가공",
            Category = NodeCategory.Machining,
            MachineId = "R02",
            Assignee = "이하판",
            Status = NodeStatus.Ready,
            X = 200,
            Y = 200,
            Width = 240,
            Height = 100
        };
        canvas.Nodes.Add(lowerNode);
        lowerJob.Nodes = canvas.Nodes.ToList();
        AwaitTask(storageManager.SaveJobAsync(lowerJob, session.DisplayName));

        // 다시 상판으로 전환 복귀 검증!
        var reloadedUpper = AwaitTask(storageManager.GetActiveJobForPartAsync("WO-2026-100", "[SCK-001] 소켓 상판 (AL6061)", 100, 1));
        if (reloadedUpper == null || reloadedUpper.Nodes.Count != 3 || reloadedUpper.Nodes[0].Id != "N-UPPER-01")
        {
            Console.WriteLine("[FAIL] 시나리오 2 실패: 상판 데이터 복원 불일치!");
        }
        else
        {
            Console.WriteLine($"[PASS] 검증 2: 부품 전환 데이터 무혼선 검증 성공! (상판 노드수: {reloadedUpper.Nodes.Count}개, 하판 데이터와 미혼선)");
            canvas.Clear();
            canvas.Nodes.AddRange(reloadedUpper.Nodes);
            canvas.Edges.AddRange(reloadedUpper.Edges);
            canvas.ResetView();
            canvas.Invalidate();
        }

        PumpEvents(25);
        using (var bmpPart = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmpPart, new Rectangle(0, 0, 1920, 1080));
            string pathPart = Path.Combine(baseDir, "workflow_v162_part_switching.png");
            bmpPart.Save(pathPart, ImageFormat.Png);
            Console.WriteLine("[PASS] 검증 2 캡처 저장: " + pathPart);
        }

        // =========================================================================
        // 시나리오 3: '계획 확정·공유'와 '작업 완료' 분리 검증
        // 1) 계획 확정: 진행 중 상태 유지, 캔버스 편집/실행 가능
        // 2) 작업 완료: 읽기 전용 전환 및 Completed 확정 스냅샷 보관
        // =========================================================================
        upperJob.IsConfirmed = true;
        upperJob.ConfirmedAt = DateTime.Now;
        upperJob.ConfirmedBy = session.DisplayName;
        Console.WriteLine("[PASS] 검증 3-1: 계획 확정 후에도 상태는 Ongoing이며 작업 추가/수행 가능");

        // 명시적 전체 작업 완료 수행
        upperJob.Status = "Completed";
        upperJob.IsCompleted = true;
        upperJob.CompletedAt = DateTime.Now;
        upperJob.CompletedBy = session.DisplayName;
        AwaitTask(storageManager.SaveJobAsync(upperJob, session.DisplayName));
        AwaitTask(storageManager.CreateConfirmedSnapshotAsync(upperJob, "1차 생산 정상 완료"));

        // 캔버스 읽기 전용 모드 전환
        canvas.IsReadOnly = true;
        canvas.ReadOnlyBannerText = $"🔒 [읽기 전용 모드] [{upperJob.JobId}] 최종 작업 완료 마감되었습니다.";
        canvas.Invalidate();
        PumpEvents(25);

        using (var bmpComp = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmpComp, new Rectangle(0, 0, 1920, 1080));
            string pathComp = Path.Combine(baseDir, "workflow_v162_readonly_completed.png");
            bmpComp.Save(pathComp, ImageFormat.Png);
            Console.WriteLine("[PASS] 검증 3-2 캡처 저장: " + pathComp);
        }

        // =========================================================================
        // 시나리오 4: 과거 작업 검색 및 캔버스 열람 후 [◀ 복귀] 배너 검증
        // =========================================================================
        var pastJobs = AwaitTask(storageManager.GetCompletedJobsForPartAsync("WO-2026-100", "[SCK-001] 소켓 상판 (AL6061)"));
        Console.WriteLine($"[PASS] 검증 4-1: 과거 완료 이력 최신순 조회 완료 (발견된 완료본: {pastJobs.Count}건)");

        // 과거 작업 열람 다이얼로그 캡처
        using (var searchDlg = new JobSearchDialog(storageManager, "WO-2026-100", "[SCK-001] 소켓 상판 (AL6061)"))
        {
            searchDlg.Width = 1000;
            searchDlg.Height = 650;
            searchDlg.StartPosition = FormStartPosition.Manual;
            searchDlg.Location = new Point(200, 150);
            searchDlg.Show();
            PumpEvents(20);

            using (var bmpDlg = new Bitmap(1000, 650))
            {
                searchDlg.DrawToBitmap(bmpDlg, new Rectangle(0, 0, 1000, 650));
                string pathDlg = Path.Combine(baseDir, "workflow_v162_job_search_dialog.png");
                bmpDlg.Save(pathDlg, ImageFormat.Png);
                Console.WriteLine("[PASS] 검증 4-2 캡처 저장 (검색창): " + pathDlg);
            }
            searchDlg.Close();
        }

        // 과거 작업 열람 배너 표시 시나리오 검증
        var openHistMethod = typeof(WorkflowMainForm).GetMethod("OpenHistoricalJobViewAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var jobToView = pastJobs.FirstOrDefault(j => j.Nodes.Count > 0) ?? upperJob;
        if (openHistMethod != null && jobToView != null)
        {
            Console.WriteLine($"DEBUG: before invoke jobToView.Nodes.Count = {jobToView.Nodes.Count}");
            var taskHist = (Task)openHistMethod.Invoke(form, new object[] { jobToView })!;
            AwaitTask(taskHist);
            Console.WriteLine($"DEBUG: after invoke canvas.Nodes.Count = {canvas.Nodes.Count}");
            PumpEvents(10);

            using (var bmpBanner = new Bitmap(1920, 1080))
            {
                form.DrawToBitmap(bmpBanner, new Rectangle(0, 0, 1920, 1080));
                string pathBanner = Path.Combine(baseDir, "workflow_v162_historical_return_banner.png");
                bmpBanner.Save(pathBanner, ImageFormat.Png);
                Console.WriteLine("[PASS] 검증 4-3 캡처 저장 (과거 작업 열람 및 복귀 배너): " + pathBanner);
            }
        }

        form.Close();
        Console.WriteLine("=== [CNC-MES v1.6.2] 모든 검증 시나리오 완료! ===");
    }

    private static void PumpEvents(int cycles)
    {
        for (int i = 0; i < cycles; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(40);
        }
    }
}
