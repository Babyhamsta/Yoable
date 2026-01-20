# Yoable

**English** | [日本語](#日本語) | [Русский](#русский) | [简体中文](#简体中文)

**Yoable** is an AI-powered image annotation tool designed to make dataset labeling faster and more efficient. It supports **YOLO v5/v8/v11 (ONNX)** models for automatic object detection and labeling. Yoable provides an intuitive interface for managing images, running AI-assisted labeling, and exporting labels in a format compatible with machine learning models.

For non-WPF version you can build the legacy source or use v1.2.0 from releases - [Legacy branch](https://github.com/Babyhamsta/Yoable/tree/legacy).

<img width="1107" height="714" alt="image" src="https://github.com/user-attachments/assets/bfea3510-7cd1-44f2-87ed-0674cf3d67ff" />

---

## English

### 🚀 Features

- **AI-Powered Auto Labeling** - Automatically detects objects using **YOLO v5/v8/v11 (ONNX)** models.
- **🗺️ Model Class Mapping** - Map model class IDs to your project's class IDs, allowing you to use pre-trained models with different class structures. Filter out unwanted classes by setting them to "nan".
- **🌐 Multilingual Support** - Full UI translation support for **English**, **日本語 (Japanese)**, **Русский (Russian)**, **简体中文 (Simplified Chinese)**, and **繁體中文 (Traditional Chinese)**. Switch languages on the fly without restarting.
- **⌨️ Customizable Hotkeys** - Fully customizable keyboard shortcuts for common actions including save project, image navigation, and label movement.
- **🔍 Class-Based Filtering** - Filter images by class labels using checkboxes. Quickly find images containing specific classes or combinations of classes.
- **Manual Labeling Tools** - Easily add, edit, and remove bounding boxes.
- **Bulk Image Import** - Load multiple images at once.
- **YOLO Label Format Support** - Import and export annotations in **YOLO format**.
- **Optional Cloud Upload** - Choose to upload labeled datasets during export to contribute to better models.
- **Customizable UI** - Light/Dark theme and customizable label appearance.
- **Crosshair Overlay** - Align annotations with precision.
- **Adjustable AI Confidence** - Set detection confidence thresholds for better accuracy.
- **Auto Updates** - Get the latest features and fixes with built-in update checks. (Can be disabled via settings)
- **Project Support** - Create and save projects so you can pick back up where you left off.

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
   - Set classes to **"nan"** to skip detection for unwanted classes
   - Custom class names are automatically detected from model metadata when available
4. **Apply Mapping**: The mapping is automatically applied when using AI auto-labeling.

#### Benefits

- **Flexible Integration**: Use pre-trained models with different class structures
- **Selective Detection**: Ignore irrelevant classes by setting them to "nan"
- **Class Consolidation**: Map multiple model classes to a single project class

### 🌐 Multilingual Support

Yoable supports **multiple languages** for a better user experience. You can switch between languages at any time through the settings.

#### Supported Languages

- **English (US)** - Default language
- **日本語 (Japanese)**
- **Русский (Russian)**
- **简体中文 (Simplified Chinese)**
- **繁體中文 (Traditional Chinese)**

#### How to Change Language

1. Open **Settings** from the menu
2. Navigate to the **Language** section
3. Select your preferred language from the dropdown
4. The interface will update immediately

#### Language Features

- **Full UI Translation**: All menus, buttons, and dialogs are translated
- **Persistent Settings**: Your language preference is saved automatically
- **Dynamic Switching**: Change language without restarting the application

### 🌍 Contributing
Yoable is **open-source**! Contribute by reporting issues, suggesting features, or improving the code.

### 📌 Support
For help and troubleshooting, visit our [GitHub Issues](https://github.com/Babyhamsta/Yoable/issues) or join our community.

---

## 日本語

[English](#english) | **日本語** | [Русский](#русский) | [简体中文](#简体中文)

### 🚀 機能

- **AI駆動の自動ラベリング** - **YOLO v5/v8/v11 (ONNX)** モデルを使用してオブジェクトを自動検出します。
- **🗺️ モデルクラスマッピング** - モデルのクラスIDをプロジェクトのクラスIDにマッピングし、異なるクラス構造を持つ事前学習済みモデルを使用できます。不要なクラスを「nan」に設定してフィルタリングします。
- **🌐 多言語サポート** - **English**、**日本語 (Japanese)**、**Русский (Russian)**、**简体中文 (Simplified Chinese)**、**繁體中文 (Traditional Chinese)** の完全なUI翻訳をサポート。再起動せずにその場で言語を切り替えられます。
- **⌨️ カスタマイズ可能なホットキー** - プロジェクトの保存、画像ナビゲーション、ラベルの移動など、一般的な操作のキーボードショートカットを完全にカスタマイズできます。
- **🔍 クラスベースのフィルタリング** - チェックボックスを使用してクラスラベルで画像をフィルタリングします。特定のクラスまたはクラスの組み合わせを含む画像をすばやく見つけます。
- **手動ラベリングツール** - バウンディングボックスを簡単に追加、編集、削除できます。
- **一括画像インポート** - 複数の画像を一度に読み込みます。
- **YOLOラベル形式サポート** - **YOLO形式**でアノテーションをインポートおよびエクスポートします。
- **オプションのクラウドアップロード** - エクスポート時にラベル付きデータセットをアップロードして、より良いモデルに貢献できます。
- **カスタマイズ可能なUI** - ライト/ダークテーマとカスタマイズ可能なラベルの外観。
- **クロスヘアオーバーレイ** - 精密にアノテーションを配置します。
- **調整可能なAI信頼度** - より高い精度のために検出信頼度のしきい値を設定します。
- **自動更新** - 組み込みの更新チェックで最新の機能と修正を取得します。（設定で無効にできます）
- **プロジェクトサポート** - プロジェクトを作成して保存し、中断したところから再開できます。

### 📥 インストール

1. [GitHub Releases](https://github.com/Babyhamsta/Yoable/releases) から最新リリースをダウンロードします。
2. Yoableをダウンロードして実行します（インストール不要！）。
3. （オプション）AI支援ラベリングのために **YOLO v5/v8/v11 (ONNX)** モデルを読み込みます。

### 🌍 貢献
Yoableは**オープンソース**です！問題の報告、機能の提案、コードの改善によって貢献してください。

### 📌 サポート
ヘルプとトラブルシューティングについては、[GitHub Issues](https://github.com/Babyhamsta/Yoable/issues) にアクセスするか、コミュニティに参加してください。

---

## Русский

[English](#english) | [日本語](#日本語) | **Русский** | [简体中文](#简体中文)

### 🚀 Возможности

- **Автоматическая разметка с ИИ** - Автоматически обнаруживает объекты с помощью моделей **YOLO v5/v8/v11 (ONNX)**.
- **🗺️ Сопоставление классов моделей** - Сопоставьте идентификаторы классов модели с идентификаторами классов вашего проекта, что позволяет использовать предобученные модели с различными структурами классов. Фильтруйте нежелательные классы, устанавливая их в значение «nan».
- **🌐 Многоязычная поддержка** - Полная поддержка перевода интерфейса для **English**, **日本語 (Japanese)**, **Русский (Russian)**, **简体中文 (Simplified Chinese)** и **繁體中文 (Traditional Chinese)**. Переключайте языки на лету без перезапуска.
- **⌨️ Настраиваемые горячие клавиши** - Полностью настраиваемые сочетания клавиш для общих действий, включая сохранение проекта, навигацию по изображениям и перемещение меток.
- **🔍 Фильтрация по классам** - Фильтруйте изображения по меткам классов с помощью флажков. Быстро находите изображения, содержащие определенные классы или комбинации классов.
- **Инструменты ручной разметки** - Легко добавляйте, редактируйте и удаляйте ограничивающие рамки.
- **Массовый импорт изображений** - Загружайте несколько изображений одновременно.
- **Поддержка формата меток YOLO** - Импортируйте и экспортируйте аннотации в **формате YOLO**.
- **Дополнительная загрузка в облако** - Выберите загрузку размеченных наборов данных при экспорте, чтобы внести вклад в улучшение моделей.
- **Настраиваемый интерфейс** - Светлая/темная тема и настраиваемый внешний вид меток.
- **Наложение перекрестия** - Выравнивайте аннотации с точностью.
- **Регулируемая достоверность ИИ** - Установите пороги достоверности обнаружения для лучшей точности.
- **Автоматические обновления** - Получайте последние функции и исправления с помощью встроенной проверки обновлений. (Можно отключить в настройках)
- **Поддержка проектов** - Создавайте и сохраняйте проекты, чтобы продолжить с того места, где вы остановились.

### 📥 Установка

1. Загрузите последний релиз с [GitHub Releases](https://github.com/Babyhamsta/Yoable/releases).
2. Загрузите и запустите Yoable (установка не требуется!).
3. (Необязательно) Загрузите модель **YOLO v5/v8/v11 (ONNX)** для разметки с помощью ИИ.

### 🌍 Участие
Yoable **с открытым исходным кодом**! Вносите свой вклад, сообщая о проблемах, предлагая функции или улучшая код.

### 📌 Поддержка
Для помощи и устранения неполадок посетите наши [GitHub Issues](https://github.com/Babyhamsta/Yoable/issues) или присоединяйтесь к нашему сообществу.

---

## 简体中文

[English](#english) | [日本語](#日本語) | [Русский](#русский) | **简体中文**

### 🚀 功能特性

- **AI 驱动的自动标注** - 使用 **YOLO v5/v8/v11 (ONNX)** 模型自动检测对象。
- **🗺️ 模型类别映射** - 将模型类别 ID 映射到项目的类别 ID，允许您使用具有不同类别结构的预训练模型。可以将不需要的类别设置为 "nan" 来过滤它们。
- **🌐 多语言支持** - 完整的界面翻译支持 **English**、**日本語 (Japanese)**、**Русский (Russian)**、**简体中文 (Simplified Chinese)** 和 **繁體中文 (Traditional Chinese)**。无需重启应用程序即可随时切换语言。
- **⌨️ 自定义快捷键** - 为常用操作（保存项目、图片导航、标签移动等）完全自定义键盘快捷键。
- **🔍 类别过滤** - 使用复选框按类别标签过滤图片。快速查找包含特定类别或类别组合的图片。
- **手动标注工具** - 轻松添加、编辑和删除边界框。
- **批量图片导入** - 一次性加载多张图片。
- **YOLO 标签格式支持** - 以 **YOLO 格式**导入和导出标注。
- **可选云端上传** - 导出时选择上传已标注的数据集，为更好的模型做出贡献。
- **可自定义界面** - 浅色/深色主题和可自定义的标签外观。
- **十字准线叠加** - 精确对齐标注。
- **可调节 AI 置信度** - 设置检测置信度阈值以获得更好的准确性。
- **自动更新** - 通过内置更新检查获取最新功能和修复。（可通过设置禁用）
- **项目支持** - 创建和保存项目，让您可以随时继续之前的工作。

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

#### 自定义快捷键
- 从菜单打开 **设置**
- 导航到 **键盘快捷键** 部分
- 点击任何操作按钮来设置自定义快捷键
- 按下您想要的按键组合（例如：`Ctrl + S`、`A`、`D` 等）
- 按 **Escape** 取消快捷键录制
- 支持的操作：
  - **保存项目** - 默认：`Ctrl + S`
  - **上一张图片** - 默认：`A`
  - **下一张图片** - 默认：`D`
  - **向上移动标签** - 默认：`上方向键`
  - **向下移动标签** - 默认：`下方向键`
  - **向左移动标签** - 默认：`左方向键`
  - **向右移动标签** - 默认：`右方向键`

#### 按类别过滤
- 在过滤面板中展开 **类别过滤** 部分
- 使用复选框选择要过滤的类别
- 将显示包含所选类别标签的图片
- 选择所有类别或清除所有选择以显示所有图片
- 类别过滤器可与状态过滤器（全部、审查、无标签、已完成）组合使用

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
   - 将类别设置为 **"nan"** 以跳过不需要的类别检测
   - 如果可用，自定义类别名称会自动从模型元数据中检测
4. **应用映射**：在使用 AI 自动标注时，映射会自动应用。

#### 优势

- **灵活集成**：使用具有不同类别结构的预训练模型
- **选择性检测**：通过将不相关的类别设置为 "nan" 来忽略它们
- **类别合并**：将多个模型类别映射到单个项目类别

### 🌐 多语言支持

Yoable 支持 **多种语言**，以提供更好的用户体验。您可以随时通过设置切换语言。

#### 支持的语言

- **English (US)** - 默认语言
- **日本語 (Japanese)**
- **Русский (Russian)**
- **简体中文 (Simplified Chinese)**
- **繁體中文 (Traditional Chinese)**

#### 如何更改语言

1. 从菜单打开 **设置**
2. 导航到 **语言** 部分
3. 从下拉菜单中选择您首选的语言
4. 界面将立即更新

#### 语言功能

- **完整界面翻译**：所有菜单、按钮和对话框都已翻译
- **持久化设置**：您的语言偏好会自动保存
- **动态切换**：无需重启应用程序即可更改语言

### 🌍 贡献
Yoable 是 **开源** 的！通过报告问题、建议功能或改进代码来做出贡献。

### 📌 支持
如需帮助和故障排除，请访问我们的 [GitHub Issues](https://github.com/Babyhamsta/Yoable/issues) 或加入我们的社区。

---

⭐ **Star this repo if you find it useful!** / **役に立ったらこのリポジトリにスターを付けてください！** / **Поставьте звезду этому репозиторию, если он вам полезен!** / **如果觉得有用，请给这个仓库点个星！**
