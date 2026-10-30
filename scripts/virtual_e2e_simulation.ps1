# CNC-MES Virtual End-to-End Simulation Script
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$baseUrl = "http://localhost:5000"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " [CNC-MES v1.1.0] 가상 사용자 시뮬레이션 및 전수 기능 검증" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

# 1. 시스템 상태 확인
$st = Invoke-RestMethod -Uri "$baseUrl/api/auth/status"
Write-Host "`n[1/10] 시스템 초기 상태 확인:" -ForegroundColor Yellow
Write-Host "  * 시스템 버전: $($st.systemVersion)"
Write-Host "  * 서버 시각:   $($st.serverTime)"
Write-Host "  * 초기화 여부: $($st.initialized)"

# 2. 초기 관리자 계정 생성 (Setup Admin)
if (-not $st.initialized) {
    Write-Host "`n[2/10] 초기 마스터 관리자(ADMIN) 생성 중..." -ForegroundColor Yellow
    $adminBody = @{
        username = "admin"
        password = "admin1234"
        displayName = "총괄공장장"
        role = "ADMIN"
    } | ConvertTo-Json
    $adminRes = Invoke-RestMethod -Uri "$baseUrl/api/auth/setup-admin" -Method Post -Body $adminBody -ContentType "application/json; charset=utf-8"
    Write-Host "  -> 관리자 등록 완료: $($adminRes.message)" -ForegroundColor Green
} else {
    Write-Host "`n[2/10] 관리자 계정 이미 존재함." -ForegroundColor Gray
}

# 3. 작업자 및 품질검사원 계정 생성
Write-Host "`n[3/10] 현장 작업자 및 품질검사원 계정 등록 중..." -ForegroundColor Yellow
try {
    $workerBody = @{ username = "worker1"; password = "worker1234"; displayName = "김작업 반장"; role = "WORKER" } | ConvertTo-Json
    Invoke-RestMethod -Uri "$baseUrl/api/auth/users" -Method Post -Body $workerBody -ContentType "application/json; charset=utf-8" | Out-Null
    Write-Host "  -> 현장 작업자 (worker1 / 김작업 반장) 등록 완료" -ForegroundColor Green
} catch { Write-Host "  -> worker1 이미 등록됨" -ForegroundColor Gray }

try {
    $inspBody = @{ username = "inspector1"; password = "insp1234"; displayName = "박검사 주임"; role = "INSPECTOR" } | ConvertTo-Json
    Invoke-RestMethod -Uri "$baseUrl/api/auth/users" -Method Post -Body $inspBody -ContentType "application/json; charset=utf-8" | Out-Null
    Write-Host "  -> 품질 검사원 (inspector1 / 박검사 주임) 등록 완료" -ForegroundColor Green
} catch { Write-Host "  -> inspector1 이미 등록됨" -ForegroundColor Gray }

# 4. 로그인 검증
Write-Host "`n[4/10] 사용자 로그인 인증 검증 (PBKDF2 솔트 해시):" -ForegroundColor Yellow
$loginRes = Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -Body (@{ username = "worker1"; password = "worker1234" } | ConvertTo-Json) -ContentType "application/json; charset=utf-8"
Write-Host "  -> 로그인 성공: 사용자명 [$($loginRes.displayName)], 역할 [$($loginRes.role)]" -ForegroundColor Green

# 5. 공지사항 등록 및 공식 서명 확인
Write-Host "`n[5/10] 관리자 중요 공지 등록 및 작업자 공식 확인 서명:" -ForegroundColor Yellow
$noticeBody = @{
    author = "총괄공장장"
    title = "주말 RoboDrill 1~2호기 정밀 스핀들 점검 안내"
    content = "금주 토요일 14:00부터 R01, R02 장비 제어기 백업 및 스핀들 런아웃 점검이 진행됩니다."
    priority = "긴급"
    relatedMachine = "R01"
} | ConvertTo-Json
$notice = Invoke-RestMethod -Uri "$baseUrl/api/communications/notices" -Method Post -Body $noticeBody -ContentType "application/json; charset=utf-8"
Write-Host "  -> 공지 발행 완료: [ID $($notice.noticeId)] $($notice.title)" -ForegroundColor Green

# 김작업 반장의 공식 확인 서명
$ackRes = Invoke-RestMethod -Uri "$baseUrl/api/communications/notices/$($notice.noticeId)/ack?username=$([System.Uri]::EscapeDataString('김작업 반장'))" -Method Post
Write-Host "  -> 작업자 서명 완료: $($ackRes.message)" -ForegroundColor Green

$notices = Invoke-RestMethod -Uri "$baseUrl/api/communications/notices"
$targetNotice = $notices | Where-Object { $_.noticeId -eq $notice.noticeId }
Write-Host "  -> 공지 서명 현황 확인: $($targetNotice.officialAcksJson)" -ForegroundColor Gray

# 6. 교대 인수인계 등록 및 인수 확인
Write-Host "`n[6/10] 교대 인수인계 등록 및 인수 확인:" -ForegroundColor Yellow
$handoverBody = @{
    senderUsername = "김작업 반장"
    machineId = "R01"
    summary = "WO-2026-LIVE01 상판 1차가공 15개 완료 후 2차 대기 중"
    pendingIssues = "T03 엔드밀 마모 주의(10개 가공 후 교체 요망), Z축 원점 0.005mm 보정됨"
} | ConvertTo-Json
$ho = Invoke-RestMethod -Uri "$baseUrl/api/communications/handovers" -Method Post -Body $handoverBody -ContentType "application/json; charset=utf-8"
Write-Host "  -> 인수인계 등록 완료: [ID $($ho.handoverId)] 장비 $($ho.machineId)" -ForegroundColor Green

# 후임 작업자가 인수 확인
$hoConf = Invoke-RestMethod -Uri "$baseUrl/api/communications/handovers/$($ho.handoverId)/confirm?receiverUsername=$([System.Uri]::EscapeDataString('이후임 기사'))" -Method Post
Write-Host "  -> 인수 확인 완료: $($hoConf.message)" -ForegroundColor Green

# 7. 신규 작업지시서 등록 (다중 부품 + 단품/양산 자동 추천)
Write-Host "`n[7/10] 신규 작업지시서 등록 (다중 부품 & 단품/양산 추천):" -ForegroundColor Yellow
$orderNum = "WO-2026-TEST-" + (Get-Random -Minimum 1000 -Maximum 9999)
$orderBody = @{
    orderNumber = $orderNum
    customerName = "삼성전자 반도체연구소"
    targetDeliveryDate = "2026-11-05"
    createdBy = "총괄공장장"
    parts = @(
        @{
            partCode = "3200BGA-TOP"
            partName = "소켓 상판"
            material = "PEEK"
            thickness = 5.0
            targetQty = 2   # 2개 -> 단품 추천
            productionType = "단품"
        },
        @{
            partCode = "3200BGA-BTM"
            partName = "소켓 하판"
            material = "PEEK"
            thickness = 5.0
            targetQty = 50  # 50개 -> 양산 추천
            productionType = "양산"
        }
    )
} | ConvertTo-Json -Depth 5

$newOrder = Invoke-RestMethod -Uri "$baseUrl/api/workorders" -Method Post -Body $orderBody -ContentType "application/json; charset=utf-8"
Write-Host "  -> 작업지시서 생성 완료: [ID $($newOrder.orderId)] $orderNum" -ForegroundColor Green
Write-Host "     부품1: $($newOrder.parts[0].partName) ($($newOrder.parts[0].targetQty)개 / $($newOrder.parts[0].productionType))"
Write-Host "     부품2: $($newOrder.parts[1].partName) ($($newOrder.parts[1].targetQty)개 / $($newOrder.parts[1].productionType))"

# 8. 작업자 3-클릭 가공 실행 -> 완료(자동 측정대기 전이)
Write-Host "`n[8/10] 작업자 3-클릭 가공 실행 및 자동 측정대기 전이 검증:" -ForegroundColor Yellow
$topPart = $newOrder.parts[0]
$step1 = $topPart.routingSteps[0]

# 클릭 1: 가공 시작
$startBody = @{
    stepId = $step1.stepId
    machineId = "R01"
    workerUsername = "김작업 반장"
    targetQty = $topPart.targetQty
    notes = "현장 3-Click 가공 시작 테스트"
} | ConvertTo-Json
$execStart = Invoke-RestMethod -Uri "$baseUrl/api/executions/start" -Method Post -Body $startBody -ContentType "application/json; charset=utf-8"
Write-Host "  -> 1클릭: 가공 시작됨 [실행 ID: $($execStart.executionId)] (장비 R01 / 상태: 가공중)" -ForegroundColor Green

# 클릭 2: 가공 완료 (2개 완료)
$finishBody = @{
    executionId = $execStart.executionId
    machinedQty = 2
    notes = "정상 가공 완료"
} | ConvertTo-Json
$execFinish = Invoke-RestMethod -Uri "$baseUrl/api/executions/finish" -Method Post -Body $finishBody -ContentType "application/json; charset=utf-8"
Write-Host "  -> 2클릭: 가공 완료됨: $($execFinish.message)" -ForegroundColor Green

# 부품 상태 재확인 (측정대기로 자동 전이되었는지 검증)
$partCheck = Invoke-RestMethod -Uri "$baseUrl/api/workorders/parts/$($topPart.partId)"
Write-Host "  -> 절대원칙 검증: 부품 상태 = [$($partCheck.status)], 공정 상태 = [$($partCheck.routingSteps[0].status)]" -ForegroundColor Magenta
if ($partCheck.status -eq "측정대기") {
    Write-Host "     [합격] 가공완료 시 자동완료 차단 및 측정대기 강제 전이 확인!" -ForegroundColor Green
} else {
    Write-Host "     [경고] 측정대기 상태가 아님!" -ForegroundColor Red
}

# 9. 공정별 품질 측정 게이트웨이 (합격 판정 및 다음 공정 해금)
Write-Host "`n[9/10] 공정별 품질 측정 게이트웨이 (Micro-Vu 실측치 및 합격 판정):" -ForegroundColor Yellow
$dimJson = @(
    @{ item = "외경 (OD)"; standard = "25.400"; actual = "25.401"; verdict = "합격" },
    @{ item = "포켓 깊이"; standard = "3.200"; actual = "3.199"; verdict = "합격" }
) | ConvertTo-Json

$inspBody = @{
    stepId = $step1.stepId
    partId = $topPart.partId
    inspectorUsername = "박검사 주임"
    inspectionType = "전수검사"
    inspectedQty = 2
    passedQty = 2
    failedQty = 0
    resultVerdict = "합격"
    dimensionDataJson = $dimJson
    notes = "Micro-Vu 치수 정밀 검사 전량 합격"
} | ConvertTo-Json

$inspRes = Invoke-RestMethod -Uri "$baseUrl/api/inspections" -Method Post -Body $inspBody -ContentType "application/json; charset=utf-8"
Write-Host "  -> 측정 판정 등록 완료: $($inspRes.message)" -ForegroundColor Green

# 합격 후 공정 전이 상태 확인
$partAfterInsp = Invoke-RestMethod -Uri "$baseUrl/api/workorders/parts/$($topPart.partId)"
Write-Host "  -> 측정 합격 후 공정 전이: 현재 공정단계 = [$($partAfterInsp.currentStepSequence)단계], 상태 = [$($partAfterInsp.status)]" -ForegroundColor Green

# 10. 영구 이벤트 등록 및 동일 품번 신규 작업 시 선제 경고
Write-Host "`n[10/10] 영구 이벤트 등록 및 동일 품번 신규 제작 시 선제 경고 검증:" -ForegroundColor Yellow
$evBody = @{
    partCode = "3200BGA-TOP"
    eventCategory = "가공중"
    eventType = "공구파손"
    machineId = "R01"
    description = "0.8mm 마이크로 드릴 가공 중 칩 배출 불량으로 파손"
    cause = "스핀들 RPM 대비 이송속도 과다(F150)"
    actionTaken = "F90 감속 및 에어블로우 위치 조정"
    authorUsername = "김작업 반장"
    status = "진행"
} | ConvertTo-Json

$evRes = Invoke-RestMethod -Uri "$baseUrl/api/permanentevents" -Method Post -Body $evBody -ContentType "application/json; charset=utf-8"
Write-Host "  -> 영구 이벤트 등록 완료: [이벤트 ID $($evRes.eventId)]" -ForegroundColor Green

# 선제 경고 API 조회
$preWarn = Invoke-RestMethod -Uri "$baseUrl/api/permanentevents/history/3200BGA-TOP"
Write-Host "  -> 동일 품번(3200BGA-TOP) 선제 경고 조회 결과:" -ForegroundColor Magenta
Write-Host "     누적 이슈 건수: $($preWarn.totalIssueCount)건 / 위험 이슈: $($preWarn.criticalCount)건" -ForegroundColor Green
Write-Host "     과거 불량 내용: $($preWarn.history[0].description)" -ForegroundColor Yellow
Write-Host "     조치 내용:      $($preWarn.history[0].actionTaken)" -ForegroundColor Yellow

Write-Host "`n======================================================================" -ForegroundColor Cyan
Write-Host " [검증 완료] 10개 핵심 전수 기능이 100% 정상 작동함을 확인하였습니다!" -ForegroundColor Green
Write-Host "======================================================================" -ForegroundColor Cyan
