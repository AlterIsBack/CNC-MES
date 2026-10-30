// CNC-MES Frontend Application v1.1.0
let currentView = 'dashboard';
let currentTopic = { type: 'GENERAL', key: '공통' };
let allPartsCache = [];
let allOrdersCache = [];
let currentUser = null;
let signalrConn = null;

// Initialization
document.addEventListener('DOMContentLoaded', async () => {
  initSignalR();
  await checkAuthStatus();
  await loadMachines();
  await loadOrders();
  await loadEvents();
  await loadNotices();
  await loadHandovers();
  await loadChat();
  setInterval(loadMachines, 10000); // Heartbeat refresh
});

// View Switching
function switchView(viewName) {
  currentView = viewName;
  document.querySelectorAll('.view-panel').forEach(el => el.classList.remove('active'));
  document.querySelectorAll('.nav-btn').forEach(el => el.classList.remove('active'));

  const target = document.getElementById(`view-${viewName}`);
  if (target) target.classList.add('active');

  const btnIndex = ['dashboard', 'operator', 'inspection', 'events', 'comms', 'users'].indexOf(viewName);
  if (btnIndex >= 0) {
    document.querySelectorAll('.nav-btn')[btnIndex]?.classList.add('active');
  }

  if (viewName === 'comms') {
    const badge = document.getElementById('newChatBadge');
    if (badge) badge.style.display = 'none';
  }

  if (viewName === 'operator') {
    populateOperatorPartSelect();
    loadOperatorExecutions();
  } else if (viewName === 'inspection') {
    populateInspectionPartSelect();
  } else if (viewName === 'users') {
    loadUsers();
  }
}

// 1. Auth & User Management
async function checkAuthStatus() {
  try {
    const res = await fetch('/api/auth/status');
    const data = await res.json();
    
    // Check local storage for logged-in user
    const saved = localStorage.getItem('cnc_mes_user');
    if (saved) {
      try {
        currentUser = JSON.parse(saved);
        updateUserUI();
      } catch { }
    }

    if (!data.initialized) {
      // First run: No admin account exists
      openModal('setupAdminModal');
    } else if (!currentUser) {
      // Prompt login if not logged in
      openModal('loginModal');
    }
  } catch (err) {
    console.error('Failed to check auth status', err);
  }
}

function updateUserUI() {
  const display = document.getElementById('currentUserDisplay');
  const btn = document.getElementById('authBtn');
  if (currentUser) {
    display.innerHTML = `<span style="color:var(--primary); font-weight:700;">[${currentUser.role}]</span> ${currentUser.displayName}`;
    btn.innerText = '로그아웃';
    btn.className = 'btn btn-outline';
  } else {
    display.innerText = '로그인 필요';
    btn.innerText = '로그인';
    btn.className = 'btn btn-primary';
  }
}

function handleAuthClick() {
  if (currentUser) {
    // Logout
    if (confirm('로그아웃 하시겠습니까?')) {
      currentUser = null;
      localStorage.removeItem('cnc_mes_user');
      updateUserUI();
      openModal('loginModal');
    }
  } else {
    openModal('loginModal');
  }
}

async function submitSetupAdmin() {
  const username = document.getElementById('adminUsername').value.trim();
  const displayName = document.getElementById('adminDisplayName').value.trim();
  const password = document.getElementById('adminPassword').value.trim();

  if (!username || !password) {
    alert('관리자 아이디와 비밀번호를 모두 입력하세요.');
    return;
  }

  try {
    const res = await fetch('/api/auth/setup-admin', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password, displayName, role: 'ADMIN' })
    });
    if (!res.ok) throw new Error(await res.text());

    // Auto login
    currentUser = { username, displayName, role: 'ADMIN' };
    localStorage.setItem('cnc_mes_user', JSON.stringify(currentUser));
    updateUserUI();
    closeModal('setupAdminModal');
    alert('마스터 관리자 계정이 생성되었습니다!');
  } catch (err) {
    alert('관리자 생성 실패: ' + err.message);
  }
}

async function submitLogin() {
  const username = document.getElementById('loginUsername').value.trim();
  const password = document.getElementById('loginPassword').value.trim();

  if (!username || !password) {
    alert('아이디와 비밀번호를 입력해주세요.');
    return;
  }

  try {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    });
    if (!res.ok) {
      const err = await res.json();
      throw new Error(err.message || '로그인 실패');
    }
    const user = await res.json();
    currentUser = user;
    localStorage.setItem('cnc_mes_user', JSON.stringify(currentUser));
    updateUserUI();
    closeModal('loginModal');
    alert(`환영합니다, ${user.displayName}님! (${user.role})`);
  } catch (err) {
    alert('로그인 오류: ' + err.message);
  }
}

// 2. SignalR Realtime Client (100% Offline)
function initSignalR() {
  if (typeof signalR === 'undefined') {
    console.warn('SignalR library not available locally.');
    return;
  }

  signalrConn = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/mes')
    .withAutomaticReconnect()
    .build();

  signalrConn.on('MachineStatusChanged', () => loadMachines());
  signalrConn.on('OrderUpdated', () => { loadOrders(); loadMachines(); });
  signalrConn.on('ExecutionStarted', () => { loadOrders(); loadMachines(); });
  signalrConn.on('ExecutionFinished', () => { loadOrders(); loadMachines(); });
  signalrConn.on('InspectionRecorded', () => { loadOrders(); loadMachines(); });
  signalrConn.on('NoticeCreated', (n) => {
    alert(`[공지 알림] ${n.title}\n${n.content}`);
    loadNotices();
  });
  signalrConn.on('NoticeAcknowledged', () => {
    loadNotices();
  });
  signalrConn.on('PermanentEventCreated', (ev) => {
    alert(`🚨 [영구 이벤트 등록 알림]\n품번: ${ev.partCode}\n유형: ${ev.eventType}\n내용: ${ev.description}`);
    loadEvents();
  });
  signalrConn.on('ChatMessageReceived', (msg) => {
    if (msg.channelType === currentTopic.type && msg.topicKey === currentTopic.key) {
      appendChatMessage(msg);
    }
  });
  signalrConn.on('ChatNotification', (msg) => {
    if (currentView !== 'comms') {
      const badge = document.getElementById('newChatBadge');
      if (badge) {
        badge.style.display = 'inline-block';
        badge.innerText = `🔔 새 채팅 (${msg.author})`;
      }
    }
  });

  signalrConn.start()
    .then(() => {
      document.getElementById('connStatus').innerText = '사내 LAN 실시간 동기화';
      joinCurrentTopic();
    })
    .catch(err => {
      console.error('SignalR offline: ', err);
      document.getElementById('connStatus').innerText = '오프라인 폴링 모드';
    });
}

function joinCurrentTopic() {
  if (signalrConn && signalrConn.state === 'Connected') {
    signalrConn.invoke('JoinTopic', currentTopic.type, currentTopic.key).catch(console.error);
  }
}

// 3. Machines Grid & Worker Active Table
async function loadMachines() {
  try {
    const res = await fetch('/api/machines');
    const machines = await res.json();
    const grid = document.getElementById('machineGrid');
    const countTitle = document.getElementById('machineCountTitle');
    if (countTitle) {
      countTitle.innerText = `CNC 가공 장비 실시간 현황 (총 ${machines.length}대)`;
    }

    // Update machine dropdowns dynamically
    const opSel = document.getElementById('operatorMachineSelect');
    if (opSel) {
      const curVal = opSel.value;
      opSel.innerHTML = machines.map(m => `<option value="${m.machineId}">${m.machineName}</option>`).join('');
      if (curVal && machines.some(m => m.machineId === curVal)) opSel.value = curVal;
    }

    if (grid) {
      grid.innerHTML = machines.map(m => {
        const statusClass = m.status === '가공중' ? 'running' :
                            (m.status === '세팅중' || m.status === '단도리/세팅') ? 'setup' :
                            m.status === '측정중' ? 'inspecting' :
                            m.status === '공구교체' ? 'toolchange' :
                            m.status === '대기' ? 'idle' :
                            m.status === '알람' ? 'alarm' : 'offline';
        
        const jobNo = m.activeJobNumber || '-';
        const partNm = m.activePartName || '-';
        const workDesc = m.currentProgram || '대기중';

        return `
          <div class="machine-card" style="position:relative;">
            <div class="machine-header">
              <span class="machine-name">${m.machineName}</span>
              <div style="display:flex; align-items:center; gap:6px;">
                <span class="status-badge ${statusClass}">${m.status}</span>
                <button onclick="deleteMachine('${m.machineId}')" title="장비 삭제" style="background:transparent; border:1px solid #475569; color:#94a3b8; border-radius:4px; padding:1px 5px; font-size:0.7rem; cursor:pointer;">✕</button>
              </div>
            </div>
            <div class="machine-meta">
              <div>제어기: ${m.controllerType} <span style="color:#64748b;">(${m.modelType || ''})</span></div>
              <div>작지번호: <span style="color:#f59e0b; font-weight:700;">${jobNo}</span></div>
              <div>가공부품: <span style="color:#10b981; font-weight:700;">${partNm}</span></div>
              <div style="font-weight:600; color:#e2e8f0;">세부작업: <span style="color:#38bdf8;">${workDesc}</span></div>
              <div>작업자: <span style="color:#f8fafc; font-weight:700;">${m.activeWorker || '-'}</span></div>
            </div>
          </div>
        `;
      }).join('');
    }

    // Populate Worker Active Table (작업자별 가공중인 작지번호 및 부품 현황)
    const workerTbody = document.getElementById('workerActiveTableBody');
    if (workerTbody) {
      let logs = [];
      try {
        const logRes = await fetch('/api/machines/work-logs?limit=40');
        if (logRes.ok) logs = await logRes.json();
      } catch { }

      const activeMachines = machines.filter(m => (m.activeWorker && m.activeWorker !== '-') || m.status !== '대기');
      if (activeMachines.length === 0 && logs.length === 0) {
        workerTbody.innerHTML = '<tr><td colspan="7" style="text-align:center; color:#64748b; padding:14px;">현재 가동 중이거나 보고된 작업자 가공 내역이 없습니다.</td></tr>';
      } else {
        let rowsHtml = activeMachines.map(m => {
          const stClass = m.status === '가공중' ? 'running' : m.status === '알람' ? 'alarm' : 'idle';
          return `
            <tr>
              <td><strong style="color:#f8fafc;">👷 ${m.activeWorker || '미지정'}</strong></td>
              <td><strong style="color:#38bdf8;">${m.machineName}</strong></td>
              <td><span style="color:#f59e0b; font-weight:700;">${m.activeJobNumber || '-'}</span></td>
              <td><span style="color:#10b981; font-weight:700;">${m.activePartName || '-'}</span></td>
              <td>${m.currentProgram || '-'}</td>
              <td><span class="status-badge ${stClass}">${m.status}</span></td>
              <td style="font-size:0.78rem; color:#94a3b8;">${m.lastHeartbeat || '-'}</td>
            </tr>
          `;
        }).join('');

        // Also append recent logs if activeMachines is empty
        if (activeMachines.length === 0 && logs.length > 0) {
          rowsHtml = logs.slice(0, 10).map(l => `
            <tr>
              <td><strong>👷 ${l.workerName}${l.subWorkerName ? ` (서브: ${l.subWorkerName})` : ''}</strong></td>
              <td><strong style="color:#38bdf8;">${l.machineId}</strong></td>
              <td><span style="color:#f59e0b; font-weight:700;">${l.orderNumber}</span></td>
              <td><span style="color:#10b981; font-weight:700;">${l.partName}</span></td>
              <td>${l.workDetail}</td>
              <td><span class="status-badge ${l.status === '가공중' ? 'running' : 'idle'}">${l.status}</span></td>
              <td style="font-size:0.78rem; color:#94a3b8;">${l.createdAt}</td>
            </tr>
          `).join('');
        }
        workerTbody.innerHTML = rowsHtml;
      }
    }
  } catch (err) {
    console.error('Failed to load machines', err);
  }
}

function openAddMachineModal() {
  openModal('addMachineModal');
}

async function submitAddMachine() {
  const machineId = document.getElementById('newMachineId').value.trim().toUpperCase();
  const machineName = document.getElementById('newMachineName').value.trim();
  const model = document.getElementById('newMachineModel').value.trim() || 'CNC Machine';
  const controllerType = document.getElementById('newMachineController').value.trim() || 'FANUC';

  if (!machineId || !machineName) {
    alert('장비 코드(ID)와 장비명을 입력해주세요.');
    return;
  }

  try {
    const res = await fetch('/api/machines', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ machineId, machineName, modelType: model, controllerType, status: '대기' })
    });
    if (!res.ok) throw new Error(await res.text());
    closeModal('addMachineModal');
    document.getElementById('newMachineId').value = '';
    document.getElementById('newMachineName').value = '';
    loadMachines();
  } catch (err) {
    alert('장비 추가 실패: ' + err.message);
  }
}

async function deleteMachine(machineId) {
  if (!confirm(`장비 [${machineId}]를 대시보드 및 목록에서 삭제하시겠습니까?`)) return;
  try {
    const res = await fetch(`/api/machines/${encodeURIComponent(machineId)}`, { method: 'DELETE' });
    if (!res.ok) throw new Error(await res.text());
    loadMachines();
  } catch (err) {
    alert('장비 삭제 실패: ' + err.message);
  }
}

// 4. Orders & Parts Management
async function loadOrders(filterStatus = '') {
  try {
    const url = filterStatus ? `/api/workorders?status=${encodeURIComponent(filterStatus)}` : '/api/workorders';
    const res = await fetch(url);
    const orders = await res.json();
    allOrdersCache = orders;
    renderFilteredOrders();

    // Cache parts
    allPartsCache = [];
    let waitingInsp = [];
    orders.forEach(o => {
      o.parts.forEach(p => {
        allPartsCache.push(p);
        if (p.status === '측정대기') {
          waitingInsp.push(p);
        }
      });
    });

    const inspCountEl = document.getElementById('waitingInspCount');
    if (inspCountEl) inspCountEl.innerText = `${waitingInsp.length}건`;

    const inspListEl = document.getElementById('waitingInspectionList');
    if (inspListEl) {
      if (waitingInsp.length === 0) {
        inspListEl.innerHTML = '<div style="color:#64748b; padding:10px;">측정 대기 중인 공정이 없습니다.</div>';
      } else {
        inspListEl.innerHTML = waitingInsp.map(p => `
          <div style="background:#0f172a; padding:10px; border-radius:6px; border-left:3px solid #f59e0b;">
            <div><strong>[${p.orderNumber}]</strong> ${p.partName} (${p.partCode})</div>
            <div style="font-size:0.75rem; color:#94a3b8;">수량: ${p.targetQty}개 / 공정: ${p.currentStepSequence}단계 가공완료</div>
            <button class="btn btn-primary" style="margin-top:6px; padding:3px 10px; font-size:0.75rem;" onclick="goToInspectionForPart(${p.partId})">🔬 측정 및 판정</button>
          </div>
        `).join('');
      }
    }
  } catch (err) {
    console.error('Failed to load orders', err);
  }
}

function renderFilteredOrders() {
  const tbody = document.getElementById('orderTableBody');
  if (!tbody) return;
  const kw = (document.getElementById('orderSearchInput')?.value || '').trim().toLowerCase();
  const filtered = allOrdersCache.filter(o => {
    if (!kw) return true;
    const partMatch = o.parts?.some(p => p.partName.toLowerCase().includes(kw) || p.partCode.toLowerCase().includes(kw));
    return o.orderNumber.toLowerCase().includes(kw) || o.customerName.toLowerCase().includes(kw) || partMatch;
  });

  if (filtered.length === 0) {
    tbody.innerHTML = '<tr><td colspan="7" style="text-align:center; color:#64748b; padding:20px;">조건에 맞는 작업지시서가 없습니다.</td></tr>';
  } else {
    tbody.innerHTML = filtered.map(o => {
      const partNames = o.parts.map(p => `${p.partName}(${p.targetQty}EA)`).join(', ');
      return `
        <tr>
          <td><strong style="color:#f59e0b;">${o.orderNumber}</strong></td>
          <td>${o.customerName}</td>
          <td style="color:#10b981; font-weight:600;">${partNames || '부품 없음'}</td>
          <td>${o.targetDeliveryDate}</td>
          <td><span class="status-badge ${o.status === '완료' ? 'running' : 'idle'}">${o.status}</span></td>
          <td>
            <div style="background:#0f172a; border-radius:4px; overflow:hidden; width:70px; height:10px; display:inline-block; vertical-align:middle; border:1px solid #334155;">
              <div style="background:var(--primary); height:100%; width:${o.progressRate}%;"></div>
            </div>
            <span style="font-size:0.75rem; margin-left:4px;">${o.progressRate}%</span>
          </td>
          <td><button class="btn btn-outline" style="padding:2px 8px; font-size:0.75rem;" onclick="viewOrderDetail(${o.orderId})">🔍 열람</button></td>
        </tr>
      `;
    }).join('');
  }
}

let currentOrderModalTab = 'all';

async function syncShareFolderOrders() {
  try {
    const res = await fetch('/api/workorders/share-folder/sync', { method: 'POST' });
    const data = await res.json();
    if (data.success) {
      alert(data.message || '공유폴더 동기화가 완료되었습니다.');
      await loadOrders();
      if (document.getElementById('orderListModal') && document.getElementById('orderListModal').classList.contains('active')) {
        renderOrderListModalFiltered(currentOrderModalTab);
      }
    } else {
      alert('동기화 실패: ' + (data.message || '오류 발생'));
    }
  } catch (err) {
    alert('공유폴더 동기화 통신 오류: ' + err.message);
  }
}

async function completeOrderAndBackup(orderId, orderNum) {
  if (!confirm(`작지번호 [${orderNum}]을(를) '가공완료' 처리하고,
공유폴더의 [00-진행중] 폴더를 [99-완료백업]으로 백업 이동하시겠습니까?`)) {
    return;
  }
  try {
    const res = await fetch(`/api/workorders/${orderId}/complete`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ completedBy: (typeof currentUser !== 'undefined' && currentUser) ? currentUser.displayName : '관리자' })
    });
    const data = await res.json();
    if (data.success) {
      alert(`작지 [${orderNum}] 가공완료 및 백업 이동 처리가 완료되었습니다!
이후 '99-완료백업' 탭에서 도면 파일 및 작업내용을 언제든 열람하실 수 있습니다.`);
      await loadOrders();
      renderOrderListModalFiltered(currentOrderModalTab);
    } else {
      alert('완료 처리 실패: ' + (data.message || '오류'));
    }
  } catch (err) {
    alert('통신 오류: ' + err.message);
  }
}

async function openOrderFilesModal(orderId) {
  const order = allOrdersCache.find(o => o.orderId === orderId);
  const titleEl = document.getElementById('orderFilesTitle');
  const pathInfoEl = document.getElementById('orderFilesPathInfo');
  const bodyEl = document.getElementById('orderFilesBody');
  if (!bodyEl) return;

  titleEl.innerText = `📂 작지 [${order ? order.orderNumber : orderId}] 연결 폴더 및 첨부파일 목록`;
  pathInfoEl.innerHTML = '<div>파일 목록을 불러오는 중...</div>';
  bodyEl.innerHTML = '';
  openModal('orderFilesModal');

  try {
    const res = await fetch(`/api/workorders/${orderId}/files`);
    const data = await res.json();

    if (!data.exists) {
      pathInfoEl.innerHTML = `<div style="color:#ef4444;">⚠️ 연결된 폴더를 찾을 수 없습니다. (경로: ${data.folder || '미지정'})</div>`;
      bodyEl.innerHTML = '<div style="padding:20px; text-align:center; color:#64748b;">폴더 내 파일이 존재하지 않습니다.</div>';
      return;
    }

    const isBackup = data.isBackup;
    const completedAt = data.completedAt || '-';
    pathInfoEl.innerHTML = `
      <div style="display:flex; justify-content:space-between; align-items:center;">
        <div>
          <div><strong>저장 위치:</strong> <code>${data.folder}</code></div>
          <div style="margin-top:4px;"><strong>상태:</strong> ${isBackup ? `<span style="color:#10b981; font-weight:bold;">📦 가공완료 백업보관 (완료시각: ${completedAt})</span>` : '<span style="color:#38bdf8; font-weight:bold;">⚙️ 00-진행중 (현재 가공 작업 진행중)</span>'}</div>
        </div>
      </div>
    `;

    if (!data.files || data.files.length === 0) {
      bodyEl.innerHTML = '<div style="padding:20px; text-align:center; color:#64748b;">폴더에 파일이 없습니다.</div>';
      return;
    }

    bodyEl.innerHTML = `
      <table style="width:100%; font-size:0.84rem;">
        <thead>
          <tr style="background:#1e293b; color:#94a3b8;">
            <th style="padding:8px; text-align:left;">파일명</th>
            <th style="padding:8px; text-align:right; width:110px;">크기</th>
            <th style="padding:8px; text-align:center; width:170px;">최종 수정일시</th>
          </tr>
        </thead>
        <tbody>
          ${data.files.map(f => {
            const sz = f.size;
            const szStr = sz < 1024 ? `${sz} B` : sz < 1048576 ? `${(sz / 1024).toFixed(1)} KB` : `${(sz / 1048576).toFixed(2)} MB`;
            return `
              <tr style="border-bottom:1px solid #334155;">
                <td style="padding:8px; font-weight:600; color:#f8fafc;">📄 ${f.fileName}</td>
                <td style="padding:8px; text-align:right; color:#94a3b8;">${szStr}</td>
                <td style="padding:8px; text-align:center; color:#64748b;">${f.updatedAt}</td>
              </tr>
            `;
          }).join('')}
        </tbody>
      </table>
    `;
  } catch (err) {
    pathInfoEl.innerHTML = `<div style="color:#ef4444;">파일 목록을 불러오는 중 오류 발생: ${err.message}</div>`;
  }
}

function openOrderListModal() {
  currentOrderModalTab = 'all';
  renderOrderListModalFiltered('all');
  openModal('orderListModal');
}

function renderOrderListModalFiltered(tab) {
  currentOrderModalTab = tab;
  const body = document.getElementById('orderListModalBody');
  if (!body) return;

  const btnAll = document.getElementById('btnOrderTabAll');
  const btnActive = document.getElementById('btnOrderTabActive');
  const btnCompleted = document.getElementById('btnOrderTabCompleted');
  const searchInput = document.getElementById('orderModalSearchInput');
  const query = (searchInput ? searchInput.value.trim().toLowerCase() : '');

  if (btnAll) btnAll.style.background = (tab === 'all' ? '#334155' : 'transparent');
  if (btnActive) btnActive.style.background = (tab === 'active' ? '#334155' : 'transparent');
  if (btnCompleted) btnCompleted.style.background = (tab === 'completed' ? '#334155' : 'transparent');

  const btnHidden = document.getElementById('btnOrderTabHidden');
  if (btnHidden) btnHidden.style.background = (tab === 'hidden' ? '#334155' : 'transparent');

  let list = allOrdersCache.slice();
  if (tab === 'hidden') {
    list = list.filter(o => o.isHidden);
  } else {
    list = list.filter(o => !o.isHidden);
    if (tab === 'active') {
      list = list.filter(o => o.status !== '가공완료' && o.status !== '완료');
    } else if (tab === 'completed') {
      list = list.filter(o => o.status === '가공완료' || o.status === '완료');
    }
  }

  if (query) {
    list = list.filter(o =>
      (o.orderNumber && o.orderNumber.toLowerCase().includes(query)) ||
      (o.customerName && o.customerName.toLowerCase().includes(query)) ||
      (o.parts && o.parts.some(p => p.partName.toLowerCase().includes(query) || p.partCode.toLowerCase().includes(query)))
    );
  }

  if (list.length === 0) {
    body.innerHTML = '<div style="padding:28px; color:#64748b; text-align:center;">해당 조건의 작업지시서가 없습니다.</div>';
    return;
  }

  body.innerHTML = list.map(o => {
    const isCompleted = (o.status === '가공완료' || o.status === '완료');
    let storageHtml = '';
    if (o.backupPath) {
      storageHtml = `<div style="font-size:0.75rem; color:#10b981; margin-top:4px;">📦 <strong>백업보관:</strong> ${o.backupPath} (${o.completedAt || ''})</div>`;
    } else if (o.folderPath) {
      storageHtml = `<div style="font-size:0.75rem; color:#38bdf8; margin-top:4px;">📁 <strong>진행폴더:</strong> ${o.folderPath}</div>`;
    }

    return `
      <div style="background:#0f172a; border:1px solid ${isCompleted ? '#334155' : '#1e293b'}; border-radius:8px; padding:14px; margin-bottom:12px; ${isCompleted ? 'opacity:0.92;' : ''}">
        <div style="display:flex; justify-content:space-between; align-items:flex-start; margin-bottom:8px;">
          <div>
            <strong style="font-size:1.05rem; color:${isCompleted ? '#10b981' : '#f59e0b'};">[${o.orderNumber}]</strong>
            <span style="margin-left:8px; font-weight:700; color:#f8fafc;">고객사: ${o.customerName}</span>
            <span style="margin-left:12px; font-size:0.82rem; color:#94a3b8;">납기일: ${o.targetDeliveryDate}</span>
            ${storageHtml}
          </div>
          <div style="display:flex; gap:6px; align-items:center;">
            <span class="status-badge ${isCompleted ? 'idle' : 'running'}" style="${isCompleted ? 'background:#065f46; color:#a7f3d0;' : ''}">${o.status} (${o.progressRate}%)</span>
            <button class="btn btn-outline" style="padding:3px 9px; font-size:0.75rem;" onclick="openOrderFilesModal(${o.orderId})">📂 파일 열람</button>
            ${!isCompleted ? `<button class="btn btn-warning" style="background:#ea580c; border-color:#ea580c; padding:3px 9px; font-size:0.75rem; color:#fff;" onclick="completeOrderAndBackup(${o.orderId}, '${o.orderNumber}')">⏹️ 가공완료 및 백업</button>` : ''}
            ${o.isHidden ? `<button class="btn btn-outline" style="padding:3px 9px; font-size:0.75rem; color:#34d399; border-color:#10b981;" onclick="unhideWorkOrder(${o.orderId}, '${o.orderNumber}')" title="목록에 다시 표시">♻️ 다시 표시</button>` : `<button class="btn btn-outline" style="padding:3px 9px; font-size:0.75rem; color:#f87171; border-color:#ef4444;" onclick="hideWorkOrder(${o.orderId}, '${o.orderNumber}')" title="목록에서 숨기기">🗑️ 숨기기</button>`}
            <button class="btn btn-primary" style="padding:3px 9px; font-size:0.75rem;" onclick="closeModal('orderListModal'); viewOrderDetail(${o.orderId});">상세 공정</button>
          </div>
        </div>
        <div style="font-size:0.82rem; color:#cbd5e1;">
          <strong>포함 부품:</strong>
          ${o.parts.map(p => `<span style="display:inline-block; background:#1e293b; padding:3px 8px; border-radius:4px; margin:2px 4px; border:1px solid #475569;">⚙ <strong>${p.partName}</strong> (${p.partCode}) - ${p.material} ${p.thickness}T / <strong>${p.targetQty}EA</strong> [${p.status}]</span>`).join('')}
        </div>
      </div>
    `;
  }).join('');
}

function filterOrders(status) {
  loadOrders(status);
}

function viewOrderDetail(orderId) {
  const order = allOrdersCache.find(o => o.orderId === orderId);
  if (!order) return;

  const isCompleted = (order.status === '가공완료' || order.status === '완료');
  let storageInfo = '';
  if (order.backupPath) {
    storageInfo = `<div style="margin-top:6px; color:#10b981;">📦 <strong>백업보관 위치:</strong> <code>${order.backupPath}</code> (완료시각: ${order.completedAt || '-'})</div>`;
  } else if (order.folderPath) {
    storageInfo = `<div style="margin-top:6px; color:#38bdf8;">📁 <strong>진행폴더 위치:</strong> <code>${order.folderPath}</code></div>`;
  }

  document.getElementById('detailOrderTitle').innerText = `작업지시서: ${order.orderNumber} (${order.customerName})`;
  let html = `
    <div style="background:#0f172a; padding:14px; border-radius:6px; margin-bottom:12px; border:1px solid #334155;">
      <div style="display:flex; justify-content:space-between; align-items:flex-start;">
        <div>
          <div><strong>고객사:</strong> ${order.customerName} | <strong>납기일:</strong> ${order.targetDeliveryDate} | <strong>상태:</strong> <span style="color:${isCompleted ? '#10b981' : '#f59e0b'}; font-weight:bold;">${order.status}</span> (${order.progressRate}%)</div>
          <div style="margin-top:4px;"><strong>등록자:</strong> ${order.createdBy} | <strong>등록일시:</strong> ${order.createdAt}</div>
          <div style="margin-top:4px;"><strong>비고:</strong> ${order.notes || '없음'}</div>
          ${storageInfo}
        </div>
        <div style="display:flex; gap:6px;">
          <button class="btn btn-outline" style="padding:4px 10px; font-size:0.8rem;" onclick="openOrderFilesModal(${order.orderId})">📂 첨부/도면/NC 파일 열람</button>
          ${!isCompleted ? `<button class="btn btn-warning" style="background:#ea580c; border-color:#ea580c; color:#fff; padding:4px 10px; font-size:0.8rem;" onclick="closeModal('orderDetailModal'); completeOrderAndBackup(${order.orderId}, '${order.orderNumber}')">⏹️ 가공완료 및 백업 이동</button>` : ''}
        </div>
      </div>
    </div>
    <div class="section-title">포함 부품 및 공정 라우팅 정보</div>
  `;

  order.parts.forEach(p => {
    html += `
      <div style="background:#0f172a; padding:12px; border-radius:6px; margin-bottom:10px; border:1px solid #334155;">
        <div style="display:flex; justify-content:space-between; font-weight:700;">
          <span>${p.partName} (${p.partCode})</span>
          <span class="status-badge running">${p.productionType} | ${p.status}</span>
        </div>
        <div style="font-size:0.8rem; color:#94a3b8; margin:4px 0;">
          소재: ${p.material} (${p.thickness} T) | 목표수량: ${p.targetQty}EA | 가공완료: ${p.completedQty}EA
        </div>
        <table style="font-size:0.78rem; margin-top:8px;">
          <thead>
            <tr><th>순서</th><th>공정명</th><th>장비</th><th>가공수량</th><th>측정수량</th><th>합격수량</th><th>상태</th></tr>
          </thead>
          <tbody>
            ${p.routingSteps.map(s => `
              <tr>
                <td>${s.stepSequence}</td>
                <td>${s.stepName}</td>
                <td>${s.targetMachine}</td>
                <td>${s.machinedQty}</td>
                <td>${s.inspectedQty}</td>
                <td><strong style="color:var(--success);">${s.passedQty}</strong></td>
                <td><span class="status-badge ${s.status === '합격완료' ? 'running' : 'idle'}">${s.status}</span></td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    `;
  });

  document.getElementById('detailOrderBody').innerHTML = html;
  openModal('orderDetailModal');
}

// 5. Operator Workflow (3-Click & Alert)
function populateOperatorPartSelect() {
  const sel = document.getElementById('operatorPartSelect');
  if (!sel) return;
  sel.innerHTML = '<option value="">-- 가공할 부품 선택 --</option>' + 
    allPartsCache.map(p => `<option value="${p.partId}">${p.orderNumber} - ${p.partName} (${p.partCode}) [${p.productionType}]</option>`).join('');
}

function onOperatorMachineChange(machineId) {
  loadOperatorExecutions();
}

async function checkPartHistoryAlert(partIdStr) {
  if (!partIdStr) {
    document.getElementById('selectedPartInfo').innerText = '선택된 부품 정보가 여기에 표시됩니다.';
    return;
  }
  const partId = parseInt(partIdStr);
  const part = allPartsCache.find(x => x.partId === partId);
  if (!part) return;

  const curStep = part.routingSteps.find(s => s.stepSequence === part.currentStepSequence);
  document.getElementById('selectedPartInfo').innerHTML = `
    <div><strong>작업번호:</strong> ${part.orderNumber} | <strong>품명:</strong> ${part.partName}</div>
    <div><strong>품번(PartCode):</strong> ${part.partCode}</div>
    <div><strong>소재/두께:</strong> ${part.material} / ${part.thickness} T</div>
    <div><strong>목표 수량:</strong> ${part.targetQty}개 (${part.productionType})</div>
    <div><strong>현재 공정:</strong> ${curStep ? curStep.stepName : '완료'} (${part.currentStepSequence}단계) / <strong>상태:</strong> ${part.status}</div>
  `;

  document.getElementById('activeSettingGroupInfo').innerHTML = `
    <div><strong>소재 및 두께:</strong> ${part.material} ${part.thickness} T</div>
    <div><strong>권장 기준 원점:</strong> G54 (X0 Y0 상단 중앙)</div>
    <div><strong>세팅 그룹:</strong> ${part.settingGroupName || '일반 엔지니어링 플라스틱 가공 그룹'}</div>
    <div><strong>주요 공구:</strong> T01(0.8 마이크로 드릴), T02(3.0 플랫 엔드밀), T03(챔퍼밀)</div>
  `;

  if (curStep) {
    loadOperatorExecutions(curStep.stepId);
  }

  // Pre-warning: Check permanent history
  try {
    const res = await fetch(`/api/permanentevents/history/${encodeURIComponent(part.partCode)}`);
    const data = await res.json();
    if (data.totalIssueCount > 0) {
      const histList = document.getElementById('partHistoryList');
      histList.innerHTML = data.history.map(h => `
        <div class="history-alert">
          <div><strong>[${h.eventType}]</strong> (${h.eventCategory}) - ${h.createdAt}</div>
          <div>${h.description}</div>
          <div style="font-size:0.8rem; color:#fca5a5; margin-top:4px;">조치내용: ${h.actionTaken || '조치 기록 없음'}</div>
        </div>
      `).join('');
      openModal('partHistoryModal');
    }
  } catch (err) {
    console.error(err);
  }
}

async function startMachining() {
  const machineId = document.getElementById('operatorMachineSelect').value;
  const partId = parseInt(document.getElementById('operatorPartSelect').value);
  if (!partId) {
    alert('가공할 부품을 먼저 선택해주세요.');
    return;
  }

  const part = allPartsCache.find(x => x.partId === partId);
  const curStep = part?.routingSteps?.find(s => s.stepSequence === part.currentStepSequence);
  if (!curStep) {
    alert('현재 진행 가능한 공정 단계가 없습니다.');
    return;
  }

  const worker = currentUser?.displayName || '현장작업자';

  try {
    const res = await fetch('/api/executions/start', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        stepId: curStep.stepId,
        machineId: machineId,
        workerUsername: worker,
        targetQty: part.targetQty,
        notes: '현장 3-Click 가공 시작'
      })
    });
    if (!res.ok) throw new Error(await res.text());
    alert(`[가공 시작] 장비 ${machineId}에서 가공이 시작되었습니다!`);
    loadMachines();
    loadOrders();
    loadOperatorExecutions(curStep.stepId);
  } catch (err) {
    alert('오류 발생: ' + err.message);
  }
}

function openFinishMachiningModal() {
  const partId = parseInt(document.getElementById('operatorPartSelect').value);
  if (!partId) {
    alert('가공할 부품을 먼저 선택해주세요.');
    return;
  }
  const part = allPartsCache.find(x => x.partId === partId);
  document.getElementById('finishQty').value = part ? part.targetQty : 1;
  openModal('finishMachiningModal');
}

async function submitFinishMachining() {
  const partId = parseInt(document.getElementById('operatorPartSelect').value);
  const qty = parseInt(document.getElementById('finishQty').value);
  if (!qty || qty <= 0) {
    alert('올바른 수량을 입력하세요.');
    return;
  }

  const part = allPartsCache.find(x => x.partId === partId);
  const curStep = part?.routingSteps?.find(s => s.stepSequence === part.currentStepSequence);
  if (!curStep) return;

  try {
    const execRes = await fetch(`/api/executions/step/${curStep.stepId}`);
    const execs = await execRes.json();
    const activeExec = execs.find(e => e.status === '가공중');
    if (!activeExec) {
      alert('현재 진행 중인 가공 실행 기록이 없습니다. 먼저 [가공 시작]을 누르세요.');
      return;
    }

    const res = await fetch('/api/executions/finish', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        executionId: activeExec.executionId,
        machinedQty: qty,
        notes: '현장 가공 완료'
      })
    });
    if (!res.ok) throw new Error(await res.text());

    closeModal('finishMachiningModal');
    alert('가공이 완료되었으며 [측정 대기] 상태로 자동 전이되었습니다!\n반드시 품질 측정을 거쳐야 다음 공정이 승인됩니다.');
    loadMachines();
    loadOrders();
    loadOperatorExecutions(curStep.stepId);
  } catch (err) {
    alert('오류: ' + err.message);
  }
}

function openReportIssueModal() {
  const partId = parseInt(document.getElementById('operatorPartSelect').value);
  const part = allPartsCache.find(x => x.partId === partId);
  if (part) {
    document.getElementById('evPartCode').value = part.partCode;
  }
  document.getElementById('evCategory').value = '가공중';
  document.getElementById('evType').value = '공구파손';
  document.getElementById('evMachine').value = document.getElementById('operatorMachineSelect').value;
  openModal('newEventModal');
}

async function loadOperatorExecutions(stepId) {
  try {
    const machineId = document.getElementById('operatorMachineSelect')?.value || '';
    const res = await fetch(`/api/machines/work-logs?machineId=${encodeURIComponent(machineId)}&limit=30`);
    const list = await res.json();
    const tbody = document.getElementById('operatorExecutionsTable');
    if (tbody) {
      if (list.length === 0) {
        tbody.innerHTML = '<tr><td colspan="7" style="text-align:center; color:#64748b;">최근 장비 작업 이력이 없습니다.</td></tr>';
      } else {
        tbody.innerHTML = list.map(l => `
          <tr>
            <td><strong style="color:#38bdf8;">${l.machineId}</strong></td>
            <td><strong style="color:#f59e0b;">${l.orderNumber || '-'}</strong></td>
            <td><strong style="color:#10b981;">${l.partName || '-'}</strong></td>
            <td>${l.workDetail || '-'}</td>
            <td>${l.workerName}${l.subWorkerName ? ` (서브:${l.subWorkerName})` : ''}</td>
            <td style="font-size:0.76rem; color:#94a3b8;">${l.createdAt}</td>
            <td><span class="status-badge ${l.status === '가공중' || l.status === '가공완료' ? 'running' : 'idle'}">${l.status}</span></td>
          </tr>
        `).join('');
      }
    }
  } catch (err) { console.error(err); }
}

// 6. Inspection Gateway (절대 필수)
function populateInspectionPartSelect() {
  const sel = document.getElementById('inspPartSelect');
  if (!sel) return;
  sel.innerHTML = '<option value="">-- 측정 대상 부품 선택 --</option>' +
    allPartsCache.map(p => `<option value="${p.partId}">${p.orderNumber} - ${p.partName} (${p.status})</option>`).join('');
}

function goToInspectionForPart(partId) {
  switchView('inspection');
  const sel = document.getElementById('inspPartSelect');
  if (sel) {
    sel.value = partId;
    loadInspectionSteps(partId);
  }
}

function loadInspectionSteps(partIdStr) {
  if (!partIdStr) return;
  const part = allPartsCache.find(x => x.partId === parseInt(partIdStr));
  if (!part) return;

  const container = document.getElementById('inspStepContainer');
  container.innerHTML = part.routingSteps.map(s => `
    <label style="display:block; margin-bottom:6px; background:#0f172a; padding:8px; border-radius:4px; border:1px solid #334155;">
      <input type="radio" name="inspStepRadio" value="${s.stepId}" ${s.stepSequence === part.currentStepSequence ? 'checked' : ''}>
      ${s.stepSequence}단계: ${s.stepName} (${s.targetMachine}) - <span class="status-badge ${s.status === '합격완료' ? 'running' : 'idle'}">${s.status}</span>
    </label>
  `).join('');

  document.getElementById('inspQty').value = part.targetQty;
  document.getElementById('inspPassedQty').value = part.targetQty;

  loadPartInspectionHistory(part.partId);
}

async function loadPartInspectionHistory(partId) {
  try {
    const res = await fetch(`/api/inspections/part/${partId}`);
    const list = await res.json();
    const box = document.getElementById('partInspectionHistory');
    if (box) {
      if (list.length === 0) {
        box.innerHTML = '이 부품의 등록된 검사 이력이 없습니다.';
      } else {
        box.innerHTML = list.map(i => `
          <div style="background:#0f172a; padding:8px; border-radius:4px; margin-bottom:6px; border-left:3px solid ${i.resultVerdict === '합격' ? 'var(--success)' : 'var(--danger)'};">
            <div><strong>[${i.resultVerdict}]</strong> ${i.inspectionType} (${i.inspectedAt}) - 검사자: ${i.inspectorUsername}</div>
            <div style="font-size:0.8rem; color:#94a3b8;">측정수량: ${i.inspectedQty} | 합격: ${i.passedQty} | 불량: ${i.failedQty} | 재가공: ${i.reworkQty}</div>
          </div>
        `).join('');
      }
    }
  } catch (err) { console.error(err); }
}

async function submitInspection() {
  const partId = parseInt(document.getElementById('inspPartSelect').value);
  const selectedStepRadio = document.querySelector('input[name="inspStepRadio"]:checked');
  if (!partId || !selectedStepRadio) {
    alert('부품과 측정할 공정 단계를 선택해주세요.');
    return;
  }

  const stepId = parseInt(selectedStepRadio.value);
  const verdict = document.getElementById('inspVerdict').value;
  const qty = parseInt(document.getElementById('inspQty').value);
  const passed = parseInt(document.getElementById('inspPassedQty').value);
  const failed = parseInt(document.getElementById('inspFailedQty').value);
  const inspector = currentUser?.displayName || '품질검사원';

  const dimData = [
    { item: '외경 (OD)', standard: '25.400', actual: document.getElementById('dimVal1').value, verdict: '합격' },
    { item: '포켓 깊이', standard: '3.200', actual: document.getElementById('dimVal2').value, verdict: '합격' },
    { item: '마이크로 홀 피치', standard: '0.800', actual: document.getElementById('dimVal3').value, verdict: '합격' }
  ];

  try {
    const res = await fetch('/api/inspections', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        stepId: stepId,
        partId: partId,
        inspectorUsername: inspector,
        inspectionType: document.querySelector('input[name="inspType"]:checked')?.value || '전수검사',
        inspectedQty: qty,
        passedQty: passed,
        failedQty: failed,
        resultVerdict: verdict,
        dimensionDataJson: JSON.stringify(dimData),
        notes: 'Micro-Vu 기준 치수 적합성 판정 완료'
      })
    });
    if (!res.ok) throw new Error(await res.text());

    alert(`측정 판정(${verdict})이 등록되었습니다!\n` + (verdict === '합격' ? '다음 공정 투입이 승인되었습니다.' : '불합격/재가공 처리되었습니다.'));
    loadOrders();
    loadPartInspectionHistory(partId);
  } catch (err) {
    alert('오류: ' + err.message);
  }
}

// 7. Permanent Events
function openNewEventModal() {
  openModal('newEventModal');
}

async function submitCreatePermanentEvent() {
  const partCode = document.getElementById('evPartCode').value.trim();
  const category = document.getElementById('evCategory').value;
  const type = document.getElementById('evType').value;
  const machine = document.getElementById('evMachine').value;
  const desc = document.getElementById('evDescription').value.trim();
  const cause = document.getElementById('evCause').value.trim();
  const action = document.getElementById('evAction').value.trim();
  const author = currentUser?.displayName || '현장작업자';

  if (!partCode || !desc) {
    alert('품번과 발생 내용은 필수입니다.');
    return;
  }

  try {
    const res = await fetch('/api/permanentevents', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        partCode,
        eventCategory: category,
        eventType: type,
        machineId: machine,
        description: desc,
        cause,
        actionTaken: action,
        authorUsername: author,
        status: '진행'
      })
    });
    if (!res.ok) throw new Error(await res.text());

    closeModal('newEventModal');
    alert('영구 이벤트가 성공적으로 기록되었습니다. 다음 제작 시 작업자에게 선제 경고됩니다.');
    loadEvents();
  } catch (err) {
    alert('등록 오류: ' + err.message);
  }
}

async function loadEvents() {
  try {
    const res = await fetch('/api/permanentevents');
    const events = await res.json();
    renderEventsTable(events);
  } catch (err) { console.error(err); }
}

async function searchEventsByPartCode() {
  const code = document.getElementById('eventSearchPartCode').value.trim();
  if (!code) { loadEvents(); return; }
  try {
    const res = await fetch(`/api/permanentevents/history/${encodeURIComponent(code)}`);
    const data = await res.json();
    renderEventsTable(data.history);
  } catch (err) { console.error(err); }
}

function renderEventsTable(events) {
  const tbody = document.getElementById('eventsTableBody');
  if (!tbody) return;
  if (events.length === 0) {
    tbody.innerHTML = '<tr><td colspan="8" style="text-align:center; color:#64748b; padding:20px;">등록된 영구 이벤트가 없습니다.</td></tr>';
    return;
  }
  tbody.innerHTML = events.map(e => `
    <tr>
      <td>#${e.eventId}</td>
      <td><strong>${e.partCode}</strong></td>
      <td><span class="status-badge idle">${e.eventCategory}</span></td>
      <td><strong style="color:#f87171;">${e.eventType}</strong></td>
      <td>${e.description}</td>
      <td>${e.actionTaken || '-'}</td>
      <td><span class="status-badge running">${e.status}</span></td>
      <td style="font-size:0.75rem; color:#94a3b8;">${e.authorUsername}<br>${e.createdAt}</td>
    </tr>
  `).join('');
}

// 8. Notices & Handovers
function openNoticeModal() {
  openModal('noticeModal');
}

async function submitCreateNotice() {
  const title = document.getElementById('noticeTitle').value.trim();
  const priority = document.getElementById('noticePriority').value;
  const machine = document.getElementById('noticeMachine').value;
  const content = document.getElementById('noticeContent').value.trim();
  const author = currentUser?.displayName || '관리자';

  if (!title || !content) {
    alert('공지 제목과 내용을 모두 입력하세요.');
    return;
  }

  try {
    const res = await fetch('/api/communications/notices', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ author, title, content, priority, relatedMachine: machine })
    });
    if (!res.ok) throw new Error(await res.text());

    closeModal('noticeModal');
    alert('공지사항이 발행되었습니다!');
    loadNotices();
  } catch (err) {
    alert('공지 등록 오류: ' + err.message);
  }
}

async function loadNotices() {
  try {
    const res = await fetch('/api/communications/notices');
    const notices = await res.json();
    const list = document.getElementById('noticesList');
    if (list) {
      if (notices.length === 0) {
        list.innerHTML = '<div style="color:#64748b; padding:10px;">등록된 현장 공지사항이 없습니다.</div>';
      } else {
        list.innerHTML = notices.map(n => {
          let acks = [];
          try { acks = JSON.parse(n.officialAcksJson || '[]'); } catch { }
          const ackBadge = acks.length > 0
            ? `<div style="margin-top:6px; font-size:0.75rem; color:#10b981; background:#064e3b44; padding:4px 8px; border-radius:4px;">✅ 확인 완료 (${acks.length}명): ${acks.map(a => `${a.username}`).join(', ')}</div>`
            : `<div style="margin-top:6px; font-size:0.75rem; color:#f59e0b;">⏳ 아직 확인 서명한 작업자가 없습니다.</div>`;

          return `
            <div style="background:#0f172a; padding:10px; border-radius:6px; border-left:3px solid var(--primary);">
              <div style="display:flex; justify-content:space-between;">
                <strong>${n.title}</strong>
                <span class="status-badge ${n.priority === '긴급' ? 'alarm' : 'running'}">${n.priority}</span>
              </div>
              <div style="font-size:0.85rem; margin:6px 0;">${n.content}</div>
              ${ackBadge}
              <div style="display:flex; justify-content:space-between; align-items:center; font-size:0.75rem; color:#94a3b8; margin-top:6px;">
                <span>${n.author} (${n.createdAt})</span>
                <button class="btn btn-outline" style="padding:2px 8px; font-size:0.75rem;" onclick="ackNotice(${n.noticeId})">✅ 공식 확인 서명</button>
              </div>
            </div>
          `;
        }).join('');
      }
    }
  } catch (err) { console.error(err); }
}

async function ackNotice(id) {
  const user = currentUser?.displayName || '현장작업자';
  await fetch(`/api/communications/notices/${id}/ack?username=${encodeURIComponent(user)}`, { method: 'POST' });
  alert(`[${user}] 공식 확인 서명이 완료되었습니다.`);
  loadNotices();
}

function openHandoverModal() {
  openModal('handoverModal');
}

async function submitCreateHandover() {
  const machine = document.getElementById('handoverMachine').value;
  const summary = document.getElementById('handoverSummary').value.trim();
  const issues = document.getElementById('handoverPendingIssues').value.trim();
  const sender = currentUser?.displayName || '현장작업자';

  if (!summary) {
    alert('인계 작업 요약을 입력하세요.');
    return;
  }

  try {
    const res = await fetch('/api/communications/handovers', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ senderUsername: sender, machineId: machine, summary, pendingIssues: issues })
    });
    if (!res.ok) throw new Error(await res.text());

    closeModal('handoverModal');
    alert('교대 인수인계가 등록되었습니다.');
    loadHandovers();
  } catch (err) {
    alert('인수인계 등록 오류: ' + err.message);
  }
}

async function loadHandovers() {
  try {
    const res = await fetch('/api/communications/handovers');
    const handovers = await res.json();
    const list = document.getElementById('handoversList');
    if (list) {
      if (handovers.length === 0) {
        list.innerHTML = '<div style="color:#64748b; padding:10px;">등록된 교대 인수인계 기록이 없습니다.</div>';
      } else {
        list.innerHTML = handovers.map(h => `
          <div style="background:#0f172a; padding:10px; border-radius:6px; border:1px solid #334155;">
            <div style="display:flex; justify-content:space-between; font-weight:700;">
              <span>장비 ${h.machineId} (인계: ${h.senderUsername})</span>
              <span class="status-badge ${h.confirmedAt ? 'running' : 'idle'}">${h.confirmedAt ? '인수완료' : '인수대기'}</span>
            </div>
            <div style="font-size:0.85rem; margin:6px 0;">${h.summary}</div>
            <div style="font-size:0.75rem; color:#f87171;">주의: ${h.pendingIssues || '특이사항 없음'}</div>
            ${!h.confirmedAt ? `<button class="btn btn-success" style="margin-top:6px; padding:3px 10px; font-size:0.75rem;" onclick="confirmHandover(${h.handoverId})">인수 확인</button>` : `<div style="font-size:0.7rem; color:#94a3b8; margin-top:4px;">인수자: ${h.receiverUsername} (${h.confirmedAt})</div>`}
          </div>
        `).join('');
      }
    }
  } catch (err) { console.error(err); }
}

async function confirmHandover(id) {
  const receiver = currentUser?.displayName || '현장작업자';
  await fetch(`/api/communications/handovers/${id}/confirm?receiverUsername=${encodeURIComponent(receiver)}`, { method: 'POST' });
  alert(`[${receiver}] 인수가 정상 확인되었습니다.`);
  loadHandovers();
}

// 9. Zulip Style Topics & Chat
async function switchTopic(type, key) {
  currentTopic = { type, key };
  document.querySelectorAll('.topic-item').forEach(el => el.classList.remove('active'));
  event.currentTarget?.classList.add('active');
  joinCurrentTopic();
  loadChat();
}

async function loadChat() {
  try {
    const res = await fetch(`/api/communications/chat?channelType=${currentTopic.type}&topicKey=${encodeURIComponent(currentTopic.key)}`);
    const messages = await res.json();
    const box = document.getElementById('chatMessages');
    if (box) {
      if (messages.length === 0) {
        box.innerHTML = '<div style="color:#64748b; text-align:center; margin-top:20px;">이 토픽에 등록된 대화가 없습니다.</div>';
      } else {
        box.innerHTML = messages.map(m => formatChatMessage(m)).join('');
      }
      box.scrollTop = box.scrollHeight;
    }
  } catch (err) { console.error(err); }
}

function formatChatMessage(m) {
  return `
    <div class="chat-bubble">
      <div class="chat-header">
        <strong>${m.author}</strong>
        <span>${m.createdAt}</span>
      </div>
      <div>${m.content}</div>
      ${m.promotedEventId ? `<span class="status-badge running" style="margin-top:4px; display:inline-block;">공식 이벤트 #${m.promotedEventId} 승격됨</span>` : ''}
    </div>
  `;
}

function appendChatMessage(msg) {
  const box = document.getElementById('chatMessages');
  if (box) {
    box.insertAdjacentHTML('beforeend', formatChatMessage(msg));
    box.scrollTop = box.scrollHeight;
  }
}

async function sendChatMessage() {
  const input = document.getElementById('chatInput');
  const text = input.value.trim();
  if (!text) return;
  const author = currentUser?.displayName || '현장작업자';

  try {
    await fetch('/api/communications/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        channelType: currentTopic.type,
        topicKey: currentTopic.key,
        author: author,
        content: text
      })
    });
    input.value = '';
    loadChat();
  } catch (err) { console.error(err); }
}

// 10. Modals Helpers & Order Creation
const PART_NAME_PRESETS = [
  '소켓 상판',
  '소켓 하판',
  'IC 가이드 플레이트',
  'BGA 테스트 블록',
  '하우징 프레임',
  '클램프 지그',
  '푸셔 블록 (Pusher)',
  '서포트 플레이트',
  '엔드 플레이트',
  '센서 브라켓'
];

function openModal(id) { document.getElementById(id)?.classList.add('active'); }
function closeModal(id) { document.getElementById(id)?.classList.remove('active'); }

function openOrderModal() {
  const tbody = document.getElementById('newPartsTableBody');
  if (tbody && tbody.children.length === 0) {
    addPartRow('소켓 상판', 'PEEK', 5.0, 2, '단품');
  } else {
    renumberPartRows();
  }
  loadShareFoldersForModal();
  openModal('orderModal');
}

function renumberPartRows() {
  const tbody = document.getElementById('newPartsTableBody');
  if (!tbody) return;
  const rows = tbody.querySelectorAll('tr');
  rows.forEach((tr, index) => {
    const noCell = tr.querySelector('.part-no-cell');
    if (noCell) noCell.innerText = String(index + 1);
  });
}

function handlePartNameSelect(selectEl) {
  const customInput = selectEl.parentElement.querySelector('.custom-partname-input');
  if (selectEl.value === '__custom__') {
    customInput.style.display = 'block';
    customInput.focus();
  } else {
    customInput.style.display = 'none';
    customInput.value = selectEl.value;
  }
}

function addPartRow(defaultPartName = '소켓 상판', defaultMaterial = 'PEEK', defaultThickness = 5.0, defaultQty = 1, defaultProdType = '단품') {
  const tbody = document.getElementById('newPartsTableBody');
  if (!tbody) return;
  const tr = document.createElement('tr');

  const optionsHtml = PART_NAME_PRESETS.map(name =>
    `<option value="${name}" ${name === defaultPartName ? 'selected' : ''}>${name}</option>`
  ).join('') + `<option value="__custom__">직접 입력...</option>`;

  tr.innerHTML = `
    <td class="part-no-cell" style="text-align:center; font-weight:700; color:#38bdf8;">1</td>
    <td>
      <div style="display:flex; flex-direction:column; gap:4px;">
        <select class="part-name-select" onchange="handlePartNameSelect(this)" style="padding:4px 6px; background:#0f172a; color:#fff; border:1px solid #334155; border-radius:4px; font-size:0.85rem;">
          ${optionsHtml}
        </select>
        <input type="text" class="custom-partname-input" placeholder="품명 직접입력" value="${defaultPartName}" style="display:none; padding:4px 6px; background:#0f172a; color:#10b981; border:1px solid #10b981; border-radius:4px; font-size:0.85rem;">
      </div>
    </td>
    <td><input type="text" class="part-material-input" value="${defaultMaterial}" style="width:100px; padding:4px 6px; background:#0f172a; color:#fff; border:1px solid #334155; border-radius:4px;"></td>
    <td><input type="number" class="part-thickness-input" value="${defaultThickness}" step="0.1" style="width:65px; padding:4px 6px; background:#0f172a; color:#fff; border:1px solid #334155; border-radius:4px;"></td>
    <td><input type="number" class="part-qty-input" value="${defaultQty}" min="1" style="width:65px; padding:4px 6px; background:#0f172a; color:#fff; border:1px solid #334155; border-radius:4px; font-weight:700;"></td>
    <td>
      <select class="part-prodtype-select" style="width:95px; padding:4px 6px; background:#0f172a; color:#f59e0b; font-weight:700; border:1px solid #334155; border-radius:4px;">
        <option value="단품" ${defaultProdType === '단품' ? 'selected' : ''}>단품</option>
        <option value="양산" ${defaultProdType === '양산' ? 'selected' : ''}>양산</option>
      </select>
    </td>
    <td style="text-align:center;">
      <button type="button" class="btn btn-outline" onclick="removePartRow(this)" style="padding:2px 8px; color:#ef4444; border-color:#ef4444; font-size:0.8rem;">✕</button>
    </td>
  `;
  tbody.appendChild(tr);
  renumberPartRows();
}

function removePartRow(btn) {
  const tbody = document.getElementById('newPartsTableBody');
  if (!tbody) return;
  if (tbody.querySelectorAll('tr').length <= 1) {
    alert('최소 1개 이상의 부품이 등록되어야 합니다.');
    return;
  }
  const tr = btn.closest('tr');
  if (tr) {
    tr.remove();
    renumberPartRows();
  }
}

async function submitCreateOrder() {
  const orderNumber = document.getElementById('newOrderNumber').value.trim();
  const customerName = document.getElementById('newCustomerName').value.trim();
  const targetDeliveryDate = document.getElementById('newDeliveryDate').value;
  const notes = document.getElementById('newOrderNotes').value;
  const creator = currentUser?.displayName || '관리자';

  if (!orderNumber) {
    alert('작업지시서 번호를 입력해주세요.');
    return;
  }

  const rows = document.querySelectorAll('#newPartsTableBody tr');
  if (rows.length === 0) {
    alert('최소 1개 이상의 부품을 등록해야 합니다.');
    return;
  }

  const parts = [];
  rows.forEach((r, idx) => {
    const partNo = String(idx + 1);
    const select = r.querySelector('.part-name-select');
    const customInput = r.querySelector('.custom-partname-input');
    const partName = (select.value === '__custom__' ? customInput.value.trim() : select.value.trim()) || `부품 ${partNo}`;
    const material = r.querySelector('.part-material-input')?.value.trim() || 'AL6061';
    const thickness = parseFloat(r.querySelector('.part-thickness-input')?.value) || 0;
    const targetQty = parseInt(r.querySelector('.part-qty-input')?.value) || 1;
    const productionType = r.querySelector('.part-prodtype-select')?.value || '단품';

    parts.push({
      partCode: partNo, // 품번은 1, 2, 3, 4... 순차 자동 부여
      partName: partName,
      material: material,
      thickness: thickness,
      targetQty: targetQty,
      productionType: productionType
    });
  });

  try {
    const res = await fetch('/api/workorders', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        orderNumber,
        customerName,
        targetDeliveryDate,
        notes,
        createdBy: creator,
        folderPath: document.getElementById('newOrderFolderPath')?.value || null,
        parts
      })
    });
    if (!res.ok) throw new Error(await res.text());
    alert('작업지시서가 성공적으로 등록되었습니다.');
    closeModal('orderModal');
    loadOrders();
  } catch (err) {
    alert('등록 실패: ' + err.message);
  }
}

// 11. User Management (Admin Only)
async function loadUsers() {
  try {
    const res = await fetch('/api/users');
    const users = await res.json();
    const tbody = document.getElementById('usersTableBody');
    if (!tbody) return;

    if (users.length === 0) {
      tbody.innerHTML = '<tr><td colspan="8" style="text-align:center; color:#64748b; padding:20px;">등록된 사용자가 없습니다.</td></tr>';
      return;
    }

    tbody.innerHTML = users.map(u => `
      <tr>
        <td>${u.id}</td>
        <td><strong>${u.username}</strong></td>
        <td>${u.displayName}</td>
        <td>
          <select onchange="changeUserRole(${u.id}, this.value)" style="padding:2px 6px; background:#0f172a; color:#fff; border:1px solid #334155; border-radius:4px; font-size:0.8rem;">
            <option value="WORKER" ${u.role === 'WORKER' ? 'selected' : ''}>현장작업자 (WORKER)</option>
            <option value="INSPECTOR" ${u.role === 'INSPECTOR' ? 'selected' : ''}>품질검사원 (INSPECTOR)</option>
            <option value="ADMIN" ${u.role === 'ADMIN' ? 'selected' : ''}>관리자 (ADMIN)</option>
          </select>
        </td>
        <td><span class="status-badge ${u.isActive ? 'running' : 'alarm'}">${u.isActive ? '활성' : '정지'}</span></td>
        <td style="font-size:0.75rem; color:#94a3b8;">${u.createdAt}</td>
        <td style="font-size:0.75rem; color:#94a3b8;">${u.lastLoginAt || '-'}</td>
        <td>
          <button class="btn btn-outline" style="padding:2px 6px; font-size:0.72rem;" onclick="resetUserPassword(${u.id})">비번 초기화</button>
          ${u.role !== 'ADMIN' ? `<button class="btn btn-danger" style="padding:2px 6px; font-size:0.72rem; margin-left:4px;" onclick="deleteUser(${u.id})">삭제</button>` : ''}
        </td>
      </tr>
    `).join('');
  } catch (err) {
    console.error('Failed to load users', err);
  }
}

async function changeUserRole(id, role) {
  try {
    const res = await fetch(`/api/users/${id}/role`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ role })
    });
    if (!res.ok) throw new Error(await res.text());
    alert('사용자 권한이 성공적으로 변경되었습니다.');
    loadUsers();
  } catch (err) {
    alert('권한 변경 실패: ' + err.message);
  }
}

async function resetUserPassword(id) {
  const newPw = prompt('재설정할 새 비밀번호를 입력하세요:', '1234');
  if (!newPw) return;
  try {
    const res = await fetch(`/api/users/${id}/password`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ newPassword: newPw })
    });
    if (!res.ok) throw new Error(await res.text());
    alert('비밀번호가 재설정되었습니다.');
  } catch (err) {
    alert('비밀번호 변경 실패: ' + err.message);
  }
}

async function deleteUser(id) {
  if (!confirm('정말 이 사용자 계정을 삭제하시겠습니까?')) return;
  try {
    const res = await fetch(`/api/users/${id}`, { method: 'DELETE' });
    if (!res.ok) throw new Error(await res.text());
    alert('사용자 계정이 삭제되었습니다.');
    loadUsers();
  } catch (err) {
    alert('삭제 실패: ' + err.message);
  }
}

function openAddUserModal() {
  openModal('addUserModal');
}

async function submitAddNewWorker() {
  const username = document.getElementById('newWorkerUsername').value.trim();
  const displayName = document.getElementById('newWorkerDisplayName').value.trim();
  const password = document.getElementById('newWorkerPassword').value.trim();
  const role = document.getElementById('newWorkerRole').value;

  if (!username || !password) {
    alert('아이디와 비밀번호를 모두 입력하세요.');
    return;
  }

  try {
    const res = await fetch('/api/auth/users', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password, displayName, role })
    });
    if (!res.ok) throw new Error(await res.text());
    alert('작업자가 등록되었습니다.');
    closeModal('addUserModal');
    loadUsers();
  } catch (err) {
    alert('작업자 등록 실패: ' + err.message);
  }
}


async function hideWorkOrder(orderId, orderNum) {
  if (!confirm(`작업지시서 [${orderNum}]을(를) 목록에서 숨기시겠습니까?\n(언제든 '👁️ 숨김 보관함' 탭에서 [다시 표시]할 수 있습니다.)`)) return;
  try {
    const res = await fetch(`/api/workorders/${orderId}/hide`, { method: 'POST' });
    const data = await res.json();
    if (data.success) {
      await loadOrders();
      renderOrderListModalFiltered(currentOrderModalTab);
    } else {
      alert('숨김 실패: ' + (data.message || '오류'));
    }
  } catch (err) {
    alert('통신 오류: ' + err.message);
  }
}

async function unhideWorkOrder(orderId, orderNum) {
  try {
    const res = await fetch(`/api/workorders/${orderId}/unhide`, { method: 'POST' });
    const data = await res.json();
    if (data.success) {
      alert(`작지 [${orderNum}]이(가) 목록에 다시 표시되도록 복원되었습니다.`);
      await loadOrders();
      renderOrderListModalFiltered(currentOrderModalTab);
    } else {
      alert('복원 실패: ' + (data.message || '오류'));
    }
  } catch (err) {
    alert('통신 오류: ' + err.message);
  }
}


// =============================================================================
// 📂 제조3팀 공유폴더 신규 작지 불러오기 및 후공정 추적 (v1.3.7)
// =============================================================================
let cachedShareFolders = [];
let allPostProcessReports = [];
let currentPpFilter = '전체';

async function loadShareFoldersForModal() {
  const select = document.getElementById('modalShareFolderSelect');
  const preview = document.getElementById('modalShareFolderPreview');
  const customPath = document.getElementById('modalShareFolderCustomPath')?.value.trim();
  if (!select) return;

  select.innerHTML = '<option value="">폴더 목록 조회 중...</option>';
  if (preview) preview.innerHTML = '';

  try {
    const url = customPath 
      ? `/api/workorders/share-folder/folders?customPath=${encodeURIComponent(customPath)}` 
      : '/api/workorders/share-folder/folders';
    const res = await fetch(url);
    const data = await res.json();

    if (!data.success || !data.folders || data.folders.length === 0) {
      select.innerHTML = '<option value="">-- [00-진행중]에 등록 가능한 폴더가 없습니다 --</option>';
      if (preview) preview.innerHTML = `<span style="color:#ef4444;">경로: ${data.folderPath || '미확인'}</span>`;
      cachedShareFolders = [];
      return;
    }

    cachedShareFolders = data.folders;
    let html = '<option value="">-- [00-진행중] 작지 폴더를 선택하세요 (' + data.folders.length + '개 발견) --</option>';

    data.folders.forEach((f, idx) => {
      const regBadge = f.isAlreadyRegistered ? ' [이미등록됨]' : ' [신규]';
      html += `<option value="${idx}">${regBadge} ${f.folderName} (파일 ${f.fileCount}개)</option>`;
    });

    select.innerHTML = html;
    if (preview) {
      preview.innerHTML = `<span style="color:#64748b;">공유폴더 위치: ${data.folderPath}</span>`;
    }
  } catch (err) {
    console.error('Failed to load share folders:', err);
    select.innerHTML = '<option value="">폴더 목록 조회 실패</option>';
  }
}

function previewShareFolderSelection() {
  const select = document.getElementById('modalShareFolderSelect');
  const preview = document.getElementById('modalShareFolderPreview');
  if (!select || !preview) return;

  const idx = select.value;
  if (idx === '' || !cachedShareFolders[idx]) {
    preview.innerHTML = '';
    return;
  }

  const folder = cachedShareFolders[idx];
  let info = `📁 <b>${folder.folderName}</b> (포함 파일 ${folder.fileCount}개): `;
  if (folder.sampleFiles && folder.sampleFiles.length > 0) {
    info += folder.sampleFiles.join(', ');
  }
  if (folder.isAlreadyRegistered) {
    info += ` <span style="color:#f59e0b; font-weight:bold;">⚠️ 이미 등록된 작지번호입니다.</span>`;
  }
  preview.innerHTML = info;
}

function onShareFolderSelectChanged() {
  previewShareFolderSelection();
  applyShareFolderToModal();
}

function applyShareFolderToModal() {
  const select = document.getElementById('modalShareFolderSelect');
  if (!select || select.value === '') return;

  const folder = cachedShareFolders[select.value];
  if (!folder) return;

  // 작업지시서 번호 자동 입력 (무조건 폴더명으로 덮어씀)
  const orderNumInput = document.getElementById('newOrderNumber');
  if (orderNumInput) {
    orderNumInput.value = folder.folderName;
    orderNumInput.style.borderColor = '#10b981';
    orderNumInput.style.color = '#38bdf8';
    orderNumInput.style.fontWeight = 'bold';
  }

  // 폴더 경로 숨김필드에 설정
  const folderPathInput = document.getElementById('newOrderFolderPath');
  if (folderPathInput) {
    folderPathInput.value = folder.fullPath;
  }

  // 비고에 파일정보 기록 (무조건 덮어씀)
  const notesInput = document.getElementById('newOrderNotes');
  if (notesInput) {
    notesInput.value = `[공유폴더 작지] 파일 ${folder.fileCount}개 (${(folder.sampleFiles || []).slice(0, 3).join(', ')})`;
  }

  // 부품 목록 1행 품명 자동 세팅
  const tbody = document.getElementById('newPartsTableBody');
  if (tbody) {
    const firstRow = tbody.querySelector('tr');
    if (firstRow) {
      const selectPart = firstRow.querySelector('.part-name-select');
      const customPart = firstRow.querySelector('.custom-partname-input');
      if (selectPart && customPart) {
        selectPart.value = '__custom__';
        customPart.style.display = 'block';
        customPart.value = folder.folderName;
      }
    }
  }

  showToast(`[${folder.folderName}] 작지 정보를 작업지시서 번호 및 품명에 자동 반영했습니다.`, 'success');
}

// -----------------------------------------------------------------------------
// 후공정 추적 모달 & API 연동
// -----------------------------------------------------------------------------
async function openPostProcessModal() {
  openModal('postProcessModal');
  await loadPostProcessReports();
}

async function loadPostProcessReports() {
  const tbody = document.getElementById('postProcessTableBody');
  if (!tbody) return;
  tbody.innerHTML = '<tr><td colspan="10" style="text-align:center; padding:20px; color:#94a3b8;">후공정 데이터를 조회 중입니다...</td></tr>';

  try {
    const res = await fetch('/api/workorders/post-process-reports');
    const data = await res.json();
    if (data.success && data.reports) {
      allPostProcessReports = data.reports;
      renderPostProcessTable();
    } else {
      tbody.innerHTML = '<tr><td colspan="10" style="text-align:center; padding:20px; color:#ef4444;">조회 실패</td></tr>';
    }
  } catch (err) {
    console.error('Failed to load post process reports:', err);
    tbody.innerHTML = '<tr><td colspan="10" style="text-align:center; padding:20px; color:#ef4444;">서버 통신 오류</td></tr>';
  }
}

function filterPostProcessModal(filter) {
  currentPpFilter = filter;
  ['All', 'Pending', 'Done'].forEach(k => {
    const btn = document.getElementById(`btnPpFilter${k}`);
    if (btn) btn.classList.remove('active-filter');
  });

  if (filter === '전체') document.getElementById('btnPpFilterAll')?.classList.add('active-filter');
  else if (filter === '후공정대기') document.getElementById('btnPpFilterPending')?.classList.add('active-filter');
  else if (filter === '후공정완료') document.getElementById('btnPpFilterDone')?.classList.add('active-filter');

  renderPostProcessTable();
}

function renderPostProcessTable() {
  const tbody = document.getElementById('postProcessTableBody');
  if (!tbody) return;

  const totalCnt = allPostProcessReports.length;
  const pendingCnt = allPostProcessReports.filter(r => r.status === '후공정대기' || r.status === '진행대기').length;
  const doneCnt = allPostProcessReports.filter(r => r.status === '후공정완료').length;

  document.getElementById('ppCntAll').innerText = totalCnt;
  document.getElementById('ppCntPending').innerText = pendingCnt;
  document.getElementById('ppCntDone').innerText = doneCnt;

  const filtered = allPostProcessReports.filter(r => {
    if (currentPpFilter === '후공정대기') return r.status === '후공정대기' || r.status === '진행대기';
    if (currentPpFilter === '후공정완료') return r.status === '후공정완료';
    return true;
  });

  if (filtered.length === 0) {
    tbody.innerHTML = '<tr><td colspan="10" style="text-align:center; padding:24px; color:#64748b;">해당 조건의 후공정 보고 내역이 없습니다.</td></tr>';
    return;
  }

  tbody.innerHTML = filtered.map(r => {
    const isDone = r.status === '후공정완료';
    const statusBadge = isDone 
      ? '<span style="display:inline-block; padding:2px 8px; border-radius:12px; background:#065f46; color:#34d399; font-size:0.75rem; font-weight:600;">완료</span>'
      : '<span style="display:inline-block; padding:2px 8px; border-radius:12px; background:#854d0e; color:#fde047; font-size:0.75rem; font-weight:600;">진행대기</span>';

    const actionBtn = isDone
      ? `<span style="color:#64748b; font-size:0.75rem;">완료(${r.completedAt ? r.completedAt.substring(5, 16) : ''})</span>`
      : `<button class="btn btn-sm btn-primary" onclick="completePostProcessItem(${r.id})" style="padding:3px 8px; font-size:0.75rem; background:#10b981; border-color:#10b981;">✅ 후공정 완료</button>`;

    // Format processes as badges
    const procBadges = (r.selectedProcesses || '없음').split(',')
      .map(p => `<span style="display:inline-block; margin:1px 2px; padding:2px 6px; background:#334155; color:#38bdf8; border-radius:4px; font-size:0.73rem;">${p.trim()}</span>`)
      .join('');

    return `
      <tr style="border-bottom:1px solid #1e293b;">
        <td style="padding:8px; color:#94a3b8;">${r.id}</td>
        <td style="padding:8px; font-weight:600; color:#fff;">${r.orderNumber}</td>
        <td style="padding:8px; color:#e2e8f0;">${r.partName}</td>
        <td style="padding:8px; color:#38bdf8;">${r.machineId}</td>
        <td style="padding:8px; color:#f1f5f9;">${r.worker}</td>
        <td style="padding:8px; font-weight:700; color:#10b981;">${r.completedQty}EA</td>
        <td style="padding:8px;">${procBadges}</td>
        <td style="padding:8px; color:#cbd5e1; font-size:0.78rem;">${r.notes || '-'}</td>
        <td style="padding:8px; text-align:center;">${statusBadge}</td>
        <td style="padding:8px; text-align:center;">${actionBtn}</td>
      </tr>
    `;
  }).join('');
}

async function completePostProcessItem(id) {
  if (!confirm(`후공정 [ID: ${id}] 항목을 완료 처리하시겠습니까?`)) return;

  try {
    const res = await fetch(`/api/workorders/post-process-reports/${id}/complete`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ completedBy: currentUser?.displayName || '관리자' })
    });
    const data = await res.json();
    if (data.success) {
      showToast('후공정 완료 처리되었습니다.', 'success');
      await loadPostProcessReports();
    } else {
      alert(data.message || '완료 처리에 실패했습니다.');
    }
  } catch (err) {
    console.error('Failed to complete post process:', err);
    alert('서버 통신 오류가 발생했습니다.');
  }
}
