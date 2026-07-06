"use strict";

(() => {
const fb = window.fb;

// Apps em segundo plano comumente "gordos" que sugerimos encerrar por padrao.
// Comparado por inclusao, em minusculas.
const SUGGESTED_KILL = [
  "googleupdate",
  "googlecrashhandler",
  "onedrive",
  "spotify",
  "epicgameslauncher",
  "yourphone",
  "phoneexperiencehost",
  "msteams",
  "ms-teams",
  "teams",
  "skype",
  "ccleaner",
  "officeclicktorun",
  "adobe",
  "acrotray",
  "msedge",
  "widgets",
];

const MANUAL_REVIEW = [
  "discord",
  "obs",
  "gamebar",
  "nvcontainer",
  "rtkauduservice",
];

const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];

function isSuggested(name) {
  const n = String(name).toLowerCase();
  return SUGGESTED_KILL.some((s) => n.includes(s));
}

function isManualReview(name) {
  const n = String(name).toLowerCase();
  return MANUAL_REVIEW.some((s) => n.includes(s));
}

function processTag(name) {
  if (isSuggested(name)) return { text: "sugerido", kind: "suggested" };
  if (isManualReview(name)) return { text: "manual", kind: "manual" };
  return null;
}

function setBusy(btn, busy) {
  if (!btn) return;
  btn.classList.toggle("is-busy", busy);
  btn.disabled = busy;
}

function moduleResult(action, text, kind = "") {
  const module = document.querySelector(`.module [data-action="${action}"]`)
    ?.closest(".module");
  const el = module && module.querySelector("[data-result]");
  if (!el) return;
  el.textContent = text || "";
  el.className = "module-result" + (kind ? " " + kind : "");
}

function moduleDetail(action, items = []) {
  const el = $("#actionLog");
  if (!el) return;

  const meta = {
    clean: ["Limpeza aplicada", "Arquivos removidos e itens preservados"],
    ram: ["RAM otimizada", "Working set e memoria fisica"],
    fps: ["Modo FPS", "Energia, Game Mode, Game DVR e rollback"],
    proc: ["Processos", "Selecao, protecoes e encerramento por PID"],
    state: ["Historico local", "Estados salvos neste PC"],
  }[action] || ["Aplicacao dos ajustes", "Resultado da ultima acao"];

  const title = $("#actionPanelTitle");
  const subtitle = $("#actionPanelSubtitle");
  const state = $("#actionPanelState");
  if (title) title.textContent = meta[0];
  if (subtitle) subtitle.textContent = meta[1];
  if (state) {
    const worst = items.some((item) => item.state === "err")
      ? "err"
      : items.some((item) => item.state === "run")
        ? "run"
        : items.some((item) => item.state === "warn")
          ? "warn"
          : items.length
            ? "ok"
            : "idle";
    state.className = `status-pill ${worst}`;
    state.textContent = {
      idle: "Pronto",
      run: "Aplicando",
      ok: "Aplicado",
      warn: "Revisar",
      err: "Falhou",
    }[worst];
  }

  el.innerHTML = "";
  for (const item of items) {
    el.appendChild(detailRow(item));
  }
}

function detailRow({ state = "info", label, value, note }) {
  const row = document.createElement("div");
  row.className = `detail-row ${state}`;

  const dot = document.createElement("span");
  dot.className = "detail-dot";

  const body = document.createElement("span");
  body.className = "detail-body";

  const title = document.createElement("span");
  title.className = "detail-title";
  title.textContent = label || "";

  const text = document.createElement("span");
  text.className = "detail-value";
  text.textContent = value || "";

  body.appendChild(title);
  body.appendChild(text);

  row.appendChild(dot);
  row.appendChild(body);

  if (note) {
    const noteEl = document.createElement("small");
    noteEl.textContent = note;
    row.appendChild(noteEl);
  }

  return row;
}

function formatMemory(mb) {
  const n = Number(mb || 0);
  return n >= 1024 ? `${(n / 1024).toFixed(1)} GB` : `${n.toFixed(0)} MB`;
}

function resetBoostTimeline() {
  const timeline = $("#boostTimeline");
  timeline.hidden = false;
  timeline.innerHTML = "";

  for (const step of [
    ["clean", "Limpeza"],
    ["ram", "RAM"],
    ["fps", "Modo FPS"],
  ]) {
    const row = document.createElement("div");
    row.className = "boost-step idle";
    row.dataset.step = step[0];
    row.innerHTML = `<span></span><strong>${step[1]}</strong><small>Aguardando</small>`;
    timeline.appendChild(row);
  }
}

function setBoostStep(step, state, text) {
  const row = $(`.boost-step[data-step="${step}"]`);
  if (!row) return;
  row.className = `boost-step ${state}`;
  const small = $("small", row);
  if (small) small.textContent = text || "";
}

// ---------- Status do sistema ----------

async function loadSystemInfo() {
  try {
    const info = await fb.systemInfo();
    if (!info || !info.ok) throw new Error("sem dados");

    $("#ramValue").textContent = `${(info.usedMB / 1024).toFixed(1)} / ${(
      info.totalMB / 1024
    ).toFixed(1)} GB`;

    const bar = $("#ramBar");
    bar.style.width = `${info.usedPct}%`;
    bar.classList.toggle("high", info.usedPct >= 80);

    $("#cpuValue").textContent = info.cpu;
    $("#cpuValue").title = info.cpu;
    $("#planValue").textContent = info.powerPlan;
    $("#planValue").title = info.powerPlan;

    renderAdmin(info.isAdmin);
  } catch (err) {
    $("#ramValue").textContent = "indisponivel";
  }
}

function renderAdmin(isAdmin) {
  const chip = $("#adminChip");
  const label = $("#adminLabel");
  chip.hidden = false;
  if (isAdmin) {
    chip.classList.add("is-admin");
    chip.classList.remove("needs-admin");
    label.textContent = "Administrador";
  } else {
    chip.classList.add("needs-admin");
    chip.classList.remove("is-admin");
    label.textContent = "Executar como admin";
    chip.onclick = () => fb.relaunchAsAdmin();
  }
}

// ---------- Acoes individuais ----------

async function runClean(btn) {
  setBusy(btn, true);
  moduleResult("clean", "Limpando temporarios seguros...", "run");
  moduleDetail("clean", [
    { state: "run", label: "Varredura", value: "em andamento" },
  ]);
  try {
    const r = await fb.cleanTemp();
    const suffix = r.deepClean ? "" : " Prefetch e Lixeira preservados.";
    moduleResult(
      "clean",
      `${r.freedMB} MB liberados em ${r.filesRemoved} arquivos.${suffix}`,
      "ok"
    );
    moduleDetail("clean", [
      {
        state: "ok",
        label: "Arquivos removidos",
        value: `${r.filesRemoved || 0} item(ns)`,
      },
      {
        state: "ok",
        label: "Espaco liberado",
        value: `${r.freedMB || 0} MB`,
      },
      ...((r.skipped || []).map((text) => ({
        state: "keep",
        label: "Preservado",
        value: text,
      }))),
      {
        state: "info",
        label: "Tradeoff",
        value: "limpeza conservadora",
        note: "Mantem caches que podem ajudar carregamento e reduzir stutter.",
      },
    ]);
    loadSystemInfo();
    return r.freedMB;
  } catch (err) {
    moduleResult("clean", "Falha: " + err.message, "err");
    moduleDetail("clean", [
      { state: "err", label: "Limpeza", value: err.message },
    ]);
    throw err;
  } finally {
    setBusy(btn, false);
  }
}

async function runRam(btn) {
  setBusy(btn, true);
  moduleResult("ram", "Otimizando memoria...", "run");
  moduleDetail("ram", [
    { state: "run", label: "Working set", value: "em andamento" },
  ]);
  try {
    const r = await fb.optimizeRam();
    const msg =
      r.freedMB > 0
        ? `${r.freedMB} MB liberados (${r.processesTrimmed} processos).`
        : `RAM ja otimizada (${r.processesTrimmed} processos verificados).`;
    moduleResult("ram", msg, "ok");
    moduleDetail("ram", [
      {
        state: r.freedMB > 0 ? "ok" : "keep",
        label: "Memoria liberada",
        value: formatMemory(r.freedMB),
      },
      {
        state: "ok",
        label: "Processos verificados",
        value: `${r.processesTrimmed || 0}`,
      },
      {
        state: "info",
        label: "Uso antes/depois",
        value: `${formatMemory(r.beforeUsedMB)} -> ${formatMemory(r.afterUsedMB)}`,
      },
      {
        state: "info",
        label: "Impacto",
        value: "mais folga antes do jogo",
        note: "Nao fecha apps; paginas podem voltar para RAM quando usadas.",
      },
    ]);
    loadSystemInfo();
    return r.freedMB;
  } catch (err) {
    moduleResult("ram", "Falha: " + err.message, "err");
    moduleDetail("ram", [
      { state: "err", label: "RAM", value: err.message },
    ]);
    throw err;
  } finally {
    setBusy(btn, false);
  }
}

async function runFps(btn) {
  setBusy(btn, true);
  moduleResult("fps", "Aplicando Modo FPS...", "run");
  moduleDetail("fps", [
    { state: "run", label: "Snapshot", value: "preparando rollback" },
  ]);
  try {
    const r = await fb.fpsMode();
    const applied = (r.applied || []).length;
    moduleResult(
      "fps",
      applied
        ? `Modo FPS ativo (${applied} ajustes aplicados).`
        : "Nenhum ajuste pode ser aplicado.",
      applied ? "ok" : "err"
    );
    moduleDetail("fps", [
      ...((r.applied || []).map((text) => ({
        state: text.includes("preservado") ? "keep" : "ok",
        label: text.includes("Snapshot") ? "Rollback" : "Aplicado",
        value: text,
      }))),
      {
        state: "warn",
        label: "Tradeoff",
        value: "mais consumo e temperatura",
        note: "Alto desempenho favorece estabilidade de clock durante a partida.",
      },
    ]);
    loadSystemInfo();
    loadStateHistory();
    return applied;
  } catch (err) {
    moduleResult("fps", "Falha: " + err.message, "err");
    moduleDetail("fps", [
      { state: "err", label: "Modo FPS", value: err.message },
    ]);
    throw err;
  } finally {
    setBusy(btn, false);
  }
}

async function runRestore(linkBtn) {
  linkBtn.disabled = true;
  moduleResult("fps", "Restaurando...", "run");
  moduleDetail("fps", [
    { state: "run", label: "Restore", value: "aplicando estado anterior" },
  ]);
  try {
    const r = await fb.restoreMode();
    moduleResult(
      "fps",
      r.stateUsed
        ? "Estado anterior restaurado pelo snapshot."
        : "Configuracoes padrao restauradas por fallback.",
      "ok"
    );
    moduleDetail("fps", [
      ...((r.restored || []).map((text) => ({
        state: r.stateUsed ? "ok" : "warn",
        label: r.stateUsed ? "Restaurado" : "Fallback",
        value: text,
      }))),
      {
        state: r.stateUsed ? "ok" : "warn",
        label: "Origem",
        value: r.stateUsed ? "snapshot salvo" : "sem snapshot anterior",
      },
    ]);
    loadSystemInfo();
    loadStateHistory();
  } catch (err) {
    moduleResult("fps", "Falha: " + err.message, "err");
    moduleDetail("fps", [
      { state: "err", label: "Restore", value: err.message },
    ]);
  } finally {
    linkBtn.disabled = false;
  }
}

// ---------- Processos ----------

function processSkeleton() {
  const wrap = document.createElement("div");
  wrap.className = "skeleton";
  for (let i = 0; i < 4; i++) {
    const row = document.createElement("div");
    row.className = "skeleton-row";
    wrap.appendChild(row);
  }
  return wrap;
}

async function analyzeProcesses(btn) {
  const list = $("#procList");
  const actions = $("#procActions");
  setBusy(btn, true);
  moduleResult("proc", "", "");
  moduleDetail("proc", [
    { state: "run", label: "Processos", value: "analisando consumo" },
  ]);
  list.hidden = false;
  actions.hidden = true;
  list.innerHTML = "";
  list.appendChild(processSkeleton());

  try {
    const r = await fb.listProcesses();
    const procs = (r.processes || []).filter((p) => p && p.name);
    list.innerHTML = "";

    if (procs.length === 0) {
      list.hidden = true;
      moduleResult("proc", "Nenhum processo pesado encontrado. Tudo limpo.", "ok");
      moduleDetail("proc", [
        { state: "ok", label: "Processos", value: "nenhum alvo pesado" },
      ]);
      return;
    }

    for (const p of procs) {
      list.appendChild(buildProcRow(p));
    }
    actions.hidden = false;
    const sel = $$("input:checked", list).length;
    moduleResult(
      "proc",
      `${procs.length} processos encontrados, ${sel} sugeridos.`,
      ""
    );
    moduleDetail("proc", [
      {
        state: sel > 0 ? "ok" : "keep",
        label: "Pre-selecionados",
        value: `${sel} app(s) de baixo risco`,
      },
      {
        state: "info",
        label: "Manual",
        value: "Discord, OBS, NVIDIA e audio ficam para revisao",
      },
      {
        state: "keep",
        label: "Protegidos",
        value: "Steam, CS2 e processos criticos",
      },
    ]);
  } catch (err) {
    list.hidden = true;
    moduleResult("proc", "Falha: " + err.message, "err");
    moduleDetail("proc", [
      { state: "err", label: "Processos", value: err.message },
    ]);
  } finally {
    setBusy(btn, false);
  }
}

function buildProcRow(p) {
  const row = document.createElement("label");
  row.className = "proc-row";
  const tagInfo = processTag(p.name);

  const cb = document.createElement("input");
  cb.type = "checkbox";
  cb.value = p.name;
  cb.dataset.pids = JSON.stringify(p.pids || []);
  cb.checked = tagInfo && tagInfo.kind === "suggested";

  const name = document.createElement("span");
  name.className = "proc-name";
  name.textContent = p.count > 1 ? `${p.name} (${p.count})` : p.name;
  name.title = p.name;
  row.title = `PIDs: ${(p.pids || []).join(", ") || "indisponivel"}`;

  const mem = document.createElement("span");
  mem.className = "proc-mem";
  mem.textContent = `${p.memMB.toFixed(0)} MB`;

  row.appendChild(cb);
  row.appendChild(name);
  if (tagInfo) {
    const tag = document.createElement("span");
    tag.className = "proc-tag" + (tagInfo.kind === "manual" ? " manual" : "");
    tag.textContent = tagInfo.text;
    row.appendChild(tag);
  }
  row.appendChild(mem);
  return row;
}

async function killSelected(btn) {
  const list = $("#procList");
  const items = $$("input:checked", list).map((cb) => {
    let pids = [];
    try { pids = JSON.parse(cb.dataset.pids || "[]"); } catch (_) {}
    return { name: cb.value, pids };
  });
  if (items.length === 0) {
    moduleResult("proc", "Selecione ao menos um processo.", "err");
    return;
  }
  setBusy(btn, true);
  moduleDetail("proc", [
    { state: "run", label: "Encerramento", value: "aplicando por PID" },
  ]);
  try {
    const r = await fb.killProcesses(items);
    const killed = (r.killed || []).length;
    const failed = (r.failed || []).length;
    const killedNames = (r.killed || [])
      .map((item) => `${item.name} (${item.pid})`)
      .slice(0, 4);
    moduleResult(
      "proc",
      `${killed} processo(s) encerrado(s) por PID${failed ? `, ${failed} falha(s)` : ""}.`,
      killed ? "ok" : "err"
    );
    await analyzeProcesses($('[data-action="proc"]'));
    moduleResult(
      "proc",
      `${killed} processo(s) encerrado(s) por PID${failed ? `, ${failed} falha(s)` : ""}.`,
      killed ? "ok" : "err"
    );
    moduleDetail("proc", [
      {
        state: killed ? "ok" : "warn",
        label: "Encerrados",
        value: killedNames.length ? killedNames.join(", ") : "nenhum processo",
      },
      ...(failed
        ? [{ state: "warn", label: "Falhas", value: `${failed} item(ns)` }]
        : []),
      {
        state: "info",
        label: "Metodo",
        value: "PID verificado antes do encerramento",
      },
    ]);
  } catch (err) {
    moduleResult("proc", "Falha: " + err.message, "err");
    moduleDetail("proc", [
      { state: "err", label: "Encerramento", value: err.message },
    ]);
  } finally {
    setBusy(btn, false);
  }
}

// ---------- Historico de estados ----------

function stateItems(history) {
  return Array.isArray(history?.items)
    ? history.items
    : history?.items
      ? [history.items]
      : [];
}

function formatStateDate(value) {
  if (!value) return "--";
  try {
    return new Intl.DateTimeFormat("pt-BR", {
      day: "2-digit",
      month: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
    }).format(new Date(value));
  } catch (_) {
    return String(value);
  }
}

function stateSourceLabel(source) {
  if (source === "automatico") return "auto";
  if (source === "manual") return "manual";
  return source || "local";
}

async function loadStateHistory() {
  const list = $("#stateHistoryList");
  if (!list || !fb?.stateHistory) return;

  try {
    const r = await fb.stateHistory();
    renderStateHistory(r);
  } catch (err) {
    list.innerHTML = "";
    const empty = document.createElement("div");
    empty.className = "history-empty error";
    empty.textContent = "Falha ao carregar historico: " + err.message;
    list.appendChild(empty);
  }
}

function renderStateHistory(result) {
  const list = $("#stateHistoryList");
  const path = $("#historyPath");
  if (!list) return;

  const items = stateItems(result?.history);
  if (path) {
    path.textContent = items.length
      ? `${items.length} estado(s) salvos neste PC`
      : "Estados salvos neste PC";
    path.title = result?.path || "";
  }

  list.innerHTML = "";
  if (items.length === 0) {
    const empty = document.createElement("div");
    empty.className = "history-empty";
    empty.textContent = "Nenhum estado salvo ainda.";
    list.appendChild(empty);
    return;
  }

  for (const item of items.slice(0, 4)) {
    list.appendChild(historyRow(item));
  }

  if (items.length > 4) {
    const more = document.createElement("div");
    more.className = "history-more";
    more.textContent = `Mostrando 4 de ${items.length} estados.`;
    list.appendChild(more);
  }
}

function historyRow(item) {
  const row = document.createElement("article");
  row.className = "history-row";

  const main = document.createElement("div");
  main.className = "history-main";

  const title = document.createElement("strong");
  title.textContent = item.label || "Estado salvo";

  const meta = document.createElement("span");
  meta.textContent = `${formatStateDate(item.createdAt)} · ${stateSourceLabel(item.source)} · ${item.powerPlanName || "plano salvo"}`;

  main.appendChild(title);
  main.appendChild(meta);

  const actions = document.createElement("div");
  actions.className = "history-row-actions";

  const restore = document.createElement("button");
  restore.type = "button";
  restore.className = "btn btn-small";
  restore.dataset.action = "state-restore";
  restore.dataset.id = item.id || "";
  restore.textContent = "Restaurar";

  const del = document.createElement("button");
  del.type = "button";
  del.className = "icon-btn danger";
  del.dataset.action = "state-delete";
  del.dataset.id = item.id || "";
  del.title = "Apagar estado";
  del.textContent = "x";

  actions.appendChild(restore);
  actions.appendChild(del);

  row.appendChild(main);
  row.appendChild(actions);
  return row;
}

async function saveCurrentState(btn) {
  setBusy(btn, true);
  moduleDetail("state", [
    { state: "run", label: "Snapshot", value: "salvando estado atual" },
  ]);

  try {
    const r = await fb.captureState();
    renderStateHistory(r);
    moduleDetail("state", [
      { state: "ok", label: "Salvo", value: r.item?.label || "Estado manual" },
      { state: "info", label: "Plano", value: r.item?.powerPlanName || "desconhecido" },
      { state: "info", label: "Arquivo", value: r.path || "%APPDATA%\\Freitas Boost" },
    ]);
  } catch (err) {
    moduleDetail("state", [
      { state: "err", label: "Historico", value: err.message },
    ]);
  } finally {
    setBusy(btn, false);
  }
}

async function restoreSavedState(btn) {
  const id = btn?.dataset?.id;
  if (!id) return;
  setBusy(btn, true);
  moduleDetail("state", [
    { state: "run", label: "Restore", value: "restaurando estado salvo" },
  ]);

  try {
    const r = await fb.restoreState(id);
    if (!r.ok) throw new Error(r.error || "Estado nao encontrado");
    renderStateHistory(r);
    moduleDetail("state", [
      { state: "ok", label: "Restaurado", value: r.item?.label || "Estado salvo" },
      { state: "info", label: "Plano", value: r.item?.powerPlanName || "desconhecido" },
      ...((r.restored || []).map((text) => ({
        state: "ok",
        label: "Aplicado",
        value: text,
      }))),
    ]);
    loadSystemInfo();
  } catch (err) {
    moduleDetail("state", [
      { state: "err", label: "Restore", value: err.message },
    ]);
  } finally {
    setBusy(btn, false);
  }
}

async function deleteSavedState(btn) {
  const id = btn?.dataset?.id;
  if (!id) return;
  setBusy(btn, true);

  try {
    const r = await fb.deleteState(id);
    renderStateHistory(r);
    moduleDetail("state", [
      {
        state: r.removed ? "ok" : "warn",
        label: "Historico",
        value: r.removed ? "estado apagado" : "estado nao encontrado",
      },
    ]);
  } catch (err) {
    moduleDetail("state", [
      { state: "err", label: "Historico", value: err.message },
    ]);
  } finally {
    setBusy(btn, false);
  }
}

// ---------- Perfil CS2 ----------

async function analyzeCs2(btn) {
  const wrap = $("#cs2Results");
  setBusy(btn, true);
  wrap.hidden = false;
  wrap.innerHTML = "";
  wrap.appendChild(processSkeleton());

  try {
    const profile = await fb.cs2Profile();
    renderCs2Profile(profile);
  } catch (err) {
    wrap.innerHTML = "";
    const error = document.createElement("p");
    error.className = "cs2-error";
    error.textContent = "Falha ao analisar CS2: " + err.message;
    wrap.appendChild(error);
  } finally {
    setBusy(btn, false);
  }
}

function renderCs2Profile(profile) {
  const wrap = $("#cs2Results");
  wrap.innerHTML = "";

  const summary = document.createElement("div");
  summary.className = "cs2-summary-grid";
  summary.appendChild(cs2Metric("GPU", profile.gpuName || "desconhecida"));
  summary.appendChild(cs2Metric("CS2", profile.cs2Detected ? "detectado" : "nao encontrado"));
  summary.appendChild(cs2Metric("Energia", profile.powerPlan || "desconhecido"));
  summary.appendChild(cs2Metric("Game DVR", profile.gameDvr || "desconhecido"));
  summary.appendChild(cs2Metric("HAGS", profile.hags || "desconhecido"));
  wrap.appendChild(summary);

  const verdict = document.createElement("p");
  verdict.className = "cs2-verdict";
  verdict.textContent = cs2Verdict(profile);
  wrap.appendChild(verdict);

  const recs = document.createElement("div");
  recs.className = "cs2-rec-list";
  for (const rec of profile.recommendations || []) {
    recs.appendChild(cs2Recommendation(rec));
  }
  wrap.appendChild(recs);
}

function cs2Metric(label, value) {
  const item = document.createElement("div");
  item.className = "cs2-metric";

  const labelEl = document.createElement("span");
  labelEl.className = "cs2-metric-label";
  labelEl.textContent = label;

  const valueEl = document.createElement("span");
  valueEl.className = "cs2-metric-value";
  valueEl.textContent = value;
  valueEl.title = value;

  item.appendChild(labelEl);
  item.appendChild(valueEl);
  return item;
}

function cs2Verdict(profile) {
  if (profile.gpuVendor === "nvidia") {
    return "Prioridade competitiva: Reflex Enabled + Boost tende a valer o custo de alguns FPS quando a meta e menor latencia.";
  }
  if (profile.gpuVendor === "amd") {
    return "Prioridade competitiva: Anti-Lag 2 deve ser testado no CS2, comparando latencia e 1% lows no mesmo mapa.";
  }
  return "Prioridade competitiva: use ajustes nativos do jogo/driver e meca 1% lows antes de perseguir FPS medio.";
}

function cs2Recommendation(rec) {
  const row = document.createElement("article");
  row.className = "cs2-rec";

  const head = document.createElement("div");
  head.className = "cs2-rec-head";

  const category = document.createElement("span");
  category.className = "cs2-rec-category";
  category.textContent = rec.category || "Ajuste";

  const impact = document.createElement("span");
  impact.className = "cs2-rec-impact";
  impact.textContent = rec.impact || "";

  head.appendChild(category);
  head.appendChild(impact);

  const title = document.createElement("h4");
  title.textContent = rec.title || "";

  const tradeoff = document.createElement("p");
  tradeoff.textContent = rec.tradeoff || "";

  const action = document.createElement("small");
  action.textContent = rec.action || "";

  row.appendChild(head);
  row.appendChild(title);
  row.appendChild(tradeoff);
  row.appendChild(action);
  return row;
}

// ---------- Boost completo ----------

async function boostAll(btn) {
  setBusy(btn, true);
  const summary = $("#boostSummary");
  summary.hidden = false;
  summary.classList.remove("is-error");
  summary.textContent = "Executando boost completo...";
  resetBoostTimeline();

  let freedClean = 0;
  let freedRam = 0;
  let fpsApplied = 0;
  const errors = [];

  setBoostStep("clean", "run", "Limpando temporarios seguros");
  try {
    freedClean = await runClean($('[data-action="clean"]'));
    setBoostStep("clean", "ok", `${freedClean || 0} MB liberados`);
  } catch (e) {
    errors.push("limpeza");
    setBoostStep("clean", "err", "Falha na limpeza");
  }

  setBoostStep("ram", "run", "Revisando working set");
  try {
    freedRam = await runRam($('[data-action="ram"]'));
    setBoostStep("ram", "ok", `${freedRam || 0} MB liberados`);
  } catch (e) {
    errors.push("RAM");
    setBoostStep("ram", "err", "Falha na RAM");
  }

  setBoostStep("fps", "run", "Aplicando ajustes reversiveis");
  try {
    fpsApplied = await runFps($('[data-action="fps"]'));
    setBoostStep("fps", fpsApplied ? "ok" : "warn", `${fpsApplied} ajuste(s)`);
  } catch (e) {
    errors.push("Modo FPS");
    setBoostStep("fps", "err", "Falha no Modo FPS");
  }

  const total = (freedClean || 0) + (freedRam || 0);
  if (errors.length) {
    summary.classList.add("is-error");
    summary.textContent = `Boost concluido com avisos em: ${errors.join(", ")}.`;
  } else {
    summary.textContent = `Boost concluido. ${total} MB liberados e ${fpsApplied} ajustes reversiveis aplicados.`;
  }

  setBusy(btn, false);
}

// ---------- Ligacoes ----------

document.addEventListener("click", (e) => {
  const trigger = e.target.closest("[data-action]");
  if (!trigger) return;
  const action = trigger.dataset.action;
  const map = {
    clean: () => runClean(trigger),
    ram: () => runRam(trigger),
    fps: () => runFps(trigger),
    restore: () => runRestore(trigger),
    cs2: () => analyzeCs2(trigger),
    "state-save": () => saveCurrentState(trigger),
    "state-refresh": () => loadStateHistory(),
    "state-restore": () => restoreSavedState(trigger),
    "state-delete": () => deleteSavedState(trigger),
    proc: () => analyzeProcesses(trigger),
    kill: () => killSelected(trigger),
  };
  if (map[action]) map[action]();
});

$("#boostAll").addEventListener("click", (e) => boostAll(e.currentTarget));

loadSystemInfo();
loadStateHistory();
setInterval(loadSystemInfo, 5000);
})();
