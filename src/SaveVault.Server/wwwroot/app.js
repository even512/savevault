/* =====================================================================
   SaveVault Dashboard – Frontend-Logik (self-contained, keine Fremd-Libs)
   ---------------------------------------------------------------------
   Sicherheit:
   - ALLE Fremddaten (Spiel-/Client-/Gerätenamen, Store, Prüfsummen,
     Aktionstexte …) werden ausschliesslich über textContent in den DOM
     gesetzt. innerHTML wird NUR mit festen, im Code definierten SVG-
     Konstanten (ICONS) benutzt – nie mit Serverdaten.
   - Die Anmeldung läuft über Benutzername + Passwort (POST /api/login bzw.
     /api/setup bei der Ersteinrichtung). Der zurückgegebene Session-Token
     liegt in sessionStorage und wird nur als Authorization-Header gesendet;
     er wird nie ins DOM oder in die Konsole geschrieben.
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

  // ---- Sortierung der Spiele-Ansicht (lokal gemerkt) --------------------
  // Wählbare Sortierfelder: interner Key + sichtbares deutsches Label.
  const GAME_SORT_KEY = "savevault:gameSort";
  const GAME_SORT_FIELDS = [
    { key: "name",     label: "Name" },
    { key: "size",     label: "Speichergröße" },
    { key: "activity", label: "Zuletzt aktiv" },
    { key: "devices",  label: "Geräte-Anzahl" }
  ];
  const GAME_SORT_DEFAULT = { field: "name", dir: "asc" };

  // Gemerkte Sortierung aus localStorage lesen; fehlender/defekter Wert → Default.
  function loadGameSort() {
    try {
      const raw = localStorage.getItem(GAME_SORT_KEY);
      if (!raw) return Object.assign({}, GAME_SORT_DEFAULT);
      const parsed = JSON.parse(raw);
      const field = GAME_SORT_FIELDS.some(f => f.key === (parsed && parsed.field))
        ? parsed.field : GAME_SORT_DEFAULT.field;
      const dir = (parsed && (parsed.dir === "asc" || parsed.dir === "desc"))
        ? parsed.dir : GAME_SORT_DEFAULT.dir;
      return { field: field, dir: dir };
    } catch (_) {
      return Object.assign({}, GAME_SORT_DEFAULT);
    }
  }
  // Aktuelle Sortierung lokal merken (localStorage evtl. gesperrt → still ignorieren).
  function saveGameSort() {
    try { localStorage.setItem(GAME_SORT_KEY, JSON.stringify(state.gameSort)); } catch (_) {}
  }

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
    gameSort: loadGameSort(),   // { field, dir } – lokal gemerkt, Default Name A→Z
    historyFilter: "all",
    data: { games: [], devices: [], conflicts: [], activity: [], pairing: null, gameStates: [], serverInfo: null },
    revCache: {},          // gameKeyValue -> RevisionInfo[]
    expandedHistory: {},
    // lokale (nicht server-persistente) Einstellungen – als Anzeige/Spielerei
    localSettings: { interval: 5, retention: 0, autoConflict: true, notify: true },
    loaded: false,
    // Live-Aktualisierung (SSE-Push + lokaler Re-Render-Takt); siehe startLive().
    live: {
      abort: null,        // AbortController des laufenden Streams
      backoff: 0,         // aktuelle Reconnect-Verzögerung (ms)
      refreshTimer: null, // Entprell-Timer fürs Nachladen nach einem Ereignis
      renderTimer: null,  // lokaler Re-Render-Takt (Zeit-/Offline-Anzeige altern)
      pollTimer: null,    // Fallback-Polling, falls Streaming nicht möglich ist
      refreshing: false,  // läuft gerade ein Nachladen?
      pending: false,     // während eines Nachladens/einer Interaktion kam ein weiteres Ereignis
      stopped: true       // bewusst gestoppt (Logout/Auth-Fehler) → kein Reconnect
    }
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
  // Gruppierung nach kanonischem Spiel
  // ---------------------------------------------------------------------
  // Der Server liefert je Bucket (privat je Gerät / geteilt / Konflikt-Kopie)
  // einen GameSummary. Für die Anzeige fassen wir alle Buckets desselben
  // Spiels (gleicher canonicalValue) zu EINER Gruppe zusammen: ein Titel,
  // ein Cover, ein aggregierter Status, aggregierte Kennzahlen.
  // =====================================================================
  function canonicalOf(summary) {
    return String((summary && (summary.canonicalValue || (summary.game && summary.game.value))) || "");
  }

  // Statuspriorität für die Aggregation (Konflikt schlägt alles, Synchronisiert
  // ist die „ruhige" Ausgangslage, Offline zuletzt).
  const STATUS_ORDER = ["Conflict", "Error", "Syncing", "Pending", "Synced", "Offline"];
  function aggregateStatus(buckets) {
    let best = null, bestRank = Infinity;
    for (const b of buckets) {
      const idx = STATUS_ORDER.indexOf(b.status);
      const rank = idx < 0 ? STATUS_ORDER.length : idx;
      if (rank < bestRank) { bestRank = rank; best = b.status; }
    }
    return best || "Offline";
  }

  // Prüft, ob ein Name bloß der (klein geschriebene) Rohschlüssel ist – dann ist er
  // KEIN echter Titel. Berücksichtigt auch prefixierte Bucket-Schlüssel (dev|x|anno-117).
  function nameLooksRaw(name, bucketValue, canonical) {
    const n = String(name || "").trim().toLowerCase();
    if (!n) return true;
    for (const c of [bucketValue, canonical]) {
      if (!c) continue;
      const cv = String(c).toLowerCase();
      const bare = cv.indexOf("|") >= 0 ? cv.slice(cv.lastIndexOf("|") + 1) : cv;
      if (n === cv || n === bare) return true;
    }
    return false;
  }
  // Bewertet einen Kandidatennamen: echter Titel (kein Rohschlüssel), Großbuchstaben
  // und Leerzeichen sprechen für einen per Heartbeat/Store angereicherten Namen.
  function nameScore(name, bucketValue, canonical) {
    if (!name) return 0;
    let score = 1;
    if (!nameLooksRaw(name, bucketValue, canonical)) score += 4;
    if (/[A-Z]/.test(name)) score += 2;
    if (/\s/.test(name)) score += 1;
    return score;
  }
  function pickDisplayName(buckets, canonical) {
    let best = null, bestScore = -1;
    for (const b of buckets) {
      const dn = b.game && b.game.displayName;
      if (!dn) continue;
      const sc = nameScore(dn, b.game && b.game.value, canonical);
      if (sc > bestScore) { bestScore = sc; best = dn; }
    }
    if (best) return best;
    for (const b of buckets) {
      if (b.game && (b.game.displayName || b.game.value)) return b.game.displayName || b.game.value;
    }
    return canonical || "Unbekanntes Spiel";
  }

  // Vervollständigt eine Gruppe { canonical, buckets } um die aggregierten Felder.
  function finalizeGroup(grp) {
    const buckets = grp.buckets;
    grp.displayName = pickDisplayName(buckets, grp.canonical);
    grp.store = (buckets.find(b => b.game && b.game.store) || { game: {} }).game.store || "";
    grp.totalBytes = buckets.reduce((s, b) => s + (b.totalBytes || 0), 0);
    grp.fileCount = buckets.reduce((s, b) => s + (b.fileCount || 0), 0);
    grp.status = aggregateStatus(buckets);
    let last = null;
    for (const b of buckets) {
      const t = lastActivityForGame(b.game && b.game.value);
      if (t && (!last || t > last)) last = t;
    }
    grp.lastActivity = last;
    const devs = new Set();
    for (const b of buckets) if (b.scope === "private" && b.ownerDeviceId) devs.add(b.ownerDeviceId);
    grp.deviceCount = devs.size;
    grp.hasShared = buckets.some(b => b.scope === "shared");
    return grp;
  }

  // Alle Buckets nach kanonischem Spiel gruppieren → ein Gruppen-Objekt pro Titel.
  function buildGameGroups() {
    const map = new Map();
    for (const s of state.data.games) {
      const canonical = canonicalOf(s);
      let grp = map.get(canonical);
      if (!grp) { grp = { canonical: canonical, buckets: [] }; map.set(canonical, grp); }
      grp.buckets.push(s);
    }
    const groups = [];
    for (const grp of map.values()) groups.push(finalizeGroup(grp));
    return groups;
  }

  // Meta-Zeile einer Spielkachel: spielweit (keine Bucket-Dubletten mehr).
  function groupMetaParts(grp) {
    const parts = [];
    if (grp.hasShared) parts.push("Geteilt");
    if (grp.deviceCount === 1) parts.push("1 Gerät");
    else if (grp.deviceCount > 1) parts.push("auf " + grp.deviceCount + " Geräten");
    if (grp.store) parts.push(grp.store);
    parts.push(formatBytes(grp.totalBytes));
    if (grp.lastActivity) parts.push(relTime(grp.lastActivity.toISOString()));
    return parts;
  }

  // Vergleich zweier Spiel-Gruppen nach Name (A→Z), deutsch und zahlen-bewusst.
  // Dient als Primär-Ordnung für „Name" und als stabile Sekundär-Ordnung sonst.
  function compareGamesByName(a, b) {
    return String(a.displayName).localeCompare(String(b.displayName), "de",
      { numeric: true, sensitivity: "base" });
  }
  // Comparator für die aktive Sortierung (state.gameSort). Die Richtung dreht die
  // Primär-Ordnung; bei Gleichstand wird stets nach Name A→Z sortiert. Bei „Zuletzt
  // aktiv" landen Gruppen ohne Aktivität (null) immer am Ende – unabhängig von der
  // Richtung.
  function gameSortComparator() {
    const field = state.gameSort.field;
    const factor = state.gameSort.dir === "desc" ? -1 : 1;
    return function (a, b) {
      let primary = 0;
      if (field === "name") {
        primary = factor * compareGamesByName(a, b);
      } else if (field === "size") {
        primary = factor * ((a.totalBytes || 0) - (b.totalBytes || 0));
      } else if (field === "devices") {
        primary = factor * ((a.deviceCount || 0) - (b.deviceCount || 0));
      } else if (field === "activity") {
        const ta = a.lastActivity ? a.lastActivity.getTime() : null;
        const tb = b.lastActivity ? b.lastActivity.getTime() : null;
        if (ta === null && tb === null) primary = 0;
        else if (ta === null) return 1;   // null immer ans Ende
        else if (tb === null) return -1;  // null immer ans Ende
        else primary = factor * (ta - tb);
      }
      if (primary !== 0) return primary;
      return compareGamesByName(a, b); // stabile Sekundär-Ordnung
    };
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
    const isSetup = !!opts.setup;

    const brand = el("div", { class: "gate__brand" }, [
      (function () { const l = el("div", { class: "gate__logo" }); l.innerHTML = ICONS.logo; return l; })(),
      el("div", null, [el("div", { class: "gate__title", text: "SaveVault" }),
                       el("div", { class: "muted", style: { "font-size": "12px" }, text: "Web-Dashboard" })])
    ]);
    card.appendChild(brand);

    card.appendChild(el("div", { class: "gate__sub",
      text: isSetup
        ? "Erste Einrichtung: Lege ein Dashboard-Konto an. Der Benutzername und das Passwort werden sicher (nur als Hash) auf dem Server gespeichert."
        : "Melde dich mit deinem Dashboard-Konto an." }));

    const userInput = el("input", { class: "text-input", type: "text",
      attrs: { id: "user-input", placeholder: "Benutzername", autocomplete: "username", spellcheck: "false" } });
    const passInput = el("input", { class: "text-input", type: "password",
      attrs: { id: "pass-input", placeholder: "Passwort", autocomplete: isSetup ? "new-password" : "current-password", spellcheck: "false" } });
    const pass2Input = isSetup
      ? el("input", { class: "text-input", type: "password",
          attrs: { id: "pass2-input", placeholder: "Passwort wiederholen", autocomplete: "new-password", spellcheck: "false" } })
      : null;

    const msg = el("div", { class: "gate__msg gate__msg--error" });
    if (opts.error) msg.textContent = opts.error;
    function showErr(t) { msg.classList.remove("muted"); msg.classList.add("gate__msg--error"); msg.textContent = t; }

    async function submit() {
      const username = userInput.value.trim();
      const password = passInput.value;
      if (!username || !password) { showErr("Bitte Benutzername und Passwort eingeben."); return; }
      if (isSetup) {
        if (password.length < 8) { showErr("Das Passwort muss mindestens 8 Zeichen haben."); return; }
        if (password !== pass2Input.value) { showErr("Die Passwörter stimmen nicht überein."); return; }
      }
      msg.classList.remove("gate__msg--error"); msg.classList.add("muted");
      msg.textContent = isSetup ? "Konto wird angelegt…" : "Anmeldung läuft…";
      try {
        const res = await fetch(isSetup ? "/api/setup" : "/api/login", {
          method: "POST", headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ username: username, password: password })
        });
        const data = await res.json().catch(() => null);
        if (!res.ok) throw new Error((data && data.error) || (isSetup ? "Einrichtung fehlgeschlagen." : "Anmeldung fehlgeschlagen."));
        if (!data || !data.sessionToken) throw new Error("Unerwartete Serverantwort.");
        state.token = data.sessionToken;
        sessionStorage.setItem(TOKEN_KEY, data.sessionToken);
        await loadAll();
        msg.textContent = "";
        startApp();
      } catch (err) {
        state.token = null;
        sessionStorage.removeItem(TOKEN_KEY);
        showErr(err && err.message ? err.message : "Fehlgeschlagen.");
      }
    }

    passInput.addEventListener("keydown", e => { if (e.key === "Enter" && !isSetup) submit(); });
    if (pass2Input) pass2Input.addEventListener("keydown", e => { if (e.key === "Enter") submit(); });

    card.appendChild(el("label", { class: "field-label", text: "Benutzername", attrs: { for: "user-input" } }));
    card.appendChild(userInput);
    card.appendChild(el("label", { class: "field-label", text: "Passwort", attrs: { for: "pass-input" } }));
    card.appendChild(passInput);
    if (isSetup) {
      card.appendChild(el("label", { class: "field-label", text: "Passwort wiederholen", attrs: { for: "pass2-input" } }));
      card.appendChild(pass2Input);
    }
    const btn = el("button", { class: "btn btn--accent", type: "button",
      text: isSetup ? "Konto anlegen" : "Anmelden", on: { click: submit } });
    card.appendChild(el("div", { class: "gate__actions" }, [btn]));
    card.appendChild(msg);

    gate.appendChild(card);
    showGate();
    userInput.focus();
  }

  // Meldet die aktuelle Sitzung ab (Session serverseitig beenden, Token verwerfen, zurück zum Login).
  async function doLogout() {
    stopLive();
    const token = state.token;
    state.token = null;
    sessionStorage.removeItem(TOKEN_KEY);
    if (token) {
      try {
        await fetch("/api/logout", { method: "POST", headers: { "Authorization": "Bearer " + token } });
      } catch (_) { /* egal – lokal sind wir bereits abgemeldet */ }
    }
    renderGate({});
  }

  // Auth-Fehler zentral behandeln: Sitzung verwerfen, zurück zum Tor.
  function handleAuthFailure(err) {
    if (err && err.status === 503) { stopLive(); renderGate({ setup: true }); return true; }
    if (err && err.isAuth) {
      stopLive();
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
    if (search.value !== state.search) search.value = state.search; // Caret nicht unnötig ans Ende springen lassen
    search.oninput = e => { state.search = e.target.value; if (state.view === "games" || state.view === "clients") renderView(); };

    document.getElementById("refresh-btn").onclick = refreshData;
  }

  function setView(view) {
    state.view = view;
    // Suche/Filter beim Wechsel zurücksetzen (klare Ausgangslage).
    if (view !== "games" && view !== "clients") { /* Suche bleibt egal */ }
    document.documentElement.classList.remove("sv-live"); // Nutzer-Navigation: darf wieder einblenden
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
      document.documentElement.classList.remove("sv-live"); // manueller Refresh: darf einblenden
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
    startLive();
  }

  // =====================================================================
  // Live-Aktualisierung (Server-Push per SSE + lokaler Re-Render-Takt)
  // ---------------------------------------------------------------------
  // Der Server hält einen offenen Stream (/api/events) und schickt bei jeder
  // Zustandsänderung ein grobes Ereignis; darauf lädt das Dashboard den vollen
  // Stand nach (entprellt) und rendert neu – ohne Zutun des Nutzers. Der Stream
  // wird per fetch()-Reader gelesen (NICHT EventSource), damit der Session-Token
  // im Authorization-Header bleibt und nie in eine URL wandert. Zusätzlich rendert
  // ein lokaler Takt regelmäßig neu, damit zeitabhängige Anzeigen (relative Zeiten,
  // aus lastSeen abgeleiteter Offline-Status) auch ohne Server-Ereignis „altern".
  // =====================================================================

  // Ist der Nutzer gerade dabei, etwas zu bedienen (Sucheingabe, Slider …)? Dann NICHT die
  // Ansicht unter seinen Fingern neu aufbauen – der Refresh wird bis danach zurückgestellt.
  function isInteracting() {
    const a = document.activeElement;
    return !!(a && !app.hidden && /^(INPUT|SELECT|TEXTAREA)$/.test(a.tagName));
  }

  // Baut ein offenes Client-Detail-Panel mit frischen Daten neu auf (Status/„zuletzt gesehen"
  // altern live mit). Nur wenn der Client-Drawer das aktuelle Overlay ist (der Marker existiert
  // nur dann – ein Modal/anderes Overlay hätte den overlayRoot geleert) und der Nutzer nicht
  // gerade darin tippt. openClientDrawer ist rein synchron aus state.data → gefahrlos.
  function refreshOpenDrawer() {
    const p = overlayRoot.querySelector(".js-client-drawer");
    if (p && p.getAttribute("data-device-id") && !isInteracting())
      openClientDrawer(p.getAttribute("data-device-id"));
  }

  // Entprellter Voll-Refresh nach einem Ereignis: neu laden + rendern, ohne den manuellen
  // Refresh-Button-Zustand zu stören. Kommt ein Ereignis, während schon geladen wird ODER der
  // Nutzer gerade tippt/schiebt, wird es als „pending" gemerkt und danach nachgeholt (kein
  // stiller Verlust). Auth-Fehler → zurück zum Tor.
  function liveRefreshNow() {
    if (!state.token || app.hidden) return;
    if (state.live.refreshing || isInteracting()) { state.live.pending = true; return; }
    state.live.refreshing = true;
    loadAll().then(() => {
      document.documentElement.classList.add("sv-live"); // Auto-Update: ohne Einblende-Animation (kein Flackern)
      buildChrome();
      renderView();
      refreshOpenDrawer(); // ein offenes Client-Panel gleich mit frischen Daten neu aufbauen
    }).catch(err => {
      if (handleAuthFailure(err)) stopLive();
      // sonst still schlucken – der nächste Tick/das nächste Ereignis versucht es erneut
    }).finally(() => {
      state.live.refreshing = false;
      // Während des Ladens kam ein weiteres Ereignis? Sofort nachziehen (sofern nicht gerade bedient).
      if (state.live.pending && !isInteracting()) { state.live.pending = false; scheduleLiveRefresh(); }
    });
  }
  function scheduleLiveRefresh() {
    if (state.live.refreshTimer) return; // schon geplant → Ereignis-Bursts entprellen
    state.live.refreshTimer = setTimeout(() => {
      state.live.refreshTimer = null;
      liveRefreshNow();
    }, 400);
  }

  function startRenderTimer() {
    if (state.live.renderTimer) return;
    state.live.renderTimer = setInterval(() => {
      if (app.hidden || !state.token || isInteracting()) return; // Interaktion nie unterbrechen
      // Zurückgestelltes Nachladen jetzt nachholen, sonst nur die Zeit-/Offline-Anzeige altern lassen.
      if (state.live.pending) { state.live.pending = false; liveRefreshNow(); }
      else {
        document.documentElement.classList.add("sv-live"); // Takt-Update: ohne Einblende-Animation
        renderView(); refreshOpenDrawer(); // auch ein offenes Client-Panel altern lassen (Offline-Übergang)
      }
    }, 12000);
  }

  // Fallback: kann der Browser den Stream nicht lesen (kein ReadableStream/getReader),
  // aktualisiert sich das Dashboard eben per periodischem Polling.
  function startPollingFallback() {
    if (state.live.pollTimer || state.live.stopped) return;
    state.live.pollTimer = setInterval(() => {
      if (!app.hidden && state.token) liveRefreshNow();
    }, 8000);
  }

  function scheduleReconnect() {
    state.live.abort = null;
    const delay = Math.min(30000, state.live.backoff ? state.live.backoff * 2 : 1000);
    state.live.backoff = delay;
    setTimeout(() => { if (!state.live.stopped) liveConnect(); }, delay);
  }

  async function liveConnect() {
    if (state.live.stopped || !state.token) return;
    if (typeof ReadableStream === "undefined") { startPollingFallback(); return; }

    const ctrl = new AbortController();
    state.live.abort = ctrl;
    try {
      const res = await fetch("/api/events", {
        headers: { "Authorization": "Bearer " + state.token, "Accept": "text/event-stream" },
        signal: ctrl.signal, cache: "no-store"
      });
      if (res.status === 401 || res.status === 503) { handleAuthFailure(AuthError("Sitzung abgelaufen.", res.status)); stopLive(); return; }
      if (res.status === 403) { handleAuthFailure(AuthError("Zugriff verweigert.", 403)); stopLive(); return; }
      // Browser kann den Stream nicht lesen → dauerhaft auf Polling ausweichen (kein Reconnect mehr).
      if (!res.body || !res.body.getReader) { startPollingFallback(); return; }
      // Vorübergehender Serverfehler (500/502 …): wie ein Abbruch behandeln → Reconnect mit Backoff.
      if (!res.ok) throw new Error("http-" + res.status);

      // Stream steht: ein evtl. laufendes Fallback-Polling beenden (nie beides gleichzeitig laufen lassen).
      if (state.live.pollTimer) { clearInterval(state.live.pollTimer); state.live.pollTimer = null; }
      state.live.backoff = 0; // Backoff zurücksetzen
      scheduleLiveRefresh();  // bei (Re)Connect einmal voll nachladen (verpasste Änderungen)

      const reader = res.body.getReader();
      const decoder = new TextDecoder();
      let buf = "";
      while (true) {
        const chunk = await reader.read();
        if (chunk.done) break;
        buf += decoder.decode(chunk.value, { stream: true });
        // SSE-Ereignisse sind durch Leerzeilen getrennt. Den Inhalt müssen wir nicht deuten:
        // JEDES Ereignis (event:/data:-Block) stößt einen Voll-Refresh an. Kommentare (: ping)
        // werden ignoriert.
        let idx;
        while ((idx = buf.indexOf("\n\n")) >= 0) {
          const block = buf.slice(0, idx);
          buf = buf.slice(idx + 2);
          if (/^(event|data):/m.test(block)) scheduleLiveRefresh();
        }
      }
      throw new Error("stream-ended"); // Server hat beendet → Reconnect
    } catch (err) {
      if (state.live.stopped || ctrl.signal.aborted) return; // von uns beendet
      scheduleReconnect();
    }
  }

  function startLive() {
    stopLive();                 // sauberer Neustart (doppelte Verbindungen vermeiden)
    state.live.stopped = false;
    state.live.backoff = 0;
    startRenderTimer();
    liveConnect();
  }
  function stopLive() {
    state.live.stopped = true;
    if (state.live.abort) { try { state.live.abort.abort(); } catch (_) {} state.live.abort = null; }
    if (state.live.refreshTimer) { clearTimeout(state.live.refreshTimer); state.live.refreshTimer = null; }
    if (state.live.renderTimer) { clearInterval(state.live.renderTimer); state.live.renderTimer = null; }
    if (state.live.pollTimer) { clearInterval(state.live.pollTimer); state.live.pollTimer = null; }
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
    const groups = buildGameGroups();

    const totalBytes = games.reduce((s, g) => s + (g.totalBytes || 0), 0);
    const conflictCount = state.data.conflicts.length;
    const stats = [
      { label: "Spiele erfasst", value: String(groups.length), suffix: "Titel", cls: "" },
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
    if (groups.length === 0) {
      gamesPanel.appendChild(el("div", { class: "empty", text: "Noch keine Spiele erfasst." }));
    } else {
      const preview = groups
        .slice()
        .sort((a, b) => (b.lastActivity ? b.lastActivity.getTime() : 0) - (a.lastActivity ? a.lastActivity.getTime() : 0))
        .slice(0, 6);
      const grid = el("div", { class: "game-preview-grid" });
      for (const grp of preview) grid.appendChild(gameCard(grp, false));
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

  // ---- GameCard (eine Kachel pro Spiel = eine Gruppe) --------------------
  function gameCard(grp, big) {
    const meta = statusMeta(grp.status);
    const card = el("button", { class: "game-card" + (big ? " is-big" : ""), type: "button",
      on: { click: () => openGameDrawer(grp.canonical) } });
    // Cover kanonisch: ein Bild je Spiel (der Server löst intern kanonisch auf).
    const coverEl = el("div", { class: "game-card__cover", style: { background: coverColor(grp.canonical) } });
    card.appendChild(coverEl);
    loadCover(grp.canonical, coverEl);
    const body = el("div", { class: "game-card__body" });
    body.appendChild(el("div", { class: "game-card__name", text: grp.displayName }));
    body.appendChild(el("div", { class: "game-card__meta", text: groupMetaParts(grp).join(" · ") }));
    const dot = el("span", { class: "dot dot--" + meta.cls + (meta.pulse ? " is-pulse" : "") });
    body.appendChild(el("div", { class: "status-line status--" + meta.cls, style: { "margin-top": "6px", "font-size": "11.5px", "font-weight": "600" } },
      [dot, document.createTextNode(meta.label)]));
    card.appendChild(body);
    return card;
  }

  // ---- ClientRow (kompakt) ----------------------------------------------
  // Ein Client gilt als „verbunden", solange sein letzter Heartbeat nicht länger als
  // CLIENT_OFFLINE_AFTER_SEC zurückliegt. Der Wert entspricht ~3 ausgebliebenen Heartbeats
  // (Default-Takt 15 s) – tolerant gegen einen einzelnen verpassten Heartbeat/Netz-Jitter,
  // aber zeitnah genug, dass ein geschlossener Client rasch als offline erscheint. (Der lokale
  // Re-Render-Takt lässt diesen Übergang „altern", ohne dass ein Server-Ereignis nötig ist.)
  const CLIENT_OFFLINE_AFTER_SEC = 45;
  function clientDerivedStatus(device) {
    const d = parseDate(device.lastSeenUtc);
    if (!d) return "Offline";
    const sec = (Date.now() - d.getTime()) / 1000;
    return sec <= CLIENT_OFFLINE_AFTER_SEC ? "Synced" : "Offline";
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

  // ---- Sortier-Regler der Spiele-Ansicht --------------------------------
  // Dropdown (Feld) + Richtungsknopf (↑/↓). Änderung: State setzen, lokal merken,
  // neu rendern. Reine Client-Werte – keine neue Eingabe von aussen.
  function buildGameSortControl() {
    const wrap = el("div", { class: "sort-control" });
    wrap.appendChild(el("label", { class: "sort-control__label", text: "Sortieren:",
      attrs: { for: "game-sort-select" } }));

    const select = el("select", { class: "sort-control__select",
      attrs: { id: "game-sort-select", "aria-label": "Spiele sortieren nach" },
      on: { change: e => { state.gameSort.field = e.target.value; saveGameSort(); renderView(); } } });
    for (const f of GAME_SORT_FIELDS) {
      select.appendChild(el("option", { text: f.label, attrs: { value: f.key } }));
    }
    select.value = state.gameSort.field; // fällt auf erstes Feld zurück, falls unbekannt
    wrap.appendChild(select);

    const asc = state.gameSort.dir === "asc";
    const dirText = asc ? "Aufsteigend (A→Z)" : "Absteigend (Z→A)";
    wrap.appendChild(el("button", { class: "sort-control__dir", type: "button",
      text: asc ? "↑" : "↓", title: "Sortierrichtung: " + dirText,
      attrs: { "aria-label": "Sortierrichtung umschalten – aktuell: " + dirText },
      on: { click: () => {
        state.gameSort.dir = state.gameSort.dir === "asc" ? "desc" : "asc";
        saveGameSort(); renderView();
      } } }));
    return wrap;
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
    // Statusfilter links, Sortier-Regler rechts (bricht bei schmalem Viewport um).
    frag.appendChild(el("div", { class: "games-toolbar" }, [pillRow, buildGameSortControl()]));

    const search = state.search.trim().toLowerCase();
    // Filter/Suche arbeiten auf SPIEL-Ebene: ein Treffer je Spiel, Statusfilter
    // gegen den aggregierten Status, Suche gegen den Spielnamen.
    const groups = buildGameGroups();
    const list = groups.filter(grp => {
      const matchSearch = !search || grp.displayName.toLowerCase().includes(search);
      const matchStatus = state.gameFilter === "all" || grp.status === state.gameFilter;
      return matchSearch && matchStatus;
    });

    if (list.length === 0) {
      frag.appendChild(el("div", { class: "empty",
        text: state.data.games.length === 0 ? "Noch keine Spiele erfasst. Koppele einen Client, damit Spielstände erscheinen." : "Keine Spiele gefunden." }));
      return frag;
    }

    // Sortierung wirkt NACH Filter und Suche auf die bereits gefilterte Liste.
    list.sort(gameSortComparator());

    const grid = el("div", { class: "card-grid-3" });
    for (const grp of list) grid.appendChild(gameCard(grp, true));
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

    // Konto (Dashboard-Anmeldung)
    const account = el("div", { class: "settings-card" });
    account.appendChild(el("div", { class: "settings-card__title", text: "Konto" }));
    account.appendChild(el("div", { class: "settings-card__sub", text: "Dashboard-Anmeldung dieses Browsers" }));
    const logoutBtn = el("button", { class: "btn btn--ghost", type: "button", text: "Abmelden",
      on: { click: () => doLogout() } });
    account.appendChild(el("div", { style: { "margin-top": "10px" } }, [logoutBtn]));
    grid.appendChild(account);

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
    stoList.appendChild(settingsRow("Spiele erfasst", String(buildGameGroups().length)));
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

  // Generischer Bestätigungsdialog (reingezogen aus dem Modal-Muster). onCancel/onConfirm
  // sind Rückrufe; onCancel führt in der Regel in den Drawer zurück statt nur zu schließen.
  function confirmModal(opts) {
    const cancel = opts.onCancel || closeOverlay;
    const modal = el("div", { class: "modal modal--sm" });
    const head = el("div", { class: "modal__head" });
    head.appendChild(el("div", null, [
      el("div", { class: "modal__title", text: opts.title }),
      opts.message ? el("div", { class: "modal__sub", text: opts.message }) : null
    ]));
    const closeBtn = el("button", { class: "close-btn", type: "button", on: { click: cancel } });
    closeBtn.appendChild(iconEl("close", "close-btn__glyph"));
    head.appendChild(closeBtn);
    modal.appendChild(head);
    if (opts.lines && opts.lines.length) {
      const box = el("div", { style: { margin: "8px 0 2px", display: "grid", gap: "6px" } });
      for (const ln of opts.lines) box.appendChild(el("div", { class: "muted", style: { "font-size": "12.5px" }, text: ln }));
      modal.appendChild(box);
    }
    modal.appendChild(el("div", { class: "modal__foot" }, [
      el("span"),
      el("div", { class: "modal__foot-right" }, [
        el("button", { class: "btn btn--ghost", type: "button", text: "Abbrechen", on: { click: cancel } }),
        el("button", { class: "btn " + (opts.danger ? "btn--danger" : "btn--accent"), type: "button",
          text: opts.confirmText || "Bestätigen", on: { click: opts.onConfirm } })
      ])
    ]));
    const scrim = el("div", { class: "modal-scrim", on: { click: e => { if (e.target === scrim) cancel(); } } }, [modal]);
    clear(overlayRoot);
    overlayRoot.appendChild(scrim);
  }

  // ---- Teilen-Aktionen / Bucket-Beschriftung ----------------------------
  // Kurze Überschrift eines Buckets im Spiel-Drawer.
  function bucketHeading(summary) {
    if (summary.isFork) return "Konflikt-Kopie";
    if (summary.scope === "shared") return "Geteilt";
    if (summary.ownerDeviceId) return "Lokal: " + deviceName(summary.ownerDeviceId);
    return "Lokal";
  }
  function scopeLabel(summary) {
    // Konflikt-Kopien tragen keinen Scope-Präfix, sind aber keine privaten/geteilten
    // Buckets, sondern bewahrte Verlierer-Stände einer KeepBoth-Lösung.
    if (summary.isFork) return "Konflikt-Kopie · bewahrter Verlierer-Stand";
    if (summary.scope === "shared") return "Geteilt · synchron über Geräte";
    return "Lokal · " + (summary.ownerDeviceId ? deviceName(summary.ownerDeviceId) : "dieses Gerät");
  }
  function sharedExistsFor(canonical) {
    return state.data.games.some(x => x.scope === "shared" && (x.canonicalValue || (x.game && x.game.value)) === canonical);
  }
  function gameScopeBar(summary, canonical) {
    canonical = canonical || summary.canonicalValue || (summary.game && summary.game.value);
    const wrap = el("div", { style: { display: "flex", "align-items": "center", gap: "10px", "flex-wrap": "wrap", margin: "2px 0 10px" } });
    wrap.appendChild(el("span", { class: "muted", style: { "font-size": "12.5px" }, text: scopeLabel(summary) }));

    if (summary.isFork) {
      // Keine Teilen-Aktion auf Konflikt-Kopien.
    } else if (summary.scope === "private") {
      if (sharedExistsFor(canonical)) {
        wrap.appendChild(el("span", { class: "muted", style: { "font-size": "12.5px" }, text: "· geteilter Stand existiert bereits" }));
      } else {
        wrap.appendChild(el("button", { class: "btn btn--accent", type: "button", text: "Über Geräte teilen",
          on: { click: () => beginShare(canonical, summary.ownerDeviceId, canonical) } }));
      }
    }
    return wrap;
  }
  // Alle privaten Buckets (mit Stand) desselben kanonischen Spiels = Teilen-Kandidaten.
  function shareCandidates(canonical) {
    return state.data.games.filter(x =>
      x.scope === "private" && (x.canonicalValue || x.game.value) === canonical && x.currentRevision > 0);
  }
  // Genau ein Kandidat → direkt einsäen (Spec: ohne Rückfrage). Mehrere → Vergleichsdialog.
  function beginShare(canonical, fallbackOwner, drawerKey) {
    const cands = shareCandidates(canonical);
    if (cands.length <= 1) {
      const owner = (cands[0] && cands[0].ownerDeviceId) || fallbackOwner;
      doShareSeed(canonical, owner, drawerKey);
      return;
    }
    openSharePicker(canonical, cands, drawerKey);
  }
  // Vergleichsdialog: Kennzahlen aller Geräte-Buckets, Admin wählt den geteilten Ausgangsstand.
  function openSharePicker(canonical, cands, drawerKey) {
    const modal = el("div", { class: "modal modal--sm" });
    const head = el("div", { class: "modal__head" });
    head.appendChild(el("div", null, [
      el("div", { class: "modal__title", text: "Über Geräte teilen" }),
      el("div", { class: "modal__sub", text: "Welcher Geräte-Stand wird der geteilte Ausgangsstand?" })
    ]));
    const closeBtn = el("button", { class: "close-btn", type: "button", on: { click: () => openGameDrawer(drawerKey) } });
    closeBtn.appendChild(iconEl("close", "close-btn__glyph"));
    head.appendChild(closeBtn);
    modal.appendChild(head);

    const list = el("div", { class: "picker-list" });
    for (const c of cands) {
      const t = lastActivityForGame(c.game.value);
      const item = el("button", { class: "picker-item", type: "button",
        on: { click: () => doShareSeed(canonical, c.ownerDeviceId, drawerKey) } });
      item.appendChild(el("div", null, [
        el("div", { style: { "font-weight": "600", "font-size": "13px" }, text: deviceName(c.ownerDeviceId) }),
        el("div", { class: "muted", style: { "font-size": "11.5px", "margin-top": "2px" },
          text: "Revision " + (c.currentRevision || 0) + " · " + formatBytes(c.totalBytes) + " · "
            + (c.fileCount || 0) + " Dateien" + (t ? " · " + relTime(t.toISOString()) : "") })
      ]));
      item.appendChild(iconEl("restore", "history-row__icon status--syncing"));
      list.appendChild(item);
    }
    modal.appendChild(list);
    modal.appendChild(el("div", { class: "modal__foot" }, [
      el("span"),
      el("button", { class: "btn btn--ghost", type: "button", text: "Abbrechen", on: { click: () => openGameDrawer(drawerKey) } })
    ]));

    const scrim = el("div", { class: "modal-scrim", on: { click: e => { if (e.target === scrim) openGameDrawer(drawerKey); } } }, [modal]);
    clear(overlayRoot);
    overlayRoot.appendChild(scrim);
  }
  async function doShareSeed(canonical, ownerDeviceId, drawerKey) {
    if (!ownerDeviceId) { toast("Kein Quell-Gerät bekannt.", true); return; }
    try {
      const resp = await api("/api/games/" + encodeURIComponent(canonical) + "/share",
        { method: "POST", body: JSON.stringify({ sourceDeviceId: ownerDeviceId }) });
      toast("Geteilter Stand etabliert (Revision " + (resp && resp.sharedRevision) + "). Geräte können jetzt beitreten.");
      await loadAll(); buildChrome(); openGameDrawer(drawerKey);
    } catch (err) {
      if (!handleAuthFailure(err)) toast(err.message || "Teilen fehlgeschlagen.", true);
    }
  }
  // ---- Spiel-Drawer (kanonisch, mit Bucket-Aufschlüsselung) --------------
  // Wird mit dem KANONISCHEN Spielschlüssel geöffnet und zeigt je zugehörigem
  // Bucket (privat je Gerät / geteilt / Konflikt-Kopie) einen Abschnitt.
  function openGameDrawer(canonical) {
    const buckets = state.data.games.filter(g => canonicalOf(g) === canonical);
    if (buckets.length === 0) return;
    const grp = finalizeGroup({ canonical: canonical, buckets: buckets.slice() });

    const drawer = el("div", { class: "drawer" });
    openOverlay(closeOverlay, drawer);

    // Kopf: Spielname + ein kanonisches Cover + aggregierte Kennzahlen.
    const head = el("div", { class: "drawer__head" });
    const drawerCover = el("div", { class: "drawer__cover", style: { background: coverColor(canonical) } });
    loadCover(canonical, drawerCover);
    const metaBits = [];
    if (grp.store) metaBits.push(grp.store);
    metaBits.push(formatBytes(grp.totalBytes));
    metaBits.push((grp.fileCount || 0) + " Dateien");
    if (grp.deviceCount === 1) metaBits.push("1 Gerät");
    else if (grp.deviceCount > 1) metaBits.push("auf " + grp.deviceCount + " Geräten");
    const idBlock = el("div", { class: "drawer__id" }, [
      drawerCover,
      el("div", null, [
        el("div", { class: "drawer__title", text: grp.displayName }),
        el("div", { class: "drawer__sub", text: metaBits.join(" · ") })
      ])
    ]);
    const closeBtn = el("button", { class: "close-btn", type: "button", title: "Schließen", on: { click: closeOverlay } });
    closeBtn.appendChild(iconEl("close", "close-btn__glyph"));
    head.appendChild(idBlock);
    head.appendChild(closeBtn);
    drawer.appendChild(head);

    // Je Bucket ein Abschnitt (bei nur einem Bucket genau einer – nichts leer).
    for (const bucket of buckets) drawer.appendChild(bucketSection(bucket, canonical));
  }

  // Ein Bucket-Abschnitt: Überschrift, Scope/Teilen, ggf. Konflikt-Banner,
  // Clients (Per-Gerät-Status) und Versionsverlauf. Die Detail-Daten werden
  // PRO BUCKET über den Bucket-Schlüssel geladen (nur das Cover ist kanonisch).
  function bucketSection(bucket, canonical) {
    const bucketValue = bucket.game && bucket.game.value;
    const section = el("div", { class: "bucket-section" });

    section.appendChild(el("div", { class: "bucket-section__head", text: bucketHeading(bucket) }));
    section.appendChild(gameScopeBar(bucket, canonical));

    // Standard-Save-Pfad (aus der neuesten Revision mit bekanntem Pfad).
    const pathEl = el("div", { class: "drawer__sub", style: { "margin-top": "2px", opacity: "0.75", "word-break": "break-all" } });
    section.appendChild(pathEl);

    // Konflikt-Banner nur, wenn genau dieser Bucket betroffen ist.
    const conflict = state.data.conflicts.find(c => c.game && c.game.value === bucketValue);
    if (conflict) {
      const banner = el("div", { class: "conflict-banner" });
      banner.appendChild(el("div", { class: "conflict-banner__text" }, [
        (function () { const b = el("b"); b.textContent = "Sync-Konflikt erkannt. "; return b; })(),
        document.createTextNode("Mehrere Clients haben abweichende Spielstände.")
      ]));
      banner.appendChild(el("button", { class: "btn btn--conflict", type: "button", text: "Lösen",
        on: { click: () => openConflictModal(conflict, canonical) } }));
      section.appendChild(banner);
    }

    section.appendChild(el("div", { class: "section-label", text: "Clients" }));
    const clientsBody = el("div", { class: "drawer-list" });
    clientsBody.appendChild(loadingState());
    section.appendChild(clientsBody);

    section.appendChild(el("div", { class: "section-label", text: "Versionsverlauf" }));
    const versionBody = el("div", { class: "version-list" });
    versionBody.appendChild(loadingState());
    section.appendChild(versionBody);

    fillBucketSection(bucketValue, canonical, clientsBody, versionBody, pathEl);
    return section;
  }

  async function fillBucketSection(bucketValue, canonical, clientsBody, versionBody, pathEl) {
    let revisions = [];
    try {
      revisions = await getRevisions(bucketValue);
    } catch (err) {
      clear(clientsBody); clear(versionBody);
      if (handleAuthFailure(err)) { closeOverlay(); return; }
      clientsBody.appendChild(el("div", { class: "empty", text: "—" }));
      versionBody.appendChild(el("div", { class: "empty", text: err.message || "Konnte Versionen nicht laden." }));
      return;
    }

    // Per-Gerät-Zustand aus /api/game-states; Revisionen liefern die letzte
    // Übertragung je Gerät für die Unterzeile.
    clear(clientsBody);
    const latestByDevice = {};
    for (const r of revisions) {
      if (!latestByDevice[r.deviceId] || r.number > latestByDevice[r.deviceId].number) latestByDevice[r.deviceId] = r;
    }
    const gameStates = (state.data.gameStates || []).filter(s => s.game && s.game.value === bucketValue);
    if (gameStates.length === 0) {
      clientsBody.appendChild(el("div", { class: "empty", text: "Noch kein Client hat diesen Stand synchronisiert." }));
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

    const withRoot = revisions.find(r => r.saveRoot);
    if (withRoot) pathEl.textContent = "Standard-Pfad: " + withRoot.saveRoot;

    clear(versionBody);
    if (revisions.length === 0) {
      versionBody.appendChild(el("div", { class: "empty", text: "Noch keine Versionen." }));
    } else {
      // Standardmäßig nur die aktuellste Version zeigen; ältere Versionen (falls
      // vorhanden) hinter einem lokalen Auf-/Zuklapp-Toggle verstecken. Der Zustand
      // lebt nur hier, da der Drawer bei jedem Öffnen neu aufgebaut wird.
      const [latest, ...older] = revisions;
      versionBody.appendChild(versionRow(latest, bucketValue, canonical));

      if (older.length > 0) {
        let expanded = false;
        const olderWrap = el("div", { class: "version-older" });
        const label = el("span", { class: "version-toggle__label" });
        const toggle = el("button", { class: "version-toggle", type: "button" }, [
          label, iconEl("chevron", "version-toggle__chevron")
        ]);

        const renderOlder = () => {
          clear(olderWrap);
          if (expanded) for (const r of older) olderWrap.appendChild(versionRow(r, bucketValue, canonical));
        };
        const updateToggle = () => {
          label.textContent = expanded ? "Ältere Versionen ausblenden" : "Ältere Versionen anzeigen (" + older.length + ")";
          toggle.classList.toggle("is-open", expanded);
        };
        toggle.addEventListener("click", () => { expanded = !expanded; updateToggle(); renderOlder(); });
        updateToggle();

        versionBody.appendChild(toggle);
        versionBody.appendChild(olderWrap);
      }
    }
  }

  // Eine Versionszeile (Revisionsnummer, Zeit, Gerät, Größe, Export/Wiederherstellen).
  // Wird sowohl für die stets sichtbare aktuellste Version als auch für die
  // eingeklappten älteren Versionen verwendet.
  function versionRow(r, bucketValue, canonical) {
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
      on: { click: (ev) => downloadRevisionExport(bucketValue, r.number, ev.currentTarget) } }));
    actions.appendChild(el("button", { class: "btn-inline", type: "button", text: "Wiederherstellen",
      on: { click: () => openRestorePicker(bucketValue, r.number, canonical) } }));
    row.appendChild(actions);
    return row;
  }

  function loadingState() {
    const wrap = el("div", { class: "center-state", style: { padding: "24px 0" } });
    wrap.appendChild(iconEl("refresh", "spin"));
    wrap.appendChild(el("div", { class: "muted", text: "Lädt…" }));
    return wrap;
  }

  // ---- Restore-Ziel-Auswahl (Modal) -------------------------------------
  function openRestorePicker(keyValue, revisionNumber, canonical) {
    // keyValue = Bucket-Schlüssel (für die Operation); canonical = Spiel (für die
    // Rückkehr in den Drawer).
    const back = () => openGameDrawer(canonical || keyValue);
    const devices = state.data.devices;
    const modal = el("div", { class: "modal modal--sm" });
    const head = el("div", { class: "modal__head" });
    head.appendChild(el("div", null, [
      el("div", { class: "modal__title", text: "Version wiederherstellen" }),
      el("div", { class: "modal__sub", text: "Revision #" + revisionNumber + " – auf welches Gerät?" })
    ]));
    const closeBtn = el("button", { class: "close-btn", type: "button", on: { click: back } });
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
      el("button", { class: "btn btn--ghost", type: "button", text: "Abbrechen", on: { click: back } })
    ]));

    const scrim = el("div", { class: "modal-scrim", on: { click: e => { if (e.target === scrim) back(); } } }, [modal]);
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
    // Marker + Geräte-ID, damit refreshOpenDrawer() dieses (rein aus state.data gebaute, synchrone)
    // Panel bei Live-Updates zerstörungsfrei neu aufbauen kann. Wird ein anderes Overlay/Modal
    // geöffnet, leert das den overlayRoot → der Marker verschwindet → keine Auto-Aktualisierung.
    const drawer = el("div", { class: "drawer js-client-drawer", attrs: { "data-device-id": deviceId } });
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
  async function openConflictModal(conflict, canonical) {
    const keyValue = conflict.game && conflict.game.value;
    const back = () => openGameDrawer(canonical || keyValue);
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
    const closeBtn = el("button", { class: "close-btn", type: "button", on: { click: back } });
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
    right.appendChild(el("button", { class: "btn btn--ghost", type: "button", text: "Abbrechen", on: { click: back } }));
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

    const scrim = el("div", { class: "modal-scrim", on: { click: e => { if (e.target === scrim) back(); } } }, [modal]);
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
    // Zuerst Gesundheits-Check (ohne Token): braucht der Server noch eine Ersteinrichtung?
    let needsSetup = false;
    try {
      const res = await fetch("/health");
      if (res.ok) { const h = await res.json(); needsSetup = !!h.needsSetup; }
    } catch (_) { /* Server evtl. nicht erreichbar – Login zeigt dann Fehler */ }

    if (needsSetup) { renderGate({ setup: true }); return; }

    if (!state.token) { renderGate({}); return; }

    // Mit vorhandener Sitzung direkt versuchen zu laden.
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
