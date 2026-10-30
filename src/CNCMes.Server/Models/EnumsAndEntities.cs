using System.Text.Json.Serialization;

namespace CNCMes.Server.Models;

public static class ProductionTypes
{
    public const string SinglePart = "단품";
    public const string MassProduction = "양산";
}

public static class OrderStatus
{
    public const string Pending = "대기";
    public const string InProgress = "가공중";
    public const string WaitingInspection = "측정대기";
    public const string Completed = "완료";
    public const string OnHold = "보류";
}

public static class StepStatus
{
    public const string Pending = "대기";
    public const string Machining = "가공중";
    public const string WaitingInspection = "측정대기";
    public const string Inspecting = "측정중";
    public const string Passed = "합격완료";
    public const string Failed = "불합격";
    public const string Reworking = "재가공";
}

public static class EventCategories
{
    public const string Machining = "가공중";
    public const string PostMachining = "가공후";
    public const string PostDelivery = "납품후";
    public const string InUse = "사용중";
}

public static class EventStatuses
{
    public const string Received = "접수";
    public const string InProgress = "진행";
    public const string Verifying = "검증대기";
    public const string Resolved = "종결";
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "WORKER"; // ADMIN, WORKER, INSPECTOR, ALL
    public int IsActive { get; set; } = 1;
    public string CreatedAt { get; set; } = string.Empty;
    public string? LastLoginAt { get; set; }
}

public class WorkOrder
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string TargetDeliveryDate { get; set; } = string.Empty;
    public string Status { get; set; } = OrderStatus.Pending;
    public int ProgressRate { get; set; } = 0;
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string? FolderPath { get; set; }
    public string? BackupPath { get; set; }
    public string? CompletedAt { get; set; }
    public bool IsHidden { get; set; } = false;

    public List<WOPart> Parts { get; set; } = new();
}

public class WOPart
{
    public int PartId { get; set; }
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PartCode { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public double Thickness { get; set; }
    public int TargetQty { get; set; }
    public int CompletedQty { get; set; }
    public string? ProductionType { get; set; } // 단품 / 양산 (미지정시 수량 기반 자동 추천)
    public int? SettingGroupId { get; set; }
    public string? SettingGroupName { get; set; }
    public int CurrentStepSequence { get; set; } = 1;
    public string Status { get; set; } = OrderStatus.Pending;
    public string CreatedAt { get; set; } = string.Empty;
    public string? PostProcess { get; set; }
    public string? PostProcessStatus { get; set; }
    public string? PostProcessNotes { get; set; }

    public List<RoutingStep> RoutingSteps { get; set; } = new();
}

public class PostProcessReport
{
    public int Id { get; set; }
    public int PartId { get; set; }
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public string Worker { get; set; } = string.Empty;
    public int CompletedQty { get; set; } = 1;
    public string SelectedProcesses { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string Status { get; set; } = "후공정대기";
    public string CreatedAt { get; set; } = string.Empty;
    public string? CompletedAt { get; set; }
}

public class RoutingStep
{
    public int StepId { get; set; }
    public int PartId { get; set; }
    public int StepSequence { get; set; }
    public string StepName { get; set; } = string.Empty; // e.g., "1차가공", "2차가공"
    public string TargetMachine { get; set; } = string.Empty;
    public string? NcProgramName { get; set; }
    public string? NcProgramVersion { get; set; }
    public string Status { get; set; } = StepStatus.Pending;
    public int MachinedQty { get; set; } = 0;
    public int InspectedQty { get; set; } = 0;
    public int PassedQty { get; set; } = 0;
    public int FailedQty { get; set; } = 0;
    public int ReworkQty { get; set; } = 0;
    public string? Notes { get; set; }
}

public class WorkAssignment
{
    public int AssignmentId { get; set; }
    public int StepId { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string WorkerUsername { get; set; } = string.Empty;
    public int AssignedQty { get; set; }
    public string Status { get; set; } = "배정";
    public string AssignedAt { get; set; } = string.Empty;
}

public class WorkExecution
{
    public int ExecutionId { get; set; }
    public int StepId { get; set; }
    public int? AssignmentId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public string WorkerUsername { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string? EndTime { get; set; }
    public int MachinedQty { get; set; } = 0;
    public string Status { get; set; } = "가공중"; // 가공중, 일시중단, 완료
    public string? Notes { get; set; }
}

public class InspectionRecord
{
    public int InspectionId { get; set; }
    public int StepId { get; set; }
    public int PartId { get; set; }
    public string? LotId { get; set; }
    public string InspectorUsername { get; set; } = string.Empty;
    public string InspectedAt { get; set; } = string.Empty;
    public string InspectionType { get; set; } = "전수검사"; // 전수검사, 샘플검사
    public int InspectedQty { get; set; }
    public int PassedQty { get; set; }
    public int FailedQty { get; set; }
    public int ReworkQty { get; set; }
    public string? DimensionDataJson { get; set; } // JSON list of items, criteria, tolerance, actual, pass/fail
    public string ResultVerdict { get; set; } = "합격"; // 합격, 불합격, 판정보류, 재가공
    public string? AttachmentPath { get; set; }
    public string? Notes { get; set; }
}

public class SettingGroup
{
    public int GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public double Thickness { get; set; }
    public string? FixtureType { get; set; }
    public string? OriginPoint { get; set; }
    public string? SharedToolsJson { get; set; }
    public string? Notes { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public class PermanentEvent
{
    public int EventId { get; set; }
    public string PartCode { get; set; } = string.Empty;
    public int? PartId { get; set; }
    public int? OrderId { get; set; }
    public int? StepId { get; set; }
    public string? MachineId { get; set; }
    public string EventCategory { get; set; } = EventCategories.Machining; // 가공중, 가공후, 납품후, 사용중
    public string EventType { get; set; } = string.Empty; // 공구파손, 치수불량, 설계변경, 조립간섭 등
    public string Description { get; set; } = string.Empty;
    public string? Cause { get; set; }
    public string? ActionTaken { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public string? RelatedDrawingRev { get; set; }
    public string? RelatedNcVersion { get; set; }
    public string Status { get; set; } = EventStatuses.Received; // 접수, 진행, 검증대기, 종결
    public string AuthorUsername { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? ResolvedAt { get; set; }

    public List<EventAttachment> Attachments { get; set; } = new();
}

public class EventAttachment
{
    public int AttachmentId { get; set; }
    public int EventId { get; set; }
    public string FileType { get; set; } = "IMAGE"; // IMAGE, PDF, EXCEL, NC
    public string OriginalFilename { get; set; } = string.Empty;
    public string StoredFilename { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? AnnotationsJson { get; set; }
    public string UploadedAt { get; set; } = string.Empty;
}

public class HandoverLog
{
    public int HandoverId { get; set; }
    public string SenderUsername { get; set; } = string.Empty;
    public string? ReceiverUsername { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public int? PartId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? PendingIssues { get; set; }
    public string? Notes { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? ConfirmedAt { get; set; }
}

public class Notice
{
    public int NoticeId { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Priority { get; set; } = "일반"; // 일반, 긴급, 중요
    public string? RelatedMachine { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? OfficialAcksJson { get; set; } // List of { username, confirmed_at }
}

public class ChatMessage
{
    public int Id { get; set; }
    public string ChannelType { get; set; } = "GENERAL"; // ORDER, PART, MACHINE, GENERAL
    public string TopicKey { get; set; } = "공통"; // e.g., "A-001", "상판", "R01"
    public string Author { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? AttachmentInfo { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public int? PromotedEventId { get; set; }
}

public class MachineStatus
{
    public string MachineId { get; set; } = string.Empty; // R01, R02, R03, R04, R05, K01, K02
    public string MachineName { get; set; } = string.Empty; // RoboDrill 1호기 ~ 5호기, Roku-Roku 1호기 ~ 2호기
    public string ModelType { get; set; } = "RoboDrill"; // RoboDrill, Roku-Roku
    public string ControllerType { get; set; } = "FANUC 31i-B5";
    public string IpAddress { get; set; } = "192.168.1.101";
    public int Port { get; set; } = 8193;
    public string Status { get; set; } = "대기"; // 가공중, 대기, 알람, 오프라인
    public string? CurrentProgram { get; set; }
    public int SpindleRpm { get; set; } = 0;
    public double FeedRate { get; set; } = 0;
    public double RunTimeMinutes { get; set; } = 0;
    public string? ActiveJobNumber { get; set; }
    public string? ActivePartName { get; set; }
    public string? ActiveWorker { get; set; }
    public string LastHeartbeat { get; set; } = string.Empty;
}

public class MachineWorkLog
{
    public int Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string WorkDetail { get; set; } = string.Empty;
    public string Status { get; set; } = "가공중";
    public string WorkerName { get; set; } = string.Empty;
    public string? SubWorkerName { get; set; }
    public int Qty { get; set; } = 0;
    public string CreatedAt { get; set; } = string.Empty;
}

public class SubWorkerTask
{
    public int TaskId { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string PartName { get; set; } = string.Empty;
    public string LeaderUsername { get; set; } = string.Empty;
    public string SubWorkerName { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
    public string? ReportContent { get; set; }
    public int ReportedQty { get; set; } = 0;
    public string Status { get; set; } = "지시중"; // 지시중, 중앙보고완료
    public string CreatedAt { get; set; } = string.Empty;
    public string? ReportedAt { get; set; }
}

public class MachineUsedTool
{
    public int ToolLogId { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = "-";
    public string PartName { get; set; } = "-";
    public string ToolName { get; set; } = string.Empty;      // 예: 황삭 엔드밀, 마이크로 드릴, 챔퍼밀
    public string Maker { get; set; } = "-";                 // 예: OSG, YG-1, Korloy, Mitsubishi
    public string ToolDiameter { get; set; } = "-";          // 예: Ø10.0, Ø3.0, Ø0.5
    public string FluteLength { get; set; } = "-";           // 예: 25L, 12L
    public string Material { get; set; } = "-";              // 예: 초경(Carbide), HSS, CBN, PCD, 코팅초경
    public string Notes { get; set; } = "-";                 // 기타 비고 / 마모상태 등
    public string WorkerName { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}
