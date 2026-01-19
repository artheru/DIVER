# Coralinker DIVER Host - AI Development Guide

## Project Overview
ASP.NET Core web application (port 4499) providing a visual node-graph editor for managing distributed MCU logic compilation and deployment. **Fully offline** - no CDN dependencies.

## Directory Structure
```
3rd/CoralinkerHost/
├── wwwroot/               # Frontend assets
│   ├── app.js            # Main UI logic (~3200 lines)
│   ├── app.css           # Styling (~1100 lines)
│   ├── index.html        # Main page structure
│   ├── controlPanel.html # Dedicated control panel page
│   ├── controlPanel.js   # Control panel widget logic
│   └── lib/              # Local libraries (LiteGraph, SignalR)
├── Services/             # Backend services
│   ├── ProjectStore.cs           # Project state management
│   ├── FileTreeService.cs        # Asset tree generation
│   ├── DiverBuildService.cs      # C# → MCU compilation
│   ├── RuntimeSessionService.cs  # Node management, log caching
│   └── TerminalBroadcaster.cs    # Real-time SignalR updates
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
  - `nodeName`: Display name (auto-increment: Node1, Node2...)
  - `mcuUri`: Serial connection string (e.g., "serial://name=COM3&baudrate=1000000")
  - `logicName`: Name of C# class to execute
  - `status`: Idle/Connecting/Running/Error/Offline
  - "📡 Update FW" button

### 2. **Build Pipeline**
User writes C# logic → DiverCompiler → MCU binary (.bin) + metadata (.bin.json, .diver, .diver.map.json)

**Important**: `.bin.json` contains variable metadata with flags:
- `0x01` = UpperIO (MCU → Host)
- `0x02` = LowerIO (Host → MCU, controlled by MCU)
- `0x00` = Mutual (controllable by Host if not LowerIO by any node)

### 3. **Variable Types (typeid mapping)**
| typeid | Type | Notes |
|--------|------|-------|
| 0 | bool | |
| 1 | byte | |
| 2 | sbyte | |
| 3 | short | |
| 4 | ushort | |
| 5 | int | |
| 6 | uint | |
| 7 | long | |
| 8 | ulong | |
| 9 | float | |
| 10 | double | |
| 11 | char | |
| 12 | string | |
| 16 | int[] | |
| 17 | byte[] | Hex input in UI |
| 18 | float[] | |

### 4. **Variable Control**
- **Controllable**: Variable not declared as `LowerIO` by any child node
- **Non-controllable**: Variable declared as `LowerIO` (MCU controls it)
- **`__iteration`**: Always node-specific LowerIO, never controllable
- **UI**: ✎ button for controllable, 🔒 icon for non-controllable

### 5. **UI Architecture**
- **Tab System**: Graph tab (always visible) + File tabs (closable)
- **Log Tabs**: Terminal + Node1 + Node2... (per-node logs)
- **Assets Panel**: Windows Explorer-style tree (emojis: 📂📁📄📦)
- **Variables Panel**: 600px wide, shows all variables with edit/lock icons
- **Control Panel**: Separate page (/controlPanel) with sliders, joysticks, switches

---

## API Endpoints Reference

### Variable Control
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/variable/set` | POST | Set controllable variable (name, value, typeHint) |
| `/api/variables/controllable` | GET | List all controllable variables |

### Node Logs
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/logs/nodes` | GET | List nodes with IDs and names |
| `/api/logs/node/{nodeId}?offset=0&limit=1000` | GET | Paginated log history |

### Project
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/project` | GET | Get current project state |
| `/api/project` | POST | Update project state |
| `/api/project/save` | POST | Persist to disk |
| `/api/project/new` | POST | Create new (clears assets) |
| `/api/project/export` | GET | Download ZIP |

### Files
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/files/tree` | GET | Get asset tree (FileNode structure) |
| `/api/files/read?path=` | GET | Read file (text or base64) |
| `/api/files/write` | POST | Save file |
| `/api/files/delete` | POST | Delete file |
| `/api/files/newInput` | POST | Create new .cs file |

### Build/Run
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/build` | POST | Compile selected asset |
| `/api/run` | POST | Execute on MCU |
| `/api/stop` | POST | Stop execution |

---

## SignalR Events

### Terminal Hub (`/hubs/terminal`)
| Event | Direction | Data |
|-------|-----------|------|
| `terminalLine` | Server→Client | `string line` |
| `nodeLogLine` | Server→Client | `string nodeId, string line` |
| `variables` | Server→Client | Variable snapshot object |

---

## Frontend State Variables (app.js)

```javascript
// Core
graph           // LiteGraph instance
canvas          // LGraphCanvas instance

// Tabs
openTabs        // Array of file tabs
activeTabId     // Currently selected tab

// Files
currentFile     // Currently loaded file
currentArtifact // Build artifact metadata
selectedAssetName // Selected .cs file for build

// Variables
varsTableBuilt  // Boolean: has variable table been built?
editingVarName  // String: currently editing variable name (null if not editing)
controllableVars // Set of controllable variable names

// Logs
activeLogTab    // "terminal" or node UUID
nodeLogPanes    // Map<nodeId, {element, content, status, lineCount}>
```

---

## Backend Services (DI)

| Service | Scope | Purpose |
|---------|-------|---------|
| `ProjectStore` | Singleton | Manages project.json |
| `FileTreeService` | Scoped | Generates asset tree |
| `DiverBuildService` | Scoped | Runs build pipeline |
| `RuntimeSessionService` | Singleton | Node management, log caching |
| `TerminalBroadcaster` | Singleton | SignalR hub for terminal/logs |

### RuntimeSessionService Key Methods
- `AddNode(nodeId, mcuUri)` - Creates node with log buffer
- `HandleNodeConsoleOutput(nodeId, message)` - Routes to RingBuffer + SignalR
- `GetNodeLogHistory(nodeId, offset, limit)` - Paginated log retrieval

---

## Development Tips

### **Running the Server:**
```powershell
cd D:\Documents\Coral\DIVER\3rd\CoralinkerHost
dotnet run --project CoralinkerHost.csproj -c Debug
# Access: http://localhost:4499
# Control Panel: http://localhost:4499/controlPanel
```

### **Debugging Frontend:**
- Open DevTools (F12) → Console tab for errors
- Check Network tab for failed API calls
- Terminal panel in UI shows backend logs via SignalR

### **Key Files to Modify:**
- **Variable types**: `wwwroot/app.js` → `getTypeNameFromId()`
- **Variable editing**: `wwwroot/app.js` → `showVarEdit()`, `setVariable()`
- **Control panel**: `wwwroot/controlPanel.js` → widget creation
- **Log tabs**: `wwwroot/app.js` → `ensureNodeLogTab()`, `appendNodeLogLine()`
- **Backend variable API**: `Web/ApiRoutes.cs` → `/api/variable/set`

### **Common Patterns:**
```javascript
// Showing modal dialog
byId("myDialog").classList.remove("hidden");

// Hiding modal dialog  
byId("myDialog").classList.add("hidden");

// Logging to terminal
logTerminal("[ui] Message here");

// API call
const result = await api("GET", "/api/endpoint");

// Set variable from frontend
await setVariable("varName", 123, "int");
```

---

## Troubleshooting

### "Variable table keeps flickering"
- Check `varsTableBuilt` flag - should only build table once
- Check `editingVarName` - should skip updating edited cell

### "Node logs not showing"
- Check SignalR connection in DevTools Network tab
- Verify `nodeLogLine` event listener attached
- Check `nodeLogPanes` map has entry for node

### "Control panel layout not saving"
- Check localStorage in DevTools → Application tab
- Verify `saveControlPanelState()` called on changes

### "mcuUri not loading from project"
- Check `loadServerProject()` correctly parses node properties
- Verify `project.json` has correct mcuUri values

### "Node status stuck on stale data"
- `RefreshState()` should set `State = null` on timeout
- Frontend should show "Offline" when state is null

---

## Current Issues & TODO

### Critical
1. **Native browser dialogs** - Replace `confirm()` with custom dialogs
2. **Connect/Start separation** - Connect should only open serial

### Important
3. **logicName dropdown** - Select from compiled artifacts in `generated/latest/`
4. **mcuUri dropdown** - Use SerialPortResolver for port discovery
5. **Multi-node LowerIO testing** - Verify variable control across nodes

### Nice to Have
6. **Digital I/O snapshots** - LED display for 16 inputs + 16 outputs
7. **Better hex editor** - Data inspector, insert/delete bytes

---

**Last Updated**: 2026-01-20
**Status**: Variable control complete, control panel implemented, per-node logs working
