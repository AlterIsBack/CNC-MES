using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client.Workflow;

public class WorkflowTemplateItem
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string TargetMaterial { get; set; } = "PEEK 5T";
    public string FixingMethod { get; set; } = "진공흡착";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsBuiltIn { get; set; } = false;
    public List<WorkflowNode> Nodes { get; set; } = new();
    public List<WorkflowEdge> Edges { get; set; } = new();
}

public static class TemplateManager
{
    private static readonly string TemplateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CNCMes", "templates");

    static TemplateManager()
    {
        try
        {
            if (!Directory.Exists(TemplateDir))
                Directory.CreateDirectory(TemplateDir);
        }
        catch { }
    }

    public static List<WorkflowTemplateItem> GetAllTemplates()
    {
        var list = new List<WorkflowTemplateItem>();

        // 1. Add Default Built-In Templates
        list.Add(CreateProbecardBuiltIn());
        list.Add(CreateSocketUpperBuiltIn());
        list.Add(CreateJigBlockBuiltIn());

        // 2. Load User-Saved Templates from Disk
        try
        {
            if (Directory.Exists(TemplateDir))
            {
                var files = Directory.GetFiles(TemplateDir, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var item = JsonSerializer.Deserialize<WorkflowTemplateItem>(json);
                        if (item != null && !string.IsNullOrWhiteSpace(item.Name))
                        {
                            item.IsBuiltIn = false;
                            list.Add(item);
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        return list;
    }

    public static void SaveTemplate(string name, string description, string material, string fixingMethod, List<WorkflowNode> nodes, List<WorkflowEdge> edges)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("템플릿 이름을 입력해주세요.");

        var item = new WorkflowTemplateItem
        {
            Name = name.Trim(),
            Description = description.Trim(),
            TargetMaterial = material,
            FixingMethod = fixingMethod,
            CreatedAt = DateTime.Now,
            IsBuiltIn = false
        };

        // Deep clone nodes and edges
        var (clonedNodes, clonedEdges) = CloneWorkflow(nodes, edges);
        item.Nodes = clonedNodes;
        item.Edges = clonedEdges;

        string sanitized = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrEmpty(sanitized)) sanitized = "template_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filePath = Path.Combine(TemplateDir, $"{sanitized}.json");

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(item, options);
        File.WriteAllText(filePath, json);
    }

    public static bool DeleteTemplate(string name)
    {
        try
        {
            string sanitized = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim();
            string filePath = Path.Combine(TemplateDir, $"{sanitized}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// 템플릿을 새로운 인스턴스로 복제하여 고유 노드 ID를 재할당하고 엣지 연결을 완벽 복원
    /// </summary>
    public static (List<WorkflowNode> Nodes, List<WorkflowEdge> Edges) InstantiateTemplate(WorkflowTemplateItem template, string orderNumber = "WO-2026-001", string partName = "")
    {
        string pName = string.IsNullOrWhiteSpace(partName) ? template.Name : partName;
        var (nodes, edges) = CloneWorkflow(template.Nodes, template.Edges);

        foreach (var node in nodes)
        {
            node.OrderNumber = orderNumber;
            node.PartName = pName;
            node.Status = NodeStatus.Ready;
            node.StartedAt = null;
            node.CompletedAt = null;
        }

        return (nodes, edges);
    }

    private static (List<WorkflowNode> Nodes, List<WorkflowEdge> Edges) CloneWorkflow(List<WorkflowNode> nodes, List<WorkflowEdge> edges)
    {
        var idMap = new Dictionary<string, string>();
        var newNodes = new List<WorkflowNode>();

        foreach (var old in nodes)
        {
            string newId = Guid.NewGuid().ToString("N")[..8];
            idMap[old.Id] = newId;

            var copy = new WorkflowNode
            {
                Id = newId,
                Title = old.Title,
                Category = old.Category,
                Status = old.Status,
                MachineId = old.MachineId,
                Assignee = old.Assignee,
                Details = old.Details,
                NextProcess = old.NextProcess,
                StartTime = old.StartTime,
                EndTime = old.EndTime,
                MaterialSize = old.MaterialSize,
                MaterialType = old.MaterialType,
                FixingMethod = old.FixingMethod,
                SubWorkerName = old.SubWorkerName,
                SubWorkerInstruction = old.SubWorkerInstruction,
                X = old.X,
                Y = old.Y,
                Width = old.Width,
                Height = old.Height
            };
            newNodes.Add(copy);
        }

        var newEdges = new List<WorkflowEdge>();
        foreach (var old in edges)
        {
            if (idMap.TryGetValue(old.FromNodeId, out var newFrom) &&
                idMap.TryGetValue(old.ToNodeId, out var newTo))
            {
                newEdges.Add(new WorkflowEdge
                {
                    Id = Guid.NewGuid().ToString("N")[..8],
                    FromNodeId = newFrom,
                    ToNodeId = newTo,
                    Type = old.Type,
                    Label = old.Label
                });
            }
        }

        return (newNodes, newEdges);
    }

    private static WorkflowTemplateItem CreateProbecardBuiltIn()
    {
        var item = new WorkflowTemplateItem
        {
            Name = "🎯 프로브카드 표준 9공정 (PEEK 5T)",
            Description = "도면확인 -> 소재검수 -> 베이스판교체 -> 1차황삭 -> CMM측정 -> 2차미세정삭 -> 버제거 -> 비전검사 -> 세척포장",
            TargetMaterial = "PEEK 5T",
            FixingMethod = "진공 흡착 (진공 플레이트)",
            IsBuiltIn = true
        };

        var n1 = new WorkflowNode { Id = "p1", Title = "준비작업 (도면 & NC 확인)", Category = NodeCategory.Preparation, MachineId = "R01", StartTime = "08:30", EndTime = "09:00", MaterialSize = "150x120x5T", MaterialType = "PEEK 5T", FixingMethod = "진공 흡착 (진공 플레이트)", Details = "[시작시간]: 08:30\r\n[종료시간]: 09:00\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 프로브카드 도면(DWG) 치수 및 마이크로 피치 공차(±0.003) 확인\r\n- 가공 NC 프로그램 로드 및 툴 시트(Tool Sheet) 점검" };
        var n2 = new WorkflowNode { Id = "p2", Title = "소재작업 (치수 & 평면도 검수)", Category = NodeCategory.Preparation, MachineId = "R01", StartTime = "09:00", EndTime = "09:30", MaterialSize = "150x120x5T", MaterialType = "PEEK 5T", FixingMethod = "진공 흡착 (진공 플레이트)", Details = "[시작시간]: 09:00\r\n[종료시간]: 09:30\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- PEEK 소재 5T 정밀 평면도 측정 및 버(Burr) 사전 제거\r\n- 마이크로미터 두께 실측 검사 (5.000 ±0.005mm)" };
        var n3 = new WorkflowNode { Id = "p3", Title = "베이스판 교체 & 원점 세팅", Category = NodeCategory.Preparation, MachineId = "R01", StartTime = "09:30", EndTime = "10:10", MaterialSize = "150x120x5T", MaterialType = "PEEK 5T", FixingMethod = "진공 흡착 (진공 플레이트)", Details = "[시작시간]: 09:30\r\n[종료시간]: 10:10\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 전용 진공 흡착 베이스판 교체 장착 및 수평 레벨링 다이얼게이지 세팅\r\n- 공작물 원점(G54) 터치 프로브 정밀 셋업" };
        var n4 = new WorkflowNode { Id = "p4", Title = "1차 전면 황삭 (면가공)", Category = NodeCategory.Machining, MachineId = "R01", StartTime = "10:10", EndTime = "11:20", MaterialSize = "150x120x5T", MaterialType = "PEEK 5T", FixingMethod = "진공 흡착 (진공 플레이트)", Details = "[시작시간]: 10:10\r\n[종료시간]: 11:20\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 전면 기준면 페이스밀링 황삭 가공 진행\r\n- 절삭유 분사 및 진공 흡착 압력 게이지 상시 모니터링" };
        var n5 = new WorkflowNode { Id = "p5", Title = "1차 중간측정 (CMM 정밀측정)", Category = NodeCategory.Inspection, MachineId = "측정실", StartTime = "11:20", EndTime = "11:50", MaterialSize = "150x120x5T", MaterialType = "PEEK 5T", FixingMethod = "진공 흡착 (진공 플레이트)", Details = "[시작시간]: 11:20\r\n[종료시간]: 11:50\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 1차 가공 후 평면도 및 기준 가이드홀 3차원 CMM 측정\r\n- 홀간 피치 누적 공차 ±0.003mm 규격 합격 판정" };
        var n6 = new WorkflowNode { Id = "p6", Title = "2차 미세피치 정삭 (홀가공)", Category = NodeCategory.Machining, MachineId = "R02", StartTime = "13:00", EndTime = "14:30", MaterialSize = "150x120x5T", MaterialType = "PEEK 5T", FixingMethod = "진공 흡착 (진공 플레이트)", Details = "[시작시간]: 13:00\r\n[종료시간]: 14:30\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 초경 0.15mm 극소경 드릴 마이크로 홀 고속 정밀가공\r\n- 80㎛ 피치 미세 패턴 가공 및 40,000 RPM 스핀들 고정밀 제어" };
        var n7 = new WorkflowNode { Id = "p7", Title = "챔퍼 및 버(Burr) 제거", Category = NodeCategory.Collaboration, MachineId = "후공정", StartTime = "14:30", EndTime = "15:10", MaterialSize = "150x120x5T", MaterialType = "PEEK 5T", FixingMethod = "진공 흡착 (진공 플레이트)", SubWorkerName = "이서브", SubWorkerInstruction = "40X 현미경 하에서 미세 홀 Burr 수작업 전수 제거 요망", Details = "[시작시간]: 14:30\r\n[종료시간]: 15:10\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 40배율 광학 현미경 하에서 미세 홀 Burr 수작업 정밀 제거\r\n- 초음파 엣지 챔퍼링 수작업 마무리 손질" };
        var n8 = new WorkflowNode { Id = "p8", Title = "최종 비전 정밀검사", Category = NodeCategory.Inspection, MachineId = "품질팀", StartTime = "15:10", EndTime = "16:00", MaterialSize = "150x120x5T", MaterialType = "PEEK 5T", FixingMethod = "진공 흡착 (진공 플레이트)", Details = "[시작시간]: 15:10\r\n[종료시간]: 16:00\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 비접촉 광학 비전 측정기 전수 피치 및 진원도 검사\r\n- 최종 검사 성적서 발행 및 합격 필증 부착" };
        var n9 = new WorkflowNode { Id = "p9", Title = "초음파 세척 & ESD 포장", Category = NodeCategory.PostProcess, MachineId = "세척실", StartTime = "16:00", EndTime = "16:40", MaterialSize = "150x120x5T", MaterialType = "PEEK 5T", FixingMethod = "진공 흡착 (진공 플레이트)", Details = "[시작시간]: 16:00\r\n[종료시간]: 16:40\r\n[Size]: 150x120x5T\r\n[재질]: PEEK 5T\r\n[고정방식]: 진공 흡착 (진공 플레이트)\r\n- 무수에탄올 3단계 초음파 정밀 세척 및 클린 에어 건조\r\n- 대전방지(ESD) 진공 팩킹 및 바코드 라벨링 출고 준비" };

        item.Nodes.AddRange(new[] { n1, n2, n3, n4, n5, n6, n7, n8, n9 });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "p1", ToNodeId = "p2" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "p2", ToNodeId = "p3" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "p3", ToNodeId = "p4" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "p4", ToNodeId = "p5" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "p5", ToNodeId = "p6" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "p6", ToNodeId = "p7", Type = EdgeType.Delegation });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "p7", ToNodeId = "p8" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "p8", ToNodeId = "p9" });

        return item;
    }

    private static WorkflowTemplateItem CreateSocketUpperBuiltIn()
    {
        var item = new WorkflowTemplateItem
        {
            Name = "⚡ 반도체 소켓 상판 7공정 (AL6061)",
            Description = "도면확인/원점세팅 -> 1차전면황삭 -> 초물측정 -> 2차배면정삭 -> 탭/면취 -> 세척 -> 최종검사",
            TargetMaterial = "AL6061-T6",
            FixingMethod = "정밀 바이스 고정",
            IsBuiltIn = true
        };

        var n1 = new WorkflowNode { Id = "s1", Title = "프로그램 작성 & 도면 확인", Category = NodeCategory.Preparation, MachineId = "R01", StartTime = "08:30", EndTime = "09:00", MaterialSize = "120x80x10T", MaterialType = "AL6061-T6", FixingMethod = "정밀 바이스 고정", Details = "[시작시간]: 08:30\r\n[종료시간]: 09:00\r\n[Size]: 120x80x10T\r\n[재질]: AL6061-T6\r\n[고정방식]: 정밀 바이스 고정\r\n- CAD/CAM 도면 검토 및 NC프로그램 시뮬레이션 완료" };
        var n2 = new WorkflowNode { Id = "s2", Title = "소재 준비 & 1차 원점 세팅", Category = NodeCategory.Preparation, MachineId = "R01", StartTime = "09:00", EndTime = "09:30", MaterialSize = "120x80x10T", MaterialType = "AL6061-T6", FixingMethod = "정밀 바이스 고정", Details = "[시작시간]: 09:00\r\n[종료시간]: 09:30\r\n[Size]: 120x80x10T\r\n[재질]: AL6061-T6\r\n[고정방식]: 정밀 바이스 고정\r\n- 정밀 바이스 장착 및 공작물 G54 좌표계 정밀 설정" };
        var n3 = new WorkflowNode { Id = "s3", Title = "1차 전면 황삭 (면가공)", Category = NodeCategory.Machining, MachineId = "R01", StartTime = "09:30", EndTime = "10:30", MaterialSize = "120x80x10T", MaterialType = "AL6061-T6", FixingMethod = "정밀 바이스 고정", Details = "[시작시간]: 09:30\r\n[종료시간]: 10:30\r\n[Size]: 120x80x10T\r\n[재질]: AL6061-T6\r\n[고정방식]: 정밀 바이스 고정\r\n- 엔드밀 고속 황삭 및 포켓 밀링 가공" };
        var n4 = new WorkflowNode { Id = "s4", Title = "초물 치수 중간측정", Category = NodeCategory.Inspection, MachineId = "측정실", StartTime = "10:30", EndTime = "11:00", MaterialSize = "120x80x10T", MaterialType = "AL6061-T6", FixingMethod = "정밀 바이스 고정", Details = "[시작시간]: 10:30\r\n[종료시간]: 11:00\r\n[Size]: 120x80x10T\r\n[재질]: AL6061-T6\r\n[고정방식]: 정밀 바이스 고정\r\n- 초품 두께 및 공차 치수 정밀 버니어 측정 합격" };
        var n5 = new WorkflowNode { Id = "s5", Title = "2차 배면 정밀 정삭", Category = NodeCategory.Machining, MachineId = "R02", StartTime = "13:00", EndTime = "14:20", MaterialSize = "120x80x10T", MaterialType = "AL6061-T6", FixingMethod = "정밀 바이스 고정", Details = "[시작시간]: 13:00\r\n[종료시간]: 14:20\r\n[Size]: 120x80x10T\r\n[재질]: AL6061-T6\r\n[고정방식]: 정밀 바이스 고정\r\n- 배면 반전 고정 및 고속 정삭 가공" };
        var n6 = new WorkflowNode { Id = "s6", Title = "초음파 세척 & 에어건 건조", Category = NodeCategory.PostProcess, MachineId = "세척실", StartTime = "14:20", EndTime = "15:00", MaterialSize = "120x80x10T", MaterialType = "AL6061-T6", FixingMethod = "정밀 바이스 고정", Details = "[시작시간]: 14:20\r\n[종료시간]: 15:00\r\n[Size]: 120x80x10T\r\n[재질]: AL6061-T6\r\n[고정방식]: 정밀 바이스 고정\r\n- 절삭유 초음파 완벽 세척 후 보호비닐 포장" };
        var n7 = new WorkflowNode { Id = "s7", Title = "최종 검사 및 포장/납품", Category = NodeCategory.Inspection, MachineId = "품질팀", StartTime = "15:00", EndTime = "15:40", MaterialSize = "120x80x10T", MaterialType = "AL6061-T6", FixingMethod = "정밀 바이스 고정", Details = "[시작시간]: 15:00\r\n[종료시간]: 15:40\r\n[Size]: 120x80x10T\r\n[재질]: AL6061-T6\r\n[고정방식]: 정밀 바이스 고정\r\n- 전수 외관 및 치수 검사 합격" };

        item.Nodes.AddRange(new[] { n1, n2, n3, n4, n5, n6, n7 });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "s1", ToNodeId = "s2" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "s2", ToNodeId = "s3" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "s3", ToNodeId = "s4" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "s4", ToNodeId = "s5" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "s5", ToNodeId = "s6" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "s6", ToNodeId = "s7" });

        return item;
    }

    private static WorkflowTemplateItem CreateJigBlockBuiltIn()
    {
        var item = new WorkflowTemplateItem
        {
            Name = "🔧 고정밀 지그 베이스 5공정 (SUS304)",
            Description = "도면세팅 -> 1차황정삭 -> 탭드릴 -> 연마/열처리 -> 3차원 CMM 정밀검사",
            TargetMaterial = "SUS304 15T",
            FixingMethod = "클램프 고정 (사이드/토글)",
            IsBuiltIn = true
        };

        var n1 = new WorkflowNode { Id = "j1", Title = "도면 검토 & 지그 클램핑 세팅", Category = NodeCategory.Preparation, MachineId = "R03", StartTime = "08:30", EndTime = "09:30", MaterialSize = "200x150x15T", MaterialType = "SUS304", FixingMethod = "클램프 고정 (사이드/토글)", Details = "[시작시간]: 08:30\r\n[종료시간]: 09:30\r\n[Size]: 200x150x15T\r\n[재질]: SUS304\r\n[고정방식]: 클램프 고정 (사이드/토글)\r\n- SUS304 변형 방지 대칭 클램핑 압력 조절" };
        var n2 = new WorkflowNode { Id = "j2", Title = "1차 전면 황정삭 (중절삭)", Category = NodeCategory.Machining, MachineId = "R03", StartTime = "09:30", EndTime = "11:30", MaterialSize = "200x150x15T", MaterialType = "SUS304", FixingMethod = "클램프 고정 (사이드/토글)", Details = "[시작시간]: 09:30\r\n[종료시간]: 11:30\r\n[Size]: 200x150x15T\r\n[재질]: SUS304\r\n[고정방식]: 클램프 고정 (사이드/토글)\r\n- 초경 인서트 페이스밀 난삭재 고속 절삭" };
        var n3 = new WorkflowNode { Id = "j3", Title = "정밀 탭 및 리머 홀 가공", Category = NodeCategory.Machining, MachineId = "R03", StartTime = "13:00", EndTime = "14:30", MaterialSize = "200x150x15T", MaterialType = "SUS304", FixingMethod = "클램프 고정 (사이드/토글)", Details = "[시작시간]: 13:00\r\n[종료시간]: 14:30\r\n[Size]: 200x150x15T\r\n[재질]: SUS304\r\n[고정방식]: 클램프 고정 (사이드/토글)\r\n- M4/M6 탭 가공 및 H7 공차 핀 홀 정밀 리밍" };
        var n4 = new WorkflowNode { Id = "j4", Title = "버 제거 및 초음파 세척", Category = NodeCategory.PostProcess, MachineId = "세척실", StartTime = "14:30", EndTime = "15:20", MaterialSize = "200x150x15T", MaterialType = "SUS304", FixingMethod = "클램프 고정 (사이드/토글)", Details = "[시작시간]: 14:30\r\n[종료시간]: 15:20\r\n[Size]: 200x150x15T\r\n[재질]: SUS304\r\n[고정방식]: 클램프 고정 (사이드/토글)\r\n- 나사산 버(Burr) 전수 수작업 검사 및 제거" };
        var n5 = new WorkflowNode { Id = "j5", Title = "3차원 CMM 홀 피치 정밀검사", Category = NodeCategory.Inspection, MachineId = "품질팀", StartTime = "15:20", EndTime = "16:20", MaterialSize = "200x150x15T", MaterialType = "SUS304", FixingMethod = "클램프 고정 (사이드/토글)", Details = "[시작시간]: 15:20\r\n[종료시간]: 16:20\r\n[Size]: 200x150x15T\r\n[재질]: SUS304\r\n[고정방식]: 클램프 고정 (사이드/토글)\r\n- CMM 기기 활용 핀홀 위치도(0.005mm) 전수 검사" };

        item.Nodes.AddRange(new[] { n1, n2, n3, n4, n5 });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "j1", ToNodeId = "j2" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "j2", ToNodeId = "j3" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "j3", ToNodeId = "j4" });
        item.Edges.Add(new WorkflowEdge { FromNodeId = "j4", ToNodeId = "j5" });

        return item;
    }
}
