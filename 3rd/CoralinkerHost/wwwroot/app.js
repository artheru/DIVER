/* global LGraph, LGraphCanvas, LiteGraph, signalR */

// IMPORTANT: Do NOT use `$` as a global binding - Visual Studio BrowserLink assumes `$` is jQuery.
const byId = (id) => document.getElementById(id);

function logTerminal(line) {
  const term = byId("terminal");
  const div = document.createElement("div");
  div.textContent = line;
  term.appendChild(div);
  term.scrollTop = term.scrollHeight;
}

// --- Generic modal dialog ---
let _dialogState = { onOk: null };
function showGenericDialog(title, bodyHtml, onOk, okText = "OK") {
  const dlg = byId("genericDialog");
  const t = byId("genericDialogTitle");
  const b = byId("genericDialogBody");
  const ok = byId("genericDialogOk");
  const cancel = byId("genericDialogCancel");
  if (!dlg || !t || !b || !ok || !cancel) {
    // Fallback
    const res = confirm(title + "\n\n" + String(bodyHtml || "").replaceAll(/<[^>]+>/g, ""));
    if (res && typeof onOk === "function") onOk();
    return;
  }

  t.textContent = title || "Dialog";
  b.innerHTML = bodyHtml || "";
  ok.textContent = okText || "OK";

  _dialogState.onOk = onOk || null;
  dlg.classList.remove("hidden");

  const close = () => {
    dlg.classList.add("hidden");
    _dialogState.onOk = null;
  };

  ok.onclick = () => {
    const fn = _dialogState.onOk;
    close();
    try { fn?.(); } catch (e) { console.warn("Dialog onOk failed", e); }
  };
  cancel.onclick = close;
  dlg.onclick = (ev) => {
    if (ev.target === dlg) close();
  };
}

function fmtBytes(n) {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  return `${(n / (1024 * 1024)).toFixed(1)} MB`;
}

async function connectSession() {
  const res = await api("POST", "/api/connect");
  if (res?.nodes) applyNodeRuntimeInfo(res.nodes);
  logTerminal("[ui] Connect requested.");
}

function applyNodeRuntimeInfo(nodes) {
  if (!graph || !Array.isArray(nodes)) return;
  const byId = new Map(nodes.map((n) => [String(n.nodeId), n]));
  for (const node of graph._nodes || []) {
    const info = byId.get(String(node.id));
    if (!info) continue;
    applyNodeVersionInfo(node, info.version || null);
    node.setDirtyCanvas(true, true);
  }
}

function applyNodeVersionInfo(node, version) {
  if (!node || !version) return;
  node.properties.versionInfo = {
    productionName: version.productionName || "",
    gitTag: version.gitTag || "",
    gitCommit: version.gitCommit || "",
    buildTime: version.buildTime || "",
  };
  node.properties.version = version.gitTag || "";
  node.properties.info = version.productionName || "";
}

function applyNodeSnapshot(snap) {
  if (!graph || !snap || !Array.isArray(snap.nodes)) return;
  const byNodeId = new Map(snap.nodes.map((n) => [String(n.nodeId), n]));
  for (const node of graph._nodes || []) {
    const info = byNodeId.get(String(node.id));
    if (!info) continue;
    // Update state fields
    if (info.runState) node.properties.runState = String(info.runState);
    node.properties.isConfigured = !!info.isConfigured;
    node.properties.isProgrammed = !!info.isProgrammed;
    node.properties.mode = info.mode || "Unknown";
    // Update version info
    if (info.version) applyNodeVersionInfo(node, info.version);
    // Update layout info
    if (info.layout) node.properties.layout = info.layout;
    // Update ports
    if (Array.isArray(info.ports)) node.properties.ports = info.ports;
    node.setDirtyCanvas(true, true);
  }
}

async function api(method, path, body) {
  const res = await fetch(path, {
    method,
    headers: body && !(body instanceof FormData) ? { "Content-Type": "application/json" } : undefined,
    body: body ? (body instanceof FormData ? body : JSON.stringify(body)) : undefined,
  });
  if (!res.ok) {
    const ct = res.headers.get("content-type") || "";
    if (ct.includes("application/json")) {
      const j = await res.json().catch(() => null);
      throw new Error(`${method} ${path} failed: ${res.status} ${j?.error || JSON.stringify(j)}`);
    }
    const t = await res.text();
    throw new Error(`${method} ${path} failed: ${res.status} ${t}`);
  }
  const ct = res.headers.get("content-type") || "";
  if (ct.includes("application/json")) return res.json();
  return res.text();
}

// --- Node Editor (LiteGraph) ---

let graph;
let CoralNode, RootNode;
let canvas;
let booting = true;

let _syncState = "synced"; // "synced" | "syncing" | "error"
function setSyncIndicator(state, title) {
  _syncState = state;
  const el = byId("syncIndicator");
  if (!el) return;
  el.classList.remove("syncSynced", "syncSyncing", "syncError");
  if (state === "syncing") {
    el.classList.add("syncSyncing");
    el.textContent = "Syncing";
  } else if (state === "error") {
    el.classList.add("syncError");
    el.textContent = "Sync Error";
  } else {
    el.classList.add("syncSynced");
    el.textContent = "Synced";
  }
  if (title) el.title = title;
}

let _autoSaveTimer = null;
function scheduleAutoSave(reason = "") {
  // Avoid spamming saves during initial boot/configure
  if (booting) return;
  if (_autoSaveTimer) clearTimeout(_autoSaveTimer);
  _autoSaveTimer = setTimeout(() => {
    _autoSaveTimer = null;
    saveOnServer({ silent: true, reason: reason || "autosave" }).catch((err) => {
      console.warn("Auto-save failed:", reason, err);
    });
  }, 700);
}

// ---- LiteGraph UX overrides (DPI, inline widget editing, resizing, context menu) ----

function gcd(a, b) {
  a = Math.abs(a | 0);
  b = Math.abs(b | 0);
  while (b) {
    const t = a % b;
    a = b;
    b = t;
  }
  return a || 1;
}

function computePixelPerfectCssSize(rawCssPx, dpr) {
  // We try to choose an integer CSS size such that cssPx * dpr is (very close to) an integer,
  // which avoids fractional scaling blur on fractional DPRs (e.g. 1.25, 1.5).
  const cssPx = Math.max(1, Math.floor(rawCssPx || 0));
  const denom = 8;
  const num = Math.round((dpr || 1) * denom);
  const approx = num / denom;
  if (Math.abs(approx - (dpr || 1)) > 0.01) {
    // DPR is weird (or changing); fall back to plain integer CSS px.
    return { cssPx, num: Math.round((dpr || 1) * 1000), denom: 1000 };
  }
  const step = denom / gcd(num, denom); // e.g. 1.25 => step 4
  const snapped = cssPx - (cssPx % step);
  return { cssPx: Math.max(step, snapped), num, denom };
}

function installLiteGraphDpiScaling(lgCanvas) {
  if (!lgCanvas || !lgCanvas.ds) return;
  if (lgCanvas.ds.__dprPatched) return;

  const ds = lgCanvas.ds;
  const origToCanvasContext = ds.toCanvasContext.bind(ds);

  ds.__dprPatched = true;
  ds.__dpr = window.devicePixelRatio || 1;
  ds.__origToCanvasContext = origToCanvasContext;

  // Add DPR scaling before LiteGraph pan/zoom transform.
  ds.toCanvasContext = function (ctx) {
    const dpr = this.__dpr || 1;
    // Reset to DPR transform first (LiteGraph expects identity here).
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    // Now apply the usual DS transform on top.
    // Keep the same operation order as LiteGraph (scale then translate).
    ctx.scale(this.scale, this.scale);
    ctx.translate(this.offset[0], this.offset[1]);
  };
}

function disableLiteGraphContextMenus(lgCanvas) {
  if (!lgCanvas) return;
  // The stray "Add Node / Add Group" menu is LiteGraph's ContextMenu, not the browser menu.
  // Disable it entirely to keep the UI clean and avoid mispositioned menus.
  lgCanvas.processContextMenu = function () {
    return null;
  };
  if (window.LiteGraph) {
    window.LiteGraph.closeAllContextMenus?.();
  }
}

function installInlinePromptEditor(lgCanvas, graphWrapEl) {
  if (!lgCanvas || !window.LGraphCanvas) return;
  // Patch ONCE globally. Do not stack multiple wrappers (causes fallback to old prompt implementations).
  if (LGraphCanvas.prototype.__inlinePromptPatched) return;
  LGraphCanvas.prototype.__inlinePromptPatched = true;

  const originalPrompt = LGraphCanvas.prototype.prompt;
  LGraphCanvas.prototype.__originalPrompt = originalPrompt;
  const canvasRef = lgCanvas; // do not trust `this` (LiteGraph sometimes calls prompt with odd binding)
  const WIDGET_H = window.LiteGraph?.NODE_WIDGET_HEIGHT ?? 20;
  const WIDGET_GAP = 4;
  const TITLE_H = window.LiteGraph?.NODE_TITLE_HEIGHT ?? 30;

  // Replace LiteGraph's bottom-left "Value / OK" prompt with an in-situ overlay input.
  LGraphCanvas.prototype.prompt = function (title, value, callback, event, multiline) {
    try {
      // Close any existing prompt_box (created by LiteGraph or previous runs).
      if (canvasRef.prompt_box && canvasRef.prompt_box.close) canvasRef.prompt_box.close();
      canvasRef.prompt_box = null;

      const wrap = graphWrapEl || canvasRef.canvas?.parentNode;
      if (!wrap) return originalPrompt.call(canvasRef, title, value, callback, event, multiline);

      // Ensure wrapper is positioning context
      if (getComputedStyle(wrap).position === "static") wrap.style.position = "relative";

      const rect = wrap.getBoundingClientRect();

      // Default anchor: mouse position
      let clientX = event?.clientX ?? rect.left + rect.width / 2;
      let clientY = event?.clientY ?? rect.top + rect.height / 2;
      let preferredWidth = 220;

      // Improved anchor: if this prompt was triggered by a widget, align to the widget row/value region.
      if (event && typeof event.canvasX === "number" && typeof event.canvasY === "number") {
        const node = typeof canvasRef.getNodeOnPos === "function"
          ? canvasRef.getNodeOnPos(event.canvasX, event.canvasY, canvasRef.visible_nodes)
          : null;
        if (node && node.widgets && node.widgets.length) {
          const localY = event.canvasY - node.pos[1];
          const localX = event.canvasX - node.pos[0];
          const startY = TITLE_H + WIDGET_GAP;
          const stride = WIDGET_H + WIDGET_GAP;
          const idx = Math.max(0, Math.min(node.widgets.length - 1, Math.floor((localY - startY) / stride)));
          const rowY = startY + idx * stride;

          // Place editor over the value "pill" (roughly right side of row).
          const graphLeft = node.pos[0] + Math.max(60, Math.floor(node.size[0] * 0.38));
          const graphTop = node.pos[1] + rowY - 2;
          const graphWidth = Math.max(160, Math.floor(node.size[0] * 0.55));
          preferredWidth = graphWidth;

          const cssX = (graphLeft + canvasRef.ds.offset[0]) * canvasRef.ds.scale;
          const cssY = (graphTop + canvasRef.ds.offset[1]) * canvasRef.ds.scale;
          const canvasRect = canvasRef.canvas.getBoundingClientRect();
          clientX = canvasRect.left + cssX;
          clientY = canvasRect.top + cssY;

          // If click was far left (label area), still align to value region.
          if (localX < node.size[0] * 0.4) {
            // ok
          }
        }
      }

      const overlay = document.createElement(multiline ? "textarea" : "input");
      if (!multiline) overlay.type = "text";
      overlay.value = value ?? "";
      overlay.className = "lgInlineEditor";

      overlay.style.position = "absolute";
      const leftPx = Math.max(6, Math.min(rect.width - 6, clientX - rect.left));
      const topPx = Math.max(6, Math.min(rect.height - 6, clientY - rect.top));
      overlay.style.left = `${leftPx}px`;
      overlay.style.top = `${topPx}px`;
      overlay.style.minWidth = "140px";
      overlay.style.maxWidth = "420px";
      overlay.style.width = `${Math.max(180, Math.min(420, preferredWidth))}px`;
      overlay.style.padding = "6px 8px";
      overlay.style.border = "1px solid rgba(79,140,255,0.65)";
      overlay.style.borderRadius = "6px";
      overlay.style.background = "#0b1220";
      overlay.style.color = "#d6e2ff";
      overlay.style.fontSize = "13px";
      overlay.style.fontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";
      overlay.style.outline = "none";
      overlay.style.zIndex = "1000";
      overlay.style.boxShadow = "0 8px 24px rgba(0,0,0,0.45)";

      const finish = (commit) => {
        if (!overlay.isConnected) return;
        const v = overlay.value;
        overlay.remove();
        if (commit && typeof callback === "function") callback(v);
        canvasRef.setDirty(true, true);
      };

      overlay.addEventListener("keydown", (e) => {
        if (e.key === "Escape") {
          e.preventDefault();
          finish(false);
        } else if (e.key === "Enter" && !multiline) {
          e.preventDefault();
          finish(true);
        }
      });
      // Prevent canvas from stealing focus immediately (causes instant blur on some browsers).
      overlay.addEventListener("mousedown", (e) => {
        e.stopPropagation();
      });
      overlay.addEventListener("pointerdown", (e) => {
        e.stopPropagation();
      });

      const createdAt = performance.now();
      overlay.addEventListener("blur", () => {
        // Some browsers will blur immediately because LiteGraph focuses the canvas on mousedown.
        // If blur happens instantly, re-focus instead of closing.
        if (performance.now() - createdAt < 120) {
          requestAnimationFrame(() => overlay.focus());
          return;
        }
        finish(true);
      });

      wrap.appendChild(overlay);
      requestAnimationFrame(() => {
        overlay.focus();
        overlay.select?.();
      });
      return overlay;
    } catch (err) {
      console.warn("Inline prompt editor failed, falling back:", err);
      return originalPrompt.call(canvasRef, title, value, callback, event, multiline);
    }
  };
}

function installNodeResizing(lgCanvas) {
  if (!lgCanvas || lgCanvas.__nodeResizeInstalled) return;
  lgCanvas.__nodeResizeInstalled = true;

  const el = lgCanvas.canvas;
  const marginPx = 12; // hover area near border (human-friendly)

  const state = {
    hoverNode: null,
    hoverHandle: null,
    resizing: null, // { node, handle, startX, startY, startPos:[x,y], startSize:[w,h] }
  };

  const getHandle = (node, localX, localY, margin, titleH) => {
    if (!node || node.flags?.collapsed) return null;
    const w = node.size?.[0] ?? 0;
    const h = node.size?.[1] ?? 0;
    if (w <= 0 || h <= 0) return null;

    // IMPORTANT: Do NOT use left/right borders for resizing because ports live on the sides.
    // This prevents resize-hit from stealing clicks that should start connections.
    const left = false;
    const right = localX >= w - margin;
    // Title bar should be draggable. Do NOT treat the top edge as resize within the title area.
    const top = localY <= margin && !(titleH && localY <= titleH);
    const bottom = localY >= h - margin;

    // Only allow bottom-right corner resizing (most discoverable + least intrusive)
    if (bottom && right) return "se";
    return null;
  };

  const cursorForHandle = (h) => {
    if (!h) return "";
    if (h === "n" || h === "s") return "ns-resize";
    if (h === "e" || h === "w") return "ew-resize";
    if (h === "ne" || h === "sw") return "nesw-resize";
    if (h === "nw" || h === "se") return "nwse-resize";
    return "";
  };

  const setCursor = (cur) => {
    // LiteGraph itself updates `canvas.style.cursor` during its mouse processing (often to "crosshair"),
    // so we apply our cursor *after* it runs.
    state.__desiredCursor = cur || "";
    requestAnimationFrame(() => {
      if (el) el.style.cursor = state.__desiredCursor || "";
    });
  };

  const updateHover = (ev) => {
    if (state.resizing) return;
    lgCanvas.adjustMouseEvent(ev);
    const x = ev.canvasX;
    const y = ev.canvasY;

    // NOTE: LiteGraph's hit-test is on the graph, not the canvas:
    // graph.getNodeOnPos(x,y, visible_nodes)
    const node = lgCanvas.graph?.getNodeOnPos
      ? lgCanvas.graph.getNodeOnPos(x, y, lgCanvas.visible_nodes)
      : null;
    if (!node) {
      state.hoverNode = null;
      state.hoverHandle = null;
      setCursor("");
      return;
    }
    const localX = x - node.pos[0];
    const localY = y - node.pos[1];
    const scale = lgCanvas.ds?.scale || 1;
    const margin = marginPx / Math.max(0.0001, scale); // convert px -> graph units
    const titleH = (window.LiteGraph?.NODE_TITLE_HEIGHT ?? 30) / Math.max(0.0001, scale);
    const handle = getHandle(node, localX, localY, margin, titleH);
    state.hoverNode = node;
    state.hoverHandle = handle;
    if (handle) {
      setCursor(cursorForHandle(handle));
      return;
    }

    // Show a clear move cursor when hovering the title bar (drag region).
    if (localY >= 0 && localY <= titleH) {
      setCursor("move");
      return;
    }
    setCursor("");
  };

  const startResizeIfNeeded = (ev) => {
    if (ev.button !== 0) return false;
    updateHover(ev);
    if (!state.hoverNode || !state.hoverHandle) return false;

    // Start resizing
    lgCanvas.adjustMouseEvent(ev);
    state.resizing = {
      node: state.hoverNode,
      handle: state.hoverHandle,
      startX: ev.canvasX,
      startY: ev.canvasY,
      startPos: [state.hoverNode.pos[0], state.hoverNode.pos[1]],
      startSize: [state.hoverNode.size[0], state.hoverNode.size[1]],
    };
    setCursor(cursorForHandle(state.hoverHandle));
    ev.preventDefault();
    ev.stopPropagation();
    return true;
  };

  const doResize = (ev) => {
    if (!state.resizing) return false;
    lgCanvas.adjustMouseEvent(ev);
    const r = state.resizing;
    const dx = ev.canvasX - r.startX;
    const dy = ev.canvasY - r.startY;

    const node = r.node;
    // Keep enough space for our custom UI sections (status + tools).
    const minW = node.type === "coral/root" ? 200 : 400;
    const minH = node.type === "coral/root" ? 90 : 480;

    let x = r.startPos[0];
    let y = r.startPos[1];
    let w = r.startSize[0];
    let h = r.startSize[1];

    const handle = r.handle;
    if (handle.includes("e")) w = r.startSize[0] + dx;
    if (handle.includes("s")) h = r.startSize[1] + dy;
    if (handle.includes("w")) {
      w = r.startSize[0] - dx;
      x = r.startPos[0] + dx;
    }
    if (handle.includes("n")) {
      h = r.startSize[1] - dy;
      y = r.startPos[1] + dy;
    }

    // Clamp while keeping opposite edge anchored
    if (w < minW) {
      if (handle.includes("w")) x -= (minW - w);
      w = minW;
    }
    if (h < minH) {
      if (handle.includes("n")) y -= (minH - h);
      h = minH;
    }

    node.pos[0] = x;
    node.pos[1] = y;
    node.size[0] = w;
    node.size[1] = h;
    node.setDirtyCanvas(true, true);

    ev.preventDefault();
    ev.stopPropagation();
    return true;
  };

  const stopResize = (ev) => {
    if (!state.resizing) return false;
    state.resizing = null;
    updateHover(ev);
    scheduleAutoSave("resize");
    ev.preventDefault();
    ev.stopPropagation();
    return true;
  };

  // Capture listeners run BEFORE LiteGraph's own pointer handlers.
  el.addEventListener("pointermove", (ev) => {
    if (state.resizing) doResize(ev);
    else updateHover(ev);
  }, true);
  // Some environments still rely on mouse events (or have partial PointerEvent quirks).
  el.addEventListener("mousemove", (ev) => {
    if (state.resizing) doResize(ev);
    else updateHover(ev);
  }, true);
  el.addEventListener("pointerdown", (ev) => {
    startResizeIfNeeded(ev);
  }, true);
  el.addEventListener("mousedown", (ev) => {
    startResizeIfNeeded(ev);
  }, true);
  window.addEventListener("pointermove", (ev) => {
    if (state.resizing) doResize(ev);
  }, true);
  window.addEventListener("pointerup", (ev) => {
    if (state.resizing) stopResize(ev);
  }, true);
}

function registerLiteGraphNodes() {
  if (!window.LiteGraph) {
    console.error("LiteGraph not available for node registration");
    return;
  }

  function _hitRect(pos, r) {
    if (!r || !pos) return false;
    const x = pos[0], y = pos[1];
    return x >= r.x && x <= r.x + r.w && y >= r.y && y <= r.y + r.h;
  }

  function _drawButton(ctx, r, label, accent) {
    ctx.save();
    ctx.globalAlpha = 0.95;
    ctx.fillStyle = accent ? "rgba(79,140,255,0.16)" : "rgba(255,255,255,0.06)";
    ctx.strokeStyle = accent ? "rgba(79,140,255,0.55)" : "rgba(255,255,255,0.10)";
    ctx.lineWidth = 1;
    ctx.beginPath();
    const rad = 6;
    if (typeof ctx.roundRect === "function") ctx.roundRect(r.x, r.y, r.w, r.h, rad);
    else {
      // fallback rounded rect
      const x = r.x, y = r.y, w = r.w, h = r.h, rr = rad;
      ctx.moveTo(x + rr, y);
      ctx.arcTo(x + w, y, x + w, y + h, rr);
      ctx.arcTo(x + w, y + h, x, y + h, rr);
      ctx.arcTo(x, y + h, x, y, rr);
      ctx.arcTo(x, y, x + w, y, rr);
      ctx.closePath();
    }
    ctx.fill();
    ctx.stroke();
    ctx.fillStyle = "rgba(214,226,255,0.95)";
    ctx.font = "12px ui-sans-serif, system-ui, Segoe UI, Roboto, Arial";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.fillText(label, r.x + r.w / 2, r.y + r.h / 2 + 0.5);
    ctx.restore();
  }

  function _drawSeparator(ctx, y, w) {
    ctx.save();
    ctx.strokeStyle = "rgba(255,255,255,0.10)";
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(8, y);
    ctx.lineTo(w - 8, y);
    ctx.stroke();
    ctx.restore();
  }

  function _stateColor(state) {
    const s = String(state || "").toLowerCase();
    if (s === "running" || s === "online") return "#3cb46e";
    if (s === "idle") return "#ffd166";
    return "#ff4f6d"; // offline/unknown
  }

  CoralNode = class extends LiteGraph.LGraphNode {
    constructor(title = "node") {
      super(title);
      this.size = [300, 360];
      this.resizable = true;
      this.flags = this.flags || {};
      this.flags.resizable = true;
      this.properties = this.properties || {};
      this.properties.mcuUri ??= "serial://name=COM3&baudrate=1000000";
      this.properties.logicName ??= "TestLogic";
      // Runtime state from MCU (updated via nodeSnapshot)
      this.properties.runState ??= "offline";
      this.properties.isConfigured ??= false;
      this.properties.isProgrammed ??= false;
      this.properties.mode ??= "Unknown";
      this.properties.versionInfo ??= null;
      this.properties.ports ??= [];

      this.addInput("in", "flow");
      this.addOutput("out", "flow");

      this.addWidget("text", "mcuUri", this.properties.mcuUri, (v) => (this.properties.mcuUri = v));
      this.addWidget("text", "logicName", this.properties.logicName, (v) => (this.properties.logicName = v));

      this.__ui = { updateRect: null, configPortRect: null };
    }

    onPropertyChanged(name, value) {
      this.properties[name] = value;
    }

    onDrawForeground(ctx) {
      const titleH = window.LiteGraph?.NODE_TITLE_HEIGHT ?? 30;
      const widgetH = window.LiteGraph?.NODE_WIDGET_HEIGHT ?? 20;
      const gap = 4;
      const w = this.size[0];
      const h = this.size[1];
      const propsCount = 2;
      const afterPropsY = titleH + propsCount * (widgetH + gap) + 6;
      _drawSeparator(ctx, afterPropsY, w);

      const runState = String(this.properties.runState || "offline");
      const offline = runState.toLowerCase() === "offline";
      const v = this.properties.versionInfo || {};
      const ports = this.properties.ports || [];

      ctx.save();
      ctx.font = "11px ui-sans-serif, system-ui, Segoe UI, Roboto, Arial";
      ctx.textAlign = "left";
      ctx.textBaseline = "middle";

      let y0 = afterPropsY + 12;

      // State line: Running/Idle/Error + Configured + Programmed + Mode
      ctx.fillStyle = "rgba(214,226,255,0.6)";
      ctx.fillText("State:", 10, y0);
      ctx.fillStyle = _stateColor(runState);
      ctx.beginPath();
      ctx.arc(50, y0, 4, 0, Math.PI * 2);
      ctx.fill();
      const flags = [];
      flags.push(runState.toUpperCase());
      if (this.properties.isConfigured) flags.push("Cfg");
      if (this.properties.isProgrammed) flags.push("Prog");
      flags.push(this.properties.mode === "DIVER" ? "DIVER" : "Bridge");
      ctx.fillStyle = "rgba(214,226,255,0.95)";
      ctx.fillText(flags.join(" | "), 58, y0);
      y0 += 16;

      // Product: ProductionName
      ctx.fillStyle = "rgba(214,226,255,0.6)";
      ctx.fillText("Product:", 10, y0);
      ctx.fillStyle = "rgba(214,226,255,0.95)";
      ctx.fillText(offline ? "??" : (v.productionName || "??"), 58, y0);
      y0 += 16;

      // Version: Commit(Tag) + BuildTime
      ctx.fillStyle = "rgba(214,226,255,0.6)";
      ctx.fillText("Version:", 10, y0);
      const commit = v.gitCommit || "";
      const tag = v.gitTag || "";
      const buildTime = v.buildTime || "";
      const verStr = offline ? "??" : `${commit}(${tag}) ${buildTime}`.trim();
      ctx.fillStyle = "rgba(214,226,255,0.95)";
      ctx.fillText(verStr.substring(0, 35), 58, y0);
      y0 += 20;

      _drawSeparator(ctx, y0 - 4, w);

      // Snapshots: use layout if available
      const layout = this.properties.layout;
      const diCount = layout?.digitalInputCount ?? 16;
      const doCount = layout?.digitalOutputCount ?? 16;
      const portCount = layout?.portCount ?? ports.length ?? 0;

      ctx.fillStyle = "rgba(214,226,255,0.6)";
      ctx.fillText("Snapshots:", 10, y0 + 6);
      y0 += 18;
      // Input row
      ctx.fillStyle = "rgba(214,226,255,0.5)";
      ctx.fillText(`In(${diCount}):`, 10, y0);
      const maxLightsPerRow = Math.min(diCount, 16);
      for (let i = 0; i < maxLightsPerRow; i++) {
        ctx.beginPath();
        ctx.fillStyle = "rgba(100,100,100,0.5)";
        ctx.arc(50 + i * 14, y0, 4, 0, Math.PI * 2);
        ctx.fill();
      }
      y0 += 14;
      // Output row
      ctx.fillStyle = "rgba(214,226,255,0.5)";
      ctx.fillText(`Out(${doCount}):`, 10, y0);
      const maxOutputLights = Math.min(doCount, 16);
      for (let i = 0; i < maxOutputLights; i++) {
        ctx.beginPath();
        ctx.fillStyle = "rgba(100,100,100,0.5)";
        ctx.arc(50 + i * 14, y0, 4, 0, Math.PI * 2);
        ctx.fill();
      }
      y0 += 18;

      _drawSeparator(ctx, y0 - 4, w);

      // Ports
      ctx.fillStyle = "rgba(214,226,255,0.6)";
      ctx.fillText(`Ports: ${portCount} Total`, 10, y0 + 6);
      y0 += 18;
      ctx.font = "10px ui-monospace, monospace";
      const maxPortsToShow = Math.min(portCount || ports.length, 8);
      for (let i = 0; i < maxPortsToShow; i++) {
        const p = ports[i];
        const portName = p?.name || `Port${i}`;
        const pStr = p ? `[${i}] ${portName}: ${p.type}, ${p.baud}` : `[${i}] --`;
        ctx.fillStyle = "rgba(214,226,255,0.7)";
        ctx.fillText(pStr.substring(0, 45), 10, y0);
        y0 += 12;
      }
      ctx.restore();

      _drawSeparator(ctx, h - 40, w);

      // Bottom buttons: UpdateFW, ConfigPort
      const btnH = 22;
      const pad = 10;
      const gapBtn = 8;
      const btnW = Math.floor((w - pad * 2 - gapBtn) / 2);
      const btnY = h - btnH - 10;
      const r1 = { x: pad, y: btnY, w: btnW, h: btnH };
      const r2 = { x: pad + btnW + gapBtn, y: btnY, w: btnW, h: btnH };
      this.__ui.updateRect = r1;
      this.__ui.configPortRect = r2;
      _drawButton(ctx, r1, "Update FW", true);
      _drawButton(ctx, r2, "Config Port", false);

      // Resize handle
      try {
        const s = this.size;
        const x = s[0] - 2;
        const y = s[1] - 2;
        ctx.save();
        ctx.globalAlpha = 0.85;
        ctx.fillStyle = "rgba(79,140,255,0.28)";
        ctx.strokeStyle = "rgba(79,140,255,0.85)";
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(x, y);
        ctx.lineTo(x - 10, y);
        ctx.lineTo(x, y - 10);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.restore();
      } catch {
        // ignore
      }
    }

    onMouseDown(e, pos) {
      if (!pos) return false;
      if (_hitRect(pos, this.__ui?.updateRect)) {
        showGenericDialog(
          "Update Firmware",
          `<div style="color:var(--text);font-weight:700;margin-bottom:6px;">${escapeHtml(this.properties.logicName || "node")}</div>
           <div>Target: <span style="color:var(--text)">${escapeHtml(this.properties.mcuUri || "")}</span></div>
           <div style="margin-top:10px;color:var(--muted)">This is a UI stub. Wire a backend endpoint when ready.</div>`,
          () => logTerminal(`[ui] Update FW requested for ${this.properties.logicName} on ${this.properties.mcuUri}`),
          "Start"
        );
        return true;
      }
      if (_hitRect(pos, this.__ui?.configPortRect)) {
        this._showPortConfigDialog();
        return true;
      }
      return false;
    }

    _showPortConfigDialog() {
      const ports = this.properties.ports || [];
      const layout = this.properties.layout;
      const nodeId = this.id;
      const portCount = layout?.portCount ?? ports.length ?? 0;

      if (portCount === 0) {
        showGenericDialog(
          "Port Configuration",
          `<div style="color:var(--muted);">No ports available. Connect to MCU first to discover hardware layout.</div>`,
          () => {},
          "Close"
        );
        return;
      }

      let html = `<div style="color:var(--muted);margin-bottom:10px;">Configure ${portCount} port(s)</div>`;
      for (let i = 0; i < portCount; i++) {
        const p = ports[i] || {};
        const portName = p.name || `Port ${i}`;
        const layoutType = p.layoutType || "Serial";
        const isSerial = (p.type || layoutType) === "Serial";
        const isCAN = layoutType === "CAN";
        html += `
          <div style="margin:8px 0;padding:8px;background:rgba(0,0,0,0.2);border-radius:4px;">
            <div style="color:var(--text);font-weight:600;margin-bottom:4px;">[${i}] ${escapeHtml(portName)} <span style="color:var(--muted);font-weight:normal;">(${layoutType})</span></div>
            <label style="display:inline-block;width:50px;color:var(--muted);">Baud:</label>
            <input id="portBaud${i}" class="dialogInput" style="width:100px;" type="number" value="${p.baud || (isCAN ? 500000 : 9600)}" />
            ${isCAN ? `
              <label style="display:inline-block;width:70px;margin-left:10px;color:var(--muted);">RetryMs:</label>
              <input id="portExtra${i}" class="dialogInput" style="width:60px;" type="number" value="${p.retryTimeMs || 10}" />
            ` : `
              <label style="display:inline-block;width:70px;margin-left:10px;color:var(--muted);">FrameMs:</label>
              <input id="portExtra${i}" class="dialogInput" style="width:60px;" type="number" value="${p.receiveFrameMs || 0}" />
            `}
            <input type="hidden" id="portType${i}" value="${layoutType}" />
          </div>`;
      }
      showGenericDialog(
        "Port Configuration",
        html,
        () => {
          const newPorts = [];
          for (let i = 0; i < portCount; i++) {
            const type = byId(`portType${i}`)?.value || "Serial";
            const baud = parseInt(byId(`portBaud${i}`)?.value || "9600", 10);
            const extra = parseInt(byId(`portExtra${i}`)?.value || "20", 10);
            if (type === "CAN") {
              newPorts.push({ type, baud, retryTimeMs: extra });
            } else {
              newPorts.push({ type, baud, receiveFrameMs: extra });
            }
          }
          this.properties.ports = newPorts;
          api("POST", `/api/node/${nodeId}/ports`, { ports: newPorts })
            .then(() => logTerminal(`[ui] Port config saved for node ${nodeId}`))
            .catch((err) => logTerminal(`[ui] Port config save failed: ${err.message}`));
          this.setDirtyCanvas(true, true);
          scheduleAutoSave("port config");
        },
        "Save"
      );
    }
  };

  RootNode = class extends LiteGraph.LGraphNode {
    constructor() {
      super("Root (PC)");
      this.size = [200, 90];
      this.resizable = true;
      this.removable = false; // Root cannot be deleted
      this.flags = this.flags || {};
      this.flags.resizable = true;  // Explicitly enable resizing
      this.properties = this.properties || {};
      this.properties.mcuUri ??= "PC";
      this.properties.logicName ??= "Root";
      this.addOutput("out", "flow");
      this.__ui = {};
    }

    onDrawForeground(ctx) {
      const w = this.size[0];
      const h = this.size[1];

      // Visible resize handle (bottom-right), ~10x10 triangle
      try {
        const s = this.size;
        const x = s[0] - 2;
        const y = s[1] - 2;
        ctx.save();
        ctx.globalAlpha = 0.85;
        ctx.fillStyle = "rgba(79,140,255,0.28)";
        ctx.strokeStyle = "rgba(79,140,255,0.7)";
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(x, y);
        ctx.lineTo(x - 10, y);
        ctx.lineTo(x, y - 10);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.restore();
      } catch {
        // ignore
      }
    }

    onMouseDown() {
      return false;
    }
  };

  LiteGraph.registerNodeType("coral/root", RootNode);
  LiteGraph.registerNodeType("coral/node", CoralNode);
}

function ensureRoot() {
  const nodes = graph._nodes || [];
  const hasRoot = nodes.some((n) => n.type === "coral/root");
  if (!hasRoot) {
    const r = LiteGraph.createNode("coral/root");
    r.pos = [80, 80];
    graph.add(r);
  }
}

function addNode() {
  if (!graph) {
    logTerminal("[ui] ERROR: Graph not initialized");
    return;
  }
  const n = LiteGraph.createNode("coral/node");
  n.pos = [320 + Math.random() * 160, 120 + Math.random() * 140];
  graph.add(n);
  logTerminal("[ui] Added new node to graph");
  scheduleAutoSave("addNode");
}

function initGraph() {
  if (!window.LiteGraph || !window.LGraph) {
    logTerminal("[ui] ERROR: LiteGraph library not loaded");
    console.error("LiteGraph library not loaded");
    return;
  }
  
  try {
    registerLiteGraphNodes();
    
    window.graph = graph = new LGraph();
    logTerminal("[ui] Graph created");

    // Auto-save graph layout/links whenever LiteGraph reports changes
    // (move nodes, resize nodes, add/remove nodes/links, etc.)
    if (!graph.__autoSavePatched && typeof graph.change === "function") {
      graph.__autoSavePatched = true;
      const origChange = graph.change.bind(graph);
      graph.change = function (...args) {
        const r = origChange(...args);
        scheduleAutoSave("graph.change");
        return r;
      };
    }
    
    const c = byId("graphCanvas");
    window.canvas = canvas = new LGraphCanvas(c, graph);
    canvas.background_image = "";
    
    // Enable node manipulation features
    canvas.allow_dragcanvas = true;
    canvas.allow_dragnodes = true;
    canvas.allow_interaction = true;
    canvas.allow_searchbox = false; // prevent LiteGraph 'Search' dialog from ever appearing
    canvas.render_canvas_border = false;
    canvas.render_connections_shadows = false;
    canvas.render_connections_border = true;
    canvas.highquality_render = true;
    
    // Disable external prompt box for inline editing
    LiteGraph.alt_drag_do_clone_nodes = false;
    LiteGraph.use_uuids = true;
    
    // Override widget prompt to prevent external textbox - enable inline editing
    disableLiteGraphContextMenus(canvas);
    // Also hard-disable search box (it appends a "Search" input to <body>)
    canvas.showSearchBox = function () { return null; };
    if (canvas.search_box && canvas.search_box.close) canvas.search_box.close();
    installInlinePromptEditor(canvas, byId("graphWrap"));
    installNodeResizing(canvas);
    installLiteGraphDpiScaling(canvas);
    
    // Custom property dialog positioning - show at node position instead of bottom
    const originalShowNodePanel = canvas.showNodePanel || LGraphCanvas.prototype.showNodePanel;
    canvas.showNodePanel = function(node) {
      if (!node) return;
      
      // Get node position in canvas space
      const nodePos = node.pos;
      const canvasX = nodePos[0] * this.ds.scale + this.ds.offset[0];
      const canvasY = nodePos[1] * this.ds.scale + this.ds.offset[1];
      
      // Call original
      const result = originalShowNodePanel.call(this, node);
      
      // Reposition the panel
      const panel = this.node_panel;
      if (panel && panel.parentNode) {
        const rect = c.getBoundingClientRect();
        const x = Math.max(10, Math.min(rect.width - 310, canvasX + rect.left));
        const y = Math.max(10, Math.min(rect.height - 200, canvasY + rect.top));
        panel.style.left = x + "px";
        panel.style.top = y + "px";
      }
      
      return result;
    };

    ensureRoot();
    graph.start();
    logTerminal("[ui] Graph initialized with root node");
    
    // Setup resize handler with proper DPI awareness for crisp rendering
    const resize = () => {
      const wrap = byId("graphWrap");
      const rect = wrap.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;

      // Pick integer CSS sizes that align well with fractional DPRs to minimize blur.
      const ww = computePixelPerfectCssSize(rect.width, dpr);
      const hh = computePixelPerfectCssSize(rect.height, dpr);
      const cssW = Math.max(100, ww.cssPx);
      const cssH = Math.max(100, hh.cssPx);
      const bufW = Math.max(100, Math.round((cssW * ww.num) / ww.denom));
      const bufH = Math.max(100, Math.round((cssH * hh.num) / hh.denom));

      c.style.width = cssW + "px";
      c.style.height = cssH + "px";

      if (canvas) {
        // Update DPR used by our patched ds.toCanvasContext
        if (canvas.ds) canvas.ds.__dpr = dpr;
        canvas.resize(bufW, bufH);
        canvas.setDirty(true, true);
      }
    };
    window.addEventListener("resize", resize);
    resize();

    // Setup double-click to add node (only if double-clicked empty space)
    c.addEventListener("dblclick", (e) => {
      canvas.adjustMouseEvent(e);
      const node = canvas.graph?.getNodeOnPos
        ? canvas.graph.getNodeOnPos(e.canvasX, e.canvasY, canvas.visible_nodes)
        : null;
      if (!node) addNode();
    });

    // Disable default context menu to prevent rubbish text
    c.addEventListener("contextmenu", (e) => {
      e.preventDefault();
      return false;
    });
  } catch (err) {
    logTerminal(`[ui] ERROR initGraph: ${err.message}`);
    console.error("initGraph failed:", err);
  }
}

// Debug modal removed - was blocking view

// --- Project persistence (client-side export/import + server save) ---

async function exportProject() {
  try {
    // Save current state first
    await saveOnServer({ silent: true, reason: "export" });
    
    // Download ZIP from server (includes project.json + assets + generated)
    const a = document.createElement("a");
    a.href = "/api/project/export";
    a.download = `coralinker-project-${Date.now()}.zip`;
    a.click();
    
    logTerminal("[ui] Exporting project as ZIP...");
  } catch (err) {
    logTerminal(`[ui] Export failed: ${err.message}`);
  }
}

function importProjectFile(file) {
  const reader = new FileReader();
  reader.onload = () => {
    const data = JSON.parse(reader.result);
    graph.clear();
    graph.configure(data);
    ensureRoot();
  };
  reader.readAsText(file);
}

let _saveInFlight = false;
let _saveQueued = false;
async function saveOnServer(opts) {
  const options = opts || {};
  const silent = options.silent !== false;
  const reason = options.reason || "save";

  if (!graph) return;
  if (_saveInFlight) {
    _saveQueued = true;
    setSyncIndicator("syncing", "Sync queued…");
    return;
  }

  _saveInFlight = true;
  setSyncIndicator("syncing", "Syncing…");

  // Convert LiteGraph format to our server shape (persist layout + links)
  const snap = graph.serialize();
  const nodes = (snap.nodes || []).map((n) => ({
    id: `${n.id}`,
    title: n.title || "node",
    kind: n.type === "coral/root" ? "root" : "node",
    x: n.pos?.[0] ?? 0,
    y: n.pos?.[1] ?? 0,
    w: n.size?.[0] ?? null,
    h: n.size?.[1] ?? null,
    properties: {
      mcuUri: n.properties?.mcuUri ?? "",
      logicName: n.properties?.logicName ?? "",
    },
  }));
  // LiteGraph serializes links either as objects OR as arrays:
  // Array format: [id, origin_id, origin_slot, target_id, target_slot, type]
  const normalizeSlot = (v) => {
    const n = (typeof v === "number") ? v : (v != null ? Number(v) : NaN);
    return Number.isFinite(n) ? n : null;
  };
  const normalizeId = (v) => {
    if (v == null) return null;
    const s = String(v);
    if (!s || s === "undefined" || s === "null") return null;
    return s;
  };
  const links = (snap.links || [])
    .map((l) => {
      // Array format: [id, origin_id, origin_slot, target_id, target_slot, type]
      if (Array.isArray(l)) {
        const id = normalizeId(l[0]);
        const originId = normalizeId(l[1]);
        const targetId = normalizeId(l[3]);
        const originSlot = normalizeSlot(l[2]);
        const targetSlot = normalizeSlot(l[4]);
        if (!id || !originId || !targetId || originSlot == null || targetSlot == null) return null;
        return { id, fromNodeId: originId, fromSlot: originSlot, toNodeId: targetId, toSlot: targetSlot };
      }
      const id = normalizeId(l?.id);
      const originId = normalizeId(l?.origin_id);
      const targetId = normalizeId(l?.target_id);
      const originSlot = normalizeSlot(l?.origin_slot);
      const targetSlot = normalizeSlot(l?.target_slot);
      if (!id || !originId || !targetId || originSlot == null || targetSlot == null) return null;
      return { id, fromNodeId: originId, fromSlot: originSlot, toNodeId: targetId, toSlot: targetSlot };
    })
    .filter(Boolean);
  const state = {
    nodes,
    links,
    selectedAsset: selectedAssetName,
    selectedFile: selectedFilePath,
    lastBuildId: lastBuildId,
  };

  try {
    await api("POST", "/api/project", state);
    await api("POST", "/api/project/save");
    setSyncIndicator("synced", `Synced (${reason})`);
    if (!silent) logTerminal("[ui] Saved project to server.");
  } catch (err) {
    setSyncIndicator("error", `Sync error: ${err.message || String(err)}`);
    if (!silent) logTerminal(`[ui] Save failed: ${err.message || String(err)}`);
    throw err;
  } finally {
    _saveInFlight = false;
    if (_saveQueued) {
      _saveQueued = false;
      // One more flush
      saveOnServer({ silent: true, reason: "queued" }).catch(() => {});
    }
  }
}

async function loadServerProject() {
  try {
    const state = await api("GET", "/api/project");
    selectedAssetName = state.selectedAsset || null;
    selectedFilePath = state.selectedFile || null;
    lastBuildId = state.lastBuildId || null;

    // If an artifact already exists for the selected input, show its variables immediately.
    if (selectedAssetName) {
      const logic = logicNameFromCsPath(selectedAssetName);
      loadArtifactMetadata(lastBuildId, logic, { silent: true });
    }

    // Clear graph first to avoid duplicates (only if graph exists)
    if (graph) {
      graph.clear();
      
      // Convert server shape back to LiteGraph
      const idMap = new Map();
      for (const n of state.nodes || []) {
        const type = n.kind === "root" ? "coral/root" : "coral/node";
        const node = LiteGraph.createNode(type);
        node.title = n.title;
        node.pos = [n.x, n.y];
        if (typeof n.w === "number" && typeof n.h === "number") {
          // Enforce minimum sizes so the node UI is never cramped (status + tool buttons)
          const minW = type === "coral/root" ? 200 : 300;
          const minH = type === "coral/root" ? 90 : 360;
          node.size = [Math.max(minW, n.w), Math.max(minH, n.h)];
        }
        node.properties = node.properties || {};
        node.properties.mcuUri = n.properties?.mcuUri ?? node.properties.mcuUri;
        node.properties.logicName = n.properties?.logicName ?? node.properties.logicName;
        // Status fields are now indicators (state/version/info)
        if (n.properties?.state) node.properties.state = n.properties.state;
        if (n.properties?.version) node.properties.version = n.properties.version;
        if (n.properties?.info) node.properties.info = n.properties.info;
        // Preserve stable ids for link restoration
        node.id = n.id;
        graph.add(node);
        idMap.set(`${n.id}`, node);
      }
      
      // Only add root if we loaded no nodes at all
      if ((state.nodes || []).length === 0) {
        ensureRoot();
      }

      // Restore links (slot indices are persisted)
      for (const l of state.links || []) {
        const from = idMap.get(`${l.fromNodeId}`);
        const to = idMap.get(`${l.toNodeId}`);
        const fromSlot = typeof l.fromSlot === "number" ? l.fromSlot : null;
        const toSlot = typeof l.toSlot === "number" ? l.toSlot : null;
        if (!from || !to || fromSlot == null || toSlot == null) continue;
        try {
          from.connect(fromSlot, to, toSlot);
        } catch {
          // ignore broken links (e.g., node type changed)
        }
      }
    }
  } catch (err) {
    logTerminal(`[ui] ERROR loadServerProject: ${err.message || String(err)}`);
    console.error("loadServerProject failed:", err);
    // Ensure we have at least a root node (only if graph exists)
    if (graph) ensureRoot();
  }
}

// --- Assets Tree + Editors ---

let selectedAssetName = null; // inputs/*.cs selection
let selectedFilePath = null; // data-relative: assets/...
let lastBuildId = null;

let currentFile = null; // { path, kind, text?, base64?, sizeBytes }
let editorMode = "text"; // "text" | "hex"
let dirty = false;

let cmEditor = null;
let cachedTreeRoot = null;

// --- Hex editor settings/state ---
let hexBytesPerRow = 16;
let hexGroupSize = 8;
let hexInsertMode = false; // currently affects ASCII typing + Ctrl+I insert
let _hexMarks = [];
let _hexView = null; // { bytes:Uint8Array, caretOff:number, caretNibble:0|1, selA:number|null, selB:number|null, activeCol:"hex"|"ascii", hexEls:[], asciiEls:[], rowTopEls:[] }

function clearHexMarks() {
  if (!_hexMarks.length) return;
  for (const m of _hexMarks) {
    try { m.clear(); } catch {}
  }
  _hexMarks = [];
}

function clamp(n, lo, hi) {
  return Math.max(lo, Math.min(hi, n));
}

function layoutForHex(bytesPerRow, groupSize) {
  const hexStart = 10; // offset(8) + 2 spaces
  const extraSpaces = groupSize > 0 ? Math.floor((bytesPerRow - 1) / groupSize) : 0;
  const hexAreaLen = bytesPerRow * 3 + extraSpaces; // "XX " * N + group paddings
  const asciiStart = hexStart + hexAreaLen + 2; // " |"
  return { hexStart, hexAreaLen, asciiStart, bytesPerRow, groupSize };
}

function byteHexCh(idx, bytesPerRow, groupSize) {
  const extra = groupSize > 0 ? Math.floor(idx / groupSize) : 0;
  return 10 + idx * 3 + extra;
}

function parseOffsetFromLine(lineText) {
  if (!lineText || lineText.length < 8) return null;
  const offHex = lineText.substring(0, 8);
  const base = parseInt(offHex, 16);
  return Number.isFinite(base) ? base : null;
}

function posToByteOffset(doc, pos) {
  if (!doc) return null;
  const lineText = doc.getLine(pos.line) || "";
  const base = parseOffsetFromLine(lineText);
  if (base == null) return null;

  const { hexStart, asciiStart, bytesPerRow, groupSize } = layoutForHex(hexBytesPerRow, hexGroupSize);
  const ch = pos.ch;

  // ASCII zone
  if (ch >= asciiStart && ch < asciiStart + bytesPerRow) return base + (ch - asciiStart);

  // Hex zone: choose nearest byte start (max 32, cheap)
  if (ch >= hexStart && ch < asciiStart) {
    let best = 0;
    let bestDist = Infinity;
    for (let i = 0; i < bytesPerRow; i++) {
      const bch = byteHexCh(i, bytesPerRow, groupSize);
      const dist = Math.abs(ch - bch);
      if (dist < bestDist) { bestDist = dist; best = i; }
    }
    return base + best;
  }

  return base;
}

function showHexSplit(show) {
  const cmHost = byId("cmHost");
  const hexHost = byId("hexSplitHost");
  if (cmHost) cmHost.classList.toggle("hidden", !!show);
  if (hexHost) hexHost.classList.toggle("hidden", !show);
}

function setHexBytes(bytes) {
  _hexView = {
    bytes: bytes || new Uint8Array(),
    caretOff: 0,
    caretNibble: 0,
    selA: null,
    selB: null,
    activeCol: "hex",
    hexEls: [],
    asciiEls: [],
    rowTopEls: [],
  };
}

function byteToAscii(b) {
  return (b >= 32 && b <= 126) ? String.fromCharCode(b) : ".";
}

function ensureHexView() {
  if (!_hexView) setHexBytes(new Uint8Array());
  return _hexView;
}

function clearHexSelectionClasses(v) {
  if (!v) return;
  const a = v.selA;
  const b = v.selB;
  if (a == null || b == null) return;
  const lo = Math.min(a, b);
  const hi = Math.max(a, b);
  for (let i = lo; i <= hi; i++) {
    const he = v.hexEls[i];
    const ae = v.asciiEls[i];
    if (he) he.classList.remove("sel");
    if (ae) ae.classList.remove("sel");
  }
}

function applyHexSelectionClasses(v) {
  if (!v) return;
  const a = v.selA;
  const b = v.selB;
  if (a == null || b == null) return;
  const lo = clamp(Math.min(a, b), 0, v.bytes.length - 1);
  const hi = clamp(Math.max(a, b), 0, v.bytes.length - 1);
  for (let i = lo; i <= hi; i++) {
    const he = v.hexEls[i];
    const ae = v.asciiEls[i];
    if (he) he.classList.add("sel");
    if (ae) ae.classList.add("sel");
  }
}

function setHexCaret(v, off, col, nibble) {
  if (!v) return;
  // clear old caret
  const oldOff = v.caretOff;
  const oldHex = v.hexEls[oldOff];
  const oldAsc = v.asciiEls[oldOff];
  if (oldHex) {
    oldHex.classList.remove("caret");
    try { delete oldHex.dataset.nib; } catch {}
  }
  if (oldAsc) oldAsc.classList.remove("caret");

  v.caretOff = clamp(off, 0, Math.max(0, v.bytes.length - 1));
  v.activeCol = col || v.activeCol;
  v.caretNibble = (nibble === 1 ? 1 : 0);

  const nh = v.hexEls[v.caretOff];
  const na = v.asciiEls[v.caretOff];
  if (nh) {
    nh.classList.add("caret");
    nh.dataset.nib = `${v.caretNibble}`;
  }
  if (na) na.classList.add("caret");

  updateHexInspector();
}

function updateHexInspector() {
  const host = byId("hexInspector");
  const status = byId("hexStatus"); // keep lightweight status too
  if (!host) return;

  const v = ensureHexView();
  const off = v.caretOff || 0;
  const bytes = v.bytes || new Uint8Array();
  const lenSel = (v.selA != null && v.selB != null) ? (Math.abs(v.selB - v.selA) + 1) : 0;

  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const b0 = bytes[off] ?? 0;

  const safe = (fn) => { try { return fn(); } catch { return null; } };
  const u8 = b0;
  const i8 = (b0 << 24) >> 24;
  const u16le = safe(() => view.getUint16(off, true));
  const u16be = safe(() => view.getUint16(off, false));
  const i16le = safe(() => view.getInt16(off, true));
  const i16be = safe(() => view.getInt16(off, false));
  const u32le = safe(() => view.getUint32(off, true));
  const u32be = safe(() => view.getUint32(off, false));
  const i32le = safe(() => view.getInt32(off, true));
  const i32be = safe(() => view.getInt32(off, false));
  const f32le = safe(() => view.getFloat32(off, true));
  const f32be = safe(() => view.getFloat32(off, false));
  const f64le = safe(() => view.getFloat64(off, true));
  const f64be = safe(() => view.getFloat64(off, false));
  const chr = byteToAscii(u8);

  const rows = [
    ["Offset", `0x${off.toString(16).toUpperCase().padStart(8, "0")}`],
    ["Sel Len", lenSel ? `${lenSel}` : ""],
    ["U8", `${u8}`],
    ["I8", `${i8}`],
    ["Char", `'${chr}'`],
    ["U16 LE", u16le == null ? "" : `${u16le}`],
    ["U16 BE", u16be == null ? "" : `${u16be}`],
    ["I16 LE", i16le == null ? "" : `${i16le}`],
    ["I16 BE", i16be == null ? "" : `${i16be}`],
    ["U32 LE", u32le == null ? "" : `${u32le}`],
    ["U32 BE", u32be == null ? "" : `${u32be}`],
    ["I32 LE", i32le == null ? "" : `${i32le}`],
    ["I32 BE", i32be == null ? "" : `${i32be}`],
    ["F32 LE", f32le == null ? "" : `${Number(f32le).toFixed(6)}`],
    ["F32 BE", f32be == null ? "" : `${Number(f32be).toFixed(6)}`],
    ["F64 LE", f64le == null ? "" : `${Number(f64le).toFixed(6)}`],
    ["F64 BE", f64be == null ? "" : `${Number(f64be).toFixed(6)}`],
  ];

  host.innerHTML = "";
  const title = document.createElement("div");
  title.className = "hexInspTitle";
  title.textContent = "Inspector";
  host.appendChild(title);

  for (const [k, val] of rows) {
    const r = document.createElement("div");
    r.className = "hexInspRow";
    const kk = document.createElement("div");
    kk.className = "hexInspKey";
    kk.textContent = k;
    const vv = document.createElement("div");
    vv.className = "hexInspVal";
    vv.textContent = val;
    r.appendChild(kk);
    r.appendChild(vv);
    host.appendChild(r);
  }

  if (status) status.textContent = `@0x${off.toString(16).toUpperCase().padStart(8, "0")}`;
}

function renderHexSplit() {
  const addrCol = byId("hexAddrCol");
  const hexCol = byId("hexHexCol");
  const asciiCol = byId("hexAsciiCol");
  if (!addrCol || !hexCol || !asciiCol) return;

  const v = ensureHexView();
  const bytes = v.bytes || new Uint8Array();
  v.hexEls = new Array(bytes.length);
  v.asciiEls = new Array(bytes.length);
  v.rowTopEls = [];

  addrCol.innerHTML = "";
  hexCol.innerHTML = "";
  asciiCol.innerHTML = "";

  const fragA = document.createDocumentFragment();
  const fragH = document.createDocumentFragment();
  const fragS = document.createDocumentFragment();

  const bpr = hexBytesPerRow || 16;
  const group = hexGroupSize || 8;

  for (let base = 0; base < bytes.length; base += bpr) {
    const rowIdx = Math.floor(base / bpr);

    const aRow = document.createElement("div");
    aRow.className = "hexRow";
    aRow.textContent = base.toString(16).toUpperCase().padStart(8, "0");
    fragA.appendChild(aRow);

    const hRow = document.createElement("div");
    hRow.className = "hexRow";
    hRow.dataset.row = `${rowIdx}`;

    const sRow = document.createElement("div");
    sRow.className = "hexRow";
    sRow.dataset.row = `${rowIdx}`;

    for (let j = 0; j < bpr; j++) {
      const off = base + j;
      if (off >= bytes.length) break;

      const b = bytes[off];
      const hx = b.toString(16).toUpperCase().padStart(2, "0");

      const hb = document.createElement("span");
      hb.className = "hexByte";
      hb.dataset.off = `${off}`;
      hb.textContent = hx;
      v.hexEls[off] = hb;
      hRow.appendChild(hb);

      // spacing
      const sp = document.createTextNode(" ");
      hRow.appendChild(sp);
      if (group > 0 && (j % group === group - 1) && j !== bpr - 1) hRow.appendChild(document.createTextNode(" "));

      const ab = document.createElement("span");
      ab.className = "asciiByte";
      ab.dataset.off = `${off}`;
      ab.textContent = byteToAscii(b);
      v.asciiEls[off] = ab;
      sRow.appendChild(ab);
    }

    fragH.appendChild(hRow);
    fragS.appendChild(sRow);
    v.rowTopEls.push(hRow); // use hex row as anchor for scroll-to
  }

  addrCol.appendChild(fragA);
  hexCol.appendChild(fragH);
  asciiCol.appendChild(fragS);

  // restore caret + selection visuals
  setHexCaret(v, v.caretOff || 0, v.activeCol || "hex", v.caretNibble || 0);
  applyHexSelectionClasses(v);
  updateHexInspector();
}

function setBusy(on) {
  const dis = !!on;
  const ids = ["btnNew", "btnSaveServer", "btnBuild", "btnRun"];
  ids.forEach((id) => {
    const el = byId(id);
    if (el) el.disabled = dis;
  });
  const stop = byId("btnStop");
  if (stop) stop.disabled = false; // always allow stop
}

function setDirty(on) {
  dirty = !!on;
  const p = byId("tabEditorPath");
  if (p) {
    const base = currentFile?.path || "No file selected";
    p.textContent = dirty ? `${base} *` : base;
  }
  
  // Update tab dirty indicator
  if (currentFile && currentFile.path) {
    const tab = openTabs.find(t => t.path === currentFile.path);
    if (tab) {
      tab.dirty = !!on;
      renderTabs();
    }
  }
}

async function refreshTree() {
  try {
    const tree = await api("GET", "/api/files/tree");
    if (!tree) {
      logTerminal("[ui] ERROR: /api/files/tree returned null or undefined");
      return;
    }
    cachedTreeRoot = tree;
    renderTree(tree);
  } catch (err) {
    logTerminal(`[ui] ERROR refreshTree: ${err.message || String(err)}`);
    console.error("refreshTree failed:", err);
  }
}

function renderTree(root) {
  const host = byId("assetsTree");
  host.innerHTML = "";

  if (!root) {
    logTerminal("[ui] ERROR: renderTree called with null/undefined root");
    console.error("renderTree: root is null or undefined");
    return;
  }

  const walk = (node, depth) => {
    if (!node) {
      console.warn("renderTree: skipping null node at depth", depth);
      return;
    }

    // No longer need to filter timestamped folders - artifacts are directly under generated/

    const row = document.createElement("div");
    row.className = "treeItem" + (node.path === selectedFilePath ? " active" : "");
    row.style.paddingLeft = `${8 + depth * 14}px`;

    // Windows Explorer-style expand/collapse arrow for directories
    const arrow = node.isDir
      ? `<span class="treeArrow">${node.__collapsed ? "▸" : "▾"}</span>`
      : `<span class="treeArrow" style="visibility:hidden">▸</span>`;

    const icon = node.isDir
      ? (node.__collapsed ? "📁" : "📂")
      : (node.name.toLowerCase().endsWith(".cs") ? "📄" 
         : node.name.toLowerCase().endsWith(".bin") ? "📦" 
         : "📄");

    const meta = node.isDir ? "" : `<span class="treeMeta">${fmtBytes(node.sizeBytes || 0)}</span>`;
    row.innerHTML = `${arrow}${icon} <span class="treeName">${escapeHtml(node.name)}</span>${meta}`;
    
    row.onclick = async (e) => {
      if (node.isDir) {
        // Toggle collapse
        node.__collapsed = !node.__collapsed;
        renderTree(cachedTreeRoot || root);
        return;
      }
      await openFileInTab(node.path);
    };
    host.appendChild(row);

    if (node.isDir && node.children && !node.__collapsed) {
      node.children.forEach((c) => walk(c, depth + 1));
    }
  };

  try {
    walk(root, 0);
  } catch (err) {
    logTerminal(`[ui] ERROR in renderTree walk: ${err.message || String(err)}`);
    console.error("renderTree walk failed:", err);
  }
}

// Tab management
let openTabs = [];
let activeTabId = "graph";

function openFileInTab(path) {
  // Check if already open
  const existing = openTabs.find(t => t.path === path);
  if (existing) {
    switchToTab(existing.id);
    return;
  }

  // Create new tab
  const tabId = "tab-" + Date.now();
  openTabs.push({
    id: tabId,
    path: path,
    name: fileNameFromPath(path),
    dirty: false
  });

  selectedFilePath = path;
  if (path.startsWith("assets/inputs/") && path.toLowerCase().endsWith(".cs")) {
    selectedAssetName = path.substring("assets/inputs/".length);
    logTerminal(`[ui] Selected input: ${selectedAssetName}`);
    // If artifact already exists from a previous session, show its variables immediately.
    const logic = logicNameFromCsPath(path);
    loadArtifactMetadata(lastBuildId, logic, { silent: true });
  }

  renderTabs();
  switchToTab(tabId);
  loadFileIntoTab(tabId, path);
}

function renderTabs() {
  const tabBar = byId("tabBar");
  tabBar.innerHTML = "";

  // Graph tab (always present, not closable)
  const graphTab = document.createElement("div");
  graphTab.className = "tab" + (activeTabId === "graph" ? " active" : "");
  graphTab.innerHTML = `<span class="tabName">Graph</span>`;
  graphTab.onclick = () => switchToTab("graph");
  tabBar.appendChild(graphTab);

  // File tabs
  openTabs.forEach(tab => {
    const tabEl = document.createElement("div");
    tabEl.className = "tab" + (activeTabId === tab.id ? " active" : "");
    tabEl.innerHTML = `<span class="tabName">${escapeHtml(tab.name)}${tab.dirty ? " *" : ""}</span><span class="tabClose" data-tab="${tab.id}">×</span>`;
    tabEl.querySelector(".tabName").onclick = () => switchToTab(tab.id);
    tabEl.querySelector(".tabClose").onclick = (e) => {
      e.stopPropagation();
      closeTab(tab.id);
    };
    tabBar.appendChild(tabEl);
  });
}

function switchToTab(tabId) {
  activeTabId = tabId;
  renderTabs();

  // Show/hide appropriate content
  const isGraphTab = tabId === "graph";
  byId("graphWrap").classList.toggle("hidden", !isGraphTab);
  
  const tabEditor = byId("tabEditor");
  if (tabEditor) {
    tabEditor.classList.toggle("hidden", isGraphTab);
  }

  if (!isGraphTab) {
    const tab = openTabs.find(t => t.id === tabId);
    if (tab) {
      if (tab.file) {
        // File already loaded
        currentFile = tab.file;
        const pathEl = byId("tabEditorPath");
        if (pathEl) pathEl.textContent = tab.path;
        
        // Apply correct mode and set content
        applyEditorMode(bestModeForFile(tab.file));
        setEditorContent(tab.file);
      } else {
        // File not loaded yet - load it now
        loadFileIntoTab(tabId, tab.path);
      }
    }
  }
}

function closeTab(tabId) {
  const tab = openTabs.find(t => t.id === tabId);
  if (tab && tab.dirty && !confirm("You have unsaved changes. Discard them?")) {
    return;
  }

  openTabs = openTabs.filter(t => t.id !== tabId);
  
  if (activeTabId === tabId) {
    // Switch to graph tab or last remaining tab
    activeTabId = openTabs.length > 0 ? openTabs[openTabs.length - 1].id : "graph";
  }

  renderTabs();
  switchToTab(activeTabId);
}

async function loadFileIntoTab(tabId, path) {
  try {
    const file = await api("GET", `/api/files/read?path=${encodeURIComponent(path)}`);
    
    const tab = openTabs.find(t => t.id === tabId);
    if (tab) {
      tab.file = file;
      tab.path = path;
    }

    if (activeTabId === tabId) {
      currentFile = file;
      const pathEl = byId("tabEditorPath");
      if (pathEl) pathEl.textContent = path;
      
      // Ensure editor exists and apply correct mode before setting content
      await ensureEditor();
      applyEditorMode(bestModeForFile(file));
      setEditorContent(file);

      // Build red-dot: if a .cs input differs from last built hash, mark rebuild needed.
      if (path.startsWith("assets/inputs/") && path.toLowerCase().endsWith(".cs")) {
        const src = file.text || "";
        const curHash = fnv1a32(src);
        const key = builtHashKeyForAsset(path.substring("assets/inputs/".length));
        const builtHash = localStorage.getItem(key);
        const needs = !builtHash || builtHash !== curHash;
        setBuildNeedsRebuild(needs, needs ? "Source changed — Build recommended" : "Up to date");
      } else {
        setBuildNeedsRebuild(false);
      }
    }
  } catch (err) {
    logTerminal(`[ui] Failed to load file: ${err.message}`);
  }
}

async function selectFile(path) {
  // Legacy function for compatibility - redirect to tab system
  await openFileInTab(path);
  await saveOnServer({ silent: true, reason: "selectFile" });
  await refreshTree();
}

async function loadFile(path) {
  currentFile = await api("GET", `/api/files/read?path=${encodeURIComponent(path)}`);
  const ep = byId("tabEditorPath");
  if (ep) ep.textContent = currentFile.path;
  setDirty(false);
  await ensureEditor();
  applyEditorMode(bestModeForFile(currentFile));
  setEditorContent(currentFile);
}

function bestModeForFile(file) {
  if (!file) return "text";
  if (file.kind === "binary") return "hex";
  // Fallback extension check
  const p = (file.path || "").toLowerCase();
  if (p.endsWith(".bin") || p.endsWith(".dll") || p.endsWith(".exe")) return "hex";
  return "text";
}

function applyEditorMode(mode) {
  editorMode = mode;
  const hexEditor = byId("tabHexEditor"); // Hidden textarea fallback
  const textEditor = byId("tabTextEditor"); // Hidden textarea fallback
  const host = byId("tabMonacoHost"); // CM container
  const hexTools = byId("hexTools");
  
  // Hide legacy editors
  if (hexEditor) hexEditor.classList.add("hidden");
  if (textEditor) textEditor.classList.add("hidden");
  if (host) host.classList.remove("hidden");
  if (hexTools) hexTools.classList.toggle("hidden", mode !== "hex");
  showHexSplit(mode === "hex");
  
  if (cmEditor) {
    if (mode === "hex") {
        cmEditor.setOption("mode", "hexdump");
        cmEditor.setOption("lineNumbers", false); // Offsets act as line numbers
        clearHexMarks();
        // Prevent CM from capturing keystrokes in hex mode (we use custom split editor)
        cmEditor.setOption("readOnly", "nocursor");
    } else {
        cmEditor.setOption("mode", "text/x-csharp"); // Default to C#, will be updated in setEditorContent
        cmEditor.setOption("lineNumbers", true);
        clearHexMarks();
        cmEditor.setOption("readOnly", false);
    }
    setTimeout(() => cmEditor.refresh(), 10);
  }
  if (mode === "hex") {
    // focus custom hex editor for keyboard edits
    setTimeout(() => byId("hexScroll")?.focus?.(), 0);
  } else {
    // Cancel any in-progress drag if we leave hex mode (e.g. tab close)
    try { byId("hexScroll")?.releasePointerCapture?.(1); } catch {}
  }
}

function setEditorContent(file) {
  if (!file) return;
  
  // Auto-detect mode if not explicitly set by user (or initial load)
  // Actually applyEditorMode is called by loadFile, so editorMode is correct.
  
  if (editorMode === "text" || (file.kind === "text" && editorMode !== "hex")) {
    const txt = file.text || "";
    if (cmEditor) {
      const lang = guessLang(file.path);
      cmEditor.setOption("mode", lang === "csharp" ? "text/x-csharp" : "null");
      if (cmEditor.getValue() !== txt) {
        cmEditor.setValue(txt);
        cmEditor.clearHistory();
      }
    }
  } else {
    // Binary/Hex mode
    const bytes = base64ToBytes(file.base64 || "");
    setHexBytes(bytes);
    renderHexSplit();
  }
}

function getEditorText() {
  if (!cmEditor) return "";
  return cmEditor.getValue();
}

function getEditorBytesBase64() {
  const v = ensureHexView();
  return bytesToBase64(v.bytes || new Uint8Array());
}

async function saveCurrentFile() {
  if (!currentFile) return;

  // If currently in hex mode, save as binary
  if (editorMode === "hex") {
    const base64 = getEditorBytesBase64();
    await api("POST", "/api/files/write", { path: currentFile.path, kind: "binary", base64 });
    logTerminal(`[ui] Saved (binary): ${currentFile.path}`);
  } else {
    // Text mode
    const text = getEditorText();
    await api("POST", "/api/files/write", { path: currentFile.path, kind: "text", text });
    logTerminal(`[ui] Saved: ${currentFile.path}`);
  }

  setDirty(false);
  await refreshTree();
}

function downloadCurrentFile() {
  if (!currentFile) return;
  if (currentFile.kind === "text") {
    const blob = new Blob([getEditorText()], { type: "text/plain" });
    triggerDownload(blob, fileNameFromPath(currentFile.path));
  } else {
    const bytes = base64ToBytes(currentFile.base64 || "");
    const blob = new Blob([bytes], { type: "application/octet-stream" });
    triggerDownload(blob, fileNameFromPath(currentFile.path));
  }
}

function triggerDownload(blob, name) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = name;
  a.click();
  URL.revokeObjectURL(url);
}

function fileNameFromPath(p) {
  const parts = String(p).split("/");
  return parts[parts.length - 1] || "download.bin";
}

function logicNameFromCsPath(path) {
  const name = fileNameFromPath(path || "");
  return name.toLowerCase().endsWith(".cs") ? name.slice(0, -3) : name;
}

function fnv1a32(str) {
  // Fast non-crypto hash for rebuild indicator
  let h = 0x811c9dc5;
  for (let i = 0; i < str.length; i++) {
    h ^= str.charCodeAt(i);
    // h *= 16777619 (FNV prime) with overflow
    h = (h + ((h << 1) + (h << 4) + (h << 7) + (h << 8) + (h << 24))) >>> 0;
  }
  return ("00000000" + h.toString(16)).slice(-8);
}

function builtHashKeyForAsset(selectedAssetNameOrPath) {
  const p = String(selectedAssetNameOrPath || "");
  return "builtHash:" + p.replaceAll("\\", "/");
}

function setBuildNeedsRebuild(on, title) {
  const btn = byId("btnBuild");
  if (!btn) return;
  btn.classList.toggle("needsBuild", !!on);
  if (title) btn.title = title;
}

let _buildBtnPrevText = null;
function setBuildBusyUi(on) {
  const btn = byId("btnBuild");
  if (!btn) return;
  if (on) {
    if (_buildBtnPrevText == null) _buildBtnPrevText = btn.textContent || "Build";
    btn.textContent = "Building…";
    btn.disabled = true;
    btn.classList.remove("needsBuild");
  } else {
    if (_buildBtnPrevText != null) btn.textContent = _buildBtnPrevText;
    _buildBtnPrevText = null;
    // other callers handle disabled state via setBusy(false)
  }
}

function guessLang(p) {
  const ext = (String(p).split(".").pop() || "").toLowerCase();
  if (ext === "cs") return "csharp";
  if (ext === "json") return "json";
  if (ext === "md") return "markdown";
  return "plaintext";
}

async function ensureEditor() {
  if (cmEditor) return;
  if (!window.CodeMirror) return;

  const mh = byId("cmHost");
  
  if (mh) mh.classList.remove("hidden");

  if (mh) {
    mh.innerHTML = ""; // Clear
    cmEditor = CodeMirror(mh, {
      value: "",
      mode: "text/x-csharp",
      theme: "monokai",
      lineNumbers: true,
      indentUnit: 4,
      matchBrackets: true,
      autoCloseBrackets: true,
      viewportMargin: Infinity
    });

    cmEditor.on("change", (cm, change) => {
      if (change.origin !== "setValue") setDirty(true);
    });

    // Hex tools UI (address jump + settings)
    const hexGo = byId("hexGo");
    const hexAddr = byId("hexAddr");
    const hexCols = byId("hexCols");
    const hexGroup = byId("hexGroup");
    const hexInsert = byId("hexInsertMode");

    const parseAddr = (s) => {
      const t = (s || "").trim().toLowerCase();
      if (!t) return null;
      if (t.startsWith("0x")) {
        const n = parseInt(t.substring(2), 16);
        return Number.isFinite(n) ? n : null;
      }
      const n = parseInt(t, 10);
      return Number.isFinite(n) ? n : null;
    };

    if (hexGo && hexAddr) {
      const go = () => {
        if (editorMode !== "hex") return;
        const off = parseAddr(hexAddr.value);
        if (off == null) return;
        const v = ensureHexView();
        setHexCaret(v, off, v.activeCol || "hex", 0);
        const hRow = v.rowTopEls[Math.floor(off / (hexBytesPerRow || 16))];
        hRow?.scrollIntoView?.({ block: "center" });
        byId("hexScroll")?.focus?.();
      };
      hexGo.onclick = go;
      hexAddr.addEventListener("keydown", (e) => {
        if (e.key === "Enter") go();
      });
    }

    const rerenderHex = () => {
      if (editorMode !== "hex") return;
      renderHexSplit();
    };

    if (hexCols) {
      // Initialize from DOM (in case HTML defaults change)
      const init = parseInt(hexCols.value, 10);
      if (Number.isFinite(init) && init > 0) hexBytesPerRow = init;
      hexCols.onchange = () => {
        const n = parseInt(hexCols.value, 10);
        if (Number.isFinite(n) && n > 0) hexBytesPerRow = n;
        rerenderHex();
      };
    }
    if (hexGroup) {
      const init = parseInt(hexGroup.value, 10);
      if (Number.isFinite(init) && init > 0) hexGroupSize = init;
      hexGroup.onchange = () => {
        const n = parseInt(hexGroup.value, 10);
        if (Number.isFinite(n) && n > 0) hexGroupSize = n;
        rerenderHex();
      };
    }
    if (hexInsert) {
      hexInsertMode = !!hexInsert.checked;
      hexInsert.onchange = () => { hexInsertMode = !!hexInsert.checked; };
    }

    // Wire custom split hex editor events once
    const scroll = byId("hexScroll");
    const hexColEl = byId("hexHexCol");
    const asciiColEl = byId("hexAsciiCol");
    if (scroll && !scroll.__hexBound) {
      scroll.__hexBound = true;

      const offFromEvent = (ev) => {
        const t = ev.target;
        const el = t && t.closest ? t.closest("[data-off]") : null;
        if (!el) return null;
        const n = parseInt(el.dataset.off, 10);
        return Number.isFinite(n) ? n : null;
      };

      const whichCol = (ev) => {
        const t = ev.target;
        if (hexColEl && hexColEl.contains(t)) return "hex";
        if (asciiColEl && asciiColEl.contains(t)) return "ascii";
        return "hex";
      };

      let dragging = false;
      const cancelDrag = () => { dragging = false; };

      scroll.addEventListener("pointerdown", (ev) => {
        if (editorMode !== "hex") return;
        const off = offFromEvent(ev);
        if (off == null) return;
        const v = ensureHexView();
        clearHexSelectionClasses(v);
        dragging = true;
        v.selA = off;
        v.selB = off;
        setHexCaret(v, off, whichCol(ev), 0);
        applyHexSelectionClasses(v);
        // On some browsers / timing (e.g. closing tab while dragging), pointerId may not be active.
        try {
          if (typeof ev.pointerId === "number") scroll.setPointerCapture?.(ev.pointerId);
        } catch {
          // ignore
        }
        ev.preventDefault();
      });
      scroll.addEventListener("pointermove", (ev) => {
        if (editorMode !== "hex") return;
        if (!dragging) return;
        const off = offFromEvent(ev);
        if (off == null) return;
        const v = ensureHexView();
        clearHexSelectionClasses(v);
        v.selB = off;
        applyHexSelectionClasses(v);
        updateHexInspector();
        ev.preventDefault();
      });
      scroll.addEventListener("pointerup", (ev) => {
        if (editorMode !== "hex") return;
        cancelDrag();
        try {
          if (typeof ev.pointerId === "number") scroll.releasePointerCapture?.(ev.pointerId);
        } catch {}
      });
      scroll.addEventListener("lostpointercapture", () => cancelDrag());
      scroll.addEventListener("blur", () => cancelDrag());
      window.addEventListener("pointercancel", () => cancelDrag(), true);

      scroll.addEventListener("keydown", (ev) => {
        if (editorMode !== "hex") return;
        const v = ensureHexView();

        const getSelRange = () => {
          if (v.selA == null || v.selB == null) return null;
          const lo = clamp(Math.min(v.selA, v.selB), 0, Math.max(0, v.bytes.length - 1));
          const hi = clamp(Math.max(v.selA, v.selB), 0, Math.max(0, v.bytes.length - 1));
          return { lo, hi };
        };

        // navigation
        if (ev.key === "ArrowLeft") { setHexCaret(v, v.caretOff - 1, v.activeCol, 0); ev.preventDefault(); return; }
        if (ev.key === "ArrowRight") { setHexCaret(v, v.caretOff + 1, v.activeCol, 0); ev.preventDefault(); return; }
        if (ev.key === "ArrowUp") { setHexCaret(v, v.caretOff - (hexBytesPerRow || 16), v.activeCol, 0); ev.preventDefault(); return; }
        if (ev.key === "ArrowDown") { setHexCaret(v, v.caretOff + (hexBytesPerRow || 16), v.activeCol, 0); ev.preventDefault(); return; }

        // Copy selection (Hex or ASCII only) based on active column
        if ((ev.ctrlKey || ev.metaKey) && (ev.key === "c" || ev.key === "C")) {
          const r = getSelRange() || { lo: v.caretOff, hi: v.caretOff };
          const slice = v.bytes.slice(r.lo, r.hi + 1);
          let text = "";
          if (v.activeCol === "ascii") {
            for (const b of slice) text += byteToAscii(b);
          } else {
            // "DE AD BE EF" format
            const group = hexGroupSize || 8;
            for (let i = 0; i < slice.length; i++) {
              text += slice[i].toString(16).toUpperCase().padStart(2, "0");
              if (i !== slice.length - 1) text += " ";
              if (group > 0 && (i % group === group - 1) && i !== slice.length - 1) text += " ";
            }
          }
          ev.preventDefault();
          navigator.clipboard?.writeText?.(text).catch(() => {});
          return;
        }

        // Ctrl+I insert bytes at caret
        if ((ev.ctrlKey || ev.metaKey) && (ev.key === "i" || ev.key === "I")) {
          ev.preventDefault();
          const s = prompt("Insert hex bytes at cursor (e.g. DE AD BE EF):", "");
          if (!s) return;
          const cleaned = s.replace(/0x/gi, " ").replace(/[^0-9a-fA-F]/g, " ").trim();
          const parts = cleaned.split(/\s+/).filter(Boolean);
          const vals = parts.map(p => parseInt(p, 16)).filter(n => Number.isFinite(n) && n >= 0 && n <= 255);
          if (!vals.length) return;
          const arr = Array.from(v.bytes);
          arr.splice(v.caretOff, 0, ...vals);
          v.bytes = new Uint8Array(arr);
          setDirty(true);
          renderHexSplit();
          setHexCaret(v, v.caretOff + vals.length, v.activeCol, 0);
          return;
        }

        if (ev.ctrlKey || ev.metaKey || ev.altKey) return;

        // edit
        if (v.activeCol === "hex") {
          if (!/^[0-9a-fA-F]$/.test(ev.key)) return;
          ev.preventDefault();
          const nib = parseInt(ev.key, 16);
          const old = v.bytes[v.caretOff] ?? 0;
          const nb = v.caretNibble === 0 ? ((nib << 4) | (old & 0x0F)) : ((old & 0xF0) | nib);
          v.bytes[v.caretOff] = nb;
          // update DOM for this byte only
          const he = v.hexEls[v.caretOff];
          const ae = v.asciiEls[v.caretOff];
          if (he) he.textContent = nb.toString(16).toUpperCase().padStart(2, "0");
          if (ae) ae.textContent = byteToAscii(nb);
          setDirty(true);
          if (v.caretNibble === 0) setHexCaret(v, v.caretOff, "hex", 1);
          else setHexCaret(v, v.caretOff + 1, "hex", 0);
          return;
        }

        if (v.activeCol === "ascii") {
          if (ev.key.length !== 1) return;
          const code = ev.key.charCodeAt(0);
          if (code < 32 || code > 126) return;
          ev.preventDefault();
          if (hexInsertMode) {
            const arr = Array.from(v.bytes);
            arr.splice(v.caretOff, 0, code);
            v.bytes = new Uint8Array(arr);
            setDirty(true);
            renderHexSplit();
            setHexCaret(v, v.caretOff + 1, "ascii", 0);
          } else {
            v.bytes[v.caretOff] = code;
            const he = v.hexEls[v.caretOff];
            const ae = v.asciiEls[v.caretOff];
            if (he) he.textContent = code.toString(16).toUpperCase().padStart(2, "0");
            if (ae) ae.textContent = byteToAscii(code);
            setDirty(true);
            setHexCaret(v, v.caretOff + 1, "ascii", 0);
          }
        }
      });
    }
  }
}

function bytesToHex(bytes) {
  const bpr = hexBytesPerRow || 16;
  const group = hexGroupSize || 8;
  let out = "";
  for (let i = 0; i < bytes.length; i += bpr) {
    // Offset
    out += i.toString(16).padStart(8, "0").toUpperCase() + "  ";
    
    // Hex bytes
    for (let j = 0; j < bpr; j++) {
      if (i + j < bytes.length) {
        out += bytes[i + j].toString(16).padStart(2, "0").toUpperCase() + " ";
      } else {
        out += "   ";
      }
      if (group > 0 && (j % group === group - 1) && j !== bpr - 1) out += " "; // group padding
    }
    
    // ASCII
    out += " |";
    for (let j = 0; j < bpr; j++) {
      if (i + j < bytes.length) {
        const b = bytes[i + j];
        out += (b >= 32 && b <= 126) ? String.fromCharCode(b) : ".";
      } else {
        out += " "; // Padding for incomplete line
      }
    }
    out += "|\n";
  }
  return out.trimEnd();
}

function hexToBytes(hex) {
  // Parse the "hexdump" format
  // We only care about the hex part between offset (first 10 chars) and the ASCII bar '|'
  const lines = hex.split('\n');
  const bytes = [];
  
  for(const line of lines) {
     if (line.length < 12) continue; // Skip short lines
     const bar = line.indexOf("|");
     const hexPart = (bar > 0 ? line.substring(10, bar) : line.substring(10)).trim();
     const parts = hexPart.split(' ').filter(p => p.length === 2);
     for(const part of parts) bytes.push(parseInt(part, 16));
  }
  return new Uint8Array(bytes);
}

// ... rest of functions


function base64ToBytes(b64) {
  const bin = atob(b64 || "");
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}

function bytesToBase64(bytes) {
  let bin = "";
  for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
  return btoa(bin);
}

function initAssetDrop() {
  const drop = byId("assetsDrop");

  const setDrag = (on) => drop.classList.toggle("drag", on);
  ["dragenter", "dragover"].forEach((ev) =>
    drop.addEventListener(ev, (e) => {
      e.preventDefault();
      setDrag(true);
    })
  );
  ["dragleave", "drop"].forEach((ev) =>
    drop.addEventListener(ev, (e) => {
      e.preventDefault();
      setDrag(false);
    })
  );

  drop.addEventListener("drop", async (e) => {
    const file = e.dataTransfer.files?.[0];
    if (!file) return;
    if (!file.name.toLowerCase().endsWith(".cs")) {
      logTerminal("[ui] Only .cs files are supported.");
      return;
    }
    const fd = new FormData();
    fd.append("file", file, file.name);
    await api("POST", "/api/assets/upload", fd);
    logTerminal(`[ui] Uploaded: ${file.name}`);
    await refreshTree();
    await selectFile(`assets/inputs/${file.name}`);
  });
}

// --- SignalR terminal + variables ---

async function initSignalR() {
  if (!window.signalR) {
    logTerminal("[ui] SignalR not available - real-time updates disabled.");
    return;
  }
  
  try {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/terminal")
      .withAutomaticReconnect()
      .build();

    conn.on("terminalLine", (line) => logTerminal(line));
    conn.on("varsSnapshot", (snap) => renderVars(snap));
    conn.on("nodeSnapshot", (snap) => applyNodeSnapshot(snap));

    await conn.start();
    logTerminal("[ui] Connected to terminal hub.");
  } catch (err) {
    logTerminal(`[ui] SignalR connection failed: ${err.message}`);
  }
}

let currentArtifact = null; // Store current artifact metadata
let isRunning = false;
let _lastLoadedArtifactLogic = null;

function renderVars(snap) {
  const host = byId("vars");
  
  // Check if we have a built artifact
  if (!currentArtifact) {
    host.innerHTML = '<div class="varsEmpty">Artifact not available<br><span class="muted">Build a .cs file first</span></div>';
    return;
  }

  // If we have an artifact but it's from SignalR update
  if (snap && snap.fields) {
    isRunning = true;
    const rows = snap.fields
      .map((f) => {
        const icon = f.icon === "arrow-down" ? "⬇" : f.icon === "arrow-up" ? "⬆" : "○";
        const editable = f.icon === "arrow-up" ? ' contenteditable="true" class="editable"' : '';
        return `<tr><td class="varName">${icon} ${escapeHtml(f.name)} <span class="muted">(${f.type})</span></td><td class="varValue"${editable}>${escapeHtml(
          f.value
        )}</td></tr>`;
      })
      .join("");
    host.innerHTML = `<div class="varsHeader">${escapeHtml(snap.targetType || currentArtifact.name)}</div><table class="varsTable">${rows}</table>`;
    return;
  }

  // Show artifact variables but with "Not Running" status
  if (currentArtifact.variables) {
    const rows = currentArtifact.variables
      .map((v) => {
        const icon = v.direction === "output" ? "⬆" : "⬇";
        const editable = v.direction === "output" ? ' contenteditable="true" class="editable"' : '';
        return `<tr><td class="varName">${icon} ${escapeHtml(v.name)} <span class="muted">(${v.type})</span></td><td class="varValue notRunning"${editable}>Not Running</td></tr>`;
      })
      .join("");
    host.innerHTML = `<div class="varsHeader">${escapeHtml(currentArtifact.name)}</div><table class="varsTable">${rows}</table>`;
  } else {
    host.innerHTML = '<div class="varsEmpty">No variables in artifact</div>';
  }
}

function loadArtifactMetadata(buildId, logicName, opts) {
  const options = opts || {};
  const silent = options.silent === true;
  // Load artifact metadata to display variables
  // The .bin.json file contains AsUpperIO variables (outputs from MCU)
  // Artifacts are now directly under generated/ (no timestamped subfolders)
  api("GET", `/api/files/read?path=assets/generated/${logicName}.bin.json`)
    .then(file => {
      try {
        const fields = JSON.parse(file.text);
        // Parse the field metadata: [{"field":"name", "typeid":N, "offset":N, "flags":N}, ...]
        // flags: 0x01=UpperIO (Host->MCU), 0x02=LowerIO (MCU->Host), 0x00=Mutual
        const variables = fields.map(f => {
          const flags = f.flags || 0x00; // backward compatibility: default to Mutual if flags missing
          let direction, icon;
          if (flags === 0x01) {
            direction = "input"; // UpperIO: Host sends to MCU
            icon = "arrow-up";
          } else if (flags === 0x02) {
            direction = "output"; // LowerIO: MCU sends to Host
            icon = "arrow-down";
          } else {
            direction = "mutual"; // Mutual: bidirectional
            icon = "circle";
          }
          return {
            name: f.field,
            type: getTypeNameFromId(f.typeid),
            direction: direction,
            icon: icon,
            offset: f.offset,
            flags: flags
          };
        });
        
        const upperCount = variables.filter(v => v.flags === 0x01).length;
        const lowerCount = variables.filter(v => v.flags === 0x02).length;
        const mutualCount = variables.filter(v => v.flags === 0x00).length;
        
        currentArtifact = {
          name: logicName,
          buildId: buildId,
          variables: variables
        };
        _lastLoadedArtifactLogic = logicName;
        
        if (!silent) {
          const parts = [];
          if (upperCount > 0) parts.push(`${upperCount} UpperIO`);
          if (lowerCount > 0) parts.push(`${lowerCount} LowerIO`);
          if (mutualCount > 0) parts.push(`${mutualCount} Mutual`);
          logTerminal(`[ui] Loaded artifact metadata: ${variables.length} variable(s) (${parts.join(", ")})`);
        }
        renderVars();
      } catch (err) {
        console.error("Failed to parse artifact metadata:", err);
        if (!silent) logTerminal(`[ui] ERROR parsing artifact metadata: ${err.message}`);
        currentArtifact = { name: logicName, variables: [] };
        _lastLoadedArtifactLogic = logicName;
        renderVars();
      }
    })
    .catch(err => {
      console.error("Failed to load artifact metadata:", err);
      // Missing artifact is common on fresh projects; avoid noisy logs.
      const msg = String(err?.message || err);
      const is404 = msg.includes(" 404 ") || msg.includes("failed: 404") || msg.includes("404");
      if (!silent || !is404) logTerminal(`[ui] ERROR loading artifact metadata: ${msg}`);
      currentArtifact = null;
      renderVars();
    });
}

function getTypeNameFromId(typeid) {
  // Type IDs from DIVER compiler
  const types = {
    0: "bool",
    1: "byte",
    2: "sbyte",
    3: "short",
    4: "ushort",
    5: "int",
    6: "uint",
    7: "long",
    8: "ulong",
    9: "float",
    10: "double",
    11: "char",
    12: "string",
    13: "object",
    14: "decimal",
    15: "datetime",
    16: "int[]",
    17: "byte[]",
    18: "float[]",
    19: "string[]",
    20: "bool[]"
  };
  return types[typeid] || `type${typeid}`;
}

function escapeHtml(s) {
  return String(s)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

// --- Buttons ---

function initButtons() {
  // Initialize Variables panel
  renderVars();

  byId("btnAddNode").onclick = () => addNode();

  byId("btnNew").onclick = () => {
    byId("newProjectDialog").classList.remove("hidden");
  };

  byId("btnNewClear").onclick = async () => {
    byId("newProjectDialog").classList.add("hidden");
    await createNewProject(false);
  };

  byId("btnNewExport").onclick = async () => {
    byId("newProjectDialog").classList.add("hidden");
    logTerminal("[ui] Exporting current project...");
    exportProject();
    await new Promise(r => setTimeout(r, 500));
    await createNewProject(false);
  };

  byId("btnNewCancel").onclick = () => {
    byId("newProjectDialog").classList.add("hidden");
    logTerminal("[ui] New project cancelled.");
  };

  async function createNewProject(keepAssets) {
    await api("POST", "/api/project/new");
    
    // Clear graph and ensure only one root
    if (graph) {
      graph.clear();
      ensureRoot();
    }
    
    selectedAssetName = null;
    selectedFilePath = null;
    lastBuildId = null;
    currentFile = null;
    currentArtifact = null;
    openTabs = [];
    activeTabId = "graph";
    
    renderTabs();
    renderVars();
    switchToTab("graph");
    
    setDirty(false);
    await refreshTree();
    logTerminal("[ui] New project created.");
  }

  byId("btnSaveServer").onclick = () => saveOnServer({ silent: false });
  byId("btnExport").onclick = exportProject;
  byId("btnImport").onclick = () => byId("importFile").click();
  byId("importFile").onchange = (e) => {
    const file = e.target.files?.[0];
    if (file) importProjectFile(file);
  };

  const connectBtn = byId("btnConnectAll");
  if (connectBtn) {
    connectBtn.style.display = "";
    connectBtn.onclick = () => connectSession().catch((err) => logTerminal(`[ui] Connect failed: ${err.message || String(err)}`));
  }

  byId("btnBuild").onclick = async () => {
    try {
      setBuildBusyUi(true);
      setBusy(true);
      await saveOnServer({ silent: true });
      if (!selectedAssetName) {
        logTerminal("[ui] Select an input .cs file first.");
        return;
      }
      const res = await api("POST", "/api/build");
      lastBuildId = res.buildId || lastBuildId;
      logTerminal(`[ui] Build done. buildId=${lastBuildId}`);

      // Update rebuild indicator: store built hash for this asset (based on current editor content if open)
      const tab = openTabs.find(t => t.path === ("assets/inputs/" + selectedAssetName));
      const src = tab?.file?.text || (currentFile?.path === ("assets/inputs/" + selectedAssetName) ? (currentFile.text || "") : "");
      if (src) {
        const builtHash = fnv1a32(src);
        localStorage.setItem(builtHashKeyForAsset(selectedAssetName), builtHash);
        setBuildNeedsRebuild(false, "Up to date");
      }
      
      // Load artifact metadata for variables panel
      if (res.artifacts && res.artifacts.length > 0) {
        loadArtifactMetadata(lastBuildId, res.artifacts[0], { silent: false });
      }
      
      await loadServerProject().catch(() => {});
      await refreshTree();
      if (selectedFilePath) await loadFile(selectedFilePath).catch(() => {});
    } catch (e) {
      logTerminal(String(e));
    } finally {
      setBuildBusyUi(false);
      setBusy(false);
    }
  };

  byId("btnClearTerminal").onclick = () => {
    byId("terminal").innerHTML = "";
  };

  byId("btnRun").onclick = async () => {
    try {
      setBusy(true);
      await saveOnServer({ silent: true });
      if (!selectedAssetName) {
        logTerminal("[ui] Select a .cs asset first.");
        return;
      }
      await api("POST", "/api/start");
      logTerminal("[ui] Start requested.");
    } catch (e) {
      logTerminal(String(e));
    } finally {
      setBusy(false);
    }
  };
  byId("btnStop").onclick = async () => {
    await api("POST", "/api/stop");
    logTerminal("[ui] Stop requested.");
  };

  byId("cmdSend").onclick = async () => {
    const cmd = byId("cmdInput").value || "";
    if (!cmd.trim()) return;
    byId("cmdInput").value = "";
    await api("POST", "/api/command", { command: cmd });
  };

  // Tab Editor controls
  // if (byId("btnTabEditorText")) byId("btnTabEditorText").onclick = () => applyEditorMode("text");
  // if (byId("btnTabEditorHex")) byId("btnTabEditorHex").onclick = () => applyEditorMode("hex");
  // Hide manual switch buttons, rely on auto-detect
  if (byId("btnTabEditorText")) byId("btnTabEditorText").classList.add("hidden");
  if (byId("btnTabEditorHex")) byId("btnTabEditorHex").classList.add("hidden");
  
  if (byId("btnTabEditorSave")) byId("btnTabEditorSave").onclick = saveCurrentFile;
  if (byId("btnTabEditorDownload")) byId("btnTabEditorDownload").onclick = downloadCurrentFile;
  if (byId("btnTabEditorDelete")) byId("btnTabEditorDelete").onclick = async () => {
    if (!currentFile) return;
    if (!currentFile.path?.startsWith("assets/")) {
      logTerminal("[ui] Delete allowed only under assets/.");
      return;
    }
    if (!confirm(`Delete ${currentFile.path}?`)) return;
    await api("POST", "/api/files/delete", { path: currentFile.path });
    logTerminal(`[ui] Deleted: ${currentFile.path}`);
    
    // Close the current tab
    const tab = openTabs.find(t => t.path === currentFile.path);
    if (tab) closeTab(tab.id);
    
    currentFile = null;
    selectedFilePath = null;
    setDirty(false);
    await saveOnServer({ silent: true });
    await refreshTree();
  };

  // "+ New Input" removed from UI (drop a .cs file is the supported path).
  // Keep the dialog around for possible future use, but do not wire it if the button is gone.
  const btnNewInput = byId("btnNewInput");
  if (btnNewInput) btnNewInput.style.display = "none";
  const btnNewInputOk = byId("btnNewInputOK");
  const btnNewInputCancel = byId("btnNewInputCancel");
  const newInputName = byId("newInputName");
  const newInputDialog = byId("newInputDialog");
  if (btnNewInput && btnNewInputOk && btnNewInputCancel && newInputName && newInputDialog) {
    btnNewInput.onclick = () => {
      newInputName.value = "NewLogic";
      newInputDialog.classList.remove("hidden");
      setTimeout(() => newInputName.focus(), 100);
    };

    btnNewInputOk.onclick = async () => {
      const name = newInputName.value || "NewLogic";
      newInputDialog.classList.add("hidden");
      try {
        const r = await api("POST", "/api/files/newInput", { name });
        await refreshTree();
        await openFileInTab(r.path);
        await saveOnServer({ silent: true });
      } catch (err) {
        logTerminal(`[ui] ERROR creating input: ${err.message}`);
      }
    };

    btnNewInputCancel.onclick = () => {
      newInputDialog.classList.add("hidden");
    };

    newInputName.addEventListener("keydown", (e) => {
      if (e.key === "Enter") btnNewInputOk.click();
    });
  }

  // Tree filter removed - not needed

  // Tab editor event listeners
  if (byId("tabTextEditor")) byId("tabTextEditor").addEventListener("input", () => setDirty(true));
  if (byId("tabHexEditor")) byId("tabHexEditor").addEventListener("input", () => setDirty(true));

  window.addEventListener("keydown", (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
      e.preventDefault();
      saveCurrentFile().catch((err) => logTerminal(String(err)));
    }
  });
}

// --- Boot ---

(async function boot() {
  try {
    // Wait for LiteGraph to be available
    let retries = 0;
    while ((!window.LiteGraph || !window.LGraph) && retries < 50) {
      await new Promise(resolve => setTimeout(resolve, 100));
      retries++;
    }
    
    if (!window.LiteGraph || !window.LGraph) {
      console.error("FATAL: LiteGraph library failed to load after 5 seconds");
      alert("Failed to load required libraries. Please refresh the page.");
      return;
    }
    
    logTerminal("[ui] Booting Coralinker UI...");
    initGraph();
    initAssetDrop();
    renderTabs(); // Initialize empty tab bar with Graph tab
    switchToTab("graph"); // Ensure Graph tab is active and editor is hidden
    initButtons();
    
    logTerminal("[ui] Loading project...");
    await loadServerProject();
    
    logTerminal("[ui] Loading file tree...");
    await refreshTree();
    
    if (selectedFilePath) {
      logTerminal(`[ui] Loading selected file: ${selectedFilePath}`);
      await loadFile(selectedFilePath).catch((err) => {
        logTerminal(`[ui] Failed to load file: ${err.message}`);
      });
    } else if (selectedAssetName) {
      const path = `assets/inputs/${selectedAssetName}`;
      logTerminal(`[ui] Loading selected asset: ${path}`);
      await loadFile(path).catch((err) => {
        logTerminal(`[ui] Failed to load asset: ${err.message}`);
      });
    }
    
    logTerminal("[ui] Connecting SignalR...");
    await initSignalR();
    logTerminal("[ui] Boot complete.");
    booting = false;
  } catch (err) {
    logTerminal(`[ui] FATAL ERROR during boot: ${err.message || String(err)}`);
    console.error("Boot failed:", err);
  }
})();


