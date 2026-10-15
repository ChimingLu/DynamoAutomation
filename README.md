# DynamoAutomation 2.x

用一張 Dynamo「主控圖」，讓 **同一張工作圖自動跑過很多個 Revit 模型**。
支援 **Revit 2020–2027（Dynamo 2.3–4.x）**，純 Dynamo 節點，不需要安裝任何 Revit 外掛。

> 本專案 fork 自 Andreas Dieckmann 的 [DynamoAutomation](https://github.com/andydandy74/DynamoAutomation)（1.x，Dynamo 0.9/1.x）。
> 2.x 保留原本「主控圖 + journal + 工作圖」的做法，改寫成可在新版 Dynamo 使用。English summary at the end.

---

## 運作方式

```
主控圖（Dynamo Sandbox / DynamoCLI / 任一 Dynamo for Revit）
  └─ Batch.RunGraph ── 每個模型：寫一份 journal ─► 啟動 Revit.exe
                                                   ├─ 開啟模型
                                                   ├─ 啟動 Dynamo（不顯示視窗）執行工作圖
                                                   └─ 工作圖最後的 Job.Finish：存檔（可選）→ 關閉 Revit
  ◄── 收集每個模型的結果 → batch_<時間>.csv
```

- 一個模型一個 Revit 程序，可設定同時跑幾個（`maxParallel`）。
- 工作圖用 `Job.*` 節點取得「目前模型、輸出資料夾、自訂參數」。
- 工作圖在 Dynamo 裡手動執行時，`Job.Finish` 不會動作，方便先在單一模型上測試。

## 已測試版本

| Revit | Dynamo | 結果 |
|---|---|---|
| 2020 | 2.3 | ✅ 數牆範例、CSV 報表、Job.Finish 關閉 Revit |
| 2021 | 2.6 | ✅ 同上 |
| 2022 | 2.10 | ✅ 同上 |
| 2023 | 2.13 | ✅ 同上 |
| 2024 | 2.19 | ✅ 同上 |
| 2025 | 3.0 | ✅ 同上，另存副本（saveMode = copy）成功 |
| 2026 | 4.2 | ✅ 同上（需先設定「受信任的位置」，見下方） |
| 2027 | 4.0 | ✅ 同上（需先設定「受信任的位置」；執行時會顯示 Dynamo 視窗，見常見問題） |

## 安裝

1. 下載或自行建置 `dist\DynamoAutomation`（建置方式見最後）。
2. 關閉 Revit，執行 `DynamoAutomation\extra\install.cmd`（或 `install.ps1`）。
   - 會把套件複製到這台電腦上所有 Dynamo for Revit 2020–2027 與 Dynamo Sandbox 的 packages 資料夾。
   - 舊的 1.x 版會移到 `%AppData%\Dynamo\DynamoAutomation_backup`。
   - 只裝特定版本：`install.ps1 -RevitVersions 2021,2025`
3. **Dynamo 4.x（Revit 2026、2027）**：在該版 Revit 開啟 Dynamo →
   偏好設定 › 安全性 › 受信任的位置，加入工作圖所在的資料夾。
   沒加的話，Dynamo 會跳出「此檔案儲存在未受信任的位置」，批次中無人回答，工作圖不會執行。
   `Batch.RunGraph` 在 `run = false` 時會先檢查並提示。

移除：`extra\uninstall.ps1`。

## 快速開始（範例在 `extra\samples`）

1. 把要處理的模型放進一個資料夾（路徑可含中文與空白）。
2. 用 Dynamo（Sandbox 或任一版 Revit 內的 Dynamo）開 `RunBatch_Master.dyn`，填入：
   模型資料夾、工作圖（`CountWalls_Worker.dyn`）、輸出資料夾、Revit 版本、存檔模式。
3. `Run` 保持 false 先執行一次：每個模型應顯示 `ready`。
4. 把 `Run` 改成 true 再執行。完成後輸出資料夾會有：
   - `batch_<時間>.csv`：每個模型的狀態、秒數、訊息
   - `wall_count.csv`：工作圖寫出的牆數
   - `_batches\<時間>\<模型>\`：該模型的 journal、Revit 錄製的 journal、log（除錯用）

> Python 引擎：Python 節點沒有指定引擎時，Dynamo 3.x/4.x 會當成 IronPython2 而無法執行。
> `Batch.RunGraph` 會依目標 Dynamo 版本，在工作圖旁的 `_DynamoAutomation\` 子資料夾寫一份設定好引擎
> （CPython3 / PythonNet3）的副本並用它執行，所以同一張工作圖可跑 Revit 2020–2027。

## 自己寫工作圖

工作圖就是一般的 Dynamo for Revit 腳本，只多兩個規則：

1. **最後一定要接 `Job.Finish`**，把最後的結果接到 `waitFor`，確保其他節點先算完。
   `Job.Finish` 會依主控圖的 `saveMode` 存檔、回報結果、關閉 Revit。
2. **不能有需要人操作的東西**：選取元件的節點、會跳對話框的節點、本機絕對路徑。

| 節點 | 用途 |
|---|---|
| `Job.IsBatchRun` | 是否由批次啟動 |
| `Job.ModelPath(manualRunPath)` | 目前處理的模型路徑；手動執行時回傳 `manualRunPath`（接 `Document.FilePath`） |
| `Job.OutputDirectory` | 主控圖指定的輸出資料夾（手動執行時為暫存資料夾） |
| `Job.Index` | 第幾個模型（0 起算） |
| `Job.Parameter(key, default)` | 讀取主控圖用 `parameterKeys` / `parameterValues` 傳入的值 |
| `Job.Log(message)` | 寫入這個模型的 log |
| `Job.Finish(waitFor, message)` | **放在最後**：存檔 → 回報 → 關閉 Revit |

## 全部節點

| 類別 | 節點 |
|---|---|
| Batch | `RunGraph`、`FindModels`（自動略過 `*.0001.rvt` 備份檔） |
| Job | 見上表 |
| Journal | `ByWorkspacePath`、`StartupArguments`、`MoveFiles`（進階：自行組 journal） |
| Report | `AppendLine`、`AppendCsvRow`（多個 Revit 同時寫入也安全，UTF-8 BOM 方便 Excel 開中文） |
| Config | `Read`、`Value`、`ResolvePath`（`key=value` 設定檔，讓圖不用改線就能換專案） |
| TextMatch | `Contains`、`Matches`、`Replace`（取代 1.x 依賴 IronPython 的 Regex 節點） |
| RevitInstall | `InstalledVersions`、`ExecutablePath`、`AddinsPath`、`DynamoVersion`、`DynamoTrustedLocations` |
| ExternalProcess | `Run`（執行外部程式並等待結束） |

`Batch.RunGraph` 參數：

| 參數 | 說明 |
|---|---|
| `saveMode` | `none` 不存檔／`copy` 另存到 `輸出資料夾\models`／`overwrite` 覆寫原檔（不支援協同模型） |
| `openDetached` | 協同模型以「分離」開啟（中央檔建議開啟，並搭配 `copy`） |
| `maxParallel` | 同時開幾個 Revit（依記憶體與授權調整）；Revit 的啟動與關閉之間會自動間隔 20 秒，啟動即失敗的會自動重試一次 |
| `timeoutMinutes` | 單一模型逾時就強制結束該 Revit，並記為 `timeout` |
| `run` | false 只檢查輸入，true 才真的執行 |

## 常見問題

- **狀態 `timeout`**：工作圖沒有跑到 `Job.Finish`。打開該模型的 `_batches\...\journal.0001.txt`（Revit 錄製）與 Dynamo 的 log 查看原因；最常見是受信任位置（Dynamo 4.x）或工作圖節點錯誤。
- **Revit 跳出對話框卡住**：journal 已開啟 `PerformAutomaticActionInErrorDialog`，大多數錯誤對話框會自動處理；遺失連結、外掛提示等仍可能需要先在模型裡處理好。
- **Revit 2027 批次時會跳出 Dynamo 視窗**：Revit 2027 的 Dynamo 只有在顯示視窗時才會從 journal 開啟並執行工作圖，所以 `Batch.RunGraph` 對 2027 會自動顯示視窗；不用理它，`Job.Finish` 會照常關閉 Revit。2020–2026 不顯示視窗。
- **為什麼不用 Revit 外掛**：2.x 刻意只用 Dynamo 節點，安裝只需要複製 Dynamo 套件，不必在每個 Revit 版本處理外掛的安全性提示。

## 從 1.x 升級

- 1.x 的範例圖是 Dynamo 1.x 的 XML 格式，Dynamo 3.x 以後無法開啟；請改用 `extra\samples` 的新範例。
- `Journal.ByWorkspacePath` + `Process.ByPathAndArguments` 的組合可改用一個 `Batch.RunGraph`。
- 工作圖結尾的 `Process.KillCurrentProcess` 改用 `Job.Finish`。
- Regex 自訂節點改用 `TextMatch.*`。

## 建置

需求：.NET SDK 8 以上、Python 3、Windows PowerShell。

```powershell
.\build.ps1          # 產出 dist\DynamoAutomation
tools\run_batch_test.ps1 -RevitVersion 2021 -ModelDir <模型資料夾> -OutputDir <輸出>   # 不開 Dynamo 直接跑一次批次
```

- `src\DynamoAutomation`：zero-touch 節點（netstandard2.0，一個 DLL 支援 Dynamo 2.3–4.x）
- `tools\make_samples.py`：產生範例 .dyn
- `installer\`：安裝／移除腳本

---

## English summary

DynamoAutomation 2.x batch-runs one Dynamo graph on many Revit models from a master graph
(Dynamo Sandbox, DynamoCLI or any Dynamo for Revit). It supports Revit 2020–2027 / Dynamo 2.3–4.x
with a single netstandard2.0 zero-touch library and installs no Revit add-in.

- `Batch.RunGraph` writes one journal per model and starts one Revit process per model
  (optionally in parallel), then collects the results into `batch_<time>.csv`.
- The worker graph reads its job with `Job.ModelPath`, `Job.OutputDirectory`, `Job.Parameter`
  and must end with `Job.Finish`, which saves (none / copy / overwrite) and closes Revit.
  When the worker graph is run by hand, `Job.Finish` does nothing.
- Dynamo 4.x only runs graphs from trusted locations; add the worker graph folder under
  Preferences > Security > Trusted Locations. `Batch.RunGraph` checks this when `run` is false.
- Install with `extra\install.cmd`; build with `build.ps1`.

License: MIT (see LICENSE). Original work © Andreas Dieckmann.
