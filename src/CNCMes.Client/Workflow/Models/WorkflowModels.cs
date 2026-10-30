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
    Ready,         // 대기 / 준비
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
    public string Title { get; set; } = "신규 공정";
    public NodeCategory Category { get; set; } = NodeCategory.Machining;
    public NodeStatus Status { get; set; } = NodeStatus.Ready;
    
    public string OrderNumber { get; set; } = "";
    public string PartName { get; set; } = "";
    public string MachineId { get; set; } = "R01";
    public string Assignee { get; set; } = "";
    public string Details { get; set; } = "";
    
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 190f;
    public float Height { get; set; } = 78f;

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    [JsonIgnore]
    public bool IsSelected { get; set; }

    [JsonIgnore]
    public RectangleF Bounds => new(X, Y, Width, Height);

    public PointF InputAnchor => new(X, Y + Height / 2f);
    public PointF OutputAnchor => new(X + Width, Y + Height / 2f);
}

public class WorkflowEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string FromNodeId { get; set; } = "";
    public string ToNodeId { get; set; } = "";
    public EdgeType Type { get; set; } = EdgeType.Sequence;
}

public class WorkflowData
{
    public string OrderNumber { get; set; } = "";
    public string PartName { get; set; } = "";
    public List<WorkflowNode> Nodes { get; set; } = new();
    public List<WorkflowEdge> Edges { get; set; } = new();
}
