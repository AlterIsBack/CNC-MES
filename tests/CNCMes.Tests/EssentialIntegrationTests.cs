using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using CNCMes.Server.Data;
using CNCMes.Server.Models;
using Xunit;

namespace CNCMes.Tests;

public class EssentialIntegrationTests : IDisposable
{
    private readonly string _testDbDir;
    private readonly SqliteDb _db;

    public EssentialIntegrationTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "CNCMes_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDir);

        var inMemorySettings = new Dictionary<string, string?>
        {
            {"ServerConfig:DataDirectory", _testDbDir}
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _db = new SqliteDb(config, NullLogger<SqliteDb>.Instance);
        _db.Initialize();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDir))
            {
                Directory.Delete(_testDbDir, true);
            }
        }
        catch { }
    }

    [Fact(DisplayName = "시나리오 A: 하나의 작업지시서에 복수 부품(상판, 하판, 가이드) 등록 및 진행률")]
    public void ScenarioA_MultiPart_WorkOrder()
    {
        var order = new WorkOrder
        {
            OrderNumber = "WO-TEST-A",
            CustomerName = "고객사A",
            TargetDeliveryDate = "2026-11-01",
            CreatedBy = "관리자",
            Parts = new List<WOPart>
            {
                new WOPart { PartCode = "TOP-01", PartName = "상판", Material = "PEEK", Thickness = 5.0, TargetQty = 20 },
                new WOPart { PartCode = "BTM-01", PartName = "하판", Material = "PEEK", Thickness = 5.0, TargetQty = 20 },
                new WOPart { PartCode = "GUD-01", PartName = "가이드", Material = "SUS304", Thickness = 3.0, TargetQty = 20 }
            }
        };

        var orderId = _db.CreateWorkOrder(order);
        Assert.True(orderId > 0);

        var retrieved = _db.GetWorkOrderById(orderId);
        Assert.NotNull(retrieved);
        Assert.Equal(3, retrieved.Parts.Count);
        Assert.Equal("상판", retrieved.Parts[0].PartName);
        Assert.Equal("하판", retrieved.Parts[1].PartName);
        Assert.Equal("가이드", retrieved.Parts[2].PartName);
    }

    [Fact(DisplayName = "시나리오 B: 장비 분산 가공 (100개를 R01, R02, K01에 나누어 독립 기록 및 취합)")]
    public void ScenarioB_Machine_Distributed_Execution()
    {
        var order = new WorkOrder
        {
            OrderNumber = "WO-TEST-B",
            CustomerName = "고객사B",
            TargetDeliveryDate = "2026-11-01",
            CreatedBy = "관리자",
            Parts = new List<WOPart>
            {
                new WOPart { PartCode = "TOP-100", PartName = "상판100", Material = "PEEK", Thickness = 5.0, TargetQty = 100 }
            }
        };
        var orderId = _db.CreateWorkOrder(order);
        var part = _db.GetPartsByOrderId(orderId)[0];
        var step = part.RoutingSteps[0]; // 1차 가공

        // R01: 40개 가공
        var e1 = _db.StartExecution(step.StepId, null, "R01", "작업자1", 40, "1차분할");
        _db.FinishExecution(e1, 40, "완료");

        // R02: 30개 가공
        var e2 = _db.StartExecution(step.StepId, null, "R02", "작업자2", 30, "2차분할");
        _db.FinishExecution(e2, 30, "완료");

        // K01: 30개 가공
        var e3 = _db.StartExecution(step.StepId, null, "K01", "작업자3", 30, "3차분할");
        _db.FinishExecution(e3, 30, "완료");

        var executions = _db.GetExecutionsByStepId(step.StepId);
        Assert.Equal(3, executions.Count);
        Assert.Equal(100, executions.Sum(x => x.MachinedQty));

        var updatedPart = _db.GetPartById(part.PartId);
        Assert.NotNull(updatedPart);
        Assert.Equal(100, updatedPart.RoutingSteps[0].MachinedQty);
        Assert.Equal("측정대기", updatedPart.RoutingSteps[0].Status);
    }

    [Fact(DisplayName = "시나리오 C: 작업지시서 교차 가공 (R01에서 A 하다가 세팅 유사한 B 수행 후 A 복귀)")]
    public void ScenarioC_Cross_Machining_SettingGroup()
    {
        // 1. 세팅 그룹 등록
        var grpId = _db.CreateSettingGroup(new SettingGroup
        {
            GroupName = "PEEK 5T 지그A 세팅",
            Material = "PEEK",
            Thickness = 5.0,
            OriginPoint = "G54 X0 Y0 Z0"
        });

        // 2. 작업 A 등록
        var orderAId = _db.CreateWorkOrder(new WorkOrder
        {
            OrderNumber = "WO-A",
            CustomerName = "고객A",
            TargetDeliveryDate = "2026-11-05",
            CreatedBy = "관리자",
            Parts = new List<WOPart> { new WOPart { PartCode = "PART-A", PartName = "A부품", Material = "PEEK", Thickness = 5.0, TargetQty = 10, SettingGroupId = grpId } }
        });

        // 3. 작업 B 등록
        var orderBId = _db.CreateWorkOrder(new WorkOrder
        {
            OrderNumber = "WO-B",
            CustomerName = "고객B",
            TargetDeliveryDate = "2026-11-06",
            CreatedBy = "관리자",
            Parts = new List<WOPart> { new WOPart { PartCode = "PART-B", PartName = "B부품", Material = "PEEK", Thickness = 5.0, TargetQty = 10, SettingGroupId = grpId } }
        });

        var stepA = _db.GetPartsByOrderId(orderAId)[0].RoutingSteps[0];
        var stepB = _db.GetPartsByOrderId(orderBId)[0].RoutingSteps[0];

        // R01에서 A 작업 5개 실행 후 중단
        var execA1 = _db.StartExecution(stepA.StepId, null, "R01", "김반장", 5, "A 1차 가공");
        _db.FinishExecution(execA1, 5, "일부 완료");

        // R01에서 동일 세팅 B 작업 10개 실행
        var execB = _db.StartExecution(stepB.StepId, null, "R01", "김반장", 10, "B 교차 가공");
        _db.FinishExecution(execB, 10, "B 전량 완료");

        // R01에서 다시 A 작업 5개 복귀 가공
        var execA2 = _db.StartExecution(stepA.StepId, null, "R01", "김반장", 5, "A 복귀 가공");
        _db.FinishExecution(execA2, 5, "A 전량 완료");

        // 검증: A의 실적과 B의 실적이 정확하게 귀속되어 분리되었는지
        var listA = _db.GetExecutionsByStepId(stepA.StepId);
        var listB = _db.GetExecutionsByStepId(stepB.StepId);

        Assert.Equal(2, listA.Count);
        Assert.Equal(10, listA.Sum(x => x.MachinedQty));

        Assert.Single(listB);
        Assert.Equal(10, listB[0].MachinedQty);
    }

    [Fact(DisplayName = "시나리오 D: 공정별 측정 게이트 (1차가공 90개, 1차검사 합격 80개, 측정대기/불량 분리)")]
    public void ScenarioD_Inspection_Gateway()
    {
        var orderId = _db.CreateWorkOrder(new WorkOrder
        {
            OrderNumber = "WO-D",
            CustomerName = "품질테스트",
            TargetDeliveryDate = "2026-11-01",
            CreatedBy = "관리자",
            Parts = new List<WOPart> { new WOPart { PartCode = "SOCKET-01", PartName = "소켓", Material = "PEEK", Thickness = 5.0, TargetQty = 90 } }
        });

        var part = _db.GetPartsByOrderId(orderId)[0];
        var step1 = part.RoutingSteps[0]; // 1차 가공
        var step2 = part.RoutingSteps[1]; // 1차 측정

        // 1차 가공 90개 완료
        var execId = _db.StartExecution(step1.StepId, null, "R01", "작업자", 90, null);
        _db.FinishExecution(execId, 90, null);

        var step1AfterMachining = _db.GetRoutingStepsByPartId(part.PartId)[0];
        Assert.Equal("측정대기", step1AfterMachining.Status);

        // 1차 측정: 90개 중 합격 80개, 불합격 10개
        var inspId = _db.RecordInspection(new InspectionRecord
        {
            StepId = step1.StepId,
            PartId = part.PartId,
            InspectorUsername = "품질반장",
            InspectionType = "전수검사",
            InspectedQty = 90,
            PassedQty = 80,
            FailedQty = 10,
            ResultVerdict = "합격",
            Notes = "치수 적합 80개 합격 판정"
        });

        Assert.True(inspId > 0);

        var inspections = _db.GetInspectionsByPartId(part.PartId);
        Assert.Single(inspections);
        Assert.Equal(80, inspections[0].PassedQty);
        Assert.Equal(10, inspections[0].FailedQty);

        var step1AfterInsp = _db.GetRoutingStepsByPartId(part.PartId)[0];
        Assert.Equal("합격완료", step1AfterInsp.Status);
    }

    [Fact(DisplayName = "시나리오 E: 단품(2개) vs 양산(100개) 자동 추천 및 분리 관리")]
    public void ScenarioE_Single_vs_Mass_Production()
    {
        var order = new WorkOrder
        {
            OrderNumber = "WO-E",
            CustomerName = "연구소",
            TargetDeliveryDate = "2026-11-10",
            CreatedBy = "관리자",
            Parts = new List<WOPart>
            {
                new WOPart { PartCode = "PROTO-01", PartName = "시제품", Material = "POM", Thickness = 3.0, TargetQty = 2 },
                new WOPart { PartCode = "MASS-01", PartName = "양산품", Material = "PEEK", Thickness = 5.0, TargetQty = 100 }
            }
        };

        var orderId = _db.CreateWorkOrder(order);
        var parts = _db.GetPartsByOrderId(orderId);

        Assert.Equal(ProductionTypes.SinglePart, parts.First(p => p.PartCode == "PROTO-01").ProductionType);
        Assert.Equal(ProductionTypes.MassProduction, parts.First(p => p.PartCode == "MASS-01").ProductionType);
    }

    [Fact(DisplayName = "시나리오 F & G: 공구 파손 이벤트 등록, 원인/조치, 도면 개정, 신규 작업 시 선제 경고")]
    public void ScenarioFG_Permanent_Event_History_And_PreWarning()
    {
        // 1. 공구 파손 이벤트 등록
        var evId = _db.CreatePermanentEvent(new PermanentEvent
        {
            PartCode = "SOC-2540",
            EventCategory = EventCategories.Machining,
            EventType = "공구파손",
            Description = "0.5mm 마이크로 드릴 가공 중 파손",
            Cause = "이송속도 과다(F120)",
            ActionTaken = "F80으로 감속 및 새 드릴 교체",
            BeforeState = "F120 / S12000",
            AfterState = "F80 / S15000",
            RelatedDrawingRev = "REV.A",
            RelatedNcVersion = "O1001_V2",
            AuthorUsername = "박작업자"
        });

        Assert.True(evId > 0);

        // 2. 다음 작업 등록 시 동일 품번 과거 불량 이력 선제 경고 조회
        var history = _db.GetEventsByPartCode("SOC-2540");
        Assert.Single(history);
        Assert.Equal("공구파손", history[0].EventType);
        Assert.Equal("이송속도 과다(F120)", history[0].Cause);
        Assert.Equal("F80으로 감속 및 새 드릴 교체", history[0].ActionTaken);
    }

    [Fact(DisplayName = "시나리오 H & I: 납품 후 조립 간섭 재작업 및 사용 중 마모 이력 연결")]
    public void ScenarioHI_PostDelivery_And_InUse_Rework()
    {
        // 최초 가공 이력 기록
        var firstOrderId = _db.CreateWorkOrder(new WorkOrder
        {
            OrderNumber = "WO-INIT-2025",
            CustomerName = "납품고객",
            TargetDeliveryDate = "2025-12-01",
            CreatedBy = "관리자",
            Parts = new List<WOPart> { new WOPart { PartCode = "SOC-4000", PartName = "납품소켓", Material = "PEEK", Thickness = 10.0, TargetQty = 1 } }
        });

        // 납품 후 조립 간섭 이벤트 등록
        _db.CreatePermanentEvent(new PermanentEvent
        {
            PartCode = "SOC-4000",
            OrderId = firstOrderId,
            EventCategory = EventCategories.PostDelivery,
            EventType = "조립간섭",
            Description = "외곽 챔퍼 부족으로 베이스플레이트 간섭 발생",
            ActionTaken = "C0.5 챔퍼 추가 수정 가공",
            AuthorUsername = "품질팀장"
        });

        // 1년 후 사용 중 마모 이벤트 등록
        _db.CreatePermanentEvent(new PermanentEvent
        {
            PartCode = "SOC-4000",
            OrderId = firstOrderId,
            EventCategory = EventCategories.InUse,
            EventType = "마모",
            Description = "10만회 테스트 후 포켓 마모 발생",
            ActionTaken = "0.02mm 재가공 치수 보정",
            AuthorUsername = "고객지원"
        });

        var allEvents = _db.GetEventsByPartCode("SOC-4000");
        Assert.Equal(2, allEvents.Count);
        Assert.Contains(allEvents, e => e.EventCategory == EventCategories.PostDelivery);
        Assert.Contains(allEvents, e => e.EventCategory == EventCategories.InUse);
    }

    [Fact(DisplayName = "시나리오 J: Zulip 스타일 채팅 및 공식 이벤트 승격, 중요 공지 서명")]
    public void ScenarioJ_Topic_Chat_And_Notice_Ack()
    {
        // 1. 토픽 채팅
        var msgId = _db.AddChatMessage(new ChatMessage
        {
            ChannelType = "ORDER",
            TopicKey = "WO-2026-001",
            Author = "김작업자",
            Content = "소재 두께 편차가 0.05mm 발생하여 오프셋 수정 필요합니다."
        });
        Assert.True(msgId > 0);

        // 2. 채팅 내용을 공식 이벤트로 승격
        var evId = _db.CreatePermanentEvent(new PermanentEvent
        {
            PartCode = "SOCKET-01",
            EventCategory = EventCategories.Machining,
            EventType = "소재문제",
            Description = "소재 두께 편차 발생으로 인한 오프셋 수정",
            AuthorUsername = "김작업자"
        });
        _db.LinkChatToPermanentEvent(msgId, evId);

        var messages = _db.GetMessagesByTopic("ORDER", "WO-2026-001");
        Assert.Single(messages);
        Assert.Equal(evId, messages[0].PromotedEventId);

        // 3. 공지사항 및 공식 확인 서명
        var noticeId = _db.CreateNotice(new Notice
        {
            Author = "총괄공장장",
            Title = "주말 FOCAS 모니터링 장비 점검 안내",
            Content = "토요일 오후 2시부터 R01, R02 제어기 펌웨어 점검 진행",
            Priority = "긴급"
        });

        _db.AcknowledgeNotice(noticeId, "작업자A");
        var notices = _db.GetAllNotices();
        var n = notices.First(x => x.NoticeId == noticeId);
        Assert.Contains("작업자A", n.OfficialAcksJson!);
    }

    [Fact(DisplayName = "시나리오 K: 데이터 무결성 및 장애 복구 (DB 재연결 및 상태 유지)")]
    public void ScenarioK_Data_Integrity_And_Recovery()
    {
        // 1. 데이터 등록
        var orderId = _db.CreateWorkOrder(new WorkOrder
        {
            OrderNumber = "WO-RECOVERY-TEST",
            CustomerName = "무결성검증",
            TargetDeliveryDate = "2026-12-31",
            CreatedBy = "관리자",
            Parts = new List<WOPart> { new WOPart { PartCode = "REC-01", PartName = "복구부품", Material = "AL6061", Thickness = 15.0, TargetQty = 5 } }
        });

        // 2. 완전히 새로운 DB 인스턴스로 동일 SQLite 파일 오픈 (서버 재기동 시뮬레이션)
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"ServerConfig:DataDirectory", _testDbDir}
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var restartedDb = new SqliteDb(config, NullLogger<SqliteDb>.Instance);
        restartedDb.Initialize();

        var recovered = restartedDb.GetWorkOrderById(orderId);
        Assert.NotNull(recovered);
        Assert.Equal("WO-RECOVERY-TEST", recovered.OrderNumber);
        Assert.Single(recovered.Parts);
        Assert.Equal("REC-01", recovered.Parts[0].PartCode);

        // 7대 장비 시딩 무결성
        var machines = restartedDb.GetAllMachines();
        Assert.Equal(7, machines.Count);
    }
}
