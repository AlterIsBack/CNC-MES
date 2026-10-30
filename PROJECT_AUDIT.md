# PROJECT AUDIT: 기존 자체 개발 프로그램 분석 및 기술 자산 정밀 감사

**작성 일시:** 2026-10-26  
**대상 환경:** CNC 가공 현장 (FANUC RoboDrill 5대, Roku-Roku 2대, 단말기 PC, 관리자 PC)  
**분석 대상 경로:** `c:\Users\USER\Desktop\내폴더`

---

## 1. 개요 및 목적

본 감사는 신규 CNC 통합 가공관리 시스템(MES) 개발에 앞서, 사용자가 기존에 자체 개발하여 현장에서 검증해 온 소프트웨어 자산의 구현 상태, 기술 스택, 재사용 가능성, 그리고 한계점을 정밀하게 파악하기 위해 수행되었다.

---

## 2. 자산별 정밀 분석

### 2.1 WorkHub (CNC 가공 현장 업무공유 시스템 v0.2.1)
- **위치:** `c:\Users\USER\Desktop\내폴더\WorkHub`
- **기술 스택:** C# ASP.NET Core 8.0, SQLite (WAL 모드), SignalR (WebSocket 양방향 실시간 동기화), HTML5/CSS/Vanilla JS
- **구현 기능:**
  - 완전 오프라인 사내 LAN 구동 (외부 인터넷 연결 불필요, 포트 5000 바인딩)
  - 다중 PC 동시 접속 및 SignalR 실시간 상태 브로드캐스트
  - 작업(Job) 등록 및 상태(`대기`, `진행중`, `완료`, `보류`), 진척률 추적
  - 작업 로그(JobLog) 및 사고/특이사항/조치 기록
  - 현장 공지사항(Notice) 및 팝업 공지
  - 실시간 채팅(Chat) 기능 및 첨부파일 연동
  - 감사 로그(Audit Log) 및 PBKDF2 암호화 계정 관리 (Admin/Worker 권한)
- **장점:**
  - 가볍고 신뢰성 높은 .NET 8 단일 프로세스/온프레미스 서버
  - 브라우저 기반으로 작업자/관리자 PC에 별도 클라이언트 설치 없이 즉시 사용 가능
- **개선 및 확장 필요점:**
  - 현재 단일 Job 단위로 관리되어, '하나의 작업지시서에 다중 부품(Multi-Part)' 및 '공정(Routing)' 구조 부재
  - 단품/양산 분리 관리 체계 미흡
  - 공정별 측정(Inspection: 기준치수, 공차, 실측값, 판정) 및 측정대기 플로우 부재
  - 세팅 그룹 및 장비 분할 가공 추적 모델 추가 필요

### 2.2 파낙모니터링프로그램
- **위치:** `c:\Users\USER\Desktop\내폴더\파낙모니터링프로그램`
- **기술 스택:** C# WPF, .NET Core / .NET Framework, FANUC FOCAS1 라이브러리 (`Focas1.cs`)
- **구현 기능:**
  - FANUC RoboDrill CNC 장비와의 이더넷(FOCAS) 통신
  - 장비 운전 상태(RUN, STOP, ALARM), 주축 회전수, 이송속도, 모달 정보 수집
  - 실행 중인 프로그램 번호(O-Number) 및 블록 추적 (`ProgramRunTracking.cs`)
  - 실시간 이벤트 및 알람 로깅 (`EventLogEngine.cs`)
  - 공구 셋업 및 공구 정보 연동 (`LegacyToolSetup.cs`, `ToolSetupStore.cs`)
- **MES 연계 방안:**
  - FOCAS 통신 모듈과 장비 상태 판정 로직을 서비스 레이어로 연동
  - 장비 7대(RoboDrill 5대, Roku-Roku 2대) 중 FOCAS 지원 장비의 실시간 상태를 MES 중앙 서버로 주기적 브로드캐스트

### 2.3 가공시간 (CNC 통합 대시보드 및 가공시간 수집기)
- **위치:** `c:\Users\USER\Desktop\내폴더\가공시간`
- **기술 스택:** Python, `focas_auto_updater.py`, `dashboard_server.py`, Excel(openpyxl), Web Frontend
- **구현 기능:**
  - FOCAS 통신을 통한 가동시간, 정지시간, 가공 완료 카운트 자동 백그라운드 수집
  - 날짜별/호기별 가공 프로그램 가동시간 및 공구 리스트 엑셀 자동 누적 기록
  - 가공시간 통합 대시보드 웹 뷰어
- **MES 연계 방안:**
  - 가공 시간 집계 알고리즘과 장비 운전시간 이력 추출 로직을 MES의 장비 가동률 및 예상 가공 완료 시점 계산에 활용

### 2.4 시뮬레이터 (NC G-Code 시뮬레이터 & 백플롯)
- **위치:** `c:\Users\USER\Desktop\내폴더\시뮬레이터`
- **기술 스택:** HTML5 Canvas, WebGL/Three.js, JavaScript (`engine.js`, `fanuc-macros.js`, `stock-workspace.js`)
- **구현 기능:**
  - FANUC 매크로 및 표준 G-code 3D 백플롯 시각화
  - 공작물(Stock) 정의 및 공구(Tool) 이동 궤적 렌더링
  - 웹 기반 경량 뷰어 구조
- **MES 연계 방안:**
  - MES 작업자 화면 및 관리자 화면에서 NC 프로그램 파일 클릭 시 팝업 3D 궤적 뷰어로 독립 임베딩

### 2.5 FTP전송프로그램 (CNCFileTransfer)
- **위치:** `c:\Users\USER\Desktop\내폴더\FTP전송프로그램`
- **기술 스택:** C# WinForms, FTP Client/Server 통신, 라이선스 관리
- **구현 기능:**
  - PC와 CNC 제어기간 NC 프로그램 송수신
  - NC 파일 디렉토리 관리, 체크섬 및 버전 추적
- **MES 연계 방안:**
  - MES의 NC 프로그램 등록/배포 시 파일 해시(SHA256) 검증 및 장비 전송 연계

### 2.6 신호등4
- **위치:** `c:\Users\USER\Desktop\내폴더\신호등4`
- **기술 스택:** C# WinForms, FANUC FOCAS 네이티브 DLL (`Fwlib32.dll`, `FWLIB64.DLL`)
- **구현 기능:**
  - 장비별 3색 신호등(녹색-가공, 황색-대기, 적색-알람/정지) 시각 모니터링
  - 알람 발생 시 사운드 경보 재생

---

## 3. 종합 평가 및 재사용 전략

사용자가 보유한 자체 프로그램들은 이미 **CNC 가공 현장의 핵심 물리적 계층(FOCAS 통신, NC 파일 전송, 가동시간 로깅, 시뮬레이션, 오프라인 LAN 웹 서버)**을 완벽하게 구현해 두었다.

따라서 이번 프로젝트의 핵심은 **이 분산된 자산들을 유기적으로 통합하고, 누락되어 있던 전문 MES 생산관리 계층(다중 부품 작업지시서, 공정별 측정, 세팅 그룹, 영구 이벤트 히스토리, 단품/양산 분리 관리)**을 일관된 단일 오프라인 웹 플랫폼 위에 완벽하게 구축하는 것이다.
