(async function () {
  const config = await getConfig();
  if (config.updateDomWithLanguageOnLoad)
    await updateDomWithLanguage("callout");
  await applyCalloutCadPlaceholders();
})();

const CAD_PRESETS = [
  { value: "10-8 | Available", label: "10-8 — Available" },
  { value: "10-97 | En route", label: "10-97 — En route" },
  { value: "10-23 | On scene", label: "10-23 — On scene" },
  { value: "10-95 | Traffic stop", label: "10-95 — Traffic stop" },
  { value: "10-7 | Out of service", label: "10-7 — Out of service" },
  { value: "10-6 | Busy", label: "10-6 — Busy" },
];

function escapeHtml(s) {
  return String(s ?? "")
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function stripGtaColorCodes(s) {
  if (!s) return "";
  if (typeof removeGTAColorCodesFromString === "function")
    return removeGTAColorCodesFromString(String(s));
  return String(s).replace(/~[a-z0-9_]+~/gi, "");
}

function getValue(data, pascal, camel) {
  return data?.[pascal] ?? data?.[camel];
}

/** Coerce plugin JSON (Pascal/camel, string/number) to LSPDFR CalloutAcceptanceState int. */
function normalizeCalloutAcceptanceState(data) {
  const v = getValue(data, "AcceptanceState", "acceptanceState");
  if (v == null || v === "") return 0;
  const n = typeof v === "number" ? v : Number(v);
  return Number.isFinite(n) ? n : 0;
}

function getCalloutActions(data, state) {
  const raw = getValue(data, "AvailableActions", "availableActions");
  if (Array.isArray(raw)) return raw.map((x) => String(x).toLowerCase());
  if (state === 0) return ["attach"];
  if (state === 1 || state === 2) return ["enroute"];
  return [];
}

function updateCadUnitReadout(text) {
  const el = document.getElementById("cadUnitStatusReadout");
  if (!el) return;
  const t = text != null ? String(text).trim() : "";
  el.textContent = t.length ? t : "—";
}

async function applyCalloutCadPlaceholders() {
  const language = await getLanguage();
  const input = document.getElementById("cadUnitCustomInput");
  if (input)
    input.placeholder =
      language.callout?.static?.cad?.customPlaceholder ||
      "Custom status (overrides preset when filled)";
}

function wireCalloutCadPanel() {
  const btn = document.getElementById("cadUnitSetStatusBtn");
  const sel = document.getElementById("cadUnitPresetSelect");
  if (!btn || !sel || btn.dataset.wired === "1") return;
  btn.dataset.wired = "1";
  sel.innerHTML = CAD_PRESETS.map(
    (p) =>
      `<option value="${escapeHtml(p.value)}">${escapeHtml(p.label)}</option>`,
  ).join("");

  btn.addEventListener("click", async () => {
    const language = await getLanguage();
    const input = document.getElementById("cadUnitCustomInput");
    const custom = input?.value?.trim() ?? "";
    const status = custom || sel.value;
    if (!status) {
      if (typeof showNotification === "function") {
        showNotification(
          language.callout?.actions?.error || "Action failed.",
          "warning",
        );
      }
      return;
    }
    try {
      const res = await fetch("/post/cadUnitStatus", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ status }),
      });
      const json = await res.json().catch(() => ({}));
      if (json.success && typeof showNotification === "function") {
        showNotification(
          language.callout?.static?.cad?.statusUpdated ||
            "Unit status updated.",
          "checkMark",
        );
      } else if (typeof showNotification === "function") {
        showNotification(json.error || `HTTP ${res.status}`, "warning");
      }
    } catch (e) {
      if (typeof showNotification === "function")
        showNotification(String(e.message || e), "warning");
    }
  });
}

wireCalloutCadPanel();

function getCalloutStatusLabel(state, language) {
  const statusLabels = language.callout?.status || {};
  if (state === 0) return statusLabels.pending || "Open";
  if (state === 1) return statusLabels.responded || "Responded";
  if (state === 2) return statusLabels.enRoute || "En Route";
  if (state === 3) return statusLabels.finished || "Finished";
  return statusLabels.unknown || "—";
}

function renderActionButtons(actions, language) {
  const buttons = [
    `<button type="button" class="calloutActionBtn calloutSetGpsBtn" data-action="setGps">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/><circle cx="12" cy="10" r="3"/></svg>
      ${escapeHtml(language.callout?.actions?.setGps || "Set GPS")}
    </button>`,
  ];
  if (actions.includes("attach")) {
    buttons.push(
      `<button type="button" class="calloutActionBtn calloutServerActionBtn calloutAcceptBtn" data-action="attach">${escapeHtml(language.callout?.actions?.attach || language.callout?.actions?.accept || "Attach")}</button>`,
    );
  }
  if (actions.includes("enroute")) {
    buttons.push(
      `<button type="button" class="calloutActionBtn calloutServerActionBtn calloutEnRouteBtn" data-action="enRoute">${escapeHtml(language.callout?.status?.enRoute || "En Route")}</button>`,
    );
  }
  if (actions.includes("close")) {
    buttons.push(
      `<button type="button" class="calloutActionBtn calloutServerActionBtn calloutCloseBtn" data-action="close">${escapeHtml(language.callout?.actions?.close || "Close")}</button>`,
    );
  }
  return buttons.join("");
}

function renderCalloutCard(data, index, language) {
  const state = normalizeCalloutAcceptanceState(data);
  const actions = getCalloutActions(data, state);
  const statusLabel = getCalloutStatusLabel(state, language);
  const location = getValue(data, "Location", "location") || {};
  const address =
    `${(location.Postal || location.postal || "").trim()} ${(location.Street || location.street || "").trim()}`.trim() ||
    "—";
  const name = getValue(data, "Name", "name") || "—";
  return `
    <div class="calloutCard ${index === 0 ? "calloutCard-expanded" : ""}" data-index="${index}">
      <button type="button" class="calloutCardHeader" aria-expanded="${index === 0}">
        <span class="calloutCardName">${escapeHtml(name)}</span>
        <span class="calloutCardStatus calloutStatus${state ?? 0}">${escapeHtml(statusLabel)}</span>
        <span class="calloutCardAddress">${escapeHtml(address)}</span>
        <span class="calloutCardChevron" aria-hidden="true">▼</span>
      </button>
      <div class="calloutCardBody" ${index !== 0 ? "hidden" : ""}>
        <ul class="calloutDetails">
          <li class="calloutDetailRow"><span class="calloutDetailLabel" data-language="address">Address</span><span class="calloutDetailValue calloutAddressVal">—</span></li>
          <li class="calloutDetailRow"><span class="calloutDetailLabel" data-language="area">Area</span><span class="calloutDetailValue calloutAreaVal">—</span></li>
          <li class="calloutDetailRow"><span class="calloutDetailLabel" data-language="county">County</span><span class="calloutDetailValue calloutCountyVal">—</span></li>
          <li class="calloutDetailRow"><span class="calloutDetailLabel" data-language="priority">Priority</span><span class="calloutDetailValue calloutPriorityVal">—</span></li>
          <li class="calloutDetailRow calloutDetailFull calloutMessageRow"><span class="calloutDetailLabel" data-language="callout.calloutInfo.message">Message</span><span class="calloutDetailValue calloutMessageVal">—</span></li>
          <li class="calloutDetailRow calloutDetailFull calloutAdvisoryRow"><span class="calloutDetailLabel" data-language="callout.calloutInfo.advisory">Advisory</span><span class="calloutDetailValue calloutAdvisoryVal">—</span></li>
        </ul>
        <div class="calloutMeta calloutMetaVal"></div>
        <div class="calloutActions">${renderActionButtons(actions, language)}</div>
      </div>
    </div>
  `;
}

async function postCalloutAction(body, language) {
  const res = await fetch("/post/calloutAction", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const json = await res.json().catch(() => ({}));
  if (json.success && typeof showNotification === "function") {
    showNotification(
      json.message || language.callout?.actions?.success || "Status updated.",
      "checkMark",
    );
  } else if (typeof showNotification === "function") {
    showNotification(json.error || `Request failed (${res.status})`, "warning");
  }
}

function appendMetaRow(container, text) {
  if (!container || !text) return;
  const row = document.createElement("div");
  row.className = "calloutMetaRow";
  row.textContent = text;
  container.appendChild(row);
}

async function renderCalloutPayload(payload) {
  const language = await getLanguage();
  const config = await getConfig();
  updateCadUnitReadout(payload?.cadUnitStatus);

  const callouts =
    payload?.callouts ??
    (payload?.Location || payload?.location ? [payload] : []);
  const emptyEl = document.getElementById("calloutEmpty");
  const containerEl = document.getElementById("calloutCardsContainer");

  if (!callouts || callouts.length === 0) {
    if (emptyEl) emptyEl.classList.remove("hidden");
    if (containerEl) {
      containerEl.classList.add("hidden");
      containerEl.innerHTML = "";
    }
    const timeline = document.getElementById("calloutTimeline");
    if (timeline) timeline.textContent = "";
    return;
  }

  if (emptyEl) emptyEl.classList.add("hidden");
  if (containerEl) containerEl.classList.remove("hidden");

  const current = callouts[0];
  const timelineEl = document.getElementById("calloutTimeline");
  if (timelineEl) {
    const displayed =
      getValue(current, "DisplayedTime", "displayedTimeUtc") ??
      getValue(current, "displayedTime", "displayedTime");
    const accepted =
      getValue(current, "AcceptedTime", "acceptedTimeUtc") ??
      getValue(current, "acceptedTime", "acceptedTime");
    const finished =
      getValue(current, "FinishedTime", "finishedTimeUtc") ??
      getValue(current, "finishedTime", "finishedTime");
    const parts = [];
    if (displayed)
      parts.push(
        `${language.callout?.status?.displayed || "Displayed"}: ${new Date(displayed).toLocaleTimeString()}`,
      );
    if (accepted)
      parts.push(
        `${language.callout?.status?.responded || "Responded"}: ${new Date(accepted).toLocaleTimeString()}`,
      );
    if (finished)
      parts.push(
        `${language.callout?.status?.finished || "Finished"}: ${new Date(finished).toLocaleTimeString()}`,
      );
    timelineEl.textContent = parts.join("  •  ") || "—";
  }

  containerEl.innerHTML = callouts
    .map((c, i) => renderCalloutCard(c, i, language))
    .join("");

  for (let i = 0; i < callouts.length; i++) {
    const data = callouts[i];
    const card = containerEl.children[i];
    const location = getValue(data, "Location", "location") || {};
    const county = location.County ?? location.county;
    const countyVal = await getLanguageValue(county);
    card.querySelector(".calloutAddressVal").textContent =
      `${(location.Postal || location.postal || "").trim()} ${(location.Street || location.street || "").trim()}`.trim() ||
      "—";
    card.querySelector(".calloutAreaVal").textContent =
      location.Area ?? location.area ?? "—";
    card.querySelector(".calloutCountyVal").textContent = countyVal || "—";
    card.querySelector(".calloutPriorityVal").textContent =
      getValue(data, "Priority", "priority") ||
      language.callout?.defaultPriority ||
      "—";

    const msg = getValue(data, "Message", "message");
    const advisory = getValue(data, "Advisory", "advisory");
    const msgRow = card.querySelector(".calloutMessageRow");
    const advRow = card.querySelector(".calloutAdvisoryRow");
    const msgEl = card.querySelector(".calloutMessageVal");
    const advEl = card.querySelector(".calloutAdvisoryVal");
    if (msg) {
      msgEl.textContent = stripGtaColorCodes(msg);
      msgRow?.classList.remove("hidden");
    } else {
      msgRow?.classList.add("hidden");
    }
    if (advisory) {
      advEl.textContent = stripGtaColorCodes(advisory);
      advRow?.classList.remove("hidden");
    } else {
      advRow?.classList.add("hidden");
    }

    const agency = getValue(data, "Agency", "agency") || "";
    const callsign = getValue(data, "Callsign", "callsign") || "";
    const agencyStr =
      config.showAgencyInCalloutInfo && agency ? ` (${agency})` : "";
    const metaEl = card.querySelector(".calloutMetaVal");
    metaEl.textContent = "";
    const displayed =
      getValue(data, "DisplayedTime", "displayedTimeUtc") ??
      getValue(data, "displayedTime", "displayedTime");
    const accepted =
      getValue(data, "AcceptedTime", "acceptedTimeUtc") ??
      getValue(data, "acceptedTime", "acceptedTime");
    const finished =
      getValue(data, "FinishedTime", "finishedTimeUtc") ??
      getValue(data, "finishedTime", "finishedTime");
    if (displayed)
      appendMetaRow(
        metaEl,
        `${language.callout?.calloutInfo?.displayedTime || "Displayed"}: ${new Date(displayed).toLocaleString()}`,
      );
    if (accepted)
      appendMetaRow(
        metaEl,
        `${language.callout?.calloutInfo?.unit || "Unit"} ${callsign}${agencyStr}  ${language.callout?.calloutInfo?.acceptedTime || "Accepted"}: ${new Date(accepted).toLocaleString()}`,
      );
    const additionalMessages =
      getValue(data, "AdditionalMessages", "additionalMessages") || [];
    for (const m of additionalMessages)
      appendMetaRow(metaEl, stripGtaColorCodes(m));
    if (finished)
      appendMetaRow(
        metaEl,
        `${language.callout?.calloutInfo?.finishedTime || "Finished"}: ${new Date(finished).toLocaleString()}`,
      );
  }

  containerEl.querySelectorAll(".calloutCardHeader").forEach((btn) => {
    btn.addEventListener("click", function () {
      const card = this.closest(".calloutCard");
      const body = card.querySelector(".calloutCardBody");
      const isExpanded = body.hidden === false;
      card.classList.toggle("calloutCard-expanded", !isExpanded);
      body.hidden = isExpanded;
      btn.setAttribute("aria-expanded", !isExpanded);
    });
  });

  containerEl.querySelectorAll(".calloutSetGpsBtn").forEach((btn) => {
    btn.addEventListener("click", async function () {
      const card = this.closest(".calloutCard");
      const idx = parseInt(card.dataset.index, 10);
      const data = callouts[idx];
      const coords = getValue(data, "Coords", "coords");
      if (!coords?.length) return;
      const res = await fetch("/post/setGpsWaypoint", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ x: coords[0], y: coords[1] }),
      });
      if (res.ok && typeof showNotification === "function") {
        showNotification(
          language.callout?.actions?.gpsSuccess || "GPS set to callout.",
          "checkMark",
        );
      } else if (typeof showNotification === "function") {
        const t = await res.text().catch(() => "");
        showNotification(t || `GPS failed (${res.status})`, "warning");
      }
    });
  });

  containerEl.querySelectorAll(".calloutServerActionBtn").forEach((btn) => {
    btn.addEventListener("click", async function () {
      const action = this.dataset.action;
      const card = this.closest(".calloutCard");
      const idx = parseInt(card?.dataset?.index ?? "-1", 10);
      const data = callouts[idx];
      const calloutId = getValue(data, "Id", "id");
      if (!calloutId) {
        if (typeof showNotification === "function") {
          showNotification(
            "Callout id missing — update MDT Pro plugin / refresh.",
            "warning",
          );
        }
        return;
      }
      if (action === "close") {
        const ok = window.confirm(
          language.callout?.actions?.closeConfirm ||
            "Close the current active callout?",
        );
        if (!ok) return;
      }
      await postCalloutAction({ action, calloutId }, language);
    });
  });
}

let calloutReconnectTimer = null;
function connectCalloutEventWs() {
  const ws = new WebSocket(`ws://${location.host}/ws`);
  ws.onopen = () => ws.send("calloutEvent");
  ws.onmessage = async (event) => {
    try {
      const parsed = JSON.parse(event.data);
      await renderCalloutPayload(parsed.response);
    } catch (e) {
      if (typeof showNotification === "function")
        showNotification(String(e.message || e), "warning");
    }
  };
  ws.onclose = () => {
    if (calloutReconnectTimer) return;
    calloutReconnectTimer = setTimeout(() => {
      calloutReconnectTimer = null;
      connectCalloutEventWs();
    }, 2500);
  };
  ws.onerror = () => ws.close();
}

connectCalloutEventWs();
