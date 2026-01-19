# Yoable

**English** | [简体中文](#简体中文)

**Yoable** is an AI-powered image annotation tool designed to make dataset labeling faster and more efficient. It supports **YOLO v5/v8/v11 (ONNX)** models for automatic object detection and labeling. Yoable provides an intuitive interface for managing images, running AI-assisted labeling, and exporting labels in a format compatible with machine learning models.

For non-WPF version you can build the legacy source or use v1.2.0 from releases - [Legacy branch](https://github.com/Babyhamsta/Yoable/tree/legacy).

<img width="1107" height="714" alt="image" src="https://github.com/user-attachments/assets/bfea3510-7cd1-44f2-87ed-0674cf3d67ff" />

---

## English

### 🆕 What's New in This Fork?

This fork includes several important improvements and new features that enhance the usability and stability of Yoable:

#### ✨ New Features

- **🗺️ Model Class Mapping** - Map model class IDs to your project's class IDs, allowing you to use pre-trained models with different class structures. You can also filter out unwanted classes by setting them to "nan (不檢測)".
- **🌐 Multilingual Support** - Full UI translation support for **繁體中文 (Traditional Chinese)**, **简体中文 (Simplified Chinese)**, and **English (US)**. Switch languages on the fly without restarting the application.
- **⌨️ Customizable Hotkeys** - Fully customizable keyboard shortcuts for common actions including save project, image navigation, and label movement. Configure your preferred key combinations in settings.
- **🔍 Class-Based Filtering** - Filter images by class labels using checkboxes. Quickly find images containing specific classes or combinations of classes for efficient workflow management.

#### 🐛 Bug Fixes & Stability

- **Filter Selection Crash Fix** - Fixed a critical bug that caused application crashes when switching between image filters. The fix ensures stable operation by properly managing event handlers during filter operations.
- **Class ID Calculation Fix** - Fixed a bug where adding new classes could result in incorrect class ID calculation. Previously, `AddClass_Click` used `GetNextClassId()` which was based on `CurrentProject.Classes`, but the actual working list was `projectClasses`. If these were out of sync, it could cause ID calculation errors. The fix:
  - Now directly calculates the next ID from `projectClasses` using `projectClasses.Max(c => c.ClassId) + 1`
  - Synchronizes `CurrentProject.Classes` after adding a new class to ensure correct saving
  - Ensures new classes always get the correct sequential ID (e.g., if IDs 0, 1, 2 exist, new class gets ID 3)

#### 📝 Documentation

- **Bilingual README** - Complete documentation in both English and Simplified Chinese for better accessibility.

These improvements make this fork more robust and user-friendly, especially for users working with different model architectures and multilingual environments.

### 🚀 Features

- **AI-Powered Auto Labeling** - Automatically detects objects using **YOLO v5/v8/v11 (ONNX)** models.
- **Manual Labeling Tools** - Easily add, edit, and remove bounding boxes.
- **Bulk Image Import** - Load multiple images at once.
- **YOLO Label Format Support** - Import and export annotations in **YOLO format**.
- **Optional Cloud Upload** - Choose to upload labeled datasets during export to contribute to better models.
- **Customizable UI** - Light/Dark theme and customizable label appearance.
- **Crosshair Overlay** - Align annotations with precision.
- **Adjustable AI Confidence** - Set detection confidence thresholds for better accuracy.
- **Auto Updates** - Get the latest features and fixes with built-in update checks. (Can be disabled via settings)
- **Project Support** - Yoable can create and save projects so you can pick back up where you left off.
- **Customizable Hotkeys** - Configure keyboard shortcuts for all common actions to speed up your workflow.
- **Class-Based Filtering** - Filter images by class labels to quickly find specific annotations.

### 📥 Installation

1. Download the latest release from our [GitHub Releases](https://github.com/Babyhamsta/Yoable/releases).
2. Download and run Yoable (No install required!).
3. (Optional) Load a **YOLO v5/v8/v11 (ONNX)** model for AI-assisted labeling.

### 🛠️ How to Use

#### Importing Images
- Click **"Import Image"** or **"Import Directory"** to load images.
- The images will appear in the **image list**.
- Use the scroll wheel to navigate through the imported images.

#### Applying Labels
- **Manual Labeling**: Use the drawing tools to create bounding boxes.
- **AI Auto-Labeling**: Click **"Auto Label Images"** to apply AI detections.

#### Managing Labels
- Labels appear in the **label list**.
- Click on a label to edit it.
- Press **Delete** to remove selected labels.
- Use arrow keys for precise label movement.

#### Importing & Exporting Labels
- **Import Labels**: Load existing YOLO-format label files.
- **Export Labels**: Save labeled data in YOLO format.
- **Cloud Upload (Optional)**: When exporting, users are asked if they want to upload their dataset. This can be disabled in settings.

#### Updating Yoable
- Yoable automatically checks for updates.
- If a new version is available, you'll be prompted to update.

#### Customizing Hotkeys
- Open **Settings** from the menu
- Navigate to the **Keyboard Shortcuts** section
- Click on any action button to set a custom hotkey
- Press your desired key combination (e.g., `Ctrl + S`, `A`, `D`, etc.)
- Press **Escape** to cancel hotkey recording
- Supported actions:
  - **Save Project** - Default: `Ctrl + S`
  - **Previous Image** - Default: `A`
  - **Next Image** - Default: `D`
  - **Move Label Up** - Default: `Up Arrow`
  - **Move Label Down** - Default: `Down Arrow`
  - **Move Label Left** - Default: `Left Arrow`
  - **Move Label Right** - Default: `Right Arrow`

#### Filtering by Class
- Expand the **Class Filter** section in the filter panel
- Use checkboxes to select which classes to filter by
- Images containing labels with the selected classes will be displayed
- Select all classes or clear all to show all images
- Class filters work in combination with status filters (All, Review, No Label, Verified)

### 🗺️ Model Class Mapping

Yoable supports **class mapping** functionality that allows you to map model class IDs to your project's class IDs. This is especially useful when:

- Your YOLO model has different class names/IDs than your project
- You want to filter out certain classes from detection
- You need to consolidate multiple model classes into a single project class

#### How to Use Class Mapping

1. **Load a YOLO Model**: First, load your YOLO model in Yoable.
2. **Open Class Mapping Dialog**: Access the class mapping feature from the model settings or menu.
3. **Configure Mappings**: 
   - Map each model class to a corresponding project class
   - Set classes to **"nan (不檢測)"** to skip detection for unwanted classes
   - Custom class names are automatically detected from model metadata when available
4. **Apply Mapping**: The mapping is automatically applied when using AI auto-labeling.

#### Benefits

- **Flexible Integration**: Use pre-trained models with different class structures
- **Selective Detection**: Ignore irrelevant classes by setting them to "nan"
- **Class Consolidation**: Map multiple model classes to a single project class

### 🌐 Multilingual Support

Yoable supports **multiple languages** for a better user experience. You can switch between languages at any time through the settings.

#### Supported Languages

- **繁體中文 (Traditional Chinese)** - Default language
- **简体中文 (Simplified Chinese)**
- **English (US)**

#### How to Change Language

1. Open **Settings** from the menu
2. Navigate to the **Language** section
3. Select your preferred language from the dropdown
4. The interface will update immediately

#### Language Features

- **Full UI Translation**: All menus, buttons, and dialogs are translated
- **Persistent Settings**: Your language preference is saved automatically
- **Dynamic Switching**: Change language without restarting the application

### 🐛 Bug Fixes & Stability Improvements

#### Filter Selection Crash Fix

A critical bug that caused application crashes when switching between image filters has been fixed. The issue occurred when:

- Switching between filter options (All, Review, No Label, Verified)
- The image list was being updated while a selection change event was triggered
- This led to attempts to access items that no longer existed in the filtered list

**The Fix:**
- Temporarily unbind the `SelectionChanged` event handler before updating the image list
- Safely restore selection after filtering is complete
- Re-bind the event handler to ensure normal functionality continues

This fix ensures stable operation when using the filter buttons, preventing crashes and maintaining proper selection state across filter changes.

#### Class ID Calculation Fix

A bug in class ID calculation has been fixed that could cause incorrect IDs when adding new classes. The issue occurred when:

- `AddClass_Click` used `projectManager?.CurrentProject?.GetNextClassId()` to calculate new class IDs
- `GetNextClassId()` was based on `CurrentProject.Classes`, but the actual working list was `projectClasses`
- If these two lists were out of sync, it could result in duplicate or incorrect class IDs

**The Fix:**
- Now directly calculates the next ID from `projectClasses` using `projectClasses.Max(c => c.ClassId) + 1`
- Synchronizes `CurrentProject.Classes` after adding a new class to ensure correct saving
- Ensures new classes always get the correct sequential ID

**Result:**
- New classes now correctly get the next available ID (e.g., if IDs 0, 1, 2 exist, new class gets ID 3)
- Project data is properly synchronized, ensuring correct saving
- No more duplicate or incorrect class IDs

### 🌍 Contributing
Yoable is **open-source**! Contribute by reporting issues, suggesting features, or improving the code.

### 📌 Support
For help and troubleshooting, visit our [GitHub Issues](https://github.com/Babyhamsta/Yoable/issues) or join our community.

---

## 简体中文

[English](#english) | **简体中文**

### 🆕 此 Fork 版本的新功能

此 fork 版本包含了多項重要的改進和新功能，提升了 Yoable 的可用性和穩定性：

#### ✨ 新功能

- **🗺️ 模型類別映射** - 將模型類別 ID 映射到項目的類別 ID，允許您使用具有不同類別結構的預訓練模型。您還可以通過將不需要的類別設置為 "nan (不檢測)" 來過濾它們。
- **🌐 多語言支持** - 完整的界面翻譯支持 **繁體中文 (Traditional Chinese)**、**简体中文 (Simplified Chinese)** 和 **English (US)**。無需重啟應用程序即可隨時切換語言。
- **⌨️ 自定義快捷鍵** - 為常用操作（保存項目、圖片導航、標籤移動等）完全自定義鍵盤快捷鍵。在設置中配置您首選的按鍵組合。
- **🔍 類別過濾** - 使用複選框按類別標籤過濾圖片。快速查找包含特定類別或類別組合的圖片，提高工作流程效率。

#### 🐛 錯誤修復與穩定性

- **過濾器選擇崩潰修復** - 修復了在切換圖片過濾器時導致應用程序崩潰的嚴重錯誤。此修復通過在過濾操作期間正確管理事件處理器來確保穩定運行。
- **類別 ID 計算修復** - 修復了添加新類別時可能導致類別 ID 計算錯誤的問題。之前，`AddClass_Click` 使用 `GetNextClassId()`，該方法基於 `CurrentProject.Classes`，但實際使用的列表是 `projectClasses`。如果兩者不同步，會導致 ID 計算錯誤。修復方案：
  - 現在直接從 `projectClasses` 計算下一個 ID：`projectClasses.Max(c => c.ClassId) + 1`
  - 添加類別後同步更新 `CurrentProject.Classes`，確保保存時正確
  - 確保新類別始終獲得正確的順序 ID（例如：如果已有 ID 0, 1, 2，新類別會得到 ID 3）

#### 📝 文檔

- **雙語 README** - 提供完整的英文和簡體中文文檔，提高可訪問性。

這些改進使此 fork 版本更加穩定和用戶友好，特別適合使用不同模型架構和多語言環境的用戶。

### 🚀 功能特性

- **AI 驱动的自动标注** - 使用 **YOLO v5/v8/v11 (ONNX)** 模型自动检测对象。
- **手动标注工具** - 轻松添加、编辑和删除边界框。
- **批量图片导入** - 一次性加载多张图片。
- **YOLO 标签格式支持** - 以 **YOLO 格式**导入和导出标注。
- **可选云端上传** - 导出时选择上传已标注的数据集，为更好的模型做出贡献。
- **可自定义界面** - 浅色/深色主题和可自定义的标签外观。
- **十字准线叠加** - 精确对齐标注。
- **可调节 AI 置信度** - 设置检测置信度阈值以获得更好的准确性。
- **自动更新** - 通过内置更新检查获取最新功能和修复。（可通过设置禁用）
- **项目支持** - Yoable 可以创建和保存项目，让您可以随时继续之前的工作。
- **自定義快捷鍵** - 為所有常用操作配置鍵盤快捷鍵，加快您的工作流程。
- **類別過濾** - 按類別標籤過濾圖片，快速查找特定標註。

### 📥 安装

1. 从我们的 [GitHub Releases](https://github.com/Babyhamsta/Yoable/releases) 下载最新版本。
2. 下载并运行 Yoable（无需安装！）。
3. （可选）加载 **YOLO v5/v8/v11 (ONNX)** 模型以进行 AI 辅助标注。

### 🛠️ 使用说明

#### 导入图片
- 点击 **"导入图片"** 或 **"导入目录"** 来加载图片。
- 图片将显示在 **图片列表** 中。
- 使用滚轮浏览导入的图片。

#### 应用标签
- **手动标注**：使用绘图工具创建边界框。
- **AI 自动标注**：点击 **"自动标注图片"** 以应用 AI 检测。

#### 管理标签
- 标签显示在 **标签列表** 中。
- 点击标签进行编辑。
- 按 **Delete** 键删除选中的标签。
- 使用方向键精确移动标签。

#### 导入和导出标签
- **导入标签**：加载现有的 YOLO 格式标签文件。
- **导出标签**：以 YOLO 格式保存已标注的数据。
- **云端上传（可选）**：导出时，系统会询问用户是否要上传其数据集。可在设置中禁用此功能。

#### 更新 Yoable
- Yoable 会自动检查更新。
- 如果有新版本可用，系统会提示您更新。

#### 自定義快捷鍵
- 从菜单打开 **设置**
- 导航到 **键盘快捷键** 部分
- 点击任何操作按钮来设置自定义快捷鍵
- 按下您想要的按键组合（例如：`Ctrl + S`、`A`、`D` 等）
- 按 **Escape** 取消快捷鍵录制
- 支持的操作：
  - **保存项目** - 默认：`Ctrl + S`
  - **上一张图片** - 默认：`A`
  - **下一张图片** - 默认：`D`
  - **向上移动标签** - 默认：`上方向键`
  - **向下移动标签** - 默认：`下方向键`
  - **向左移动标签** - 默认：`左方向键`
  - **向右移动标签** - 默认：`右方向键`

#### 按類別過濾
- 在過濾面板中展開 **類別過濾** 部分
- 使用複選框選擇要過濾的類別
- 將顯示包含所選類別標籤的圖片
- 選擇所有類別或清除所有選擇以顯示所有圖片
- 類別過濾器可與狀態過濾器（全部、審查、無標籤、已完成）組合使用

### 🗺️ 模型类别映射

Yoable 支持 **类别映射** 功能，允许您将模型的类别 ID 映射到项目的类别 ID。这在以下情况下特别有用：

- 您的 YOLO 模型具有与项目不同的类别名称/ID
- 您想要从检测中过滤掉某些类别
- 您需要将多个模型类别合并为单个项目类别

#### 如何使用类别映射

1. **加载 YOLO 模型**：首先在 Yoable 中加载您的 YOLO 模型。
2. **打开类别映射对话框**：从模型设置或菜单中访问类别映射功能。
3. **配置映射**：
   - 将每个模型类别映射到相应的项目类别
   - 将类别设置为 **"nan (不檢測)"** 以跳过不需要的类别检测
   - 如果可用，自定义类别名称会自动从模型元数据中检测
4. **应用映射**：在使用 AI 自动标注时，映射会自动应用。

#### 优势

- **灵活集成**：使用具有不同类别结构的预训练模型
- **选择性检测**：通过将不相关的类别设置为 "nan" 来忽略它们
- **类别合并**：将多个模型类别映射到单个项目类别

### 🌐 多语言支持

Yoable 支持 **多种语言**，以提供更好的用户体验。您可以随时通过设置切换语言。

#### 支持的语言

- **繁體中文 (Traditional Chinese)** - 默认语言
- **简体中文 (Simplified Chinese)**
- **English (US)**

#### 如何更改语言

1. 从菜单打开 **设置**
2. 导航到 **语言** 部分
3. 从下拉菜单中选择您首选的语言
4. 界面将立即更新

#### 语言功能

- **完整界面翻译**：所有菜单、按钮和对话框都已翻译
- **持久化设置**：您的语言偏好会自动保存
- **动态切换**：无需重启应用程序即可更改语言

### 🐛 错误修复与稳定性改进

#### 过滤器选择崩溃修复

已修复一个导致在切换图片过滤器时应用程序崩溃的严重错误。该问题在以下情况下发生：

- 在过滤器选项之间切换（全部、审查、无标签、已完成）
- 在触发选择更改事件时更新图片列表
- 这导致尝试访问已不在过滤列表中的项目

**修复方案：**
- 在更新图片列表之前暂时解除 `SelectionChanged` 事件处理器的绑定
- 在过滤完成后安全地恢复选择
- 重新绑定事件处理器以确保正常功能继续运行

此修复确保了使用过滤器按钮时的稳定运行，防止崩溃并在过滤器更改时保持正确的选择状态。

#### 類別 ID 計算修復

已修復一個類別 ID 計算錯誤，該問題在添加新類別時可能導致 ID 計算錯誤。該問題在以下情況下發生：

- `AddClass_Click` 使用 `projectManager?.CurrentProject?.GetNextClassId()` 來計算新類別 ID
- `GetNextClassId()` 基於 `CurrentProject.Classes`，但實際使用的列表是 `projectClasses`
- 如果這兩個列表不同步，可能會導致重複或錯誤的類別 ID

**修復方案：**
- 現在直接從 `projectClasses` 計算下一個 ID：`projectClasses.Max(c => c.ClassId) + 1`
- 添加類別後同步更新 `CurrentProject.Classes`，確保保存時正確
- 確保新類別始終獲得正確的順序 ID

**結果：**
- 新類別現在會正確獲得下一個可用的 ID（例如：如果已有 ID 0, 1, 2，新類別會得到 ID 3）
- 項目數據已正確同步，確保保存時正確
- 不再出現重複或錯誤的類別 ID

### 🌍 贡献
Yoable 是 **开源** 的！通过报告问题、建议功能或改进代码来做出贡献。

### 📌 支持
如需帮助和故障排除，请访问我们的 [GitHub Issues](https://github.com/Babyhamsta/Yoable/issues) 或加入我们的社区。

---

⭐ **如果觉得有用，请给这个仓库点个星！** / **Star this repo** if you find it useful!
