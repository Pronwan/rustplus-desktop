# RustPlus Desktop - Refactoring & Architecture Plan

This document outlines the strategy for reorganizing the RustPlus Desktop project to improve maintainability, scalability, and code clarity.

## 1. Current State Assessment

### 🏗 Architecture
- **Paradigm**: Hybrid MVVM. While a `MainViewModel` exists, significant logic remains in `MainWindow.xaml.cs` and its partial classes.
- **Monolith**: `MainWindow.xaml.cs` is over 5,000 lines long. Even with partial class splitting, it remains tightly coupled to the UI.
- **Componentization**: Logical sections (Devices, Map, Camera) are separated into files but still belong to the `MainWindow` class, making them hard to test or reuse.

### 📂 Organization
- **Root Directory**: Overcrowded with mixed concerns (Models, Services, Windows, Scripts, and Assets).
- **Inconsistency**: Some converters are in a folder, others are nested in classes. Some models are in `Models/`, others in the root.

---

## 2. Refactoring Progress

### ✅ Completed
- **MainWindow Partial Splitting**: Logic for `Overlay`, `Devices`, `Connection`, `Team`, `Map`, and `Camera` has been moved to `Views/MainWindow/`.
- **Initial Folder Structure**: `Models/`, `Views/`, `Services/`, and `Converters/` folders exist.
- **Service Extraction**: Core logic for FCM listening (`PairingListenerRealProcess`) and Rust+ communication (`RustPlusClientReal`) is in dedicated classes.

### ⏳ Still Needs Refactoring
- **MVVM Implementation**: Move logic from `MainWindow.xaml.cs` event handlers into `MainViewModel` commands.
- **UserControls**: Convert `MainWindow` logical sections from partial classes into actual `UserControl` components (e.g., `DevicesTabControl`).
- **Dependency Injection**: Use a service provider to manage service lifetimes instead of manual instantiation in the View.

### ✅ Completed
- **Cleanup Root**: Moved all non-View files to their respective folders:
  - Window files → `Views/Windows/` (12 windows)
  - Image assets → `Assets/Images/` (20+ images)
  - Python scripts → `Scripts/` (3 files)
  - Installer config → `Installer/` (1 file)
  - Updated `MainWindow.xaml` image path reference
  - Fixed `.csproj` file

---

## 3. Proposed Folder Structure

A clean, industry-standard WPF structure:

```text
RustPlusDesktop/
├── Assets/                 # Static resources
│   ├── Icons/              # .ico and .png icons
│   ├── Images/             # Backgrounds, UI images
│   ├── Sounds/             # Alert sounds (.wav)
│   └── Data/               # JSON files (item lists, etc.)
├── Converters/             # XAML Value Converters
├── Models/                 # Data entities (DTOs, Database models)
├── Services/               # Logic, API Clients, External integrations
│   ├── Interfaces/         # IService definitions
│   └── Implementations/    # Concrete service classes
├── ViewModels/             # Application state and Command logic
│   ├── Base/               # ViewModelBase, RelayCommand
│   └── Components/         # ViewModels for specific UserControls
├── Views/                  # UI Components
│   ├── Windows/            # Actual Window objects
│   ├── Controls/           # Reusable UserControls (Devices, Map, etc.)
│   └── Styles/             # XAML ResourceDictionaries (Themes, Brushes)
├── Utils/                  # Static helpers, extensions, global constants
└── Scripts/                # Python scripts and external tools
```

---

## 4. Step-by-Step Action Plan

### Phase 1: Physical Reorganization
1. **Move Models**: Relocate `SmartDevice.cs`, `ServerProfile.cs`, `TeamChatMessage.cs`, etc., to `/Models`.
2. **Move Services**: Relocate `SteamLoginService.cs`, `StorageService.cs`, `TrackingService.cs`, etc., to `/Services`.
3. **Move Assets**: Create `/Assets` and move all images, icons, and sounds there. Update XAML URIs accordingly.
4. **Move ViewModels**: Move `ViewModel.cs` to `/ViewModels/MainViewModel.cs`.

### Phase 2: Decoupling MainWindow
1. **Create UserControls**: Create `DevicesView.xaml`, `MapView.xaml`, etc., in `/Views/Controls`.
2. **Transfer Logic**: Move the logic from `MainWindow.Devices.cs` into the code-behind or ViewModel of the new `DevicesView`.
3. **Simplify MainWindow**: `MainWindow.xaml` should eventually just be a shell containing these controls.

### Phase 3: Pure MVVM & DI
1. **RelayCommand**: Implement a standard `RelayCommand` to handle UI clicks in the ViewModel.
2. **Service Provider**: Initialize services in `App.xaml.cs` and pass them to ViewModels via constructor injection.
3. **Event Aggregator**: Use a pub/sub pattern for communication between components (e.g., "Server Changed" event) instead of direct method calls.

---

## 5. Priorities for Next Session
1. **Extract Map Logic**: The Map logic is the most complex; turning it into a standalone `UserControl` will significantly clean up `MainWindow`.
2. **Extract Devices Logic**: Create a `DevicesView` UserControl from the partial class logic.
3. **Extract Team Logic**: Create a `TeamView` UserControl from the partial class logic.
