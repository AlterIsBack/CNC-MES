# OPENSOURCE COMPARISON: 오픈소스 분석 및 CNC 현장 적합성 평가

**작성 일시:** 2026-10-26  
**분석 대상:** OpenMES, WebErpMesv2, Zulip  
**비교 기준:** CNC 현장 적합성, 오프라인 LAN 단독 설치성, C#/FOCAS 장비 연동성, 데이터 구조 유연성, 라이선스 의무

---

## 1. 후보 오픈소스 정밀 분석

### 1.1 OpenMES (Mes-Open/OpenMes)
- **공식 저장소:** https://github.com/Mes-Open/OpenMes
- **기술 스택:** PHP 8.x / Laravel Framework, MySQL / PostgreSQL, Docker / Docker Compose
- **라이선스:** GNU Affero General Public License v3.0 (AGPL-3.0)
- **주요 기능:**
  - 작업 주문(Work Order), 생산 라우팅(Routing), 작업장(Work Center) 관리
  - 작업자 작업 시작/종료 인터페이스 (Kiosk UI)
  - 자재 소요 및 재고 기본 추적
- **CNC 현장 적합성 평가 및 한계점:**
  1. **교차 가공 및 세팅 그룹 미지원**: OpenMES는 단일 제품 중심의 선형 생산라인 구조로 설계되어 있어, 유사 세팅을 활용하여 여러 작업지시서의 부품을 동시에 또는 교차로 가공하는 정밀 가공 현장의 유연성을 수용하지 못함.
  2. **장비 제어기(FANUC FOCAS) 연동의 물리적 단절**: FANUC FOCAS 라이브러리는 C/C++ 윈도우 네이티브 바이너리(`Fwlib32.dll`, `FWLIB64.DLL`)입니다. PHP / Laravel 웹 환경에서 이 DLL과 직접 고속 통신하는 것은 불가능에 가까우며, 별도의 중계 데몬이 필수적임.
  3. **사내 오프라인 폐쇄망 배포 난이도**: Windows 기반 사내 폐쇄망 환경에서 PHP, Composer, 웹서버(Nginx/Apache), 데이터베이스, Docker Desktop을 셋업하는 것은 현장 작업자/관리자 PC 환경에 과도한 리소스 낭비와 유지보수 오버헤드를 초래함.
  4. **AGPL-3.0 라이선스 의무**: AGPL-3.0은 네트워크를 통해 서비스를 제공하는 경우에도 모든 수정 소스코드의 공개 의무를 부과합니다. 사내 독자 가공 노하우와 FOCAS 인터페이스 코드가 결합될 경우 라이선스 침해 리스크가 존재함.

### 1.2 WebErpMesv2 (SMEWebify/WebErpMesv2)
- **공식 저장소:** https://github.com/SMEWebify/WebErpMesv2
- **기술 스택:** PHP / Laravel, AdminLTE / Bootstrap
- **주요 기능:**
  - 견적(Quotation)부터 송장(Invoicing)까지의 ERP 전주기
  - BOM, 라우팅, 유한 능력 생산 스케줄링, CAD 뷰어 연동
- **CNC 현장 적합성 평가 및 한계점:**
  1. **현장 작업자 UI 부적합**: ERP 중심 시스템 특성상 입력 필드와 메뉴 뎁스가 너무 깊어, "작업자는 최대한 적게 입력하고 시스템은 최대한 자동 처리"해야 하는 원칙에 정면으로 위배됨.
  2. **단품(1~3개) 가공 특성 결여**: 엔지니어링 플라스틱/반도체 소켓 부품의 잦은 설계변경, 도면 개정, 공정 중 수정가공 흐름을 지원하기에는 데이터 모델이 너무 경직되어 있음.

### 1.3 Zulip (zulip/zulip)
- **공식 저장소:** https://github.com/zulip/zulip
- **기술 스택:** Python 3 / Django, PostgreSQL, Redis, RabbitMQ, Memcached, Tornado
- **라이선스:** Apache License 2.0
- **주요 기능:**
  - 스트림(Stream) 및 토픽(Topic) 기반의 체계적인 스레드형 대화
  - 마크다운 지원, 첨부파일, API 봇
- **CNC 현장 적합성 평가 및 한계점:**
  1. **Windows 사내 LAN 단독 설치 불가**: Zulip은 공식적으로 Linux(Ubuntu/Debian) 전용 서버 소프트웨어입니다. 사내 폐쇄망 Windows PC에서 구동하려면 무거운 WSL2 또는 가상머신, 수많은 데몬(Postgres, Redis, RabbitMQ 등)을 상시 유지해야 하므로 단일 PC 온프레미스 서버로서 비효율적임.
  2. **MES 생산 데이터와의 격리**: Zulip 자체는 훌륭한 메신저이나, 가공 작업지시서, 부품, 공정, 불량 이벤트와의 즉각적인 상태 연동 및 인수인계 공식 결재 흐름을 구현하기 위해서는 광범위한 Webhook/Bot 개발이 별도로 필요함.

---

## 2. 채택 아키텍처 및 재사용 전략

### 결론: 하이브리드 아키텍처 (C# .NET 8 경량 코어 + 오픈소스 우수 패턴 내재화)

1. **OpenMES의 선진 MES 데이터 구조 벤치마킹 및 내재화:**
   - 작업지시서(Work Order) - 부품(Part) - 공정(Routing) - 검사(Inspection)의 다계층 데이터 모델을 C# / SQLite WAL 기반으로 최적화하여 내재화.
   - 선형적 제약을 탈피하여 **세팅 그룹(Setting Group)** 및 **장비 분할/교차 가공(Cross-Machining)** 완벽 지원.

2. **Zulip의 토픽 기반(Topic-based) 커뮤니케이션 개념 내재화:**
   - 기존 검증된 `WorkHub`의 C# SignalR WebSocket 엔진을 확장.
   - 작업지시서별, 부품별, 공정별로 자동으로 토픽 채널이 생성되고, 대화 중 중요한 기술적 결정이나 불량 보고는 클릭 한 번으로 **"공식 이벤트 히스토리"**로 자동 승격되는 일체형 시스템 구축.

3. **기존 자체 개발 C# FOCAS / Python 가공시간 자산 100% 결합:**
   - .NET 8 단일 런타임에서 FOCAS 통신 모듈과 직접 네이티브 통신 가능.
   - 외부 종속성 없는 완전한 단독 실행 파일(`.exe`) 및 브라우저 기반 다중 접속 보장.
   - 오프라인 사내 LAN 환경에서 관리자 PC 1대로 즉시 가동.
