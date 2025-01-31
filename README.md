# Yoable - AI-Assisted Image Labeling Tool 🖼️🤖

Yoable is a **powerful yet simple image labeling tool** built in **C# .NET 8.0**, designed for **bounding box annotation** and **AI-assisted auto-labeling**. It supports manual labeling as well as **YOLOv5 & YOLOv8 ONNX models** for automatic object detection. 

---

## **✨ Features**
### **📂 Image Management**
- **Import entire directories** or individual images (`.jpg`, `.png`).
- **Displays images in a list** with preview support.
- **Scalable UI layout** for easy navigation.

### **🏷️ Labeling System**
- **Manual bounding box creation** with resizable edges.
- **Click & drag labels** to reposition them.
- **Supports deleting labels** with the `Delete` key.
- **Keyboard arrow support** to move labels precisely.

### **🖥️ AI-Assisted Labeling (YOLO ONNX)**
- **Supports YOLOv5 & YOLOv8 ONNX models** with **DirectML (GPU) or CPU**.
- **Auto Label**: Runs AI on all images and applies detections as labels.
- **Auto Suggest**: Suggests labels that require user confirmation before adding.

### **🔄 Dynamic Model Handling**
- **Supports multiple ONNX input sizes** (`640x640`, `1280x1280`, etc.).
- **Automatically detects YOLO version** based on ONNX output structure.
- **Runs on DirectML (GPU) when available**, otherwise falls back to CPU.

### **💾 Label Export & YOLO Format**
- **Exports labels in YOLO format** (`.txt` with normalized coordinates).
- **Allows users to select output directories** for saving labels.

---

## **🛠️ Installation & Setup**
### **🔹 Prerequisites**
- **.NET 8.0 SDK** (https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- **Windows 10/11** (for DirectML support)
- **A Pretrained YOLO ONNX Model**

### **🔹 How to Run**
1. **Clone the Repository**
   ```sh
   git clone https://github.com/Babyhamsta/Yoable.git
   cd yoable
   ```

2. Build & Run
   ```sh
   dotnet build
   dotnet run
   ```

3. Load Images & Models
Import images via File > Import Directory / Import Image.
Load YOLO model via Auto Label / Auto Suggest.

## 🖥️ **Usage Guide**
### 🏷️ **Creating & Managing Labels**
Left-click + drag: Draws a new bounding box.
Click on a label: Selects and highlights it.
Resize using corner handles.
Move with arrow keys.
Press Delete to remove a label.

### 🤖 AI-Assisted Labeling
Auto Label: Runs AI detection and auto-applies labels.
Auto Suggest: Shows detections for approval before applying.
Supports YOLOv5 & YOLOv8 ONNX models with automatic shape detection.

### 💾 Exporting Labels
Click File > Export Labels.
Labels are saved in YOLO .txt format with image-aligned names.

## 📜 License
GNU General Public License v3.0
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but without any warranty; without even the implied warranty of merchantability or fitness for a particular purpose. See the GNU General Public License for more details.

📜 You can read the full license [here](https://github.com/Babyhamsta/Yoble/blob/master/LICENSE.txt).

## 🤝 Contributing
Want to improve Yoable? Fork the repo and submit a PR!

For issues or feature requests, submit a GitHub Issue.
