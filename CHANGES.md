# 本機分支相對 GitHub (Babyhamsta/Yoable, origin/master) 的變更

比較基準：`origin/master` @ `37ec380`（"Update README installation requirements"）
本機領先 5 個已提交的 commit，另外工作區還有尚未提交的變更。

## 一、已提交但尚未推送的 5 個 commit

1. **Add automatic UI language detection based on system locale** (`5a5664d`)
   - 首次啟動且尚無語言偏好設定時，自動偵測系統 UI 語系並套用最接近的支援語言
   - 中文依文字系統分流：繁體（zh-Hant/zh-TW/zh-HK/zh-MO）→ zh-TW，簡體 → zh-CN
   - 系統語言不支援時退回英文；使用者手動選擇的語言永遠優先，不會被自動偵測覆蓋

2. **Ignore release packages (zip/rar/7z) in git** (`11029bb`)
   - `.gitignore` 新增規則，避免發布用的壓縮檔被提交進版本庫

3. **Use dist/ folder for release packages and ignore it** (`2fbe1e4`)
   - 發布封裝統一輸出到 `dist/` 資料夾，並加入 `.gitignore`

4. **Fix label exports and YouTube downloads** (`c2c5fdd`)
   - 修正標籤匯出流程的問題
   - 修正 YouTube 下載器（`YoutubeDownloader.cs`）的相關錯誤
   - 調整 `NewProjectDialog.xaml` 版面
   - 專案檔版本號更新

5. **Update README installation requirements** (`37ec380`)
   - 更新 README 的安裝需求說明

## 二、工作區尚未提交的變更

### 1. 圖片載入效能優化（大型專案 8000~10000 張圖片時的卡頓改善）

**問題**：
- 每次切換圖片時，在 **UI 執行緒同步解碼**整張圖片，且沒有任何快取，切回看過的圖也要重新解碼
- 開啟大型專案時，即使 `.yoable` 專案檔的 `ImageReference` 早就有 `Width`/`Height` 欄位，存檔時卻從未寫入，導致每次開專案都要對上萬張圖逐一開檔解碼標頭來取得尺寸

**解法**：
- 新增 `Managers/ImageCacheManager.cs`：記憶體內的 LRU 點陣圖快取（預設上限 512MB / 512 張），並支援背景預載（prefetch）
  - 快取的 `BitmapImage` 會 `Freeze()`，可在背景執行緒解碼、UI 執行緒直接顯示
- `ImageManager.cs`：
  - 新增 `Cache` 屬性（`ImageCacheManager`）
  - 新增 `AddImageWithKnownSize(path, dimensions)`：跳過解碼標頭，直接用已知尺寸登記圖片
  - `LoadImagesFromPathsAsync` 新增 `clearExisting` 參數，支援與"已知尺寸快速路徑"混合載入
- `UI/DrawingCanvas.cs`：`LoadImage` 拆出一個接受已解碼 `ImageSource` 的多載，避免重複解碼
- `MainWindow.xaml.cs`：
  - `ImageListBox_SelectionChanged` 改為 `async`：快取命中則瞬間顯示；未命中則在背景執行緒解碼，不卡 UI
  - 加入 generation 計數器，快速連續切圖時會捨棄過時的解碼結果，避免標籤存錯檔案
  - 新增 `PrefetchNeighboringImages()`：每次切圖後自動背景預載後續 12 張、前面 4 張圖片，讓連續標註時下一張幾乎都是瞬間顯示
- `ProjectManager.cs`：
  - 匯出專案資料時，`ImageReference` 現在會寫入圖片的 `Width`/`Height`
  - 匯入專案資料時，優先使用已存的尺寸直接註冊圖片（完全跳過解碼），只有舊專案檔（尺寸為 0x0）才會退回原本的解碼路徑
  - `SaveProjectAs` 改為 `SaveProjectAsAsync`：資料夾建立與標籤檔複製移到背景執行緒，避免另存新檔時 UI 卡住；並修正失敗時專案路徑未回滾的問題

### 2. 標籤「變更類別」功能

- `MainWindow.xaml`：標籤清單（`LabelListBox`）的每個項目新增一顆「變更類別」圖示按鈕，並支援雙擊清單項目開啟類別選單
- `MainWindow.xaml.cs`：
  - 新增 `ChangeLabelClass_Click` / `LabelListBox_MouseDoubleClick` / `ShowChangeClassMenu`
  - 以 `ContextMenu` 列出專案所有類別（含顏色色塊），點選後即時套用到該標籤並刷新畫布與清單
- 多語系字串新增 `Main_ChangeClass`（English/日本語/Русский/简体中文/繁體中文皆已補上）

### 3. 手動標註進度標記（Manual Labeling Progress）

- `Models/ProjectData.cs`：新增 `ManualProgressImageFile` 欄位，記錄使用者手動標註進度的檔名檢查點
- `MainWindow.xaml`：選單新增「設定目前為手動進度」／「跳至手動進度」兩個選項
- `MainWindow.xaml.cs`：
  - `SetManualProgress_Click`：將目前選取圖片設為進度檢查點，並重新計算所有圖片狀態
  - `JumpToManualProgress_Click`：跳轉並選取到檢查點對應的圖片（會先清除篩選以確保可見）
  - `IsAtOrBeforeManualProgress`：判斷某檔名是否在檢查點（依檔名排序）之前或等於檢查點
  - 狀態判定邏輯（`DetermineImageStatus`）納入此檢查點：已在檢查點之前的 AI/匯入標籤，即使重新匯入也維持「已驗證」狀態，不會被誤判為需要複審
- `ProjectManager.cs` 的狀態校正邏輯同步套用此規則
- 新增對應多語系字串：`Menu_SetManualProgress`、`Menu_JumpManualProgress`、`Msg_ManualProgress_*` 系列（5 支語言檔皆已同步）

### 4. 匯入標籤去重合併（Idempotent Import）

- `Managers/LabelManager.cs`：
  - 新增 `IsSameImportedLabel`：以 0.01 的座標容差判斷兩個標籤是否視為同一個框（因為 YOLO 匯出座標常有四捨五入誤差）
  - 新增 `MergeImportedLabels`：合併既有標籤與匯入標籤時，先去除既有重複，再避免加入與既有標籤相同的匯入標籤
  - 套用到兩處標籤匯入流程，確保重複匯入同一份已匯出的資料集不會造成標籤重複堆疊

### 5. 其他小修正

- `MainWindow.xaml.cs`：
  - 「另存新檔」對話框補上 `DefaultExt`/`AddExtension`，避免使用者輸入檔名時漏副檔名
  - 修正 `IsDescendantOf`（或同類的視覺樹判斷輔助方法）在 `OriginalSource` 為 `Run`（TextBlock 內的行內文字，非 Visual 節點）時，呼叫 `VisualTreeHelper.GetParent` 會拋例外的問題；改為先判斷是否為 `Visual`/`Visual3D` 節點，否則退回邏輯樹（`LogicalTreeHelper`）向上查找
- `SettingsWindow.xaml`：語言下拉選單的顯示樣板，將原本用多個 `Run` 拼接語言名稱＋國際化名稱的寫法，改為 `StackPanel` + `StringFormat` 綁定，避免留白/格式問題
