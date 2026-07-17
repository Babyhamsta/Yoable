# Agent Notes — Yoable

給 AI coding agent 的專案指南。Yoable 是一個 WPF 桌面應用程式，用於 YOLO 資料集的影像標註，支援 ONNX 模型自動標註。

---

## 1. 硬性規則（優先於其他所有指引）

- **絕對不要 push 到 `Babyhamsta/Yoable`**（也就是 `origin`）。那是上游倉庫，不是我們的。
  - `origin` → `https://github.com/Babyhamsta/Yoable`（上游，**唯讀**，只能 fetch/pull）
  - `myfork` → `https://github.com/asenyeroao-ct/Yoable`（我們的 fork，push 目標）
  - 要 push 時明確指定 remote：`git push myfork <branch>`。**不要**用裸的 `git push`。
- 未經使用者要求，不要自行 commit 或 push。
- 不要提交 `dist/`、`Releases/`、`bin/`、`obj/`、`.vs/`，以及任何 `.zip`/`.rar`/`.7z` 發布壓縮檔（`.gitignore` 已涵蓋，別用 `-f` 繞過）。

---

## 2. 專案概觀

| 項目 | 內容 |
|---|---|
| 框架 | .NET 9 WPF（`net9.0-windows7.0`），C# with `Nullable` + `ImplicitUsings` enable |
| 方案檔 | `YoableWPF.sln` → 單一專案 `YoableWPF/YoableWPF.csproj` |
| 版本號 | 定義在 `YoableWPF.csproj` 的 `<Version>`（目前 3.2.0），由 `VersionInfo.cs` 於執行期讀取 |
| 平台 | 僅 Windows x64（DirectML / OpenCvSharp 有原生相依） |
| 打包 | Costura.Fody 把相依 DLL 併入單一 exe |

主要相依套件：ModernWpfUI（主題）、Microsoft.ML.OnnxRuntime.DirectML（推論）、OpenCvSharp4（影像處理／樣板比對）、YoutubeExplode（影片下載）、Extended.Wpf.Toolkit。

### 建置與執行

```bash
dotnet build YoableWPF.sln -c Debug                 # 建置
dotnet run --project YoableWPF/YoableWPF.csproj     # 執行
dotnet build YoableWPF.sln -c Release               # 發布用建置
```

沒有測試專案。**驗證方式是實際跑起來操作 UI**，不是跑測試。改動涉及大型資料集效能時，用數千張圖的專案實測切圖流暢度。

---

## 3. 程式碼結構

```
YoableWPF/
├── App.xaml.cs              進入點：升級 settings → 初始化 LanguageManager → 開 StartupWindow
├── StartupWindow.xaml       啟動畫面 + 專案選擇器（不是 MainWindow 先開）
├── MainWindow.xaml(.cs)     主視窗，3374 行，所有 manager 的組裝點與事件中樞
├── VersionInfo.cs           從 assembly attribute 讀版本
├── Managers/                商業邏輯（見下表）
├── Models/                  共用資料模型
├── UI/DrawingCanvas.cs      標註畫布：繪製／拖曳／縮放 bounding box
├── Resources/Languages/     5 個語系的 ResourceDictionary
└── Properties/Settings.*    使用者設定（Settings.Designer.cs 是自動產生，別手改）
```

### Managers（`YoableWPF/Managers/`）

| 檔案 | 職責 |
|---|---|
| `YoloAi.cs` (1917行) | ONNX 模型載入、YOLO v5/v8/v11 推論、NMS、model→project 類別對應 |
| `ProjectManager.cs` (1433行) | `.yoable` 專案檔的存讀、另存新檔、最近專案 |
| `PropagationManager.cs` (1191行) | Auto Suggest：影像相似度 / 物件相似度 / 追蹤三種標籤傳播模式 |
| `LabelManager.cs` (1008行) | 標籤的記憶體儲存、YOLO txt 匯入匯出、suggestion 的 accept/reject |
| `ImageManager.cs` | 影像清單、尺寸登記、狀態（NoLabel/VerificationNeeded/Verified/Suggested） |
| `ImageCacheManager.cs` | 已解碼 bitmap 的 LRU 快取（預設 512MB / 512 張）+ 背景預載 |
| `UIStateManager.cs` | UI 狀態同步 |
| `LanguageManager.cs` | 多語系單例，含系統語系自動偵測 |
| `OverlayManager.cs` | 載入中／進度覆蓋層 |
| `HotkeyManager.cs` | 可自訂快捷鍵 |
| `UpdateManager.cs` | GitHub 自動更新檢查 |
| `YoutubeDownloader.cs` | YouTube 影片下載並抽格 |

### Models（`YoableWPF/Models/`）

- `Models.cs` — `ImageStatus` enum、`ImageListItem`、`LabelClass`（含凍結的 `SolidColorBrush`）
- `ProjectData.cs` — `.yoable` 專案檔的序列化結構、`ImageReference`
- `SuggestedLabel.cs`、`LabelListItemView.cs`、`RecentProjectInfo.cs`

---

## 4. 關鍵慣例與陷阱

### 4.1 多語系（改 UI 文字必看）

- **每個使用者可見字串都必須進 5 個語系檔**：`en-US`、`ja-JP`、`ru-RU`、`zh-CN`、`zh-TW`。
- **不變條件：5 個檔案的 `x:Key` 數量必須完全相同（目前各 426 個）。** 加字串時 5 個都要加，漏一個就是 bug。

  驗證：
  ```bash
  for f in YoableWPF/Resources/Languages/*.xaml; do echo -n "$f: "; grep -c "x:Key=" "$f"; done
  ```
  五行數字必須一致。

- key 命名採 `區塊_用途`：`Common_OK`、`Main_ChangeClass`、`Settings_Theme`。
- XAML 中用 `{DynamicResource Key}`（**不是** `StaticResource`，否則切換語言不會即時更新）；C# 中用 `LanguageManager.Instance.GetString("Key")`。
- 找不到 key 時會 fallback 到英文，再找不到就直接顯示 key 本身 — 所以漏翻譯不會 crash，只會默默顯示英文或 key，要主動檢查。

### 4.2 執行緒與效能（大型資料集是本專案的核心痛點）

專案要處理 8000~10000 張影像，效能問題都出在這裡：

- **絕不在 UI 執行緒同步解碼影像。** 走 `imageManager.Cache`：快取命中就直接顯示，未命中在背景執行緒解碼。
- 快取的 `BitmapImage` 一律 `Freeze()` — 這是能跨執行緒使用的前提。任何新建的 `Brush`/`Bitmap` 若會跨執行緒或大量重複使用，也應 `Freeze()`（見 `LabelClass.CreateFrozenBrush`）。
- **切圖有 generation 計數器**（`MainWindow.ImageListBox_SelectionChanged`）：快速連續切圖時，過時的背景解碼結果必須被捨棄，否則**標籤會存到錯的檔案**。動這段程式碼時務必保留這個機制。
- 切圖後 `PrefetchNeighboringImages()` 會背景預載後 12 張、前 4 張。
- 專案檔的 `ImageReference` 已存 `Width`/`Height`，開專案時走 `AddImageWithKnownSize()` 完全跳過解碼；只有舊版專案檔（尺寸 0x0）才 fallback 到解碼路徑。**新增專案檔欄位時記得同時處理舊檔相容。**
- 檔案 I/O 與資料夾建立放背景執行緒（參考 `SaveProjectAsAsync`）。
- `LabelManager.LabelStorage` / `ImageManager.ImagePathMap` 是 `ConcurrentDictionary`，本來就預期多執行緒存取。

### 4.3 架構現況

- `MainWindow` 的 manager 欄位刻意是 `public`，因為 `ProjectManager` 要回頭存取它們。這是既有設計，不要順手「修正」成 private。
- `MainWindow.xaml.cs` 有 3374 行，是事實上的 God object。**不要為了整潔而大規模重構它** — 除非使用者明確要求。改動請局部化。
- 沒有 MVVM framework，也沒有 DI container，就是 code-behind + 手動 new manager。照著現有風格寫，不要引入新架構。

### 4.4 標籤與類別

- 標籤以 YOLO 格式存 txt：`classId cx cy w h`（正規化座標）。
- 專案內部區分 **app 建立的標籤**（存在專案資料夾）與 **匯入的外部標籤**（指向原始路徑）— 見 `ProjectData.AppCreatedLabels` vs `ImportedLabelPaths`。
- 專案類別 ID 可以和模型的類別 ID 不同，透過 `ModelClassMappings` 對應；設成 `"nan"` 表示過濾掉該類別。
- 刪除類別後可能出現孤兒標籤，`LabelManager.FixAllOrphanedLabels()` 負責處理。

---

## 5. 程式風格

- 跟著周圍的程式碼寫：4 空白縮排、大括號自成一行（Allman）、public 成員 PascalCase、私有欄位 camelCase（manager 類別多半**不加** `_` 前綴，但 `Models.cs` 的 backing field 有用 `_` — 依所在檔案的既有風格為準）。
- public API 加 `///` XML doc comment，其他地方註解從簡。
- 程式碼註解用英文（現有程式碼皆為英文）；`CHANGES.md` 用繁體中文。
- 例外處理傾向 catch + `System.Diagnostics.Debug.WriteLine` + 優雅降級，而不是讓 UI crash。

---

## 6. Git 工作流程

- 主分支：`master`。
- 本機領先上游數個 commit，`CHANGES.md`（未提交，本機專用）記錄相對 `origin/master` 的所有差異 — **改動有意義時請同步更新它**。
- commit message 用英文祈使句：`Improve image performance and labeling workflows`、`Fix label exports and YouTube downloads`。
- 從上游同步：`git fetch origin && git rebase origin/master`。
- 推自己的變更：`git push myfork master`。

`.github/workflows/claude-code-review.yml` 會在 PR 上跑 Claude Code Review。
