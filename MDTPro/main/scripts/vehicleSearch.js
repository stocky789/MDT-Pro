(async function () {
  const config = await getConfig();
  if (config.updateDomWithLanguageOnLoad)
    await updateDomWithLanguage("vehicleSearch");

  const alprPlate =
    (typeof sessionStorage !== "undefined" &&
      sessionStorage.getItem("alprVehicleSearchPlate")) ||
    null;
  if (alprPlate && typeof alprPlate === "string") {
    if (typeof sessionStorage !== "undefined")
      sessionStorage.removeItem("alprVehicleSearchPlate");
    const trimmed = alprPlate.trim();
    if (trimmed) {
      const input = document.querySelector(
        ".searchInputWrapper #vehicleSearchInput",
      );
      if (input) {
        input.value = trimmed;
        try {
          await performSearch(trimmed);
        } catch {
          /* performSearch shows its own error */
        }
      }
    }
  } else {
    try {
      const ctxRes = await fetch("/data/contextVehicle");
      if (ctxRes.ok) {
        const ctx = await ctxRes.json();
        const plate =
          ctx && typeof ctx.LicensePlate === "string"
            ? ctx.LicensePlate.trim()
            : "";
        const input = document.querySelector(
          ".searchInputWrapper #vehicleSearchInput",
        );
        if (plate && input && !input.value.trim()) {
          input.value = plate;
          await performSearch(plate);
        }
      }
    } catch {
      /* ignore — plugin may be offline or no context vehicle */
    }
  }

  await loadNearbyVehicles();
  await loadSearchHistory();

  const FALLBACK_REFRESH_INTERVAL_MS = 30000;
  let fallbackRefreshTimer = null;
  async function refreshVehicleLists() {
    if (document.hidden) return;
    await loadNearbyVehicles();
    await loadSearchHistory();
  }
  function startFallbackRefreshTimer() {
    if (fallbackRefreshTimer) return;
    fallbackRefreshTimer = setInterval(
      refreshVehicleLists,
      FALLBACK_REFRESH_INTERVAL_MS,
    );
  }
  function stopFallbackRefreshTimer() {
    if (fallbackRefreshTimer) {
      clearInterval(fallbackRefreshTimer);
      fallbackRefreshTimer = null;
    }
  }
  function connectInvalidationSocket() {
    let reconnectTimer = null;
    let socket = null;
    let stopped = false;
    const scheduleReconnect = () => {
      if (stopped || reconnectTimer) return;
      reconnectTimer = setTimeout(() => {
        reconnectTimer = null;
        connect();
      }, 3000);
    };
    const connect = () => {
      if (stopped) return;
      try {
        const scheme = location.protocol === "https:" ? "wss" : "ws";
        const ws = new WebSocket(`${scheme}://${location.host}/ws`);
        socket = ws;
        ws.onopen = () => {
          stopFallbackRefreshTimer();
          ws.send("dataInvalidationSubscribe");
        };
        ws.onmessage = async (event) => {
          const msg = JSON.parse(event.data);
          const scope = msg?.response?.scope || "all";
          if (scope === "all" || scope === "vehicleSearch")
            await refreshVehicleLists();
        };
        ws.onclose = () => {
          if (socket === ws) socket = null;
          startFallbackRefreshTimer();
          scheduleReconnect();
        };
        ws.onerror = () => {
          try {
            ws.close();
          } catch (_) {}
        };
      } catch (_) {
        startFallbackRefreshTimer();
        scheduleReconnect();
      }
    };
    connect();
    return () => {
      stopped = true;
      if (reconnectTimer) clearTimeout(reconnectTimer);
      if (socket) {
        try {
          socket.close();
        } catch (_) {}
        socket = null;
      }
    };
  }
  const stopInvalidationSocket = connectInvalidationSocket();
  startFallbackRefreshTimer();
  document.addEventListener("visibilitychange", async () => {
    if (document.hidden) {
      stopFallbackRefreshTimer();
    } else {
      await refreshVehicleLists();
      startFallbackRefreshTimer();
    }
  });
  window.addEventListener("pagehide", () => {
    stopFallbackRefreshTimer();
    stopInvalidationSocket?.();
  });
})();

const VEHICLE_RESPONSE_ALIASES = {
  LicensePlate: ["licensePlate", "plate"],
  ModelName: ["modelName"],
  ModelDisplayName: ["modelDisplayName", "displayName"],
  IsStolen: ["isStolen"],
  Owner: ["owner", "ownerName", "OwnerName"],
  Color: ["color"],
  VinStatus: ["vinStatus"],
  Make: ["make"],
  Model: ["model"],
  PrimaryColor: ["primaryColor"],
  SecondaryColor: ["secondaryColor"],
  PrimaryColorSpecific: ["primaryColorSpecific"],
  SecondaryColorSpecific: ["secondaryColorSpecific"],
  VehicleIdentificationNumber: ["vehicleIdentificationNumber", "vin"],
  RegistrationStatus: ["registrationStatus"],
  RegistrationExpiration: ["registrationExpiration"],
  RegistrationExpirationVerifiedFromLiveDocument: [
    "registrationExpirationVerifiedFromLiveDocument",
  ],
  InsuranceStatus: ["insuranceStatus"],
  InsuranceExpiration: ["insuranceExpiration"],
  InsuranceExpirationVerifiedFromLiveDocument: [
    "insuranceExpirationVerifiedFromLiveDocument",
  ],
  BOLOs: ["bolos"],
  CanModifyBOLOs: ["canModifyBOLOs"],
};

const DOCUMENT_FIELD_KEYS = [
  "RegistrationStatus",
  "RegistrationExpiration",
  "InsuranceStatus",
  "InsuranceExpiration",
];

function normalizeVehicleResponseFields(vehicle) {
  if (!vehicle || typeof vehicle !== "object") return vehicle;
  for (const [target, aliases] of Object.entries(VEHICLE_RESPONSE_ALIASES)) {
    if (vehicle[target] !== undefined && vehicle[target] !== null) continue;
    for (const alias of aliases) {
      if (vehicle[alias] !== undefined && vehicle[alias] !== null) {
        vehicle[target] = vehicle[alias];
        break;
      }
    }
  }
  return vehicle;
}

function syncVehicleDocumentsSectionVisibility() {
  const documentsTitle = document.querySelector("#vehicleDocumentsTitle");
  const documentsGrid = document.querySelector("#vehicleDocumentsGrid");
  if (!documentsTitle || !documentsGrid) return;

  const anyDocumentVisible = DOCUMENT_FIELD_KEYS.some((key) => {
    const row = documentsGrid.querySelector(
      `[data-property="${key}"]`,
    )?.parentElement;
    return row && !row.classList.contains("hidden");
  });

  documentsTitle.classList.toggle("hidden", !anyDocumentVisible);
  documentsGrid.classList.toggle("hidden", !anyDocumentVisible);
}

function formatVehicleDate(value) {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? String(value)
    : date.toLocaleDateString();
}

document
  .querySelector(".searchInputWrapper #vehicleSearchInput")
  .addEventListener("keydown", async function (e) {
    if (e.key == "Enter") {
      e.preventDefault();
      document.querySelector(".searchInputWrapper button").click();
    }
  });

document
  .querySelector(".searchInputWrapper button")
  .addEventListener("click", async function () {
    if (this.classList.contains("loading")) return;
    showLoadingOnButton(this);

    this.blur();
    await performSearch(
      document
        .querySelector(".searchInputWrapper #vehicleSearchInput")
        .value.trim(),
    );

    hideLoadingOnButton(this);
  });

document
  .querySelector(".nearbyPlatesRefresh")
  .addEventListener("click", async function () {
    if (this.classList.contains("loading")) return;
    showLoadingOnButton(this);
    await loadNearbyVehicles({ explicitScan: true });
    hideLoadingOnButton(this);
  });

async function loadNearbyVehicles(options = {}) {
  const language = await getLanguage();
  const url = options.explicitScan
    ? "/data/nearbyVehicles?scan=explicit"
    : "/data/nearbyVehicles";
  const nearbyVehicles = await (
    await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: "8",
    })
  ).json();

  const wrapper = document.querySelector(".nearbyPlatesWrapper");
  const list = document.querySelector(".nearbyPlatesList");
  list.innerHTML = "";

  if (!nearbyVehicles || nearbyVehicles.length === 0) {
    wrapper.classList.remove("hidden");
    const emptyEl = document.createElement("div");
    emptyEl.classList.add("searchCount");
    emptyEl.innerHTML =
      language.vehicleSearch.notifications.noNearbyVehicles ||
      "No nearby vehicles found.";
    list.appendChild(emptyEl);
    return;
  }

  wrapper.classList.remove("hidden");

  for (const vehicle of nearbyVehicles) {
    const item = document.createElement("button");
    if (vehicle.IsStolen) item.classList.add("stolen");
    const model = vehicle.ModelDisplayName
      ? ` - ${vehicle.ModelDisplayName}`
      : "";
    const distance =
      vehicle.Distance != null ? ` (${vehicle.Distance.toFixed(1)}m)` : "";
    item.innerHTML = `${vehicle.LicensePlate}${model}${distance}`;
    item.addEventListener("click", function () {
      document.querySelector(".searchInputWrapper #vehicleSearchInput").value =
        vehicle.LicensePlate;
      document.querySelector(".searchInputWrapper button").click();
    });
    list.appendChild(item);
  }
}

async function loadSearchHistory() {
  const history = await (
    await fetch("/data/searchHistory", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: "vehicle",
    })
  ).json();

  const wrapper = document.querySelector(".searchHistoryWrapper");
  const list = document.querySelector(".searchHistoryList");
  list.innerHTML = "";

  if (history.length === 0) {
    wrapper.classList.add("hidden");
    return;
  }

  wrapper.classList.remove("hidden");

  for (const entry of history) {
    const item = document.createElement("button");
    item.innerHTML = `${entry.ResultName} <span class="searchCount">(${entry.SearchCount})</span>`;
    item.addEventListener("click", async function () {
      document.querySelector(".searchInputWrapper #vehicleSearchInput").value =
        entry.ResultName;
      document.querySelector(".searchInputWrapper button").click();
    });
    list.appendChild(item);
  }
}

let integrationStopEventsProviderPromise = null;
function getStopEventsProvider() {
  if (!integrationStopEventsProviderPromise) {
    integrationStopEventsProviderPromise = fetch("/integration")
      .then((r) => (r.ok ? r.json() : {}))
      .then(
        (j) =>
          (j &&
            typeof j.stopEventsProvider === "string" &&
            j.stopEventsProvider) ||
          "none",
      )
      .catch(() => "none");
  }
  return integrationStopEventsProviderPromise;
}

async function performSearch(query) {
  const language = await getLanguage();
  const stopEventsProvider = await getStopEventsProvider();
  const stpStopIntegrationActive = stopEventsProvider === "StopThePed";
  if (!query) {
    topWindow.showNotification(
      language.vehicleSearch.notifications.emptySearchInput,
      "warning",
    );
    return;
  }
  const res = await fetch("/data/specificVehicle", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: query,
  });
  const response = res.ok ? await res.json().catch(() => null) : null;

  if (!response) {
    document.querySelector(".searchResponseWrapper")?.classList.add("hidden");
    topWindow.showNotification(
      language.vehicleSearch.notifications.vehicleNotFound,
      "warning",
    );
    return;
  }

  normalizeVehicleResponseFields(response);

  if (typeof response === "object" && response !== null) {
    const o = response;
    const aliases = {
      licensePlate: "LicensePlate",
      modelDisplayName: "ModelDisplayName",
      modelName: "ModelName",
      owner: "Owner",
      ownerName: "Owner",
      make: "Make",
      model: "Model",
      color: "Color",
      primaryColor: "PrimaryColor",
      secondaryColor: "SecondaryColor",
      primaryColorSpecific: "PrimaryColorSpecific",
      secondaryColorSpecific: "SecondaryColorSpecific",
      isStolen: "IsStolen",
      vehicleIdentificationNumber: "VehicleIdentificationNumber",
      vinStatus: "VinStatus",
      registrationStatus: "RegistrationStatus",
      registrationExpiration: "RegistrationExpiration",
      insuranceStatus: "InsuranceStatus",
      insuranceExpiration: "InsuranceExpiration",
      bolos: "BOLOs",
      canModifyBOLOs: "CanModifyBOLOs",
      canModifyBolos: "CanModifyBOLOs",
    };
    for (const [from, to] of Object.entries(aliases)) {
      if ((o[to] == null || String(o[to]).trim() === "") && o[from] != null)
        o[to] = o[from];
    }
    const ownerEmpty = o.Owner == null || String(o.Owner).trim() === "";
    if (ownerEmpty && o.OwnerName) o.Owner = o.OwnerName;
  }

  // Alert notification for stolen vehicles
  if (response.IsStolen) {
    topWindow.showNotification(
      `${language.vehicleSearch.notifications?.stolen || "ALERT"}: ${language.vehicleSearch.notifications?.vehicleStolen || "Vehicle"} ${response.LicensePlate} ${language.vehicleSearch.notifications?.reportedStolen || "reported STOLEN"}`,
      "error",
      -1,
    );
  }

  document.title = `${language.vehicleSearch.static.title}: ${response.LicensePlate}`;

  document.querySelector(".searchResponseWrapper").classList.remove("hidden");

  document
    .querySelectorAll(".searchResponseWrapper .basicInfo > div")
    .forEach((w) => {
      w.classList.remove("hidden");
    });

  const emptyToken = language.values?.empty ?? "";
  const optionalFields = new Set([
    "Owner",
    "Make",
    "Model",
    "PrimaryColor",
    "SecondaryColor",
    "PrimaryColorSpecific",
    "SecondaryColorSpecific",
    "VehicleIdentificationNumber",
    "VinStatus",
    "RegistrationExpiration",
    "InsuranceExpiration",
  ]);
  const hasUsableValue = (value) =>
    value !== null &&
    value !== undefined &&
    String(value).trim() !== "" &&
    String(value).trim() !== emptyToken;
  const norm = (value) =>
    String(value ?? "")
      .trim()
      .toLowerCase();
  const getFieldEl = (key) =>
    document.querySelector(`.searchResponseWrapper [data-property="${key}"]`);
  const getFieldWrap = (key) => getFieldEl(key)?.parentElement;
  const hideField = (key) => getFieldWrap(key)?.classList.add("hidden");
  const showField = (key) => getFieldWrap(key)?.classList.remove("hidden");

  async function setVehicleField(
    key,
    { optional = false, formatter = null } = {},
  ) {
    const el = getFieldEl(key);
    if (!el) return;
    const value = response[key];
    if (optional && !hasUsableValue(value)) {
      hideField(key);
      return;
    }
    showField(key);
    el.value = formatter ? formatter(value) : await getLanguageValue(value);
    el.style.color = getColorForValue(value);
  }

  await setVehicleField("LicensePlate");
  await setVehicleField("ModelDisplayName");
  await setVehicleField("IsStolen");
  await setVehicleField("Make", { optional: true });
  await setVehicleField("Model", { optional: true });
  await setVehicleField("Owner", { optional: true });
  await setVehicleField("VehicleIdentificationNumber", { optional: true });
  await setVehicleField("VinStatus", { optional: true });
  await setVehicleField("RegistrationStatus", {
    optional: !hasUsableValue(response.RegistrationStatus),
  });
  await setVehicleField("InsuranceStatus", {
    optional: !hasUsableValue(response.InsuranceStatus),
  });

  for (const key of ["RegistrationExpiration", "InsuranceExpiration"]) {
    const value = response[key];
    const el = getFieldEl(key);
    if (!el) continue;
    if (!hasUsableValue(value)) {
      hideField(key);
      continue;
    }
    showField(key);
    el.style.color = "var(--color-text-primary)";
    const parsedDate = new Date(value);
    if (Number.isNaN(parsedDate.getTime())) {
      hideField(key);
      continue;
    }
    el.value = formatVehicleDate(value);
    const statusKey =
      key === "RegistrationExpiration"
        ? "RegistrationStatus"
        : "InsuranceStatus";
    const statusNorm = norm(response[statusKey]);
    const expMs = new Date(value).getTime();
    const looksPast = !Number.isNaN(expMs) && expMs < Date.now();
    const warnFromDate = looksPast && statusNorm !== "valid";
    const warnFromStatus = /expired|revoked|suspended|invalid|none/i.test(
      statusNorm,
    );
    if (
      (stpStopIntegrationActive && (warnFromDate || warnFromStatus)) ||
      (!stpStopIntegrationActive && looksPast)
    ) {
      el.style.color = "var(--color-warning)";
    }
  }

  const vinStatusEl = getFieldEl("VinStatus");
  if (vinStatusEl && response.VinStatus === "Scratched") {
    vinStatusEl.style.color = "var(--color-warning)";
  }

  syncVehicleDocumentsSectionVisibility();

  const compactNorm = (value) => norm(value).replace(/[^a-z0-9]/g, "");
  const modelDisplay = norm(response.ModelDisplayName);
  const cdfModel = norm(response.Model);
  const compactModelDisplay = compactNorm(response.ModelDisplayName);
  const compactCdfModel = compactNorm(response.Model);
  if (
    !hasUsableValue(response.Model) ||
    (modelDisplay && cdfModel === modelDisplay) ||
    (compactModelDisplay && compactCdfModel === compactModelDisplay)
  ) {
    hideField("Model");
  }

  const colorEl = getFieldEl("Color");
  if (colorEl) {
    const raw = response.Color;
    if (!hasUsableValue(raw)) {
      hideField("Color");
    } else {
      showField("Color");
      const parts = String(raw)
        .split("-")
        .map((s) => s.trim());
      const rgbTriplet =
        parts.length === 3 &&
        parts.every((p) => /^\d{1,3}$/.test(p)) &&
        parts.every((p) => {
          const n = Number(p);
          return n >= 0 && n <= 255;
        });
      if (rgbTriplet) {
        const [r, g, b] = parts;
        colorEl.textContent = "";
        colorEl.style.backgroundColor = `rgb(${r}, ${g}, ${b})`;
        colorEl.style.height = "19px";
      } else {
        colorEl.style.backgroundColor = "";
        colorEl.style.height = "";
        colorEl.textContent = raw;
      }
    }
  }

  for (const key of [
    "PrimaryColor",
    "SecondaryColor",
    "PrimaryColorSpecific",
    "SecondaryColorSpecific",
  ]) {
    await setVehicleField(key, { optional: true });
  }
  const colorSummary = norm(response.Color);
  const primarySecondary = [response.PrimaryColor, response.SecondaryColor]
    .filter(hasUsableValue)
    .map(norm);
  if (
    colorSummary &&
    primarySecondary.length &&
    primarySecondary.every((v) => colorSummary.includes(v))
  ) {
    hideField("PrimaryColor");
    hideField("SecondaryColor");
  }
  if (norm(response.PrimaryColorSpecific) === norm(response.PrimaryColor))
    hideField("PrimaryColorSpecific");
  if (norm(response.SecondaryColorSpecific) === norm(response.SecondaryColor))
    hideField("SecondaryColorSpecific");

  const modelName = response.ModelName;
  const modelDisplayEl = getFieldEl("ModelDisplayName");
  if (modelDisplayEl) {
    modelDisplayEl.parentElement.querySelector("img")?.remove();
    modelDisplayEl.parentElement.classList.remove("hasVehicleImage");
    if (hasUsableValue(modelName)) {
      const imageEl = document.createElement("img");
      imageEl.src = `https://docs.fivem.net/vehicles/${String(modelName).toLowerCase()}.webp`;
      imageEl.onerror = () => {
        imageEl.remove();
        modelDisplayEl.parentElement.classList.remove("hasVehicleImage");
      };
      modelDisplayEl.parentElement.classList.add("hasVehicleImage");
      modelDisplayEl.parentElement.appendChild(imageEl);
    }
  }

  const ownerEl = getFieldEl("Owner");
  if (ownerEl) {
    if (hasUsableValue(response.Owner) && response.Owner !== "Government") {
      ownerEl.parentElement.classList.add("clickable");
      ownerEl.parentElement.onclick = () => openInPedSearch(response.Owner);
    } else {
      ownerEl.parentElement.classList.remove("clickable");
      ownerEl.parentElement.onclick = null;
    }
  }

  for (const key of optionalFields) {
    if (!hasUsableValue(response[key])) hideField(key);
  }
  syncVehicleDocumentsSectionVisibility();

  document
    .querySelectorAll(
      ".searchResponseWrapper .vehicleSearchRecordsSection, .searchResponseWrapper .vehicleSearchRecordsTitle, .searchResponseWrapper .impoundActionSection, .searchResponseWrapper .impoundReportsSection, .searchResponseWrapper .impoundReportsTitle",
    )
    .forEach((el) => el?.remove());

  // Vehicle search records (contraband from PR vehicle search)
  let searchRecordsResponse = [];
  try {
    const res = await fetch("/data/vehicleSearchByPlate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(response.LicensePlate ?? ""),
    });
    if (res.ok) searchRecordsResponse = await res.json();
  } catch (_) {}
  if (!Array.isArray(searchRecordsResponse)) searchRecordsResponse = [];

  // BOLOs section (Be On the Look-Out)
  const boloPlaceholder = document.querySelector(
    ".searchResponseWrapper .boloSectionPlaceholder",
  );
  if (boloPlaceholder) {
    boloPlaceholder.innerHTML = "";
  }
  const bolos = Array.isArray(response.BOLOs) ? response.BOLOs : [];
  const canModifyBOLOs = response.CanModifyBOLOs === true;
  const boloSection = document.createElement("div");
  boloSection.classList.add("boloSection");
  const boloTitle = document.createElement("div");
  boloTitle.classList.add("searchResponseSectionTitle", "boloTitle");
  boloTitle.innerHTML =
    language.vehicleSearch?.static?.bolosTitle || "BOLOs (Be On the Look-Out)";
  boloSection.appendChild(boloTitle);

  if (!canModifyBOLOs && bolos.length > 0) {
    const hint = document.createElement("div");
    hint.classList.add("boloHint");
    hint.textContent =
      language.vehicleSearch?.static?.boloRemoveVehicleRequired ||
      "Vehicle must be nearby to remove BOLOs.";
    boloSection.appendChild(hint);
  }

  if (bolos.length > 0) {
    const boloList = document.createElement("div");
    boloList.classList.add("boloList");
    for (const b of bolos) {
      const reason = b.Reason || b.reason || "Unknown";
      const issuedBy = b.IssuedBy || b.issuedBy || "";
      const exp =
        b.ExpirationDate ||
        b.expirationDate ||
        b.ExpiresAt ||
        b.expiresAt ||
        b.Expires ||
        b.expires;
      const expStr = exp ? new Date(exp).toLocaleDateString() : "-";
      const row = document.createElement("div");
      row.classList.add("boloRow");
      const info = document.createElement("div");
      info.classList.add("boloInfo");
      info.innerHTML = `<strong>${escapeHtml(reason)}</strong>${issuedBy ? ` &mdash; ${escapeHtml(issuedBy)}` : ""} (expires ${escapeHtml(expStr)})`;
      row.appendChild(info);
      if (canModifyBOLOs) {
        const removeBtn = document.createElement("button");
        removeBtn.type = "button";
        removeBtn.classList.add("boloRemoveBtn");
        removeBtn.textContent =
          language.vehicleSearch?.static?.removeBOLO || "Remove";
        removeBtn.addEventListener("click", async () => {
          removeBtn.disabled = true;
          const res = await (
            await fetch("/post/removeBOLO", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({
                LicensePlate: response.LicensePlate,
                Reason: reason,
                Issued:
                  b.Issued ||
                  b.issued ||
                  b.IssuedAt ||
                  b.issuedAt ||
                  b.IssuedDate ||
                  b.issuedDate ||
                  null,
                Expires:
                  b.Expires ||
                  b.expires ||
                  b.ExpiresAt ||
                  b.expiresAt ||
                  b.ExpirationDate ||
                  b.expirationDate ||
                  null,
              }),
            })
          ).json();
          if (res && res.success) {
            topWindow.showNotification(
              language.vehicleSearch?.notifications?.boloRemoved ||
                "BOLO removed.",
              "checkMark",
            );
            await performSearch(response.LicensePlate);
          } else {
            topWindow.showNotification(
              res?.error || "Failed to remove BOLO.",
              "warning",
            );
            removeBtn.disabled = false;
          }
        });
        row.appendChild(removeBtn);
      }
      boloList.appendChild(row);
    }
    boloSection.appendChild(boloList);
  }

  const addBtn = document.createElement("button");
  addBtn.type = "button";
  addBtn.classList.add("boloAddBtn");
  addBtn.textContent = language.vehicleSearch?.static?.addBOLO || "Add BOLO";
  addBtn.addEventListener("click", () =>
    showAddBOLOModal(response, language, performSearch),
  );
  boloSection.appendChild(addBtn);
  if (boloPlaceholder) {
    boloPlaceholder.appendChild(boloSection);
  }

  // Previous impound reports for this vehicle (by plate) — persisted in SQL, so re-encounters show history
  let impoundReports = [];
  try {
    const plate =
      response.LicensePlate != null && response.LicensePlate !== ""
        ? response.LicensePlate
        : "";
    const res = await fetch("/data/impoundReportsByPlate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(plate),
    });
    if (res.ok) {
      const data = await res.json();
      impoundReports = Array.isArray(data) ? data : [];
    }
  } catch (_) {
    impoundReports = [];
  }
  if (impoundReports.length > 0) {
    const impoundReportsTitle = document.createElement("div");
    impoundReportsTitle.classList.add(
      "searchResponseSectionTitle",
      "impoundReportsTitle",
    );
    impoundReportsTitle.innerHTML =
      language.vehicleSearch?.static?.impoundReportsTitle || "Impound Reports";
    document
      .querySelector(".searchResponseWrapper")
      .appendChild(impoundReportsTitle);

    const impoundReportsSection = document.createElement("div");
    impoundReportsSection.classList.add(
      "inputWrapper",
      "grid",
      "impoundReportsSection",
    );
    document
      .querySelector(".searchResponseWrapper")
      .appendChild(impoundReportsSection);

    const openFn =
      typeof topWindow !== "undefined" &&
      typeof topWindow.openIdInReport === "function"
        ? topWindow.openIdInReport
        : typeof openIdInReport === "function"
          ? openIdInReport
          : null;

    for (const r of impoundReports) {
      const row = document.createElement("div");
      row.classList.add("clickable");
      if (openFn) {
        row.addEventListener("click", () => openFn(r.Id, "impound"));
      }

      const label = document.createElement("label");
      label.textContent =
        language.reports?.list?.reportType?.impound || "Impound";

      const input = document.createElement("input");
      input.type = "text";
      input.disabled = true;
      const dateStr = r.TimeStamp
        ? new Date(r.TimeStamp).toLocaleDateString()
        : "";
      const reason = r.ImpoundReason || "";
      input.value = `${r.Id || ""}${dateStr ? ` - ${dateStr}` : ""}${reason ? ` - ${reason}` : ""}`;

      row.appendChild(label);
      row.appendChild(input);
      impoundReportsSection.appendChild(row);
    }
  }

  const impoundSection = document.createElement("div");
  impoundSection.classList.add(
    "impoundActionSection",
    "searchResponseSectionTitle",
  );
  const impoundBtn = document.createElement("button");
  impoundBtn.type = "button";
  impoundBtn.classList.add("createImpoundBtn");
  impoundBtn.textContent =
    language.vehicleSearch?.createImpoundReport || "Create Impound Report";
  impoundBtn.addEventListener("click", () => {
    const fn =
      typeof topWindow !== "undefined" && topWindow.openReportWithPrefill
        ? topWindow.openReportWithPrefill
        : typeof openReportWithPrefill === "function"
          ? openReportWithPrefill
          : null;
    if (fn) {
      fn("impound", {
        source: "vehicleSearch",
        vehiclePlate: response.LicensePlate,
        vehicleData: {
          LicensePlate: response.LicensePlate,
          ModelDisplayName: response.ModelDisplayName,
          ModelName: response.ModelName,
          Owner: response.Owner,
          VehicleIdentificationNumber: response.VehicleIdentificationNumber,
          VinStatus: response.VinStatus,
        },
      });
    }
  });
  impoundSection.appendChild(impoundBtn);
  document.querySelector(".searchResponseWrapper").appendChild(impoundSection);

  if (searchRecordsResponse && searchRecordsResponse.length > 0) {
    const sectionTitle = document.createElement("div");
    sectionTitle.classList.add(
      "searchResponseSectionTitle",
      "vehicleSearchRecordsTitle",
    );
    sectionTitle.innerHTML =
      language.vehicleSearch?.static?.searchResultsTitle ||
      "Search Results (Contraband)";
    document.querySelector(".searchResponseWrapper").appendChild(sectionTitle);

    const recordsSection = document.createElement("div");
    recordsSection.classList.add(
      "inputWrapper",
      "grid",
      "vehicleSearchRecordsSection",
    );
    const seen = new Set();
    for (const r of searchRecordsResponse) {
      const key = `${r.ItemType || ""}|${r.Description || ""}|${r.DrugType || ""}|${r.ItemLocation || ""}`;
      if (seen.has(key)) continue;
      seen.add(key);
      const el = document.createElement("div");
      const isWeapon = !!(
        r.WeaponModelId ||
        (r.ItemType && /weapon|firearm|gun/i.test(r.ItemType))
      );
      if (isWeapon) el.classList.add("clickable");
      const label = document.createElement("label");
      label.textContent = r.ItemType || "Item";
      if (r.ItemLocation) label.textContent += ` (${r.ItemLocation})`;
      const input = document.createElement("input");
      input.type = "text";
      input.disabled = true;
      input.value = r.Description || r.DrugType || "-";
      el.appendChild(label);
      el.appendChild(input);
      if (isWeapon) {
        const lookupKey =
          (r.Description && r.Description.trim()) ||
          r.WeaponModelId ||
          response?.LicensePlate ||
          "";
        el.addEventListener("click", () => openFirearmsSearch(lookupKey));
      }
      recordsSection.appendChild(el);
    }
    document
      .querySelector(".searchResponseWrapper")
      .appendChild(recordsSection);
  }

  // Reload search history after successful search
  await loadNearbyVehicles();
  await loadSearchHistory();
}

function showAddBOLOModal(vehicleResponse, language, onSuccess) {
  const modal = document.getElementById("addBoloModal");
  const form = document.getElementById("addBoloForm");
  const plateInput = document.getElementById("addBoloPlate");
  const reasonInput = document.getElementById("addBoloReason");
  const expiresInput = document.getElementById("addBoloExpires");
  const cancelBtn = document.querySelector(".addBoloModalCancel");
  if (!modal || !form) return;
  plateInput.value = vehicleResponse?.LicensePlate || "";
  reasonInput.value = "";
  expiresInput.value = "7";
  modal.classList.remove("hidden");
  reasonInput.focus();
  function closeModal() {
    modal.classList.add("hidden");
  }
  const cancelHandler = () => {
    cancelBtn.removeEventListener("click", cancelHandler);
    form.onsubmit = null;
    modal.onclick = null;
    closeModal();
  };
  form.onsubmit = async (e) => {
    e.preventDefault();
    const reason = reasonInput?.value?.trim();
    if (!reason) return;
    const expiresDays = parseInt(expiresInput?.value || "7", 10);
    const days =
      isNaN(expiresDays) || expiresDays < 1
        ? 7
        : Math.min(365, Math.max(1, expiresDays));
    const expires = new Date();
    expires.setDate(expires.getDate() + days);
    const submitBtn = form.querySelector(".addBoloModalSubmit");
    if (submitBtn) submitBtn.disabled = true;
    try {
      const res = await (
        await fetch("/post/addBOLO", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            LicensePlate: vehicleResponse.LicensePlate,
            Reason: reason,
            ExpiresAt: expires.toISOString(),
            IssuedBy: "LSPD",
            ModelDisplayName: vehicleResponse?.ModelDisplayName || undefined,
          }),
        })
      ).json();
      if (res && res.success) {
        if (
          typeof topWindow !== "undefined" &&
          typeof topWindow.showNotification === "function"
        ) {
          topWindow.showNotification(
            language.vehicleSearch?.notifications?.boloAdded || "BOLO added.",
            "checkMark",
          );
        }
        cancelHandler();
        if (typeof onSuccess === "function")
          await onSuccess(vehicleResponse.LicensePlate);
      } else {
        if (
          typeof topWindow !== "undefined" &&
          typeof topWindow.showNotification === "function"
        ) {
          topWindow.showNotification(
            res?.error || "Failed to add BOLO.",
            "warning",
          );
        }
      }
    } catch (_) {
      if (
        typeof topWindow !== "undefined" &&
        typeof topWindow.showNotification === "function"
      ) {
        topWindow.showNotification("Failed to add BOLO.", "warning");
      }
    }
    if (submitBtn) submitBtn.disabled = false;
  };
  cancelBtn.addEventListener("click", cancelHandler);
  modal.onclick = (e) => {
    if (e.target === modal) cancelHandler();
  };
}

function escapeHtml(s) {
  if (s == null) return "";
  const d = document.createElement("div");
  d.textContent = s;
  return d.innerHTML;
}

function getColorForValue(value) {
  switch (value) {
    case true:
    case "Revoked":
    case "None":
      return "var(--color-error)";
    case false:
    case "Valid":
      return "var(--color-success)";
    case "Expired":
      return "var(--color-warning)";
    default:
      return "var(--color-text-primary)";
  }
}
