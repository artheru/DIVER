# Prompt for Next AI Session - Coralinker DIVER Host

## Context
You are continuing work on `{workspaceFolder}\3rd\CoralinkerHost`, an ASP.NET Core web application (port 4499) that provides a visual node-graph editor for managing MCU logic compilation. The UI is **fully offline** with local libraries.

## Current Status (2026-01-20)

### ✅ Recently Completed Features

#### Variable Control System
- **Direct Variable Modification**: Click ✎ button on controllable variables to edit inline
- **Controllable Detection**: Variables not declared as `LowerIO` by any node are controllable
- **Type-aware Input**: Supports int, float, string, byte[] (hex input)
- **Integer Rounding**: Int types automatically rounded to nearest integer
- **Stable DOM**: Variable list built once at Connect, only values update (no DOM flickering during edit)
- **Edit Protection**: Currently edited variable won't be overwritten by SignalR updates

#### Dedicated Control Panel (`/controlPanel`)
- **Separate Page**: Clean, standalone remote control interface
- **Widget Types**:
  - Single Slider (H/V, auto-return/hold, unidirectional/bidirectional, linear/log)
  - Joystick (2D cross slider with X/Y variable binding)
  - Switch (2-state: 0/1, or 3-state: -1/0/1)
- **Persistence**: Layout saved to localStorage, accessible from main page
- **Logarithmic Fix**: Handles edge cases (min=0) without NaN

#### Per-Node Log Panels
- **Tabbed Interface**: Terminal + Node1 + Node2... (each node has own tab)
- **Auto-refresh**: New log lines pushed via SignalR in real-time
- **History Loading**: Latest 1000 lines loaded on tab open
- **Separate from Terminal**: Node logs don't pollute main terminal
- **Scrolling**: Fixed overflow for proper scrollbars

#### Node Naming
- **Auto-increment Names**: New nodes get "Node1", "Node2", etc.
- **Editable**: nodeName widget on each node
- **Tab Sync**: Log tab titles update when node renamed

#### UI/UX Improvements
- **Wider Variables Panel**: 600px (was 320px) for long variable names
- **Clear Button**: Moved next to "Synced" indicator for clearing logs
- **Node UI Fix**: propsCount=3 for nodeName/mcuUri/logicName widgets

#### Backend Improvements
- **GetState Timeout**: Sets node status to Offline on timeout
- **mcuUri Loading**: Fixed project load to restore saved mcuUri
- **UTF-8 Terminal**: dotnet build output displays correctly
- **Detailed Logs**: Build steps show file counts, artifact sizes

### ⚠️ Outstanding Issues

#### Architecture
1. **Multi-node LowerIO Testing**: TODO - verify variable control across multiple nodes
2. **Connect/Start Separation**: Connect should only open serial, Start does Config+Program
3. **logicName Dropdown**: Should select from compiled artifacts
4. **mcuUri Dropdown**: Should use SerialPortResolver

#### UI
1. **Native Browser Dialogs**: Delete confirmation still uses `confirm()`
2. **Snapshots**: Digital I/O snapshot display not implemented

---

## Key Technical Details

### Variable Control Flow
```
Frontend (app.js)                    Backend (ApiRoutes.cs)
─────────────────                    ─────────────────────
showVarEdit() →                      POST /api/variable/set
  inline <input> →                     ↓
  setVariable() →                    Check isLowerIO for all nodes
    POST /api/variable/set →           ↓
    ← {ok:true}                      HostRuntime.SetCartVariable()
    updateVarsValues() skips           ↓
    currently editing cell           ← {ok:true, value:...}
```

### Node Log Flow
```
MCU console_printf() →
  MCUNode.OnConsoleOutput →
    RuntimeSessionService.HandleNodeConsoleOutput() →
      RingBuffer.Add() (per-node cache) →
      TerminalBroadcaster.NodeLogLineAsync() →
        SignalR "nodeLogLine" event →
          Frontend appendNodeLogLine()
```

### Log Tab DOM Structure
```html
<div id="logPanesContainer">
  <div class="logPane active" data-pane="terminal">...</div>
  <div class="logPane" data-node-id="uuid1">...</div>
  <div class="logPane" data-node-id="uuid2">...</div>
</div>
```

---

## File Locations

### Frontend (wwwroot/)
- `app.js` - Main UI logic (~3200 lines)
- `app.css` - Styling (~1100 lines)
- `index.html` - Main page structure
- `controlPanel.html` - Dedicated control panel page
- `controlPanel.js` - Control panel logic
- `lib/` - litegraph.min.js, signalr.min.js, require.min.js

### Backend
- `Web/ApiRoutes.cs` - All API endpoints including variable control
- `Services/ProjectStore.cs` - Project management
- `Services/FileTreeService.cs` - Tree generation
- `Services/DiverBuildService.cs` - C# → MCU compilation
- `Services/RuntimeSessionService.cs` - Node management, log caching
- `Services/TerminalBroadcaster.cs` - SignalR broadcasting

### Data (runtime, gitignored)
- `data/project.json` - Node graph state
- `data/assets/inputs/` - User .cs files
- `data/assets/generated/latest/` - Build outputs
- `data/builds/` - Temporary build directories

---

## API Endpoints

### Variable Control
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/variable/set` | POST | Set controllable variable value |
| `/api/variables/controllable` | GET | List all controllable variables |

### Node Logs
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/logs/nodes` | GET | List all nodes with IDs/names |
| `/api/logs/node/{nodeId}` | GET | Get paginated log history |

### Existing
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/project` | GET/POST | Project state |
| `/api/files/tree` | GET | Asset tree |
| `/api/build` | POST | Compile logic |
| `/api/run` | POST | Execute on MCU |

---

## Quick Commands

```powershell
# Start server
cd D:\Documents\Coral\DIVER\3rd\CoralinkerHost
dotnet run --project CoralinkerHost.csproj -c Debug

# Kill server
Get-Process -Name "dotnet","CoralinkerHost" -ErrorAction SilentlyContinue | Stop-Process -Force

# Test API
curl http://localhost:4499/api/variables/controllable
curl http://localhost:4499/api/logs/nodes
```

---

## Testing Checklist

### Variable Control
- [ ] Connect to MCU, variables appear with correct types
- [ ] Click ✎ on controllable variable, inline edit appears
- [ ] Type new value, press Enter → value sent to backend
- [ ] Press Escape → edit cancelled
- [ ] Non-controllable variables show 🔒 icon
- [ ] Editing variable doesn't get overwritten by SignalR updates

### Control Panel
- [ ] Open /controlPanel from Variables panel link
- [ ] Add slider widget, bind to variable
- [ ] Drag slider → variable updates
- [ ] Save layout, refresh page → layout restored
- [ ] Logarithmic mode works without NaN

### Node Logs
- [ ] Each connected node gets its own tab
- [ ] Clicking tab shows that node's logs
- [ ] New log lines appear automatically
- [ ] Scrollbar works for overflow
- [ ] Clear button clears active tab only

---

## Summary for Next Session

**Goal**: Continue polishing UI and implement remaining features

**Completed This Session**:
1. ✅ Variable control with inline editing
2. ✅ Dedicated control panel page with widgets
3. ✅ Per-node log panels with tabs
4. ✅ Node naming with auto-increment
5. ✅ Various UI fixes (scrolling, layout, encoding)

**Priority Tasks**:
1. Replace native `confirm()` dialogs with custom styled dialogs
2. Implement Connect/Start/Stop separation
3. Add logicName dropdown from compiled artifacts
4. Add mcuUri dropdown from SerialPortResolver
5. Multi-node LowerIO variable control testing

**Server**: http://localhost:4499
**Control Panel**: http://localhost:4499/controlPanel
**Codebase**: D:\Documents\Coral\DIVER\3rd\CoralinkerHost
