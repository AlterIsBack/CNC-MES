using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json.Serialization;

namespace CNCMes.Client.Workflow.Models;

public enum NodeCategory
{
    Preparation,   // 작업 준비 (도면, 소재, 세팅, 베이스교체, 좌표)
    Machining,     // 가공 (면가공, 황삭, 정삭, 드릴, 탭, 챔퍼, 버제거)
    Inspection,    // 검사 (중간측정, 치수확인, 초품검사, 최종검사)
    PostProcess,   // 후속작업 (세척, 마킹, 아노다이징, 핀압입, 포장)
    Collaboration, // 협업 (서브인원 작업지시, 인수인계, 보고)
    Issue          // 이슈 (공구파손, 치수불량, 장비알람, 설계변경)
}

public enum NodeStatus
{
    Ready,         // 대기 / 예정 / 미착수
    Running,       // 진행중 / 가공중
    PendingCheck,  // 측정 / 검사대기
    Completed,     // 완료
    Issue          // 불량 / 긴급이슈
}

public enum EdgeType
{
    Sequence,      // 순차 진행
    Parallel,      // 병렬 분기
    Delegation,    // 서브인원 위임
    IssueLink      // 이슈 연결
}

public class WorkflowNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    // [요구사항 1, 2] 작업 소속과 불변 고유 식별자 분리
    // 노드의 소속 작업(JobId)은 연결선과 무관하게 독립 유지되며, 빈 문자열이면 UNASSIGNED로 처리
    public string JobId { get; set; } = "";

    // [요구사항 4] 실행 이력 보존을 위한 소프트 삭제 플래그 (노드 삭제 시에도 과거 실행 기록과의 연결 보존)
    public bool IsDeleted { get; set; } = false;

    public string Title { get; set; } = "신규 공정";
    public NodeCategory Category { get; set; } = NodeCategory.Machining;
    public NodeStatus Status { get; set; } = NodeStatus.Ready;
    
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public int PartId { get; set; }
    public string PartName { get; set; } = "";
    public string MachineId { get; set; } = "R01";
    public string Assignee { get; set; } = "";
    public string Details { get; set; } = "";
    public string NextProcess { get; set; } = "";

    // Sub-Worker Dedicated Fields
    public string SubWorkerName { get; set; } = "";
    public string SubWorkerInstruction { get; set; } = "";
    public bool IsSubWorkerTaskSent { get; set; } = false;

    // Issue Tracking & History Fields (발생 내용, 원인 분석, 대응 및 조치)
    public bool HasIssueHistory { get; set; } = false;
    public string IssueType { get; set; } = "";              // 불량/이슈 유형
    public string IssueOccurredDetails { get; set; } = "";   // 발생 내용 상세
    public string IssueRootCause { get; set; } = "";          // 원인 분석
    public string IssueActionTaken { get; set; } = "";        // 대응 및 조치 내용
    public DateTime? IssueReportedAt { get; set; }           // 이슈 발생/보고 일시
    public DateTime? IssueResolvedAt { get; set; }           // 이슈 조치완료 일시
    public string IssueResolvedBy { get; set; } = "";        // 조치 작업자
    
    // Timeline & Custom Field Attributes ([시작시간], [종료시간], [소재사이즈], [소재종류] 등)
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public string MaterialSize { get; set; } = "";
    public string MaterialType { get; set; } = "";
    public string FixingMethod { get; set; } = "바이스";
    public bool IsMaterialCustomized { get; set; } = false; // 바이스, 진공흡착, 클램프, 마그네틱, 전용지그 등
    public Dictionary<string, string> CustomFields { get; set; } = new();

    // [요구사항 3, 5] 첨부파일 경로 목록
    public List<string> AttachmentFilePaths { get; set; } = new();

    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 195f;
    public float Height { get; set; } = 80f;

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    [JsonIgnore]
    public bool IsSelected { get; set; }

    [JsonIgnore]
    public RectangleF Bounds => new(X, Y, Width, Height);

    public PointF InputAnchor => new(X, Y + Height / 2f);
    public PointF OutputAnchor => new(X + Width, Y + Height / 2f);
    public PointF TopAnchor => new(X + Width / 2f, Y);
    public PointF BottomAnchor => new(X + Width / 2f, Y + Height);

    [JsonIgnore]
    public PointF[] AllAnchors => new[] { InputAnchor, OutputAnchor, TopAnchor, BottomAnchor };

    public PointF GetAnchorByPort(string port) => port switch
    {
        "Left" => InputAnchor,
        "Right" => OutputAnchor,
        "Top" => TopAnchor,
        "Bottom" => BottomAnchor,
        _ => OutputAnchor
    };

    public (string portName, PointF point) GetClosestAnchorWithPort(PointF target)
    {
        var candidates = new (string, PointF)[]
        {
            ("Left", InputAnchor),
            ("Right", OutputAnchor),
            ("Top", TopAnchor),
            ("Bottom", BottomAnchor)
        };
        var closest = candidates[1];
        float minD = float.MaxValue;
        foreach (var c in candidates)
        {
            float d = (c.Item2.X - target.X) * (c.Item2.X - target.X) + (c.Item2.Y - target.Y) * (c.Item2.Y - target.Y);
            if (d < minD)
            {
                minD = d;
                closest = c;
            }
        }
        return closest;
    }

    public PointF GetClosestAnchor(PointF target)
    {
        return GetClosestAnchorWithPort(target).point;
    }
}

public class WorkflowEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string FromNodeId { get; set; } = "";
    public string ToNodeId { get; set; } = "";
    
    // [요구사항 2] 작업 소속과 연결선 분리: 다른 작업 간 연결 시 각 노드의 JobId 참조
    public string FromJobId { get; set; } = "";
    public string ToJobId { get; set; } = "";

    // 사용자가 지정한 시작/종료 노드점 포트 ("Left", "Right", "Top", "Bottom", "Auto")
    public string FromPort { get; set; } = "Auto";
    public string ToPort { get; set; } = "Auto";

    public EdgeType Type { get; set; } = EdgeType.Sequence;
    public string Label { get; set; } = "";

    [JsonIgnore]
    public bool IsSelected { get; set; }
}

public class WorkflowTitleBox
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string JobId { get; set; } = "";
    public string Title { get; set; } = "1차 가공 공정군";
    public string ColorHex { get; set; } = "#0284c7"; // Default sky blue
    public float X { get; set; } = 40f;
    public float Y { get; set; } = 40f;
    public float Width { get; set; } = 420f;
    public float Height { get; set; } = 200f;

    [JsonIgnore]
    public bool IsSelected { get; set; }

    [JsonIgnore]
    public RectangleF Bounds => new(X, Y, Width, Height);

    [JsonIgnore]
    public RectangleF HeaderBounds => new(X, Y, Width, 32f);

    [JsonIgnore]
    public RectangleF ResizeHandleBounds => new(X + Width - 18f, Y + Height - 18f, 18f, 18f);

    [JsonIgnore]
    public RectangleF AutoFitButtonBounds => new(X + Width - 88f, Y + 5f, 68f, 22f);

    public bool ContainsNode(WorkflowNode node)
    {
        float cx = node.X + (node.Width / 2f);
        float cy = node.Y + (node.Height / 2f);
        return Bounds.Contains(cx, cy);
    }

    public void AutoFit(IEnumerable<WorkflowNode> nodes)
    {
        var list = nodes.ToList();
        if (list.Count == 0) return;
        float minX = list.Min(n => n.X);
        float minY = list.Min(n => n.Y);
        float maxX = list.Max(n => n.X + n.Width);
        float maxY = list.Max(n => n.Y + n.Height);

        X = minX - 25f;
        Y = minY - 45f;
        Width = Math.Max(260f, (maxX - minX) + 50f);
        Height = Math.Max(140f, (maxY - minY) + 70f);
    }
}

/// <summary>
/// [요구사항 1] 작업지시서·부품에 연결된 작업 단위 (불변 고유 식별자 JobId)
/// </summary>
public class WorkUnitJob
{
    public string JobId { get; set; } = ""; // 고유 불변 식별자 (예: JOB-20261103-A1B2C3D4)
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public int PartId { get; set; }
    public string PartName { get; set; } = "";
    public int TargetQuantity { get; set; } = 1;
    public string Status { get; set; } = "대기"; // 대기, 진행중, 완료, 재가공
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = "";
    public DateTime LastSavedAt { get; set; } = DateTime.Now;
    public string LastSavedBy { get; set; } = "";

    // [요구사항 6] 완료 후 재가공 연결
    public string ParentJobId { get; set; } = ""; // 원작업 고유식별자
    public bool IsRework { get; set; } = false;
    public string ReworkReason { get; set; } = "";
    public int ReworkQuantity { get; set; } = 0;
    public string PreviousSnapshotId { get; set; } = ""; // 원작업 완료 확정 기록 ID

    // 작업 확정 정보
    public bool IsConfirmed { get; set; } = false;
    public DateTime? ConfirmedAt { get; set; }
    public string ConfirmedBy { get; set; } = "";
    public string ConfirmedSnapshotId { get; set; } = "";

    // [요구사항] 전체 작업 완료 정보
    public bool IsCompleted { get; set; } = false;
    public DateTime? CompletedAt { get; set; }
    public string CompletedBy { get; set; } = "";
    public string CompletedSnapshotId { get; set; } = "";

    // 소속 노드, 엣지, 타이틀 박스
    public List<WorkflowNode> Nodes { get; set; } = new();
    public List<WorkflowEdge> Edges { get; set; } = new();
    public List<WorkflowTitleBox> TitleBoxes { get; set; } = new();
}

/// <summary>
/// [요구사항 4] 계획 버전 이력 (공정 추가/삭제, 순서 변경, 장비/작업자 변경 등 중요 계획 변경 기록)
/// </summary>
public class PlanVersionRecord
{
    public string VersionId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public int VersionNumber { get; set; } = 1;
    public string JobId { get; set; } = "";
    public DateTime ChangedAt { get; set; } = DateTime.Now;
    public string ChangedBy { get; set; } = "";
    public string ChangeReason { get; set; } = "";
    
    // 계획 스냅샷 (노드 및 엣지 구성 상태)
    public List<WorkflowNode> NodesSnapshot { get; set; } = new();
    public List<WorkflowEdge> EdgesSnapshot { get; set; } = new();
}

/// <summary>
/// [요구사항 4, 7] 실제 실행/측정/이슈 이력 (계획 수정/노드 삭제에도 영구 보존, 날짜별 실적 집계 기준)
/// </summary>
public class ExecutionHistoryRecord
{
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string JobId { get; set; } = "";
    public string NodeId { get; set; } = "";
    public string NodeTitle { get; set; } = "";
    public string MachineId { get; set; } = "";
    public string WorkerName { get; set; } = "";
    public string EventType { get; set; } = "StatusChange"; // Start, Complete, Check, Issue, Memo
    public string Status { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public DateTime? ActualStartAt { get; set; }
    public DateTime? ActualEndAt { get; set; }
    public string InspectionDetails { get; set; } = "";
    public string IssueDetails { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>
/// [요구사항 5] 작업 완료 시 확정 기록 (불변 스냅샷, 관련 파일 보관 사본)
/// </summary>
public class ConfirmedSnapshotRecord
{
    public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string JobId { get; set; } = "";
    public string ParentJobId { get; set; } = "";
    public string OrderNumber { get; set; } = "";
    public string PartName { get; set; } = "";
    public DateTime ConfirmedAt { get; set; } = DateTime.Now;
    public string ConfirmedBy { get; set; } = "";
    
    public List<WorkflowNode> ConfirmedNodes { get; set; } = new();
    public List<WorkflowEdge> ConfirmedEdges { get; set; } = new();
    public List<ExecutionHistoryRecord> ExecutionsSnapshot { get; set; } = new();

    // 완료 시점 보관 파일 사본 (원본 파일명 -> 보관된 사본 파일 상대경로)
    public Dictionary<string, string> ArchivedFiles { get; set; } = new();

    public bool IsImmutable { get; set; } = true;
}

/// <summary>
/// [요구사항 5] 완료 기록 정정 이력
/// </summary>
public class CorrectionRecord
{
    public string CorrectionId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string SnapshotId { get; set; } = "";
    public string JobId { get; set; } = "";
    public DateTime CorrectedAt { get; set; } = DateTime.Now;
    public string CorrectedBy { get; set; } = "";
    public string Reason { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";
}

/// <summary>
/// [요구사항 7] 다차원 조회 필터
/// </summary>
public class JobQueryFilter
{
    public string? OrderNumber { get; set; }
    public string? PartName { get; set; }
    public string? WorkerName { get; set; }
    public string? MachineId { get; set; }
    public string? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

/// <summary>
/// 하위 호환성을 위한 레거시 래퍼
/// </summary>
public class WorkflowData
{
    public string JobId { get; set; } = "";
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public int PartId { get; set; }
    public string PartName { get; set; } = "";
    public List<WorkflowNode> Nodes { get; set; } = new();
    public List<WorkflowEdge> Edges { get; set; } = new();
    public List<WorkflowTitleBox> TitleBoxes { get; set; } = new();
    
    public DateTime LastSavedAt { get; set; } = DateTime.Now;
    public bool IsConfirmed { get; set; } = false;
    public string ConfirmedBy { get; set; } = "";
}
