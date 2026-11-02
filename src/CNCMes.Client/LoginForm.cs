using CNCMes.Client.Workflow;
namespace CNCMes.Client;

public class LoginForm : Form
{
    private readonly ApiClient _api = new();

    private TextBox _txtServerUrl = null!;
    private Label _lblConnStatus = null!;
    private Label _lblConnGuide = null!;
    private Button _btnAutoDiscover = null!;
    private Button _btnTest = null!;

    // Login Controls
    private TextBox _txtLoginUser = null!;
    private TextBox _txtLoginPass = null!;
    private Button _btnLogin = null!;

    // Register Controls
    private TextBox _txtRegUser = null!;
    private TextBox _txtRegPass = null!;
    private TextBox _txtRegName = null!;
    private Button _btnRegister = null!;

    public LoginForm()
    {
        InitializeComponents();
        Shown += async (s, e) => await InitializeConnectionOnStartup();
    }

    private void InitializeComponents()
    {
        Text = "CNC-MES 현장 작업자 클라이언트 v1.4.1 (내부망 전용)";
        Size = new Size(560, 580);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.FromArgb(15, 23, 42); // Dark slate
        ForeColor = Color.White;
        Font = new Font("맑은 고딕", 9.5f, FontStyle.Regular);

        // Header Title
        var lblTitle = new Label
        {
            Text = "⚙️ CNC-MES 작업자 터미널",
            Font = new Font("맑은 고딕", 15f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            Location = new Point(20, 15),
            AutoSize = true
        };
        Controls.Add(lblTitle);

        var lblSub = new Label
        {
            Text = "인터넷 없는 사내 폐쇄망 환경 100% 직결 모드",
            Font = new Font("맑은 고딕", 8.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(22, 42),
            AutoSize = true
        };
        Controls.Add(lblSub);

        // Server Config Group
        var grpServer = new GroupBox
        {
            Text = "중앙 서버 연결 설정 (사내 LAN)",
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(20, 68),
            Size = new Size(500, 130)
        };

        var lblUrl = new Label { Text = "서버 주소:", Location = new Point(15, 25), AutoSize = true, ForeColor = Color.White };
        _lblConnStatus = new Label { Text = "연결 확인 중...", Location = new Point(90, 25), AutoSize = true, ForeColor = Color.Yellow };

        _txtServerUrl = new TextBox
        {
            Text = _api.Config.ServerUrl,
            Location = new Point(15, 48),
            Size = new Size(470, 25),
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        _btnTest = new Button
        {
            Text = "연결 테스트",
            Location = new Point(15, 80),
            Size = new Size(120, 30),
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnTest.Click += async (s, e) => await CheckConnection();

        _btnAutoDiscover = new Button
        {
            Text = "🔍 사내망 서버 자동 검색",
            Location = new Point(145, 80),
            Size = new Size(180, 30),
            BackColor = Color.FromArgb(2, 132, 199),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 9.0f, FontStyle.Bold)
        };
        _btnAutoDiscover.Click += async (s, e) => await AutoDiscoverServer();

        _lblConnGuide = new Label
        {
            Text = "",
            Location = new Point(15, 112),
            AutoSize = true,
            Font = new Font("맑은 고딕", 8.0f),
            ForeColor = Color.FromArgb(248, 113, 113)
        };

        var myIps = string.Join(", ", ApiClient.GetLocalIPv4List());
        var lblMyIp = new Label
        {
            Text = $"💻 내 PC IP: {myIps}",
            Location = new Point(335, 87),
            Size = new Size(150, 20),
            Font = new Font("맑은 고딕", 8.0f),
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoEllipsis = true
        };
        grpServer.Controls.Add(lblMyIp);
        grpServer.Controls.Add(_lblConnStatus);
        grpServer.Controls.Add(_txtServerUrl);
        grpServer.Controls.Add(_btnTest);
        grpServer.Controls.Add(_btnAutoDiscover);
        grpServer.Controls.Add(_lblConnGuide);
        Controls.Add(grpServer);

        // Tab Control (로그인 vs 회원가입)
        var tabControl = new TabControl
        {
            Location = new Point(20, 210),
            Size = new Size(500, 315)
        };

        // Tab 1: 로그인 (담당 장비 선택 제거 — 메인 화면에서 다중 장비 자유 선택)
        var tabLogin = new TabPage(" 작업자 로그인 ") { BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White };

        var lblLoginGuide = new Label
        {
            Text = "💡 로그인 후 메인 화면에서 여러 대의 장비를 동시에 선택·관리할 수 있습니다.",
            Location = new Point(20, 18),
            AutoSize = true,
            Font = new Font("맑은 고딕", 8.8f, FontStyle.Regular),
            ForeColor = Color.FromArgb(125, 211, 252)
        };

        var lblLoginUser = new Label { Text = "작업자 아이디:", Location = new Point(20, 50), AutoSize = true, Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold) };
        _txtLoginUser = new TextBox
        {
            Text = _api.Config.LastUsername,
            Location = new Point(20, 76),
            Size = new Size(440, 28),
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 10.5f)
        };

        var lblLoginPass = new Label { Text = "비밀번호:", Location = new Point(20, 120), AutoSize = true, Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold) };
        _txtLoginPass = new TextBox
        {
            PasswordChar = '●',
            Location = new Point(20, 146),
            Size = new Size(440, 28),
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("맑은 고딕", 10.5f)
        };
        _txtLoginPass.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) await DoLogin(); };

        _btnLogin = new Button
        {
            Text = "🚀 작업자 로그인 (다중 장비 통합 관제 시작)",
            Location = new Point(20, 198),
            Size = new Size(440, 48),
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 11f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _btnLogin.FlatAppearance.BorderSize = 0;
        _btnLogin.Click += async (s, e) => await DoLogin();

        tabLogin.Controls.Add(lblLoginGuide);
        tabLogin.Controls.Add(lblLoginUser);
        tabLogin.Controls.Add(_txtLoginUser);
        tabLogin.Controls.Add(_txtLoginPass);
        tabLogin.Controls.Add(lblLoginPass);
        tabLogin.Controls.Add(_btnLogin);

        // Tab 2: 신규 등록
        var tabRegister = new TabPage(" 신규 작업자 등록 ") { BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White };

        var lblRegNotice = new Label
        {
            Text = "신규 작업자는 여기서 아이디와 비밀번호를 직접 생성합니다.\n등록된 계정의 권한 및 비밀번호 변경은 관리자 화면에서 관리됩니다.",
            Location = new Point(20, 15),
            Size = new Size(440, 40),
            ForeColor = Color.FromArgb(148, 163, 184)
        };

        var lblRegUser = new Label { Text = "희망 아이디 (영문/숫자):", Location = new Point(20, 65), AutoSize = true };
        _txtRegUser = new TextBox
        {
            Location = new Point(20, 90),
            Size = new Size(440, 25),
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var lblRegName = new Label { Text = "작업자 성명 (예: 김철수):", Location = new Point(20, 130), AutoSize = true };
        _txtRegName = new TextBox
        {
            Location = new Point(20, 155),
            Size = new Size(440, 25),
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var lblRegPass = new Label { Text = "비밀번호 설정:", Location = new Point(20, 195), AutoSize = true };
        _txtRegPass = new TextBox
        {
            PasswordChar = '●',
            Location = new Point(20, 220),
            Size = new Size(440, 25),
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        _btnRegister = new Button
        {
            Text = "신규 작업자 등록 완료",
            Location = new Point(20, 275),
            Size = new Size(440, 42),
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _btnRegister.Click += async (s, e) => await DoRegister();

        tabRegister.Controls.Add(lblRegNotice);
        tabRegister.Controls.Add(lblRegUser);
        tabRegister.Controls.Add(_txtRegUser);
        tabRegister.Controls.Add(lblRegName);
        tabRegister.Controls.Add(_txtRegName);
        tabRegister.Controls.Add(lblRegPass);
        tabRegister.Controls.Add(_txtRegPass);
        tabRegister.Controls.Add(_btnRegister);

        tabControl.TabPages.Add(tabLogin);
        tabControl.TabPages.Add(tabRegister);
        Controls.Add(tabControl);
    }

    private async Task InitializeConnectionOnStartup()
    {
        bool ok = await CheckConnection();
        if (!ok)
        {
            // Automatically discover the server on the offline LAN when initial connection (e.g., localhost) fails!
            await AutoDiscoverServer(showNotFoundPopup: false);
        }
    }

    private async Task<bool> CheckConnection()
    {
        var rawUrl = _txtServerUrl.Text.Trim();
        if (string.IsNullOrEmpty(rawUrl)) return false;

        _api.SetServerUrl(rawUrl);
        _txtServerUrl.Text = _api.Config.ServerUrl;

        _lblConnStatus.Text = "연결 시도 중...";
        _lblConnStatus.ForeColor = Color.Yellow;
        _lblConnGuide.Text = "";
        _btnTest.Enabled = false;

        var (ok, msg) = await _api.TestConnectionAsync();
        _btnTest.Enabled = true;

        if (ok)
        {
            _lblConnStatus.Text = "● 사내 LAN 서버 연결 정상";
            _lblConnStatus.ForeColor = Color.FromArgb(34, 197, 94);
            _lblConnGuide.Text = $"연결된 서버: {_api.Config.ServerUrl}";
            _lblConnGuide.ForeColor = Color.FromArgb(134, 239, 172);
            return true;
        }
        else
        {
            _lblConnStatus.Text = "× 서버 연결 실패";
            _lblConnStatus.ForeColor = Color.FromArgb(239, 68, 68);
            _lblConnGuide.Text = "⚠️ 안내: " + msg;
            _lblConnGuide.ForeColor = Color.FromArgb(248, 113, 113);
            return false;
        }
    }

    private async Task AutoDiscoverServer(bool showNotFoundPopup = true)
    {
        _btnAutoDiscover.Enabled = false;
        _btnAutoDiscover.Text = "⏳ 사내망 스캔 중...";
        _lblConnStatus.ForeColor = Color.Yellow;
        _lblConnGuide.Text = "";

        var discoveredUrl = await _api.DiscoverServerAsync(msg =>
        {
            try
            {
                if (InvokeRequired)
                {
                    Invoke(() => { _lblConnStatus.Text = msg; });
                }
                else
                {
                    _lblConnStatus.Text = msg;
                }
            }
            catch { }
        });

        _btnAutoDiscover.Enabled = true;
        _btnAutoDiscover.Text = "🔍 사내망 서버 자동 검색";

        if (!string.IsNullOrEmpty(discoveredUrl))
        {
            _txtServerUrl.Text = discoveredUrl;
            await CheckConnection();
            if (showNotFoundPopup)
            {
                MessageBox.Show($"사내 폐쇄망에서 CNC-MES 중앙 서버를 발견하여 즉시 연결했습니다!\n\n서버 주소: {discoveredUrl}", 
                    "서버 자동 발견 성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        else
        {
            await CheckConnection();
            if (showNotFoundPopup)
            {
                var myIps = string.Join(", ", ApiClient.GetLocalIPv4List());
                MessageBox.Show(
                    $"사내망에서 서버를 자동으로 찾지 못했습니다.\n\n" +
                    $"* 현재 내 PC의 사내 IP: [{myIps}]\n\n" +
                    "1. 서버 PC에서 서버 프로그램(run_server.bat)이 실행 중인지 확인해 주십시오.\n" +
                    "2. 서버 PC의 IP 주소(예: 192.168.0.10) 또는 공유폴더 컴퓨터 이름을 상단 '서버 주소' 칸에 입력 후 [연결 테스트]를 눌러주십시오.",
                    "서버 탐색 실패 안내", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private async Task DoLogin()
    {
        if (!string.IsNullOrWhiteSpace(_txtServerUrl.Text))
        {
            _api.SetServerUrl(_txtServerUrl.Text);
            _txtServerUrl.Text = _api.Config.ServerUrl;
        }

        var user = _txtLoginUser.Text.Trim();
        var pass = _txtLoginPass.Text.Trim();

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            MessageBox.Show("아이디와 비밀번호를 입력해주세요.", "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnLogin.Enabled = false;
        _btnLogin.Text = "로그인 인증 중...";

        var (ok, msg, session) = await _api.LoginAsync(user, pass);
        _btnLogin.Enabled = true;
        _btnLogin.Text = "🚀 작업자 로그인 (다중 장비 통합 관제 시작)";

        if (!ok || session == null)
        {
            MessageBox.Show(msg, "로그인 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Launch Modern Workflow Client Form (v1.4.1)
        Hide();
        var workerForm = new WorkflowMainForm(_api, session);
        workerForm.FormClosed += (s, e) => Close();
        workerForm.Show();
    }

    private async Task DoRegister()
    {
        if (!string.IsNullOrWhiteSpace(_txtServerUrl.Text))
        {
            _api.SetServerUrl(_txtServerUrl.Text);
            _txtServerUrl.Text = _api.Config.ServerUrl;
        }

        var user = _txtRegUser.Text.Trim();
        var name = _txtRegName.Text.Trim();
        var pass = _txtRegPass.Text.Trim();

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass) || string.IsNullOrEmpty(name))
        {
            MessageBox.Show("아이디, 성명, 비밀번호를 모두 입력해주세요.", "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnRegister.Enabled = false;
        var (ok, msg) = await _api.RegisterAsync(user, pass, name);
        _btnRegister.Enabled = true;

        if (ok)
        {
            MessageBox.Show(msg, "등록 성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _txtLoginUser.Text = user;
            _txtLoginPass.Text = pass;
        }
        else
        {
            MessageBox.Show(msg, "등록 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
