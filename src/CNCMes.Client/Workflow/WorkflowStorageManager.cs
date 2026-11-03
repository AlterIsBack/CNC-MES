using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client.Workflow;

public enum SaveState
{
    Idle,
    Saving,
    Success,
    Failed
}

public class SaveStatusEventArgs : EventArgs
{
    public SaveState State { get; set; }
    public string Message { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 작업지시서·부품 단위 워크플로우 저장/복구/버전/이력 관리 매니저
/// </summary>
public class WorkflowStorageManager
{
    private readonly string _rootDir;
    private readonly string _jobsDir;
    private readonly string _legacyDir;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private System.Threading.Timer? _debounceTimer;
    private readonly object _debounceSync = new();
    private Func<Task>? _pendingSaveAction;

    public event EventHandler<SaveStatusEventArgs>? StatusChanged;

    public WorkflowStorageManager(string? baseDir = null)
    {
        _rootDir = baseDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CNCMes", "workflows");
        _jobsDir = Path.Combine(_rootDir, "jobs");
        _legacyDir = _rootDir; // legacy files were in root

        EnsureDirectories();
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(_rootDir);
        Directory.CreateDirectory(_jobsDir);
    }

    public string GetJobDir(string jobId)
    {
        string safeJobId = string.IsNullOrWhiteSpace(jobId) ? "UNASSIGNED" : jobId.Replace("/", "_").Replace("\\", "_");
        string dir = Path.Combine(_jobsDir, safeJobId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // =========================================================================
    // 1. 고유 식별자 생성 및 작업(WorkUnitJob) 단위 관리
    // =========================================================================
    public static string GenerateJobId(string orderNumber, string partName)
    {
        string safeOrder = string.IsNullOrWhiteSpace(orderNumber) ? "NO_ORDER" : orderNumber.Trim().Replace(" ", "_");
        string safePart = string.IsNullOrWhiteSpace(partName) ? "NO_PART" : partName.Trim().Replace(" ", "_");
        string shortGuid = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"JOB-{safeOrder}-{safePart}-{shortGuid}";
    }

    public WorkUnitJob CreateJob(string orderNumber, string partName, int orderId = 0, int partId = 0, int targetQty = 1, string author = "")
    {
        string jobId = GenerateJobId(orderNumber, partName);
        var job = new WorkUnitJob
        {
            JobId = jobId,
            OrderId = orderId,
            OrderNumber = orderNumber,
            PartId = partId,
            PartName = partName,
            TargetQuantity = targetQty,
            CreatedAt = DateTime.Now,
            CreatedBy = author,
            LastSavedAt = DateTime.Now,
            LastSavedBy = author,
            Status = "대기"
        };
        return job;
    }

    // =========================================================================
    // 2. 저장 및 장애 복구 (.bak)
    // =========================================================================
    public async Task<bool> SaveJobAsync(WorkUnitJob job, string author = "")
    {
        await _lock.WaitAsync();
        OnStatusChanged(SaveState.Saving, "저장 중...");

        string jobDir = GetJobDir(job.JobId);
        string jobFile = Path.Combine(jobDir, "job.json");
        string backupFile = Path.Combine(jobDir, "job.json.bak");
        string tempFile = Path.Combine(jobDir, $"job.json.tmp_{Guid.NewGuid():N}");

        try
        {
            job.LastSavedAt = DateTime.Now;
            if (!string.IsNullOrEmpty(author)) job.LastSavedBy = author;

            // 노드의 JobId 정렬 (미지정 노드는 작업의 JobId로 귀속)
            foreach (var node in job.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.JobId))
                {
                    node.JobId = job.JobId;
                }
            }

            // 기존 정상 파일 백업 (.bak)
            if (File.Exists(jobFile))
            {
                File.Copy(jobFile, backupFile, overwrite: true);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(job, options);

            // 임시 파일에 먼저 기록 후 원자적 교체
            await File.WriteAllTextAsync(tempFile, json);
            if (File.Exists(jobFile)) File.Delete(jobFile);
            File.Move(tempFile, jobFile);

            // 활성 세션 정보 갱신
            await UpdateActiveSessionAsync(job.JobId);

            OnStatusChanged(SaveState.Success, $"💾 저장 완료 ({DateTime.Now:HH:mm:ss})");
            return true;
        }
        catch (Exception ex)
        {
            // 오류 발생 시 백업본 복구 시도
            try
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                if (File.Exists(backupFile) && !File.Exists(jobFile))
                {
                    File.Copy(backupFile, jobFile, overwrite: true);
                }
            }
            catch { }

            OnStatusChanged(SaveState.Failed, $"⚠️ 저장 실패: {ex.Message}");
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<WorkUnitJob?> LoadJobAsync(string jobId)
    {
        await _lock.WaitAsync();
        try
        {
            string jobDir = GetJobDir(jobId);
            string jobFile = Path.Combine(jobDir, "job.json");
            string backupFile = Path.Combine(jobDir, "job.json.bak");

            string fileToRead = jobFile;
            if (!File.Exists(jobFile) && File.Exists(backupFile))
            {
                fileToRead = backupFile;
            }

            if (!File.Exists(fileToRead)) return null;

            string json = await File.ReadAllTextAsync(fileToRead);
            var job = JsonSerializer.Deserialize<WorkUnitJob>(json);
            return job;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadJobAsync Error] {ex.Message}");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    // =========================================================================
    // 3. 지연 자동 저장 (Debounce)
    // =========================================================================
    public void RequestDebouncedSave(Func<Task> saveAction, int delayMs = 600)
    {
        lock (_debounceSync)
        {
            _pendingSaveAction = saveAction;
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(async _ =>
            {
                Func<Task>? actionToRun = null;
                lock (_debounceSync)
                {
                    actionToRun = _pendingSaveAction;
                    _pendingSaveAction = null;
                }
                if (actionToRun != null)
                {
                    try
                    {
                        await actionToRun();
                    }
                    catch (Exception ex)
                    {
                        OnStatusChanged(SaveState.Failed, $"자동저장 오류: {ex.Message}");
                    }
                }
            }, null, delayMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// 부품 전환 등 즉시 저장이 필요할 때 대기 중인 지연 자동 저장을 취소합니다.
    /// </summary>
    public void CancelPendingDebounceSave()
    {
        lock (_debounceSync)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _pendingSaveAction = null;
        }
    }

    /// <summary>
    /// 대기 중인 지연 자동 저장이 있다면 즉시 실행하고 완료를 대기합니다.
    /// </summary>
    public async Task FlushDebouncedSaveAsync()
    {
        Func<Task>? actionToRun = null;
        lock (_debounceSync)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            actionToRun = _pendingSaveAction;
            _pendingSaveAction = null;
        }
        if (actionToRun != null)
        {
            try
            {
                await actionToRun();
            }
            catch (Exception ex)
            {
                OnStatusChanged(SaveState.Failed, $"즉시저장 오류: {ex.Message}");
            }
        }
    }

    // =========================================================================
    // 4. 계획 버전과 실행 이력 분리 관리
    // =========================================================================
    /// <summary>
    /// 노드 이동(X,Y)은 버전을 만들지 않고, 공정 추가/삭제, 순서변경, 담당자/장비 변경 등 주요 계획 변경 시 새 버전을 생성합니다.
    /// </summary>
    public async Task<PlanVersionRecord?> RecordPlanVersionIfChangedAsync(
        WorkUnitJob currentJob,
        List<WorkflowNode> newNodes,
        List<WorkflowEdge> newEdges,
        string changedBy,
        string changeReason)
    {
        // 주요 계획 변경 감지: 단순 좌표(X,Y) 이동은 계획 변경으로 간주하지 않음
        bool isPlanChanged = DetectMajorPlanChange(currentJob.Nodes, currentJob.Edges, newNodes, newEdges);
        if (!isPlanChanged)
        {
            return null;
        }

        string jobDir = GetJobDir(currentJob.JobId);
        string versionsFile = Path.Combine(jobDir, "plan_versions.json");

        List<PlanVersionRecord> history = new();
        if (File.Exists(versionsFile))
        {
            try
            {
                string existingJson = await File.ReadAllTextAsync(versionsFile);
                history = JsonSerializer.Deserialize<List<PlanVersionRecord>>(existingJson) ?? new();
            }
            catch { }
        }

        int nextVer = history.Count + 1;
        var versionRecord = new PlanVersionRecord
        {
            VersionNumber = nextVer,
            JobId = currentJob.JobId,
            ChangedAt = DateTime.Now,
            ChangedBy = changedBy,
            ChangeReason = changeReason,
            NodesSnapshot = newNodes.Select(CloneNodeWithoutTransient).ToList(),
            EdgesSnapshot = newEdges.Select(CloneEdge).ToList()
        };

        history.Add(versionRecord);
        string saveJson = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(versionsFile, saveJson);

        return versionRecord;
    }

    private static bool DetectMajorPlanChange(
        List<WorkflowNode> oldNodes,
        List<WorkflowEdge> oldEdges,
        List<WorkflowNode> newNodes,
        List<WorkflowEdge> newEdges)
    {
        // 1. 노드 개수 변경 (추가/삭제)
        if (oldNodes.Count != newNodes.Count) return true;
        // 2. 엣지 개수 변경 (연결선 추가/삭제)
        if (oldEdges.Count != newEdges.Count) return true;

        // 3. 노드 내용 비교 (제목, 카테고리, 담당자, 장비, 지시사항 등)
        var oldDict = oldNodes.ToDictionary(n => n.Id, n => n);
        foreach (var nn in newNodes)
        {
            if (!oldDict.TryGetValue(nn.Id, out var on)) return true; // 새로운 노드 ID
            if (on.Title != nn.Title) return true;
            if (on.Category != nn.Category) return true;
            if (on.MachineId != nn.MachineId) return true;
            if (on.Assignee != nn.Assignee) return true;
            if (on.SubWorkerName != nn.SubWorkerName) return true;
        }

        // 4. 엣지 연결 관계 비교
        var oldEdgeSet = new HashSet<string>(oldEdges.Select(e => $"{e.FromNodeId}->{e.ToNodeId}:{e.Type}"));
        foreach (var ne in newEdges)
        {
            string key = $"{ne.FromNodeId}->{ne.ToNodeId}:{ne.Type}";
            if (!oldEdgeSet.Contains(key)) return true;
        }

        return false;
    }

    /// <summary>
    /// 실제 실행/측정/이슈 기록 (계획 수정이나 노드 삭제로도 절대 삭제되지 않고 영구 보존)
    /// </summary>
    public async Task RecordExecutionHistoryAsync(ExecutionHistoryRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.JobId)) return;
        string jobDir = GetJobDir(record.JobId);
        string execFile = Path.Combine(jobDir, "executions.json");

        await _lock.WaitAsync();
        try
        {
            List<ExecutionHistoryRecord> history = new();
            if (File.Exists(execFile))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(execFile);
                    history = JsonSerializer.Deserialize<List<ExecutionHistoryRecord>>(json) ?? new();
                }
                catch { }
            }

            history.Add(record);
            string outJson = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(execFile, outJson);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<ExecutionHistoryRecord>> LoadExecutionHistoriesAsync(string jobId)
    {
        string jobDir = GetJobDir(jobId);
        string execFile = Path.Combine(jobDir, "executions.json");
        if (!File.Exists(execFile)) return new();

        try
        {
            string json = await File.ReadAllTextAsync(execFile);
            return JsonSerializer.Deserialize<List<ExecutionHistoryRecord>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    // =========================================================================
    // 5. 작업 완료 시 확정 기록 (불변 스냅샷 & 파일 사본 보관)
    // =========================================================================
    public async Task<ConfirmedSnapshotRecord> CreateConfirmedSnapshotAsync(WorkUnitJob job, string confirmedBy)
    {
        await _lock.WaitAsync();
        try
        {
            string jobDir = GetJobDir(job.JobId);
            string snapshotsDir = Path.Combine(jobDir, "snapshots");
            Directory.CreateDirectory(snapshotsDir);

            string snapshotId = $"SNAP_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..24];
            string snapshotFilesDir = Path.Combine(snapshotsDir, $"{snapshotId}_files");
            Directory.CreateDirectory(snapshotFilesDir);

            var executions = await LoadExecutionHistoriesAsync(job.JobId);

            var snapshot = new ConfirmedSnapshotRecord
            {
                SnapshotId = snapshotId,
                JobId = job.JobId,
                ParentJobId = job.ParentJobId,
                OrderNumber = job.OrderNumber,
                PartName = job.PartName,
                ConfirmedAt = DateTime.Now,
                ConfirmedBy = confirmedBy,
                ConfirmedNodes = job.Nodes.Select(CloneNodeWithoutTransient).ToList(),
                ConfirmedEdges = job.Edges.Select(CloneEdge).ToList(),
                ExecutionsSnapshot = executions,
                IsImmutable = true
            };

            // 관련 첨부파일 보관 사본 저장 (원본 덮어쓰거나 이동하지 않고 사본을 스냅샷 폴더에 보관)
            foreach (var node in job.Nodes)
            {
                foreach (var filePath in node.AttachmentFilePaths)
                {
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            string fileName = Path.GetFileName(filePath);
                            string destFile = Path.Combine(snapshotFilesDir, $"{node.Id}_{fileName}");
                            File.Copy(filePath, destFile, overwrite: true);
                            snapshot.ArchivedFiles[filePath] = Path.GetRelativePath(jobDir, destFile);
                        }
                        catch { }
                    }
                }
            }

            string snapshotJsonFile = Path.Combine(snapshotsDir, $"{snapshotId}.json");
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(snapshotJsonFile, json);

            // 원본 Job 상태를 완료/확정으로 갱신
            job.IsConfirmed = true;
            job.ConfirmedAt = snapshot.ConfirmedAt;
            job.ConfirmedBy = confirmedBy;
            job.ConfirmedSnapshotId = snapshotId;
            job.Status = "완료";

            string jobFile = Path.Combine(jobDir, "job.json");
            string jobJson = JsonSerializer.Serialize(job, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jobFile, jobJson);

            return snapshot;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 완료 확정 기록 정정 이력 추가 (확정 기록을 덮어쓰지 않고 별도 정정 이력 보존)
    /// </summary>
    public async Task AddCorrectionRecordAsync(CorrectionRecord correction)
    {
        string jobDir = GetJobDir(correction.JobId);
        string correctionsFile = Path.Combine(jobDir, "corrections.json");

        await _lock.WaitAsync();
        try
        {
            List<CorrectionRecord> list = new();
            if (File.Exists(correctionsFile))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(correctionsFile);
                    list = JsonSerializer.Deserialize<List<CorrectionRecord>>(json) ?? new();
                }
                catch { }
            }

            list.Add(correction);
            string outJson = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(correctionsFile, outJson);
        }
        finally
        {
            _lock.Release();
        }
    }

    // =========================================================================
    // 6. 완료 후 재가공 (Rework Job 생성 및 원작업 연결)
    // =========================================================================
    public async Task<WorkUnitJob> CreateReworkJobAsync(
        WorkUnitJob originalJob,
        string reworkReason,
        int reworkQuantity,
        string author)
    {
        string reworkJobId = GenerateJobId(originalJob.OrderNumber, originalJob.PartName) + "-RWK";

        var reworkJob = new WorkUnitJob
        {
            JobId = reworkJobId,
            ParentJobId = originalJob.JobId,
            IsRework = true,
            ReworkReason = reworkReason,
            ReworkQuantity = reworkQuantity,
            OrderId = originalJob.OrderId,
            OrderNumber = originalJob.OrderNumber,
            PartId = originalJob.PartId,
            PartName = originalJob.PartName,
            TargetQuantity = reworkQuantity > 0 ? reworkQuantity : originalJob.TargetQuantity,
            Status = "재가공대기",
            CreatedAt = DateTime.Now,
            CreatedBy = author,
            LastSavedAt = DateTime.Now,
            LastSavedBy = author,
            PreviousSnapshotId = originalJob.ConfirmedSnapshotId
        };

        // 기존 공정 구성 복사하되, 실행 실적 및 상태는 'Ready(대기)'로 깨끗하게 초기화
        foreach (var on in originalJob.Nodes)
        {
            var nodeCopy = CloneNodeWithoutTransient(on);
            nodeCopy.Id = Guid.NewGuid().ToString("N")[..8];
            nodeCopy.JobId = reworkJobId;
            nodeCopy.Status = NodeStatus.Ready;
            nodeCopy.StartedAt = null;
            nodeCopy.CompletedAt = null;
            nodeCopy.HasIssueHistory = false;
            nodeCopy.IssueType = "";
            nodeCopy.IssueOccurredDetails = "";
            reworkJob.Nodes.Add(nodeCopy);
        }

        // 엣지 재연결 매핑
        var idMap = new Dictionary<string, string>();
        for (int i = 0; i < originalJob.Nodes.Count && i < reworkJob.Nodes.Count; i++)
        {
            idMap[originalJob.Nodes[i].Id] = reworkJob.Nodes[i].Id;
        }

        foreach (var oe in originalJob.Edges)
        {
            if (idMap.TryGetValue(oe.FromNodeId, out var newFrom) && idMap.TryGetValue(oe.ToNodeId, out var newTo))
            {
                reworkJob.Edges.Add(new WorkflowEdge
                {
                    FromJobId = reworkJobId,
                    ToJobId = reworkJobId,
                    FromNodeId = newFrom,
                    ToNodeId = newTo,
                    Type = oe.Type,
                    Label = oe.Label,
                    FromPort = oe.FromPort,
                    ToPort = oe.ToPort
                });
            }
        }

        // 타이틀 박스 복사
        foreach (var tb in originalJob.TitleBoxes)
        {
            reworkJob.TitleBoxes.Add(new WorkflowTitleBox
            {
                JobId = reworkJobId,
                Title = tb.Title,
                ColorHex = tb.ColorHex,
                X = tb.X,
                Y = tb.Y,
                Width = tb.Width,
                Height = tb.Height
            });
        }

        // 새 재작업 저장
        await SaveJobAsync(reworkJob, author);
        return reworkJob;
    }

    // =========================================================================
    // 7. 다차원 조회 (작업지시서, 부품, 작업자, 장비, 상태, 날짜)
    // =========================================================================
    /// <summary>
    /// 지정된 작업지시서와 부품의 가장 최신 '진행 중(미완료)' 작업을 조회합니다.
    /// </summary>
    public async Task<WorkUnitJob?> GetActiveJobForPartAsync(string orderNumber, string partName, int orderId = 0, int partId = 0)
    {
        var allJobs = await LoadAllJobsAsync();
        // 1. 고유 ID 일치 우선 (orderId, partId가 유효한 경우)
        var match = allJobs
            .Where(j => !j.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && !j.Status.Equals("완료", StringComparison.OrdinalIgnoreCase))
            .Where(j => (orderId > 0 && j.OrderId == orderId && partId > 0 && j.PartId == partId) ||
                        (!string.IsNullOrEmpty(orderNumber) && j.OrderNumber.Equals(orderNumber, StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrEmpty(partName) && j.PartName.Equals(partName, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(j => j.LastSavedAt)
            .FirstOrDefault();
        return match;
    }

    /// <summary>
    /// 지정된 부품의 과거 완료된 작업(Completed) 목록을 최신순으로 조회합니다.
    /// </summary>
    public async Task<List<WorkUnitJob>> GetCompletedJobsForPartAsync(string orderNumber, string partName, int orderId = 0, int partId = 0)
    {
        var allJobs = await LoadAllJobsAsync();
        return allJobs
            .Where(j => j.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) || j.Status.Equals("완료", StringComparison.OrdinalIgnoreCase))
            .Where(j => (orderId > 0 && j.OrderId == orderId && partId > 0 && j.PartId == partId) ||
                        (!string.IsNullOrEmpty(orderNumber) && j.OrderNumber.Equals(orderNumber, StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrEmpty(partName) && j.PartName.Equals(partName, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(j => j.CompletedAt ?? j.LastSavedAt)
            .ToList();
    }

    public async Task<List<WorkUnitJob>> QueryJobsAsync(JobQueryFilter filter)
    {
        var allJobs = await LoadAllJobsAsync();
        var results = new List<WorkUnitJob>();

        foreach (var job in allJobs)
        {
            // 작업지시서 필터
            if (!string.IsNullOrWhiteSpace(filter.OrderNumber) &&
                !job.OrderNumber.Contains(filter.OrderNumber, StringComparison.OrdinalIgnoreCase))
                continue;

            // 부품명 필터
            if (!string.IsNullOrWhiteSpace(filter.PartName) &&
                !job.PartName.Contains(filter.PartName, StringComparison.OrdinalIgnoreCase))
                continue;

            // 상태 필터
            if (!string.IsNullOrWhiteSpace(filter.Status) &&
                !job.Status.Equals(filter.Status, StringComparison.OrdinalIgnoreCase))
                continue;

            // 작업자 / 장비 / 날짜 필터 (실제 실행 이력을 기준으로 정밀 필터링 - 하루 작업 내역 중복 복제 없이 실적 기준 조회)
            var executions = await LoadExecutionHistoriesAsync(job.JobId);

            if (!string.IsNullOrWhiteSpace(filter.WorkerName))
            {
                bool workerMatched = job.Nodes.Any(n => n.Assignee.Contains(filter.WorkerName, StringComparison.OrdinalIgnoreCase)) ||
                                     executions.Any(e => e.WorkerName.Contains(filter.WorkerName, StringComparison.OrdinalIgnoreCase));
                if (!workerMatched) continue;
            }

            if (!string.IsNullOrWhiteSpace(filter.MachineId))
            {
                bool machineMatched = job.Nodes.Any(n => n.MachineId.Equals(filter.MachineId, StringComparison.OrdinalIgnoreCase)) ||
                                      executions.Any(e => e.MachineId.Equals(filter.MachineId, StringComparison.OrdinalIgnoreCase));
                if (!machineMatched) continue;
            }

            if (filter.FromDate.HasValue || filter.ToDate.HasValue)
            {
                DateTime from = filter.FromDate?.Date ?? DateTime.MinValue;
                DateTime to = (filter.ToDate?.Date ?? DateTime.MaxValue).AddDays(1).AddTicks(-1);

                // 해당 날짜 범위에 발생한 실제 실행 실적 또는 생성/완료 기록 검사
                bool dateMatched = (job.CreatedAt >= from && job.CreatedAt <= to) ||
                                   (job.LastSavedAt >= from && job.LastSavedAt <= to) ||
                                   executions.Any(e => e.Timestamp >= from && e.Timestamp <= to);
                if (!dateMatched) continue;
            }

            results.Add(job);
        }

        return results;
    }

    public async Task<List<WorkUnitJob>> LoadAllJobsAsync()
    {
        var list = new List<WorkUnitJob>();
        if (!Directory.Exists(_jobsDir)) return list;

        string[] subdirs = Directory.GetDirectories(_jobsDir);
        foreach (var dir in subdirs)
        {
            string jobFile = Path.Combine(dir, "job.json");
            if (File.Exists(jobFile))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(jobFile);
                    var job = JsonSerializer.Deserialize<WorkUnitJob>(json);
                    if (job != null) list.Add(job);
                }
                catch { }
            }
        }
        return list;
    }

    // =========================================================================
    // 8. 기존 데이터 마이그레이션 (소속 불명 데이터는 UNASSIGNED로 보존)
    // =========================================================================
    public async Task MigrateLegacyWorkflowsAsync()
    {
        if (!Directory.Exists(_legacyDir)) return;

        // 1. 레거시 단일 활성 파일: autosave_current_active.json
        string legacyActive = Path.Combine(_legacyDir, "autosave_current_active.json");
        if (File.Exists(legacyActive))
        {
            try
            {
                string json = await File.ReadAllTextAsync(legacyActive);
                var legacy = JsonSerializer.Deserialize<WorkflowData>(json);
                if (legacy != null && legacy.Nodes.Count > 0)
                {
                    string jobId = !string.IsNullOrWhiteSpace(legacy.OrderNumber) && !string.IsNullOrWhiteSpace(legacy.PartName)
                        ? GenerateJobId(legacy.OrderNumber, legacy.PartName)
                        : "JOB-UNASSIGNED-ACTIVE";

                    var job = new WorkUnitJob
                    {
                        JobId = jobId,
                        OrderId = legacy.OrderId,
                        OrderNumber = legacy.OrderNumber,
                        PartId = legacy.PartId,
                        PartName = legacy.PartName,
                        CreatedAt = legacy.LastSavedAt,
                        LastSavedAt = legacy.LastSavedAt,
                        IsConfirmed = legacy.IsConfirmed,
                        ConfirmedBy = legacy.ConfirmedBy,
                        Nodes = legacy.Nodes,
                        Edges = legacy.Edges,
                        TitleBoxes = legacy.TitleBoxes
                    };
                    await SaveJobAsync(job, "MIGRATION");
                }
            }
            catch { }
        }

        // 2. 레거시 {orderNo}_{partCode}.json 파일들
        string[] jsonFiles = Directory.GetFiles(_legacyDir, "*.json");
        foreach (var file in jsonFiles)
        {
            string fileName = Path.GetFileName(file);
            if (fileName.Equals("autosave_current_active.json", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("index.json", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("active_session.json", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                string json = await File.ReadAllTextAsync(file);
                var legacy = JsonSerializer.Deserialize<WorkflowData>(json);
                if (legacy != null && legacy.Nodes.Count > 0)
                {
                    string jobId = !string.IsNullOrWhiteSpace(legacy.OrderNumber)
                        ? GenerateJobId(legacy.OrderNumber, legacy.PartName)
                        : $"JOB-UNASSIGNED-{Path.GetFileNameWithoutExtension(file)}";

                    var job = new WorkUnitJob
                    {
                        JobId = jobId,
                        OrderId = legacy.OrderId,
                        OrderNumber = legacy.OrderNumber,
                        PartId = legacy.PartId,
                        PartName = legacy.PartName,
                        CreatedAt = legacy.LastSavedAt,
                        LastSavedAt = legacy.LastSavedAt,
                        IsConfirmed = legacy.IsConfirmed,
                        ConfirmedBy = legacy.ConfirmedBy,
                        Nodes = legacy.Nodes,
                        Edges = legacy.Edges,
                        TitleBoxes = legacy.TitleBoxes
                    };
                    await SaveJobAsync(job, "MIGRATION");
                }
            }
            catch { }
        }
    }

    // =========================================================================
    // 활성 세션 보존 및 복원 (프로그램 재실행 시 복원)
    // =========================================================================
    public async Task SetLastActiveJobIdAsync(string currentJobId)
    {
        await UpdateActiveSessionAsync(currentJobId);
    }

    private async Task UpdateActiveSessionAsync(string currentJobId)
    {
        string sessionFile = Path.Combine(_rootDir, "active_session.json");
        var session = new ActiveSessionInfo
        {
            ActiveJobId = currentJobId,
            LastActiveAt = DateTime.Now
        };
        string json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(sessionFile, json);
    }

    public async Task<string?> GetLastActiveJobIdAsync()
    {
        string sessionFile = Path.Combine(_rootDir, "active_session.json");
        if (!File.Exists(sessionFile)) return null;
        try
        {
            string json = await File.ReadAllTextAsync(sessionFile);
            var session = JsonSerializer.Deserialize<ActiveSessionInfo>(json);
            return session?.ActiveJobId;
        }
        catch
        {
            return null;
        }
    }

    private void OnStatusChanged(SaveState state, string message)
    {
        StatusChanged?.Invoke(this, new SaveStatusEventArgs
        {
            State = state,
            Message = message,
            Timestamp = DateTime.Now
        });
    }

    // Deep Clone Helpers
    private static WorkflowNode CloneNodeWithoutTransient(WorkflowNode src)
    {
        string json = JsonSerializer.Serialize(src);
        return JsonSerializer.Deserialize<WorkflowNode>(json) ?? new();
    }

    private static WorkflowEdge CloneEdge(WorkflowEdge src)
    {
        return new WorkflowEdge
        {
            Id = src.Id,
            FromJobId = src.FromJobId,
            ToJobId = src.ToJobId,
            FromNodeId = src.FromNodeId,
            ToNodeId = src.ToNodeId,
            Type = src.Type,
            Label = src.Label,
            FromPort = src.FromPort,
            ToPort = src.ToPort
        };
    }
}

public class ActiveSessionInfo
{
    public string ActiveJobId { get; set; } = "";
    public DateTime LastActiveAt { get; set; } = DateTime.Now;
}
