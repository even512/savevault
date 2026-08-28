/* =====================================================================
   SaveVault Dashboard – Frontend-Logik (self-contained, keine Fremd-Libs)
   ---------------------------------------------------------------------
   Sicherheit:
   - ALLE Fremddaten (Spiel-/Client-/Gerätenamen, Store, Prüfsummen,
     Aktionstexte …) werden ausschliesslich über textContent in den DOM
     gesetzt. innerHTML wird NUR mit festen, im Code definierten SVG-
     Konstanten (ICONS) benutzt – nie mit Serverdaten.
   - Das Master-Token liegt in sessionStorage und wird nur als
     Authorization-Header gesendet; es wird nie ins DOM oder in die
     Konsole geschrieben.
   ===================================================================== */
(function () {
  "use strict";

  // ---- feste Icon-SVGs (vom Code kontrolliert – innerHTML hierfür ok) ----
  const ICONS = {
    logo: '<svg viewBox="0 0 24 24" width="30" height="30" fill="none" aria-hidden="true"><path d="M4 2h12.5L20 5.5V21a1 1 0 01-1 1H4a1 1 0 01-1-1V3a1 1 0 011-1z" fill="white"></path><rect x="7" y="2.4" width="8" height="6" rx="0.6" fill="oklch(0.4 0.07 195)"></rect><rect x="6" y="12.6" width="12" height="8" rx="1.1" fill="oklch(0.4 0.07 195)"></rect><rect x="7.4" y="14.4" width="9.2" height="1.1" rx="0.55" fill="white" opacity="0.55"></rect><rect x="7.4" y="16.8" width="6.2" height="1.1" rx="0.55" fill="white" opacity="0.55"></rect></svg>',
    grid: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><rect x="3" y="3" width="7" height="7" rx="1.5"></rect><rect x="14" y="3" width="7" height="7" rx="1.5"></rect><rect x="3" y="14" width="7" height="7" rx="1.5"></rect><rect x="14" y="14" width="7" height="7" rx="1.5"></rect></svg>',
    disc: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><circle cx="12" cy="12" r="9"></circle><circle cx="12" cy="12" r="2.5"></circle></svg>',
    monitor: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><rect x="2" y="4" width="20" height="13" rx="2"></rect><path d="M8 21h8M12 17v4"></path></svg>',
    clock: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><circle cx="12" cy="12" r="9"></circle><path d="M12 7v5l3 3"></path></svg>',
    settings: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 11-2.83 2.83l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09a1.65 1.65 0 00-1-1.51 1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 11-2.83-2.83l.06-.06a1.65 1.65 0 00.33-1.82 1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09a1.65 1.65 0 001.51-1 1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 112.83-2.83l.06.06a1.65 1.65 0 001.82.33H9a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 112.83 2.83l-.06.06a1.65 1.65 0 00-.33 1.82V9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z"></path></svg>',
    chevron: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><path d="M9 18l6-6-6-6"></path></svg>',
    search: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><circle cx="11" cy="11" r="7"></circle><path d="M21 21l-4.3-4.3"></path></svg>',
    refresh: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><path d="M23 4v6h-6M1 20v-6h6"></path><path d="M3.51 9a9 9 0 0114.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0020.49 15"></path></svg>',
    close: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><path d="M18 6L6 18M6 6l12 12"></path></svg>',
    upload: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><path d="M12 16V4M6 10l6-6 6 6M4 20h16"></path></svg>',
    download: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><path d="M12 4v12M6 10l6 6 6-6M4 20h16"></path></svg>',
    warn: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><path d="M12 2L1 21h22L12 2z"></path><path d="M12 9v5M12 18h.01"></path></svg>',
    restore: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><path d="M3 12a9 9 0 109-9 9.75 9.75 0 00-6.74 2.74L3 8"></path><path d="M3 3v5h5"></path></svg>',
    link: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="100%" height="100%"><path d="M10 13a5 5 0 007.54.54l3-3a5 5 0 00-7.07-7.07l-1.72 1.71"></path><path d="M14 11a5 5 0 00-7.54-.54l-3 3a5 5 0 007.07 7.07l1.71-1.71"></path></svg>'
  };

  const TOKEN_KEY = "savevault.token";

  // ---- Status: feste Zuordnung Enum -> CSS-Klasse + deutscher Text -------
  const STATUS = {
    Synced:   { cls: "synced",   label: "Synchronisiert",      pulse: false },
    Syncing:  { cls: "syncing",  label: "Wird synchronisiert", pulse: true },
    Conflict: { cls: "conflict", label: "Konflikt",            pulse: true },
    Pending:  { cls: "pending",  label: "Ausstehend",          pulse: false },
    Offline:  { cls: "offline",  label: "Offline",             pulse: false },
    Error:    { cls: "error",    label: "Fehler",              pulse: false }
  };
  function statusMeta(key) { return STATUS[key] || { cls: "offline", label: String(key || "Unbekannt"), pulse: false }; }

  // Aktions-Zuordnung für den Verlauf (feste, vom Server gelieferte Codes).
  const ACTIONS = {
    upload:   { label: "Hochgeladen",       cls: "synced",   icon: "upload" },
    download: { label: "Heruntergeladen",   cls: "syncing",  icon: "download" },
    conflict: { label: "Konflikt",          cls: "conflict", icon: "warn" },
    resolve:  { label: "Konflikt gelöst",   cls: "synced",   icon: "warn" },
    restore:  { label: "Wiederhergestellt", cls: "syncing",  icon: "restore" },
    pair:     { label: "Gerät gekoppelt",   cls: "synced",   icon: "link" }
  };
  function actionMeta(a) { return ACTIONS[a] || { label: String(a || "—"), cls: "offline", icon: "upload" }; }

  const NAV = [
    { key: "dashboard", label: "Dashboard",     icon: "grid" },
    { key: "games",     label: "Spiele",        icon: "disc" },
    { key: "clients",   label: "Clients",       icon: "monitor" },
    { key: "history",   label: "Verlauf",       icon: "clock" },
    { key: "settings",  label: "Einstellungen", icon: "settings" }
  ];
  const VIEW_TITLES = {
    dashboard: ["Dashboard", "Übersicht über Spiele, Clients und Sync-Status"],
    games:     ["Spiele", "Alle erfassten Spielstände im Heimnetzwerk"],
    clients:   ["Clients", "Geräte, die SaveVault synchronisieren"],
    history:   ["Verlauf", "Verlauf aller Sync-Vorgänge"],
    settings:  ["Einstellungen", "Server, Sync und Speicherverwaltung"]
  };

  // ---- Anwendungszustand -------------------------------------------------
  const state = {
    token: sessionStorage.getItem(TOKEN_KEY) || null,
    view: "dashboard",
    search: "",
    gameFilter: "all",
    historyFilter: "all",
    data: { games: [], devices: [], conflicts: [], activity: [], pairing: null, gameStates: [], serverInfo: null },
    revCache: {},          // gameKeyValue -> RevisionInfo[]
    expandedHistory: {},
    // lokale (nicht server-persistente) Einstellungen – als Anzeige/Spielerei
    localSettings: { interval: 5, retention: 0, autoConflict: true, notify: true },
    loaded: false
  };

  // =====================================================================
  // DOM-Helfer (XSS-sicher: Text nur via textContent)
  // =====================================================================
  function el(tag, opts, children) {
    const node = document.createElement(tag);
    if (opts) {
      if (opts.class) node.className = opts.class;
      if (opts.text != null) node.textContent = String(opts.text);
      if (opts.title != null) node.title = String(opts.title);
      if (opts.type) node.type = opts.type;
      if (opts.attrs) for (const k in opts.attrs) node.setAttribute(k, opts.attrs[k]);
      if (opts.style) for (const k in opts.style) node.style.setProperty(k, opts.style[k]);
      if (opts.on) for (const ev in opts.on) node.addEventListener(ev, opts.on[ev]);
    }
    if (children != null) {
      const arr = Array.isArray(children) ? children : [children];
      for (const c of arr) { if (c == null) continue; node.appendChild(typeof c === "string" ? document.createTextNode(c) : c); }
    }
    return node;
  }
  // Icon: setzt innerHTML NUR aus fester ICONS-Konstante.
  function iconEl(name, cls) {
    const span = el("span", { class: cls || "" });
    span.setAttribute("aria-hidden", "true");
    span.innerHTML = ICONS[name] || "";
    return span;
  }
  function clear(node) { while (node.firstChild) node.removeChild(node.firstChild); }

  // Statuszeile (Punkt + Text), rein aus fester Klassenmenge.
  function statusLine(statusKey, extraText, onClick) {
    const m = statusMeta(statusKey);
    const dot = el("span", { class: "dot dot--lg dot--" + m.cls + (m.pulse ? " is-pulse" : "") });
    const line = el("span", { class: "status-line status--" + m.cls + (onClick ? " status-clickable" : "") },
      [dot, document.createTextNode(extraText != null ? extraText : m.label)]);
    if (onClick) line.addEventListener("click", onClick);
    return line;
  }

  // ---- Formatierung ------------------------------------------------------
  function parseDate(s) {
    if (!s) return null;
    // Server liefert UTC; falls kein Zeitzonen-Suffix, als UTC interpretieren.
    if (!/[zZ]$|[+-]\d\d:?\d\d$/.test(s)) s = s + "Z";
    const d = new Date(s);
    return isNaN(d.getTime()) ? null : d;
  }
  function relTime(s) {
    const d = parseDate(s);
    if (!d) return "—";
    const diff = Date.now() - d.getTime();
    const min = Math.floor(diff / 60000);
    if (min < 1) return "jetzt";
    if (min < 60) return "vor " + min + " Min.";
    const hrs = Math.floor(min / 60);
    if (hrs < 24) return "vor " + hrs + " Std.";
    const days = Math.floor(hrs / 24);
    if (days === 1) return "gestern";
    if (days < 7) return "vor " + days + " Tagen";
    return d.toLocaleDateString("de-DE", { day: "2-digit", month: "2-digit", year: "numeric" });
  }
  function absTime(s) {
    const d = parseDate(s);
    if (!d) return "—";
    return d.toLocaleString("de-DE", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });
  }
  function formatBytes(n) {
    if (n == null || isNaN(n)) return "—";
    if (n < 1024) return n + " B";
    const units = ["KB", "MB", "GB", "TB"];
    let v = n / 1024, i = 0;
    while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
    return (v < 10 ? v.toFixed(1) : Math.round(v)) + " " + units[i];
  }
  function shortHash(h) {
    if (!h) return "—";
    return h.length > 12 ? h.slice(0, 4) + "…" + h.slice(-4) : h;
  }
  // Deterministische Cover-Farbe aus dem Schlüssel (nur Zahl -> feste oklch-Vorlage).
  function coverColor(key) {
    let hash = 0;
    const str = String(key || "");
    for (let i = 0; i < str.length; i++) hash = (hash * 31 + str.charCodeAt(i)) >>> 0;
    const hue = hash % 360;
    return "linear-gradient(150deg, oklch(0.52 0.13 " + hue + "), oklch(0.2 0.015 280))";
  }

  // ---- Datenzugriff ------------------------------------------------------
  function deviceName(id) {
    const d = state.data.devices.find(x => x.id === id);
    return d ? d.name : (id || "Unbekanntes Gerät");
  }
  // IP-Adresse eines Geräts (Fremddaten -> nur via textContent verwenden). Null -> „—".
  function deviceIp(device) {
    return device && device.ipAddress ? device.ipAddress : "—";
  }
  // Anzahl erfasster Spiele pro Gerät: echter Serverwert, sonst aus dem Verlauf abgeleitet.
  function deviceGameCount(device) {
    if (device && device.gameCount != null) return String(device.gameCount);
    return String(distinctGamesForDevice(device ? device.id : null));
  }
  function gameDisplay(game) {
    if (!game) return "Unbekanntes Spiel";
    return game.displayName || game.value || "Unbekanntes Spiel";
  }
  // Letzte Aktivität (Zeit) für ein Spiel aus dem Verlauf ableiten.
  function lastActivityForGame(keyValue) {
    let best = null;
    for (const a of state.data.activity) {
      if (a.gameKeyValue === keyValue) {
        const d = parseDate(a.timestampUtc);
        if (d && (!best || d > best)) best = d;
      }
    }
    return best;
  }

  // =====================================================================
  // API-Schicht (Bearer-Token, deutsche Fehlermeldungen)
  // =====================================================================
  function AuthError(message, status) { const e = new Error(message); e.isAuth = true; e.status = status; return e; }

  async function api(path, options) {
    options = options || {};
    const headers = Object.assign({}, options.headers || {});
    if (state.token) headers["Authorization"] = "Bearer " + state.token;
    if (options.body != null) headers["Content-Type"] = "application/json";
    let res;
    try {
      res = await fetch(path, { method: options.method || "GET", headers: headers, body: options.body });
    } catch (netErr) {
      throw new Error("Server nicht erreichbar. Läuft der SaveVault-Server?");
    }
    if (res.status === 503) throw AuthError("Server ist nicht eingerichtet (SAVEVAULT_TOKEN fehlt).", 503);
    if (res.status === 401) throw AuthError("Nicht angemeldet oder Token ungültig.", 401);
    if (res.status === 403) throw AuthError("Zugriff verweigert – das Master-Token wird benötigt.", 403);
    if (res.status === 204) return null;
    let payload = null;
    try { payload = await res.json(); } catch (_) { payload = null; }
    if (!res.ok) {
      const msg = payload && payload.error ? payload.error : ("Serverfehler (" + res.status + ").");
      throw new Error(msg);
    }
    return payload;
  }

  // Optionaler master-only-Aufruf: schlägt er fehl (z. B. 401/403 oder älterer
  // Server ohne diesen Endpunkt), bleibt es beim leeren Fallback statt Absturz.
  async function apiTolerant(path) {
    try { return await api(path); } catch (_) { return null; }
  }

  async function loadAll() {
    // /activity und /devices sind master-only; schlägt eines mit 401/403 fehl,
    // ist der Token kein Master-Token -> zurück zum Login.
    // /game-states und /server-info sind ebenfalls master-only, aber optional:
    // fehlt ein Ergebnis, zeigt das Dashboard einfach leere/geerbte Werte.
    const [games, devices, conflicts, activity, pairing, gameStates, serverInfo] = await Promise.all([
      api("/api/games"),
      api("/api/devices"),
      api("/api/conflicts"),
      api("/api/activity?limit=200"),
      api("/api/pairing-code"),
      apiTolerant("/api/game-states"),
      apiTolerant("/api/server-info")
    ]);
    state.data.games = (games && games.games) || [];
    state.data.devices = Array.isArray(devices) ? devices : [];
    state.data.conflicts = (conflicts && conflicts.conflicts) || [];
    state.data.activity = Array.isArray(activity) ? activity : [];
    state.data.pairing = pairing || null;
    state.data.gameStates = (gameStates && gameStates.states) || [];
    state.data.serverInfo = serverInfo || null;
    state.revCache = {};
    state.loaded = true;
  }

  async function getRevisions(keyValue) {
    if (state.revCache[keyValue]) return state.revCache[keyValue];
    const enc = encodeURIComponent(keyValue);
    const resp = await api("/api/games/" + enc + "/revisions");
    const list = (resp && resp.revisions) || [];
    state.revCache[keyValue] = list;
    return list;
  }

  // Box-Art: wird als Blob mit dem Bearer-Header geladen (das Token darf nie in eine URL/CSS),
  // dann als Hintergrundbild über die farbige Platzhalter-Kachel gelegt. Ergebnis wird je Spiel
  // gecacht ("none" = kein Cover verfügbar), damit nicht bei jedem Rendern neu geladen wird.
  const coverCache = {};
  async function loadCover(keyValue, coverEl) {
    if (!keyValue || !coverEl) return;
    const cached = coverCache[keyValue];
    if (cached === "none") return;
    if (cached) { applyCover(coverEl, cached); return; }
    try {
      const headers = {};
      if (state.token) headers["Authorization"] = "Bearer " + state.token;
      const res = await fetch("/api/games/" + encodeURIComponent(keyValue) + "/cover", { headers });
      if (!res.ok) { coverCache[keyValue] = "none"; return; }
      const url = URL.createObjectURL(await res.blob());
      coverCache[keyValue] = url;
      applyCover(coverEl, url);
    } catch (_) {
      coverCache[keyValue] = "none";
    }
  }
  function applyCover(coverEl, url) {
    coverEl.style.backgroundImage = 'url("' + url + '")';
    coverEl.style.backgroundSize = "cover";
    coverEl.style.backgroundPosition = "center";
  }

  // Export einer Revision als ZIP: Blob-Fetch mit Bearer-Header (Token bleibt im Header),
  // dann ein kurzlebiger <a download> löst den Browser-Download aus.
  async function downloadRevisionExport(keyValue, number, btn) {
    const original = btn ? btn.textContent : null;
    if (btn) { btn.disabled = true; btn.textContent = "Exportiere…"; }
    try {
      const headers = {};
      if (state.token) headers["Authorization"] = "Bearer " + state.token;
      const res = await fetch(
        "/api/games/" + encodeURIComponent(keyValue) + "/revisions/" + number + "/export", { headers });
      if (res.status === 401 || res.status === 403) { handleAuthFailure(AuthError("Nicht autorisiert.", res.status)); return; }
      if (!res.ok) throw new Error("Export fehlgeschlagen (HTTP " + res.status + ").");

      const blob = await res.blob();
      let filename = "export-rev" + number + ".zip";
      const cd = res.headers.get("Content-Disposition") || "";
      const m = /filename="?([^"]+)"?/i.exec(cd);
      if (m) { try { filename = decodeURIComponent(m[1]); } catch (_) { filename = m[1]; } }

      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url; a.download = filename;
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 5000);
      toast("Export „" + filename + "“ heruntergeladen.");
    } catch (err) {
      toast(err.message || "Export fehlgeschlagen.", true);
    } finally {
      if (btn) { btn.disabled = false; btn.textContent = original; }
    }
  }

  // =====================================================================
  // Anmelde-/Zustands-Tor
  // =====================================================================
  const gate = document.getElementById("gate");
  const app = document.getElementById("app");

  function showGate() { app.hidden = true; gate.hidden = false; }
  function showApp() { gate.hidden = true; app.hidden = false; }

  function renderGate(opts) {
    opts = opts || {};
    clear(gate);
    const card = el("div", { class: "gate__card" });

    const brand = el("div", { class: "gate__brand" }, [
      (function () { const l = el("div", { class: "gate__logo" }); l.innerHTML = ICONS.logo; return l; })(),
      el("div", null, [el("div", { class: "gate__title", text: "SaveVault" }),
                       el("div", { class: "muted", style: { "font-size": "12px" }, text: "Web-Dashboard" })])
    ]);
    card.appendChild(brand);

    if (opts.notConfigured) {
      card.appendChild(el("div", { class: "gate__sub",
        text: "Der Server läuft, ist aber noch nicht eingerichtet. Es wurde kein Master-Token (SAVEVAULT_TOKEN) gesetzt. Bitte setze die Umgebungsvariable und starte den Server neu." }));
      card.appendChild(el("div", { class: "gate__msg gate__msg--warn", text: "Status: nicht eingerichtet" }));
      const retry = el("div", { class: "gate__actions" }, [
        el("button", { class: "btn btn--accent", type: "button", text: "Erneut prüfen",
          on: { click: () => boot() } })
      ]);
      card.appendChild(retry);
      gate.appendChild(card);
      showGate();
      return;
    }

    card.appendChild(el("div", { class: "gate__sub",
      text: "Melde dich mit dem Master-Token deines Servers an (die Variable SAVEVAULT_TOKEN). Das Token bleibt nur in dieser Browser-Sitzung und wird ausschliesslich als Zugangskopf gesendet." }));

    const label = el("label", { class: "field-label", text: "Master-Token", attrs: { for: "token-input" } });
    const input = el("input", { class: "text-input", type: "password",
      attrs: { id: "token-input", placeholder: "Token eingeben…", autocomplete: "current-password", spellcheck: "false" } });
    input.addEventListener("keydown", e => { if (e.key === "Enter") submit(); });

    const msg = el("div", { class: "gate__msg gate__msg--error" });
    if (opts.error) msg.textContent = opts.error;

    async function submit() {
      const value = input.value.trim();
      if (!value) { msg.textContent = "Bitte ein Token eingeben."; return; }
      state.token = value;
      msg.classList.remove("gate__msg--error");
      msg.classList.add("muted");
      msg.textContent = "Anmeldung läuft…";
      try {
        await loadAll();
        sessionStorage.setItem(TOKEN_KEY, value);
        msg.textContent = "";
        startApp();
      } catch (err) {
        state.token = null;
        sessionStorage.removeItem(TOKEN_KEY);
        msg.classList.remove("muted");
        msg.classList.add("gate__msg--error");
        msg.textContent = err && err.message ? err.message : "Anmeldung fehlgeschlagen.";
      }
    }

    const btn = el("button", { class: "btn btn--accent", type: "button", text: "Anmelden", on: { click: submit } });
    card.appendChild(label);
    card.appendChild(input);
    card.appendChild(el("div", { class: "gate__actions" }, [btn]));
    card.appendChild(msg);

    gate.appendChild(card);
    showGate();
    input.focus();
  }

  // Auth-Fehler zentral behandeln: Token verwerfen, zurück zum Tor.
  function handleAuthFailure(err) {
    if (err && err.status === 503) { renderGate({ notConfigured: true }); return true; }
    if (err && err.isAuth) {
      state.token = null;
      sessionStorage.removeItem(TOKEN_KEY);
      renderGate({ error: err.message });
      return true;
    }
    return false;
  }

  // =====================================================================
  // App-Start / Chrome (Sidebar, Topbar)
  // =====================================================================
  function buildChrome() {
    // Feste Icons (innerHTML nur aus kontrollierten ICONS-Konstanten). Zuweisung statt
    // appendChild, damit wiederholte buildChrome()-Aufrufe keine Icons stapeln.
    document.getElementById("brand-logo").innerHTML = ICONS.logo;
    document.getElementById("search-icon").innerHTML = ICONS.search;
    document.getElementById("refresh-glyph").innerHTML = ICONS.refresh;

    const nav = document.getElementById("nav");
    clear(nav);
    for (const item of NAV) {
      const btn = el("button", { class: "nav-item" + (state.view === item.key ? " is-active" : ""), type: "button",
        on: { click: () => setView(item.key) } });
      btn.dataset.key = item.key;
      btn.appendChild(iconEl(item.icon, "nav-item__icon"));
      btn.appendChild(el("span", { text: item.label }));
      if (item.key === "clients") {
        // Badge = offene Konflikte (auffällig, wie im Mockup).
      }
      if (item.key === "games") {
        const cnt = state.data.conflicts.length;
        if (cnt > 0) btn.appendChild(el("span", { class: "nav-item__badge", text: String(cnt) }));
      }
      nav.appendChild(btn);
    }

    const search = document.getElementById("search-input");
    search.value = state.search;
    search.oninput = e => { state.search = e.target.value; if (state.view === "games" || state.view === "clients") renderView(); };

    document.getElementById("refresh-btn").onclick = refreshData;
  }

  function setView(view) {
    state.view = view;
    // Suche/Filter beim Wechsel zurücksetzen (klare Ausgangslage).
    if (view !== "games" && view !== "clients") { /* Suche bleibt egal */ }
    buildChrome();
    renderView();
  }

  function updateTopbar() {
    const t = VIEW_TITLES[state.view] || ["", ""];
    document.getElementById("view-title").textContent = t[0];
    document.getElementById("view-subtitle").textContent = t[1];
    for (const btn of document.querySelectorAll(".nav-item"))
      btn.classList.toggle("is-active", btn.dataset.key === state.view);
  }

  async function refreshData() {
    const btn = document.getElementById("refresh-btn");
    btn.classList.add("is-busy");
    try {
      await loadAll();
      buildChrome();
      renderView();
    } catch (err) {
      if (!handleAuthFailure(err)) toast(err.message || "Aktualisieren fehlgeschlagen.", true);
    } finally {
      btn.classList.remove("is-busy");
    }
  }

  function startApp() {
    showApp();
    buildChrome();
    renderView();
  }

  // =====================================================================
  // Ansichten
  // =====================================================================
  function renderView() {
    updateTopbar();
    const root = document.getElementById("view-root");
    clear(root);
    switch (state.view) {
      case "dashboard": root.appendChild(viewDashboard()); break;
      case "games":     root.appendChild(viewGames()); break;
      case "clients":   root.appendChild(viewClients()); break;
      case "history":   root.appendChild(viewHistory()); break;
      case "settings":  root.appendChild(viewSettings()); break;
    }
  }

  // ---- Dashboard ---------------------------------------------------------
  function viewDashboard() {
    const frag = document.createDocumentFragment();
    const games = state.data.games;

    const totalBytes = games.reduce((s, g) => s + (g.totalBytes || 0), 0);
    const conflictCount = state.data.conflicts.length;
    const stats = [
      { label: "Spiele erfasst", value: String(games.length), suffix: games.length === 1 ? "Titel" : "Titel", cls: "" },
      { label: "Verbundene Clients", value: String(state.data.devices.length), suffix: "registriert", cls: "" },
      { label: "Speicher gesamt", value: formatBytes(totalBytes), suffix: "", cls: "" },
      { label: "Aktive Konflikte", value: String(conflictCount), suffix: "offen", cls: conflictCount ? "conflict" : "synced" }
    ];
    const statGrid = el("div", { class: "stat-grid" });
    for (const s of stats) {
      const valueNode = el("div", { class: "stat__value" + (s.cls ? " status--" + s.cls : ""), text: s.value });
      statGrid.appendChild(el("div", { class: "stat" }, [
        el("div", { class: "stat__label", text: s.label }),
        el("div", { class: "stat__value-row" }, [valueNode, s.suffix ? el("div", { class: "stat__suffix", text: s.suffix }) : null])
      ]));
    }
    frag.appendChild(statGrid);

    // Zwei Panels: Spiele-Vorschau + Client-Liste
    const cols = el("div", { class: "dash-cols" });

    // Spiele-Panel
    const gamesPanel = el("div", { class: "panel" });
    gamesPanel.appendChild(el("div", { class: "panel__head" }, [
      el("div", { class: "panel__title", text: "Spiele" }),
      el("button", { class: "panel__link", type: "button", text: "Alle anzeigen →", on: { click: () => setView("games") } })
    ]));
    if (games.length === 0) {
      gamesPanel.appendChild(el("div", { class: "empty", text: "Noch keine Spiele erfasst." }));
    } else {
      const preview = games
        .map(g => ({ g, t: lastActivityForGame(g.game && g.game.value) }))
        .sort((a, b) => (b.t ? b.t.getTime() : 0) - (a.t ? a.t.getTime() : 0))
        .slice(0, 6);
      const grid = el("div", { class: "game-preview-grid" });
      for (const item of preview) grid.appendChild(gameCard(item.g, false));
      gamesPanel.appendChild(grid);
    }
    cols.appendChild(gamesPanel);

    // Clients-Panel
    const clientsPanel = el("div", { class: "panel" });
    clientsPanel.appendChild(el("div", { class: "panel__head" }, [
      el("div", { class: "panel__title", text: "Clients" }),
      el("button", { class: "panel__link", type: "button", text: "Alle anzeigen →", on: { click: () => setView("clients") } })
    ]));
    if (state.data.devices.length === 0) {
      clientsPanel.appendChild(el("div", { class: "empty", text: "Noch kein Client gekoppelt." }));
    } else {
      const list = el("div", { class: "stack" });
      for (const d of state.data.devices) list.appendChild(clientRow(d));
      clientsPanel.appendChild(list);
    }
    cols.appendChild(clientsPanel);

    frag.appendChild(cols);
    return frag;
  }

  // ---- GameCard ----------------------------------------------------------
  function gameCard(summary, big) {
    const game = summary.game || {};
    const name = gameDisplay(game);
    const meta = statusMeta(summary.status);
    const lastT = lastActivityForGame(game.value);
    const metaParts = [];
    if (game.store) metaParts.push(game.store);
    metaParts.push(formatBytes(summary.totalBytes));
    if (lastT) metaParts.push(relTime(lastT.toISOString()));

    const card = el("button", { class: "game-card" + (big ? " is-big" : ""), type: "button",
      on: { click: () => openGameDrawer(game.value) } });
    const coverEl = el("div", { class: "game-card__cover", style: { background: coverColor(game.value) } });
    card.appendChild(coverEl);
    loadCover(game.value, coverEl);
    const body = el("div", { class: "game-card__body" });
    body.appendChild(el("div", { class: "game-card__name", text: name }));
    body.appendChild(el("div", { class: "game-card__meta", text: metaParts.join(" · ") }));
    const dot = el("span", { class: "dot dot--" + meta.cls + (meta.pulse ? " is-pulse" : "") });
    body.appendChild(el("div", { class: "status-line status--" + meta.cls, style: { "margin-top": "6px", "font-size": "11.5px", "font-weight": "600" } },
      [dot, document.createTextNode(meta.label)]));
    card.appendChild(body);
    return card;
  }

  // ---- ClientRow (kompakt) ----------------------------------------------
  function clientDerivedStatus(device) {
    const d = parseDate(device.lastSeenUtc);
    if (!d) return "Offline";
    const min = (Date.now() - d.getTime()) / 60000;
    return min <= 3 ? "Synced" : "Offline"; // ohne Live-Status: „verbunden" via lastSeen
  }
  function clientStatusLabel(device) {
    return clientDerivedStatus(device) === "Synced" ? "Verbunden" : "Offline";
  }
  function clientRow(device) {
    const st = clientDerivedStatus(device);
    const meta = statusMeta(st);
    const row = el("button", { class: "client-row", type: "button", on: { click: () => openClientDrawer(device.id) } });
    const avatar = el("div", { class: "client-row__avatar" });
    avatar.appendChild(iconEl("monitor", "client-row__avatar-icon"));
    avatar.appendChild(el("span", { class: "dot dot--avatar dot--" + meta.cls + (meta.pulse ? " is-pulse" : "") }));
    row.appendChild(avatar);
    const body = el("div", { class: "client-row__body" });
    body.appendChild(el("div", { class: "client-row__name", text: device.name }));
    body.appendChild(el("div", { class: "client-row__status status--" + meta.cls, text: clientStatusLabel(device) }));
    row.appendChild(body);
    row.appendChild(el("div", { class: "client-row__seen", text: relTime(device.lastSeenUtc) }));
    return row;
  }

  // ---- Spiele-Ansicht ----------------------------------------------------
  function viewGames() {
    const frag = document.createDocumentFragment();
    const filters = [
      ["all", "Alle"], ["Synced", "Synchronisiert"], ["Syncing", "Wird synchronisiert"],
      ["Conflict", "Konflikt"], ["Pending", "Ausstehend"], ["Offline", "Offline"], ["Error", "Fehler"]
    ];
    const pillRow = el("div", { class: "pill-row" });
    for (const [key, label] of filters) {
      pillRow.appendChild(el("button", { class: "pill" + (state.gameFilter === key ? " is-active" : ""), type: "button",
        text: label, on: { click: () => { state.gameFilter = key; renderView(); } } }));
    }
    frag.appendChild(pillRow);

    const search = state.search.trim().toLowerCase();
    const list = state.data.games.filter(g => {
      const matchSearch = !search || gameDisplay(g.game).toLowerCase().includes(search);
      const matchStatus = state.gameFilter === "all" || g.status === state.gameFilter;
      return matchSearch && matchStatus;
    });

    if (list.length === 0) {
      frag.appendChild(el("div", { class: "empty",
        text: state.data.games.length === 0 ? "Noch keine Spiele erfasst. Koppele einen Client, damit Spielstände erscheinen." : "Keine Spiele gefunden." }));
      return frag;
    }
    const grid = el("div", { class: "card-grid-3" });
    for (const g of list) grid.appendChild(gameCard(g, true));
    frag.appendChild(grid);
    return frag;
  }

  // ---- Clients-Ansicht ---------------------------------------------------
  function distinctGamesForDevice(id) {
    const set = new Set();
    for (const a of state.data.activity) if (a.deviceId === id && a.gameKeyValue) set.add(a.gameKeyValue);
    return set.size;
  }
  function viewClients() {
    const frag = document.createDocumentFragment();
    const search = state.search.trim().toLowerCase();
    const list = state.data.devices.filter(d => !search || (d.name || "").toLowerCase().includes(search));
    if (list.length === 0) {
      frag.appendChild(el("div", { class: "empty",
        text: state.data.devices.length === 0 ? "Noch kein Client gekoppelt. Der Pairing-Code steht unter „Einstellungen“." : "Keine Clients gefunden." }));
      return frag;
    }
    const grid = el("div", { class: "client-grid" });
    for (const d of list) grid.appendChild(clientCard(d));
    frag.appendChild(grid);
    return frag;
  }
  function clientCard(device) {
    const st = clientDerivedStatus(device);
    const meta = statusMeta(st);
    const card = el("button", { class: "client-card", type: "button", on: { click: () => openClientDrawer(device.id) } });
    const head = el("div", { class: "client-card__head" });
    const avatar = el("div", { class: "client-card__avatar" });
    avatar.appendChild(iconEl("monitor", "client-card__avatar-icon"));
    avatar.appendChild(el("span", { class: "dot dot--avatar dot--" + meta.cls + (meta.pulse ? " is-pulse" : "") }));
    head.appendChild(avatar);
    head.appendChild(el("div", null, [
      el("div", { class: "client-card__name", text: device.name }),
      el("div", { class: "client-card__sub", text: (device.os || "unbekanntes System") + " · " + deviceIp(device) })
    ]));
    card.appendChild(head);

    card.appendChild(el("div", { class: "client-card__status status--" + meta.cls },
      [el("span", { class: "dot dot--" + meta.cls + (meta.pulse ? " is-pulse" : "") }), document.createTextNode(clientStatusLabel(device))]));

    const grid = el("div", { class: "client-card__grid" });
    grid.appendChild(kv("Agent", device.agentVersion || "—"));
    grid.appendChild(kv("Zuletzt gesehen", relTime(device.lastSeenUtc)));
    grid.appendChild(kv("Speicher", formatBytes(device.storageBytes)));
    grid.appendChild(kv("Spiele", deviceGameCount(device)));
    card.appendChild(grid);
    return card;
  }
  function kv(key, value) {
    return el("div", null, [document.createTextNode(key), el("div", { class: "kv__value", text: value })]);
  }

  // ---- Verlauf -----------------------------------------------------------
  function viewHistory() {
    const frag = document.createDocumentFragment();
    const filters = [["all", "Alle"], ["conflict", "Konflikte"], ["resolve", "Gelöst"], ["restore", "Wiederherstellungen"]];
    const pillRow = el("div", { class: "pill-row" });
    for (const [key, label] of filters) {
      pillRow.appendChild(el("button", { class: "pill" + (state.historyFilter === key ? " is-active" : ""), type: "button",
        text: label, on: { click: () => { state.historyFilter = key; renderView(); } } }));
    }
    frag.appendChild(pillRow);

    let entries = state.data.activity.slice();
    if (state.historyFilter !== "all") entries = entries.filter(a => a.action === state.historyFilter);
    entries.sort((a, b) => (parseDate(b.timestampUtc)?.getTime() || 0) - (parseDate(a.timestampUtc)?.getTime() || 0));

    if (entries.length === 0) {
      frag.appendChild(el("div", { class: "empty", text: "Noch keine Aktivität." }));
      return frag;
    }
    const wrap = el("div", { class: "history" });
    entries.forEach((a, i) => wrap.appendChild(historyRow(a, i)));
    frag.appendChild(wrap);
    return frag;
  }
  function historyRow(a, i) {
    const meta = actionMeta(a.action);
    const open = !!state.expandedHistory[a.id];
    const row = el("div", { class: "history-row" + (open ? " is-open" : "") });
    const head = el("button", { class: "history-row__head", type: "button",
      on: { click: () => { state.expandedHistory[a.id] = !open; renderView(); } } });
    head.appendChild(el("div", { class: "history-row__time", text: absTime(a.timestampUtc) }));
    head.appendChild(iconEl(meta.icon, "history-row__icon status--" + meta.cls));
    head.appendChild(el("div", { class: "history-row__game", text: a.gameDisplayName || (a.action === "pair" ? "—" : (a.gameKeyValue || "—")) }));
    head.appendChild(el("div", { class: "history-row__client", text: a.deviceName || deviceName(a.deviceId) }));
    head.appendChild(el("div", { class: "history-row__action status--" + meta.cls, text: meta.label }));
    head.appendChild(el("div", { class: "history-row__size", text: a.bytes != null ? formatBytes(a.bytes) : "—" }));
    head.appendChild(iconEl("chevron", "history-row__chevron"));
    row.appendChild(head);

    if (open) {
      const box = el("div", { class: "history-detail__box" });
      box.appendChild(kv("Größe", a.bytes != null ? formatBytes(a.bytes) : "—"));
      box.appendChild(kv("Dateien", a.fileCount != null ? String(a.fileCount) : "—"));
      box.appendChild(kv("Revision", a.revision != null ? "#" + a.revision : "—"));
      box.appendChild(kv("Detail", a.detail || "—"));
      row.appendChild(el("div", { class: "history-detail" }, [box]));
    }
    return row;
  }

  // ---- Einstellungen -----------------------------------------------------
  function viewSettings() {
    const frag = document.createDocumentFragment();
    const grid = el("div", { class: "settings-grid" });
    const ls = state.localSettings;

    // Server-Info (echte Werte aus /api/server-info; alle Felder via textContent).
    const info = state.data.serverInfo;
    const server = el("div", { class: "settings-card" });
    server.appendChild(el("div", { class: "settings-card__title", text: "Server" }));
    server.appendChild(el("div", { class: "settings-card__sub",
      text: info && info.version ? ("SaveVault-Server · Version " + info.version) : "Verbindung zum SaveVault-Dienst" }));
    const sl = el("div", { class: "settings-list" });
    sl.appendChild(settingsRow("Container", info && info.container ? info.container : "—"));
    sl.appendChild(settingsRow("Server-Version", info && info.version ? info.version : "—"));
    sl.appendChild(settingsRow("Port", info && info.port != null ? String(info.port) : "—"));
    sl.appendChild(settingsRow("Storage-Pfad", info && info.dataRoot ? info.dataRoot : "—"));
    sl.appendChild(settingsRow("Box-Art (IGDB)",
      info && typeof info.coverEnabled === "boolean" ? (info.coverEnabled ? "aktiv" : "inaktiv (keine IGDB-Zugangsdaten)") : "—"));
    const configured = info ? !!info.configured : false;
    const statusCls = configured ? "synced" : "offline";
    const statusText = configured ? "Eingerichtet" : "Nicht eingerichtet";
    const statusVal = el("span", { class: "settings-row__val status--" + statusCls },
      [el("span", { class: "dot dot--" + statusCls }), document.createTextNode(" " + statusText)]);
    statusVal.style.display = "inline-flex"; statusVal.style.alignItems = "center"; statusVal.style.gap = "6px";
    sl.appendChild(el("div", { class: "settings-row" }, [el("span", { class: "settings-row__key", text: "Status" }), statusVal]));
    server.appendChild(sl);
    grid.appendChild(server);

    // Sync (lokal)
    const sync = el("div", { class: "settings-card" });
    sync.appendChild(el("div", { class: "settings-card__title", text: "Sync" }));
    sync.appendChild(el("div", { class: "settings-card__sub", text: "Anzeige-Einstellungen (nur lokal in diesem Browser)" }));
    const syncList = el("div", { class: "settings-list" });
    // Intervall-Slider
    const intervalHead = el("div", { class: "range-head" }, [
      el("span", { class: "settings-row__key", text: "Sync-Intervall" }),
      el("span", { class: "settings-row__val", text: ls.interval + " Min." })
    ]);
    const intervalSlider = el("input", { class: "slider", type: "range", attrs: { min: "1", max: "30", value: String(ls.interval) } });
    intervalSlider.addEventListener("input", e => { ls.interval = Number(e.target.value); intervalHead.lastChild.textContent = ls.interval + " Min."; });
    syncList.appendChild(el("div", null, [intervalHead, intervalSlider]));
    // Toggles
    syncList.appendChild(toggleRow("Auto-Konflikterkennung", "autoConflict"));
    syncList.appendChild(toggleRow("Benachrichtigung bei Konflikt", "notify"));
    sync.appendChild(syncList);
    sync.appendChild(el("div", { class: "note", text: "Diese Optionen werden im MVP nicht serverseitig gespeichert." }));
    grid.appendChild(sync);

    // Speicher & Versionen
    const storage = el("div", { class: "settings-card" });
    storage.appendChild(el("div", { class: "settings-card__title", text: "Speicher & Versionen" }));
    storage.appendChild(el("div", { class: "settings-card__sub", text: "Belegung und Aufbewahrung" }));
    const stoList = el("div", { class: "settings-list" });
    const retHead = el("div", { class: "range-head" }, [
      el("span", { class: "settings-row__key", text: "Versionen pro Spiel behalten" }),
      el("span", { class: "settings-row__val", text: ls.retention === 0 ? "unbegrenzt" : String(ls.retention) })
    ]);
    const retSlider = el("input", { class: "slider", type: "range", attrs: { min: "0", max: "50", value: String(ls.retention) } });
    retSlider.addEventListener("input", e => { ls.retention = Number(e.target.value); retHead.lastChild.textContent = ls.retention === 0 ? "unbegrenzt" : String(ls.retention); });
    stoList.appendChild(el("div", null, [retHead, retSlider]));
    const usedBytes = state.data.games.reduce((s, g) => s + (g.totalBytes || 0), 0);
    stoList.appendChild(settingsRow("Belegter Speicher", formatBytes(usedBytes)));
    const revCount = state.data.games.reduce((s, g) => s + (g.currentRevision || 0), 0);
    stoList.appendChild(settingsRow("Spiele erfasst", String(state.data.games.length)));
    storage.appendChild(stoList);
    storage.appendChild(el("div", { class: "note", text: "Standard: unbegrenzte Historie (jede hochgeladene Version bleibt erhalten)." }));
    grid.appendChild(storage);

    // Netzwerk & Pairing
    const net = el("div", { class: "settings-card" });
    net.appendChild(el("div", { class: "settings-card__title", text: "Netzwerk & Pairing" }));
    net.appendChild(el("div", { class: "settings-card__sub", text: "Neuen Client im Heimnetzwerk verbinden" }));
    const pairBox = el("div", { class: "pairing-box" });
    const codeText = state.data.pairing && state.data.pairing.code ? state.data.pairing.code : "—";
    const codeEl = el("div", { class: "pairing-code", text: codeText });
    const regenBtn = el("button", { class: "link-btn", type: "button", text: "Erneuern", on: { click: () => regeneratePairing(codeEl, net) } });
    pairBox.appendChild(codeEl);
    pairBox.appendChild(regenBtn);
    net.appendChild(pairBox);
    if (state.data.pairing && state.data.pairing.updatedUtc)
      net.appendChild(el("div", { class: "note", text: "Zuletzt geändert: " + absTime(state.data.pairing.updatedUtc) + ". Der Code gilt einmalig; nach dem Koppeln erzeugt der Server automatisch einen neuen." }));
    grid.appendChild(net);

    frag.appendChild(grid);
    return frag;
  }
  function settingsRow(key, value) {
    return el("div", { class: "settings-row" }, [
      el("span", { class: "settings-row__key", text: key }),
      el("span", { class: "settings-row__val", text: value })
    ]);
  }
  function toggleRow(label, prop) {
    const ls = state.localSettings;
    const toggle = el("button", { class: "toggle " + (ls[prop] ? "is-on" : "is-off"), type: "button",
      attrs: { "aria-pressed": String(!!ls[prop]) } }, [el("span", { class: "toggle__knob" })]);
    toggle.addEventListener("click", () => {
      ls[prop] = !ls[prop];
      toggle.className = "toggle " + (ls[prop] ? "is-on" : "is-off");
      toggle.setAttribute("aria-pressed", String(ls[prop]));
    });
    return el("div", { class: "settings-row" }, [el("span", { class: "settings-row__key", text: label }), toggle]);
  }

  async function regeneratePairing(codeEl, container) {
    try {
      const resp = await api("/api/pairing-code/regenerate", { method: "POST", body: "{}" });
      if (resp && resp.code) { state.data.pairing = resp; codeEl.textContent = resp.code; toast("Neuer Pairing-Code erzeugt."); renderView(); }
    } catch (err) {
      if (!handleAuthFailure(err)) toast(err.message || "Erneuern fehlgeschlagen.", true);
    }
  }

  // =====================================================================
  // Overlays (Drawer / Modal / Toast)
  // =====================================================================
  const overlayRoot = document.getElementById("overlay-root");
  function closeOverlay() { clear(overlayRoot); }
  function openOverlay(scrimOnClick, panel) {
    clear(overlayRoot);
    const scrim = el("div", { class: "scrim", on: { click: scrimOnClick || closeOverlay } });
    overlayRoot.appendChild(scrim);
    overlayRoot.appendChild(panel);
  }

  // ---- Spiel-Drawer ------------------------------------------------------
  async function openGameDrawer(keyValue) {
    const summary = state.data.games.find(g => g.game && g.game.value === keyValue);
    if (!summary) return;
    const game = summary.game;

    const drawer = el("div", { class: "drawer" });
    openOverlay(closeOverlay, drawer);

    // Kopf
    const head = el("div", { class: "drawer__head" });
    const drawerCover = el("div", { class: "drawer__cover", style: { background: coverColor(keyValue) } });
    loadCover(keyValue, drawerCover);
    // Zeile für den Standard-Save-Pfad (wird nach dem Laden der Revisionen befüllt, falls bekannt).
    const pathEl = el("div", { class: "drawer__sub", style: { "margin-top": "2px", opacity: "0.75", "word-break": "break-all" } });
    const idBlock = el("div", { class: "drawer__id" }, [
      drawerCover,
      el("div", null, [
        el("div", { class: "drawer__title", text: gameDisplay(game) }),
        el("div", { class: "drawer__sub", text: (game.store ? game.store + " · " : "") + formatBytes(summary.totalBytes) + " · " + (summary.fileCount || 0) + " Dateien" }),
        pathEl
      ])
    ]);
    const closeBtn = el("button", { class: "close-btn", type: "button", title: "Schließen", on: { click: closeOverlay } });
    closeBtn.appendChild(iconEl("close", "close-btn__glyph"));
    head.appendChild(idBlock);
    head.appendChild(closeBtn);
    drawer.appendChild(head);

    // Konflikt-Banner
    const conflict = state.data.conflicts.find(c => c.game && c.game.value === keyValue);
    if (conflict) {
      const banner = el("div", { class: "conflict-banner" });
      banner.appendChild(el("div", { class: "conflict-banner__text" }, [
        (function () { const b = el("b"); b.textContent = "Sync-Konflikt erkannt. "; return b; })(),
        document.createTextNode("Mehrere Clients haben abweichende Spielstände.")
      ]));
      banner.appendChild(el("button", { class: "btn btn--conflict", type: "button", text: "Lösen",
        on: { click: () => openConflictModal(conflict) } }));
      drawer.appendChild(banner);
    }

    // Bereiche mit Ladezustand
    const clientsSection = el("div");
    clientsSection.appendChild(el("div", { class: "section-label", text: "Clients" }));
    const clientsBody = el("div", { class: "drawer-list" });
    clientsBody.appendChild(loadingState());
    clientsSection.appendChild(clientsBody);
    drawer.appendChild(clientsSection);

    const versionSection = el("div");
    versionSection.appendChild(el("div", { class: "section-label", text: "Versionsverlauf" }));
    const versionBody = el("div", { class: "version-list" });
    versionBody.appendChild(loadingState());
    versionSection.appendChild(versionBody);
    drawer.appendChild(versionSection);

    // Revisionen laden
    let revisions = [];
    try {
      revisions = await getRevisions(keyValue);
    } catch (err) {
      clear(clientsBody); clear(versionBody);
      if (handleAuthFailure(err)) return;
      const msg = el("div", { class: "empty", text: err.message || "Konnte Versionen nicht laden." });
      versionBody.appendChild(msg);
      return;
    }

    // Per-Gerät-Zustand aus dem echten /api/game-states-Endpunkt (statt aus der
    // Revisionshistorie abgeleitet). Die Revisionen liefern zusätzlich den
    // Zeitpunkt der letzten Übertragung je Gerät für die Unterzeile.
    clear(clientsBody);
    const latestByDevice = {};
    for (const r of revisions) {
      if (!latestByDevice[r.deviceId] || r.number > latestByDevice[r.deviceId].number) latestByDevice[r.deviceId] = r;
    }
    const gameStates = (state.data.gameStates || []).filter(s => s.game && s.game.value === keyValue);
    if (gameStates.length === 0) {
      clientsBody.appendChild(el("div", { class: "empty", text: "Noch kein Client hat dieses Spiel synchronisiert." }));
    } else {
      for (const gs of gameStates) {
        const r = latestByDevice[gs.deviceId];
        const item = el("div", { class: "drawer-item" });
        item.appendChild(el("div", null, [
          el("div", { class: "drawer-item__name", text: deviceName(gs.deviceId) }),
          el("div", { class: "drawer-item__sub", text: r ? ("zuletzt " + relTime(r.timestampUtc)) : "noch nicht übertragen" })
        ]));
        item.appendChild(statusLine(gs.status));
        clientsBody.appendChild(item);
      }
    }

    // Standard-Save-Pfad (aus der neuesten Revision, die einen kennt) im Kopf anzeigen.
    const withRoot = revisions.find(r => r.saveRoot);
    if (withRoot) pathEl.textContent = "Standard-Pfad: " + withRoot.saveRoot;

    // Versionsverlauf
    clear(versionBody);
    if (revisions.length === 0) {
      versionBody.appendChild(el("div", { class: "empty", text: "Noch keine Versionen." }));
    } else {
      for (const r of revisions) {
        const row = el("div", { class: "version-row" });
        const info = el("div", null, [
          el("div", { class: "version-row__time" }, [
            document.createTextNode("#" + r.number + " · " + absTime(r.timestampUtc)),
            r.isConflict ? el("span", { class: "status--conflict", text: "  (Konflikt)", style: { "font-size": "11px" } }) : null
          ]),
          el("div", { class: "version-row__sub", text: deviceName(r.deviceId) + " · " + formatBytes(r.totalBytes) + " · " + r.fileCount + " Dateien" })
        ]);
        row.appendChild(info);
        const actions = el("div", { class: "version-row__actions" });
        actions.appendChild(el("button", { class: "btn-inline", type: "button", text: "Export",
          title: "Diese Version als ZIP herunterladen",
          on: { click: (ev) => downloadRevisionExport(keyValue, r.number, ev.currentTarget) } }));
        actions.appendChild(el("button", { class: "btn-inline", type: "button", text: "Wiederherstellen",
          on: { click: () => openRestorePicker(keyValue, r.number) } }));
        row.appendChild(actions);
        versionBody.appendChild(row);
      }
    }
  }

  function loadingState() {
    const wrap = el("div", { class: "center-state", style: { padding: "24px 0" } });
    wrap.appendChild(iconEl("refresh", "spin"));
    wrap.appendChild(el("div", { class: "muted", text: "Lädt…" }));
    return wrap;
  }

  // ---- Restore-Ziel-Auswahl (Modal) -------------------------------------
  function openRestorePicker(keyValue, revisionNumber) {
    const devices = state.data.devices;
    const modal = el("div", { class: "modal modal--sm" });
    const head = el("div", { class: "modal__head" });
    head.appendChild(el("div", null, [
      el("div", { class: "modal__title", text: "Version wiederherstellen" }),
      el("div", { class: "modal__sub", text: "Revision #" + revisionNumber + " – auf welches Gerät?" })
    ]));
    const closeBtn = el("button", { class: "close-btn", type: "button", on: { click: () => openGameDrawer(keyValue) } });
    closeBtn.appendChild(iconEl("close", "close-btn__glyph"));
    head.appendChild(closeBtn);
    modal.appendChild(head);

    if (devices.length === 0) {
      modal.appendChild(el("div", { class: "empty", text: "Kein Gerät verfügbar." }));
    } else {
      const list = el("div", { class: "picker-list" });
      for (const d of devices) {
        const item = el("button", { class: "picker-item", type: "button", on: { click: () => doRestore(keyValue, revisionNumber, d.id) } });
        item.appendChild(el("div", null, [
          el("div", { style: { "font-weight": "600", "font-size": "13px" }, text: d.name }),
          el("div", { class: "muted", style: { "font-size": "11.5px", "margin-top": "2px" }, text: (d.os || "") + " · zuletzt " + relTime(d.lastSeenUtc) })
        ]));
        item.appendChild(iconEl("restore", "history-row__icon status--syncing"));
        list.appendChild(item);
      }
      modal.appendChild(list);
    }
    modal.appendChild(el("div", { class: "modal__foot" }, [
      el("span"),
      el("button", { class: "btn btn--ghost", type: "button", text: "Abbrechen", on: { click: () => openGameDrawer(keyValue) } })
    ]));

    const scrim = el("div", { class: "modal-scrim", on: { click: e => { if (e.target === scrim) openGameDrawer(keyValue); } } }, [modal]);
    clear(overlayRoot);
    overlayRoot.appendChild(scrim);
  }

  async function doRestore(keyValue, revisionNumber, deviceId) {
    try {
      const enc = encodeURIComponent(keyValue);
      const resp = await api("/api/games/" + enc + "/restore", { method: "POST",
        body: JSON.stringify({ targetDeviceId: deviceId, targetRevision: revisionNumber }) });
      if (resp && resp.accepted) {
        toast("Wiederherstellung angefordert – wird beim nächsten Abgleich angewandt.");
        await loadAll(); buildChrome(); renderView(); closeOverlay();
      } else {
        toast("Wiederherstellung nicht angenommen.", true);
      }
    } catch (err) {
      if (!handleAuthFailure(err)) toast(err.message || "Wiederherstellung fehlgeschlagen.", true);
    }
  }

  // ---- Client-Drawer -----------------------------------------------------
  function openClientDrawer(deviceId) {
    const device = state.data.devices.find(d => d.id === deviceId);
    if (!device) return;
    const drawer = el("div", { class: "drawer" });
    openOverlay(closeOverlay, drawer);

    const st = clientDerivedStatus(device);
    const meta = statusMeta(st);

    const head = el("div", { class: "drawer__head" });
    const avatar = el("div", { class: "client-card__avatar" });
    avatar.appendChild(iconEl("monitor", "client-card__avatar-icon"));
    const idBlock = el("div", { style: { display: "flex", gap: "14px", "align-items": "center" } }, [
      avatar,
      el("div", null, [
        el("div", { class: "drawer__title", text: device.name }),
        el("div", { class: "drawer__sub", text: (device.os || "unbekanntes System") + " · " + deviceIp(device) })
      ])
    ]);
    const closeBtn = el("button", { class: "close-btn", type: "button", on: { click: closeOverlay } });
    closeBtn.appendChild(iconEl("close", "close-btn__glyph"));
    head.appendChild(idBlock);
    head.appendChild(closeBtn);
    drawer.appendChild(head);

    drawer.appendChild(el("div", { class: "status-line status--" + meta.cls, style: { "margin-bottom": "18px", "font-weight": "600" } },
      [el("span", { class: "dot dot--lg dot--" + meta.cls + (meta.pulse ? " is-pulse" : "") }), document.createTextNode(clientStatusLabel(device))]));

    const info = el("div", { class: "client-info-grid" });
    info.appendChild(kv("Agent", device.agentVersion || "—"));
    info.appendChild(kv("Zuletzt gesehen", relTime(device.lastSeenUtc)));
    info.appendChild(kv("Speicher", formatBytes(device.storageBytes)));
    info.appendChild(kv("Spiele", deviceGameCount(device)));
    drawer.appendChild(info);

    drawer.appendChild(el("div", { class: "section-label", text: "Zuletzt synchronisierte Spiele" }));
    const recent = state.data.activity
      .filter(a => a.deviceId === deviceId && a.gameKeyValue)
      .sort((a, b) => (parseDate(b.timestampUtc)?.getTime() || 0) - (parseDate(a.timestampUtc)?.getTime() || 0))
      .slice(0, 8);
    if (recent.length === 0) {
      drawer.appendChild(el("div", { class: "empty", text: "Noch keine Aktivität dieses Clients." }));
    } else {
      const list = el("div", { class: "version-list" });
      for (const a of recent) {
        const am = actionMeta(a.action);
        const row = el("div", { class: "recent-row" });
        row.appendChild(el("div", { class: "recent-row__cover", style: { background: coverColor(a.gameKeyValue) } }));
        row.appendChild(el("div", { class: "recent-row__body" }, [
          el("div", { class: "recent-row__game", text: a.gameDisplayName || a.gameKeyValue }),
          el("div", { class: "recent-row__time", text: relTime(a.timestampUtc) })
        ]));
        row.appendChild(el("div", { class: "recent-row__action status--" + am.cls, text: am.label }));
        list.appendChild(row);
      }
      drawer.appendChild(list);
    }
  }

  // ---- Konflikt-Modal ----------------------------------------------------
  async function openConflictModal(conflict) {
    const keyValue = conflict.game && conflict.game.value;
    let revisions = [];
    try { revisions = await getRevisions(keyValue); }
    catch (err) { if (handleAuthFailure(err)) return; toast(err.message || "Konnte Konfliktdaten nicht laden.", true); return; }
    const revByNum = {};
    for (const r of revisions) revByNum[r.number] = r;

    let selected = null; // gewählter Participant-Index (KeepDevice)

    const modal = el("div", { class: "modal" });
    const head = el("div", { class: "modal__head" });
    head.appendChild(el("div", null, [
      el("div", { class: "modal__title", text: "Konflikt lösen" }),
      el("div", { class: "modal__sub", text: gameDisplay(conflict.game) + " · abweichende Spielstände gefunden" })
    ]));
    const closeBtn = el("button", { class: "close-btn", type: "button", on: { click: () => openGameDrawer(keyValue) } });
    closeBtn.appendChild(iconEl("close", "close-btn__glyph"));
    head.appendChild(closeBtn);
    modal.appendChild(head);

    const grid = el("div", { class: "conflict-versions" });
    const cards = [];
    const participants = conflict.participants || [];
    participants.forEach((p, idx) => {
      const r = revByNum[p.revision];
      const card = el("button", { class: "conflict-version", type: "button" });
      card.appendChild(el("div", { class: "conflict-version__client", text: deviceName(p.deviceId) }));
      card.appendChild(el("div", { class: "conflict-version__time", text: r ? absTime(r.timestampUtc) : "Revision #" + p.revision }));
      const fields = el("div", { class: "conflict-version__fields" });
      fields.appendChild(field("Zeitpunkt", r ? absTime(r.timestampUtc) : "—"));
      fields.appendChild(field("Größe", r ? formatBytes(r.totalBytes) : "—"));
      fields.appendChild(field("Dateien", r ? String(r.fileCount) : "—"));
      fields.appendChild(field("Gerät", deviceName(p.deviceId)));
      fields.appendChild(field("Prüfsumme", r ? shortHash(r.manifestHash) : "—"));
      card.appendChild(fields);
      const chosen = el("div", { class: "conflict-version__chosen", text: "✓ Ausgewählt" });
      chosen.style.display = "none";
      card.appendChild(chosen);
      card.addEventListener("click", () => {
        selected = idx;
        cards.forEach((c, i) => { c.card.classList.toggle("is-selected", i === idx); c.chosen.style.display = i === idx ? "block" : "none"; });
        resolveBtn.disabled = false;
      });
      cards.push({ card: card, chosen: chosen, participant: p });
      grid.appendChild(card);
    });
    modal.appendChild(grid);
    modal.appendChild(el("div", { class: "note", text: "Der gewählte Stand wird zur aktuellen Version; die anderen Geräte laden ihn beim nächsten Abgleich. „Beide behalten“ legt die andere Fassung als umbenanntes zweites Save-Set ab – nichts wird gelöscht." }));

    const foot = el("div", { class: "modal__foot" });
    foot.appendChild(el("button", { class: "btn btn--ghost", type: "button", text: "Beide behalten (umbenennen)",
      on: { click: () => resolveConflict(conflict, { resolution: "KeepBoth" }) } }));
    const right = el("div", { class: "modal__foot-right" });
    right.appendChild(el("button", { class: "btn btn--ghost", type: "button", text: "Abbrechen", on: { click: () => openGameDrawer(keyValue) } }));
    const resolveBtn = el("button", { class: "btn btn--accent", type: "button", text: "Konflikt lösen",
      on: { click: () => {
        if (selected == null) return;
        const p = participants[selected];
        resolveConflict(conflict, { resolution: "KeepDevice", winningDeviceId: p.deviceId, winningRevision: p.revision });
      } } });
    resolveBtn.disabled = true;
    right.appendChild(resolveBtn);
    foot.appendChild(right);
    modal.appendChild(foot);

    const scrim = el("div", { class: "modal-scrim", on: { click: e => { if (e.target === scrim) openGameDrawer(keyValue); } } }, [modal]);
    clear(overlayRoot);
    overlayRoot.appendChild(scrim);
  }
  function field(k, v) {
    return el("div", { class: "conflict-version__field" }, [el("span", { class: "k", text: k }), el("span", { class: "v", text: v })]);
  }

  async function resolveConflict(conflict, body) {
    try {
      const enc = encodeURIComponent(conflict.id);
      const resp = await api("/api/conflicts/" + enc + "/resolve", { method: "POST", body: JSON.stringify(body) });
      if (resp && resp.accepted) {
        toast(body.resolution === "KeepBoth" ? "Beide Versionen behalten und umbenannt." : "Konflikt gelöst – Version übernommen.");
        await loadAll(); buildChrome(); renderView(); closeOverlay();
      } else {
        toast("Konfliktlösung nicht angenommen.", true);
      }
    } catch (err) {
      if (!handleAuthFailure(err)) toast(err.message || "Konfliktlösung fehlgeschlagen.", true);
    }
  }

  // ---- Toast -------------------------------------------------------------
  const toastRoot = document.getElementById("toast-root");
  let toastTimer = null;
  function toast(message, isError) {
    clear(toastRoot);
    const t = el("div", { class: "toast" + (isError ? " toast--error" : ""), text: message });
    toastRoot.appendChild(t);
    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = setTimeout(() => clear(toastRoot), 4000);
  }

  // ---- Tastatur: Escape schließt Overlays -------------------------------
  document.addEventListener("keydown", e => {
    if (e.key === "Escape" && overlayRoot.firstChild) closeOverlay();
  });

  // =====================================================================
  // Bootstrap
  // =====================================================================
  async function boot() {
    // Zuerst Gesundheits-Check (ohne Token) für „nicht eingerichtet".
    let configured = true;
    try {
      const res = await fetch("/health");
      if (res.ok) { const h = await res.json(); configured = !!h.configured; }
    } catch (_) { /* Server evtl. nicht erreichbar – Login zeigt dann Fehler */ }

    if (!configured) { renderGate({ notConfigured: true }); return; }

    if (!state.token) { renderGate({}); return; }

    // Mit vorhandenem Token direkt versuchen zu laden.
    try {
      await loadAll();
      startApp();
    } catch (err) {
      if (handleAuthFailure(err)) return;
      // Netzfehler o.ä. -> Login mit Meldung
      renderGate({ error: err.message || "Verbindung fehlgeschlagen." });
    }
  }

  boot();
})();
