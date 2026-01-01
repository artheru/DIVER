# Coralinker DIVER Host - AI Development Guide

## Project Overview
ASP.NET Core web application (port 4499) providing a visual node-graph editor for managing distributed MCU logic compilation and deployment. **Fully offline** - no CDN dependencies.

## Directory Structure
```
3rd/CoralinkerHost/
├── wwwroot/               # Frontend assets
│   ├── app.js            # Main UI logic (1000+ lines)
│   ├── app.css           # Styling
│   ├── index.html        # UI structure
│   └── lib/              # Local libraries (LiteGraph, SignalR)
├── Services/             # Backend services
│   ├── ProjectStore.cs           # Project state management
│   ├── FileTreeService.cs        # Asset tree generation
│   ├── DiverBuildService.cs      # C# → MCU compilation
│   ├── RuntimeSessionService.cs  # Execution management
│   └── TerminalBroadcaster.cs    # Real-time updates
├── Web/
│   └── ApiRoutes.cs      # REST API endpoints
└── data/                 # Runtime data (gitignored)
    ├── project.json      # Node graph + state
    ├── assets/
    │   ├── inputs/       # User .cs files
    │   └── generated/    # Build outputs
    │       └── latest/   # Current build artifacts
    └── builds/           # Temp build directories
```

## Key Concepts

### 1. **Node Graph**
- **Root Node**: PC-based anchor (1 per project, not removable)
- **Coral Nodes**: MCU-based logic executors with:
  - `mcuUri`: Serial connection string (e.g., "serial://COM3,2000000")
  - `logicName`: Name of C# class to execute
  - `status`: Idle/Connecting/Running/Error
  - "📡 Update FW" button

### 2. **Build Pipeline**
User writes C# logic → DiverCompiler → MCU binary (.bin) + metadata (.bin.json, .diver, .diver.map.json)

**Important**: `.bin.json` contains **AsUpperIO** variables only (MCU outputs). Format:
```json
[{"field":"prim_b", "typeid":0, "offset":0}, {"field":"arr_send", "typeid":16, "offset":2}]
```

### 3. **Variable Types (typeid mapping)**
| typeid | Type | Notes |
|--------|------|-------|
| 0 | bool | |
| 5 | int | |
| 6 | uint | |
| 9 | float | |
| 12 | string | **Check actual typeid in .bin.json** |
| 16 | int[] | |
| 17 | byte[] | |
| 18 | float[] | |

**ISSUE**: Type mapping may be incorrect - verify with actual .bin.json output from DiverCompiler.

### 4. **UI Architecture**
- **Tab System**: Graph tab (always visible) + File tabs (closable)
- **Assets Panel**: Windows Explorer-style tree (emojis: 📂📁📄📦)
- **Variables Panel**: VS Code watch-style (⬆ for AsUpperIO, ⬇ for AsLowerIO)
- **Terminal**: Real-time build/run output via SignalR

## Current Issues & TODO

### **Critical Issues:**
1. **Variable Type Mapping**: `str` shows as `int[]` instead of `string`
   - **Root Cause**: Incorrect typeid mapping in `getTypeNameFromId()`
   - **Fix**: Check actual typeids in .bin.json and update mapping

2. **Code Editor**: Using basic textarea (no syntax highlighting, line numbers)
   - **TODO**: Integrate CodeMirror 6 or Ace Editor (download for offline use)
   - **Path**: `wwwroot/lib/codemirror/` or `wwwroot/lib/ace/`

3. **Hex Editor**: Custom implementation incomplete
   - **TODO**: Use hex-editor library or improve custom implementation
   - **Required**: Offset column, hex bytes, ASCII display, editable

4. **Multi-file Build**: Currently only builds selected file
   - **TODO**: Build pipeline should include all .cs files from inputs/
   - **Backend**: Modify `DiverBuildService` to copy all inputs

5. **Command Input**: Send button not functional
   - **TODO**: Wire up `$("cmdSend").onclick` to send command via API

### **Known Bugs:**
- Boot warning: "Failed to load file: Cannot set properties of null" when loading .bin.json at boot
  - **Fix**: Skip .bin.json files in boot file loading logic
- Tab switching may not update editor content properly
  - **Fix**: Ensure `setEditorContent()` called in `switchToTab()`

## API Endpoints Reference

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/project` | GET | Get current project state |
| `/api/project` | POST | Update project state |
| `/api/project/save` | POST | Persist to disk |
| `/api/project/new` | POST | Create new (clears assets) |
| `/api/project/export` | GET | Download ZIP |
| `/api/files/tree` | GET | Get asset tree (FileNode structure) |
| `/api/files/read?path=` | GET | Read file (text or base64) |
| `/api/files/write` | POST | Save file |
| `/api/files/delete` | POST | Delete file |
| `/api/files/newInput` | POST | Create new .cs file |
| `/api/build` | POST | Compile selected asset |
| `/api/run` | POST | Execute on MCU |
| `/api/stop` | POST | Stop execution |

## Testing Checklist

### **Before Declaring Complete:**
- [ ] New project creates only 1 root node
- [ ] Add node, connect nodes, save, reload → state persists
- [ ] Edit file in tab, switch tabs → content correct
- [ ] Build invalid script → error shown in terminal
- [ ] Build valid script → variables extracted with **correct types**
- [ ] Export ZIP → contains project.json + assets + generated
- [ ] Variables panel shows all AsUpperIO variables
- [ ] Hex view shows offset + hex + ASCII
- [ ] Code editor has line numbers and syntax highlighting
- [ ] No console errors, no blocking dialogs
- [ ] Canvas crisp on high-DPI displays

## Development Tips

### **Running the Server:**
```powershell
cd D:\src\DIVER\3rd\CoralinkerHost
dotnet run --project CoralinkerHost.csproj -c Debug --no-launch-profile
# Access: http://localhost:4499
```

### **Debugging Frontend:**
- Open DevTools (F12) → Console tab for errors
- Check Network tab for failed API calls
- Terminal panel in UI shows backend logs via SignalR

### **Key Files to Modify:**
- **Variable types**: `wwwroot/app.js` → `getTypeNameFromId()`
- **Code editor**: `wwwroot/index.html` + `app.js` → integrate library
- **Hex editor**: `wwwroot/app.js` → `bytesToHex()` and `hexToBytes()`
- **Build logic**: `Services/DiverBuildService.cs` (not in CoralinkerHost, check parent dirs)

### **Common Patterns:**
```javascript
// Showing modal dialog
$("myDialog").classList.remove("hidden");

// Hiding modal dialog  
$("myDialog").classList.add("hidden");

// Logging to terminal
logTerminal("[ui] Message here");

// API call
const result = await api("GET", "/api/endpoint");
```

## Notes for Next AI Session

### **User Expectations:**
- Professional, modern UI (like VS Code / Windows Explorer)
- Fully offline operation (all libs local)
- Clean, bug-free workflows
- Proper error handling and logging
- No browser prompts/alerts (custom dialogs only)

### **Current State:**
- Core functionality working
- Some type mapping issues remain
- Code/Hex editors need improvement
- Multi-file build not implemented

### **Architecture Decisions:**
- LiteGraph for node editor (cannot be easily replaced)
- SignalR for real-time updates (works well)
- Textarea for code editing (Monaco disabled for offline - **consider CodeMirror**)
- Custom hex editor (functional but basic - **consider library**)

## Quick Reference

### **Frontend State Variables:**
- `graph` - LiteGraph instance
- `canvas` - LGraphCanvas instance
- `openTabs` - Array of file tabs
- `activeTabId` - Currently selected tab
- `currentFile` - Currently loaded file
- `currentArtifact` - Build artifact metadata
- `selectedAssetName` - Selected .cs file for build

### **Backend Services (DI):**
- `ProjectStore` - Singleton, manages project.json
- `FileTreeService` - Scoped, generates asset tree
- `DiverBuildService` - Scoped, runs build pipeline
- `RuntimeSessionService` - Singleton, manages execution
- `TerminalBroadcaster` - Singleton, SignalR hub

## Troubleshooting

### "Graph not loading"
- Check if LiteGraph CDN failed → Verify `wwwroot/lib/litegraph.min.js` exists
- Check console for "LiteGraph library not loaded"

### "Multiple root nodes"
- Check `data/project.json` for duplicate root entries
- Ensure `loadServerProject()` clears graph before loading

### "Variables wrong type"
- Check `.bin.json` file for actual typeid values
- Update `getTypeNameFromId()` mapping

### "Build errors"
- Check terminal output for MSBuild errors
- Verify DiverCompiler.exe exists in parent directories
- Check `data/builds/` for build logs

---

**Last Updated**: 2026-01-01
**Status**: Core features implemented, refinements needed

