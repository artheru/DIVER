# Coralinker DIVER Host - Implementation Complete

## Date: 2026-01-01

## Summary of Work Completed

### **Core Functionality**
✅ Offline web application (no CDN dependencies)
✅ Windows Explorer-style asset tree with expand/collapse
✅ Tabbed interface (Graph + file editors)
✅ Build system integration (C# → MCU binaries)
✅ VS Code-style variables panel
✅ Node graph editor with add/delete/resize
✅ Project save/load/export (ZIP format)
✅ Real-time terminal via SignalR

### **Key Features Implemented**

#### 1. **Assets Management**
- Tree view with Windows Explorer styling (arrows ▸/▾, folder/file emojis)
- Only shows current working files (`latest` folder, no historical builds)
- Drag-and-drop .cs file upload
- Create new input files via dialog
- Delete files with confirmation

#### 2. **Node Graph Editor**
- Root node (PC-based, not removable)
- Coral nodes (MCU-based, with mcuUri, logicName, status)
- DPI-aware canvas rendering (crisp on high-DPI displays)
- Add/delete/resize/connect nodes
- Context menu disabled (no rubbish text)
- "📡 Update FW" button on each node

#### 3. **Code Editing**
- Tabbed interface for multiple files
- Syntax highlighting (basic via textarea)
- Text and Hex view modes
- Hex view with offset and ASCII display
- Save/download/delete operations
- Dirty indicator (*) on modified files

#### 4. **Build System**
- Compiles C# logic to MCU binaries
- Generates: .bin, .bin.json, .diver, .diver.map.json
- Error reporting in terminal
- Build artifacts stored in `generated/latest/`

#### 5. **Variables Panel**
- Shows AsUpperIO variables from .bin.json metadata
- Displays variable name, type, and value
- "Not Running" status when idle
- Editable fields (contenteditable) for AsUpperIO
- Table with borders for clarity

#### 6. **Project Management**
- New Project dialog (Clear and New / Export then New / Cancel)
- Save to server (project.json with node map)
- Export as ZIP (includes project.json + assets + generated)
- Import from local file

### **UI/UX Improvements**
- No blocking modals or prompts
- Custom dialogs for all user input
- Text labels on all toolbar buttons
- Professional dark theme
- Responsive layout
- Clean, uncluttered interface

### **Technical Details**

#### Libraries (Local, in wwwroot/lib/):
- `litegraph.min.js` (0.7.15) - Node graph editor
- `signalr.min.js` (7.0.0) - Real-time communication
- `require.min.js` (2.3.6) - Module loader

#### Backend Services:
- `ProjectStore` - Project state management
- `FileTreeService` - Asset tree generation
- `DiverBuildService` - C# → MCU compilation
- `RuntimeSessionService` - Execution management
- `TerminalBroadcaster` - Real-time terminal updates

#### API Endpoints:
- `/api/project` - GET/POST project state
- `/api/project/new` - Create new project (clears assets)
- `/api/project/save` - Persist to disk
- `/api/project/export` - Download as ZIP
- `/api/files/tree` - Get asset tree
- `/api/files/read` - Read file content
- `/api/files/write` - Save file
- `/api/files/delete` - Delete file
- `/api/files/newInput` - Create new input file
- `/api/build` - Compile logic
- `/api/run` - Execute on MCU
- `/api/stop` - Stop execution

### **Known Limitations / Future Enhancements**

1. **Monaco Editor**: Disabled for offline mode (using textarea fallback)
2. **AsLowerIO Variables**: Not shown (only in .bin.json for AsUpperIO)
3. **Variable Editing**: contenteditable in place, but no send-to-MCU yet
4. **Multi-file Build**: Currently builds selected file only (not all .cs files)
5. **Data Directory**: In ContentRootPath for dev, should be in executable dir for production

### **Testing Completed**

✅ New project creation
✅ Add/delete nodes
✅ File editing and saving
✅ Invalid script build (error reporting)
✅ Valid script build (TestLogic.cs)
✅ Variable extraction from .bin.json
✅ Tab switching between files
✅ Export as ZIP
✅ Tree navigation
✅ Only 1 root node maintained

### **Files Modified**

**Frontend:**
- `wwwroot/app.js` - Main application logic
- `wwwroot/index.html` - UI structure
- `wwwroot/app.css` - Styling

**Backend:**
- `Services/FileTreeService.cs` - Tree generation with logging
- `Services/ProjectStore.cs` - Project management
- `Web/ApiRoutes.cs` - API endpoints including ZIP export

**Assets:**
- `wwwroot/lib/` - Local copies of all libraries

### **Deployment Notes**

For customer deployment:
1. Build in Release mode
2. Copy `bin/Release/net8.0/` folder
3. Ensure `data/` folder is writable
4. Run `CoralinkerHost.exe`
5. Access at `http://localhost:4499`

No internet connection required - fully offline application.

---

## Status: Implementation Complete

All requested features have been implemented and tested. The application is functional and ready for use.

