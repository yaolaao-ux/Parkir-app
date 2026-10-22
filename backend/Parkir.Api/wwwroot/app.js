const loginView = document.querySelector("#login-view");
const appView = document.querySelector("#app-view");
const loginForm = document.querySelector("#login-form");
const loginMessage = document.querySelector("#login-message");
const appMessage = document.querySelector("#app-message");
const state = { token: null, fullName: "", vehicleTypes: [], currentView: "dashboard" };
const money = new Intl.NumberFormat("id-ID", { style: "currency", currency: "IDR", maximumFractionDigits: 0 });
const dateFormat = new Intl.DateTimeFormat("id-ID", { dateStyle: "long" });

document.querySelector("#today-label").textContent = dateFormat.format(new Date());
loginForm.addEventListener("submit", login);
document.querySelector("#logout-button").addEventListener("click", logout);
document.querySelector("#refresh-button").addEventListener("click", loadDashboard);
document.querySelector("#add-tariff-button").addEventListener("click", () => openTariffDialog());
document.querySelector("#add-member-button").addEventListener("click", openMemberDialog);
document.querySelector("#add-gate-button").addEventListener("click", () => document.querySelector("#gate-dialog").showModal());
document.querySelector("#tariff-form").addEventListener("submit", saveTariff);
document.querySelector("#member-form").addEventListener("submit", saveMember);
document.querySelector("#gate-form").addEventListener("submit", saveGate);
document.querySelector("#report-form").addEventListener("submit", event => {
    event.preventDefault();
    loadReport();
});
document.querySelector("#export-report-button").addEventListener("click", exportReport);
document.querySelectorAll("[data-close]").forEach(button => {
    button.addEventListener("click", () => document.getElementById(button.dataset.close).close());
});
document.querySelectorAll(".nav-item").forEach(button => {
    button.addEventListener("click", () => showView(button.dataset.view));
});
const today = new Date();
document.querySelector("#report-to").value = localDate(today);
document.querySelector("#report-from").value = localDate(new Date(today.getFullYear(), today.getMonth(), 1));

async function login(event) {
    event.preventDefault();
    loginMessage.textContent = "";
    const form = new FormData(loginForm);
    try {
        const response = await fetch("/api/auth/login", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ username: form.get("username"), password: form.get("password") })
        });
        const result = await readResponse(response);
        if (result.role !== "ADMIN") {
            loginMessage.textContent = "Akun ini tidak memiliki akses administrator.";
            return;
        }
        state.token = result.accessToken;
        state.fullName = result.fullName;
        loginForm.reset();
        loginView.hidden = true;
        appView.hidden = false;
        document.querySelector("#admin-name").textContent = result.fullName;
        await loadCurrentView();
    } catch (error) {
        loginMessage.textContent = error.message;
    }
}

function logout() {
    state.token = null;
    state.fullName = "";
    appView.hidden = true;
    loginView.hidden = false;
    loginForm.reset();
    document.querySelector("#login-username").focus();
}

async function api(path, options = {}) {
    const headers = { Authorization: `Bearer ${state.token}`, ...(options.headers || {}) };
    if (options.body) headers["Content-Type"] = "application/json";
    const response = await fetch(path, { ...options, headers });
    if (response.status === 401) {
        logout();
        throw new Error("Sesi berakhir. Silakan masuk kembali.");
    }
    if (response.status === 403) throw new Error("Akses hanya tersedia untuk administrator.");
    return readResponse(response);
}

async function readResponse(response) {
    if (response.status === 204) return null;
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(payload.error || `Permintaan gagal (${response.status}).`);
    return payload;
}

function showView(viewName) {
    state.currentView = viewName;
    document.querySelectorAll(".nav-item").forEach(button => {
        button.classList.toggle("active", button.dataset.view === viewName);
    });
    document.querySelectorAll(".view-panel").forEach(panel => {
        panel.hidden = panel.id !== `${viewName}-view`;
    });
    const titles = {
        dashboard: ["IKHTISAR HARI INI", "Ringkasan"],
        tariffs: ["KONFIGURASI OPERASIONAL", "Tarif kendaraan"],
        members: ["DATA LANGGANAN", "Member"],
        reports: ["REKAP OPERASIONAL", "Laporan"],
        gates: ["INFRASTRUKTUR PARKIR", "Gate dan pos"]
    };
    document.querySelector("#page-kicker").textContent = titles[viewName][0];
    document.querySelector("#page-title").textContent = titles[viewName][1];
    clearMessage();
    loadCurrentView();
}

async function loadCurrentView() {
    if (!state.token) return;
    try {
        if (state.currentView === "dashboard") await loadDashboard();
        if (state.currentView === "tariffs") await loadTariffs();
        if (state.currentView === "members") await loadMembers();
        if (state.currentView === "reports") await loadReport();
        if (state.currentView === "gates") await loadGates();
    } catch (error) {
        showMessage(error.message, true);
    }
}

async function loadDashboard() {
    const [data, devices] = await Promise.all([
        api("/api/admin/dashboard"),
        api("/api/admin/devices")
    ]);
    document.querySelector("#revenue-value").textContent = money.format(data.revenueToday);
    document.querySelector("#inside-value").textContent = data.vehiclesInside.toLocaleString("id-ID");
    document.querySelector("#entries-value").textContent = data.entriesToday.toLocaleString("id-ID");
    document.querySelector("#exits-value").textContent = data.exitsToday.toLocaleString("id-ID");
    document.querySelector("#last-updated").textContent = `Diperbarui ${new Intl.DateTimeFormat("id-ID", { timeStyle: "short" }).format(new Date())}`;
    const rows = document.querySelector("#device-rows");
    rows.replaceChildren();
    document.querySelector("#device-empty").hidden = devices.length > 0;
    for (const device of devices) {
        const row = document.createElement("tr");
        appendCell(row, device.displayName, true);
        appendCell(row, device.deviceType === "GATE_IN" ? "Gerbang masuk" : "POS kasir");
        appendCell(row, device.ipAddress || "-");
        appendCell(row, device.firmwareVersion || "-");
        appendCell(row, new Intl.DateTimeFormat("id-ID", { dateStyle: "short", timeStyle: "medium" })
            .format(new Date(device.lastSeen)));
        const statusCell = document.createElement("td");
        const status = document.createElement("span");
        status.className = `device-status ${device.isOnline ? "online" : "offline"}`;
        status.textContent = device.isOnline ? "ONLINE" : "OFFLINE";
        statusCell.append(status);
        row.append(statusCell);
        rows.append(row);
    }
    clearMessage();
}

async function loadReport() {
    const from = document.querySelector("#report-from").value;
    const to = document.querySelector("#report-to").value;
    const query = new URLSearchParams({ from, to });
    const report = await api(`/api/admin/reports?${query}`);
    document.querySelector("#report-revenue").textContent = money.format(report.totalRevenue);
    document.querySelector("#report-entries").textContent = report.totalEntries.toLocaleString("id-ID");
    document.querySelector("#report-exits").textContent = report.totalExits.toLocaleString("id-ID");
    const rows = document.querySelector("#report-rows");
    rows.replaceChildren();
    document.querySelector("#report-empty").hidden = report.days.length > 0;
    for (const day of report.days) {
        const row = document.createElement("tr");
        appendCell(row, new Intl.DateTimeFormat("id-ID", { dateStyle: "medium" }).format(new Date(`${day.reportDate}T00:00:00`)));
        appendCell(row, day.entries.toLocaleString("id-ID"));
        appendCell(row, day.exits.toLocaleString("id-ID"));
        appendCell(row, money.format(day.revenue));
        rows.append(row);
    }
    clearMessage();
}

async function loadGates() {
    const gates = await api("/api/admin/gates");
    const rows = document.querySelector("#gate-rows");
    rows.replaceChildren();
    document.querySelector("#gate-empty").hidden = gates.length > 0;
    for (const gate of gates) {
        const row = document.createElement("tr");
        appendCell(row, gate.gateId, true);
        appendCell(row, gate.gateType === "GATE_IN" ? "Masuk" : "Keluar");
        appendCell(row, gate.displayName);
        appendCell(row, gate.lastSeen
            ? new Intl.DateTimeFormat("id-ID", { dateStyle: "short", timeStyle: "medium" }).format(new Date(gate.lastSeen))
            : "Belum heartbeat");
        const deviceCell = document.createElement("td");
        const onlineBadge = document.createElement("span");
        onlineBadge.className = `device-status ${gate.isOnline ? "online" : "offline"}`;
        onlineBadge.textContent = gate.isOnline ? "ONLINE" : "OFFLINE";
        deviceCell.append(onlineBadge);
        row.append(deviceCell);

        const enabledCell = document.createElement("td");
        const enabledSelect = document.createElement("select");
        enabledSelect.className = "status-select";
        enabledSelect.setAttribute("aria-label", `Status gate ${gate.gateId}`);
        for (const [value, label] of [[true, "Aktif"], [false, "Nonaktif"]]) {
            const option = document.createElement("option");
            option.value = String(value);
            option.textContent = label;
            option.selected = gate.enabled === value;
            enabledSelect.append(option);
        }
        enabledSelect.addEventListener("change", () => updateGate(gate, enabledSelect.value === "true"));
        enabledCell.append(enabledSelect);
        row.append(enabledCell);
        rows.append(row);
    }
    clearMessage();
}

async function saveGate(event) {
    event.preventDefault();
    const values = new FormData(event.currentTarget);
    const input = {
        gateId: values.get("gateId").trim().toUpperCase(),
        gateType: values.get("gateType"),
        displayName: values.get("displayName").trim(),
        enabled: true
    };
    try {
        await api("/api/admin/gates", { method: "POST", body: JSON.stringify(input) });
        event.currentTarget.reset();
        document.querySelector("#gate-dialog").close();
        showMessage(`Gate ${input.gateId} terdaftar. Atur PARKIR_GATE_ID pada POS atau SET_GATE_ID pada serial ESP32 agar cocok.`);
        await loadGates();
    } catch (error) {
        showMessage(error.message, true);
    }
}

async function updateGate(gate, enabled) {
    try {
        await api(`/api/admin/gates/${encodeURIComponent(gate.gateId)}`, {
            method: "PUT",
            body: JSON.stringify({ ...gate, enabled })
        });
        showMessage(`Gate ${gate.gateId} ${enabled ? "diaktifkan" : "dinonaktifkan"}.`);
    } catch (error) {
        showMessage(error.message, true);
        await loadGates();
    }
}

async function exportReport() {
    const from = document.querySelector("#report-from").value;
    const to = document.querySelector("#report-to").value;
    const query = new URLSearchParams({ from, to });
    try {
        const response = await fetch(`/api/admin/reports/export.csv?${query}`, {
            headers: { Authorization: `Bearer ${state.token}` }
        });
        if (response.status === 401) {
            logout();
            throw new Error("Sesi berakhir. Silakan masuk kembali.");
        }
        if (!response.ok) {
            const body = await response.json().catch(() => ({}));
            throw new Error(body.error || `Ekspor gagal (${response.status}).`);
        }

        const blob = await response.blob();
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = `laporan-parkir-${from}-${to}.csv`;
        link.click();
        URL.revokeObjectURL(url);
        clearMessage();
    } catch (error) {
        showMessage(error.message, true);
    }
}

async function loadTariffs() {
    state.vehicleTypes = await api("/api/admin/vehicle-types");
    const rows = document.querySelector("#tariff-rows");
    rows.replaceChildren();
    document.querySelector("#tariff-empty").hidden = state.vehicleTypes.length > 0;
    for (const item of state.vehicleTypes) {
        const row = document.createElement("tr");
        appendCell(row, item.typeName, true);
        appendCell(row, money.format(item.firstHourRate));
        appendCell(row, money.format(item.hourlyRate));
        appendCell(row, `${item.gracePeriodMinutes} menit`);
        appendCell(row, money.format(item.lostTicketFee));
        appendCell(row, money.format(item.maxDailyRate));
        const action = document.createElement("td");
        const button = document.createElement("button");
        button.className = "table-action";
        button.type = "button";
        button.textContent = "Ubah";
        button.addEventListener("click", () => openTariffDialog(item));
        action.append(button);
        row.append(action);
        rows.append(row);
    }
    clearMessage();
}

function openTariffDialog(item = null) {
    const form = document.querySelector("#tariff-form");
    form.reset();
    form.elements.id.value = item?.id || "";
    form.elements.typeName.value = item?.typeName || "";
    form.elements.firstHourRate.value = item?.firstHourRate ?? "";
    form.elements.hourlyRate.value = item?.hourlyRate ?? "";
    form.elements.gracePeriodMinutes.value = item?.gracePeriodMinutes ?? 5;
    form.elements.lostTicketFee.value = item?.lostTicketFee ?? "";
    form.elements.maxDailyRate.value = item?.maxDailyRate ?? "";
    document.querySelector("#tariff-dialog-title").textContent = item ? "Ubah tarif" : "Tambah tarif";
    document.querySelector("#tariff-dialog").showModal();
}

async function saveTariff(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const values = new FormData(form);
    const id = values.get("id");
    const input = {
        typeName: values.get("typeName"),
        firstHourRate: Number(values.get("firstHourRate")),
        hourlyRate: Number(values.get("hourlyRate")),
        gracePeriodMinutes: Number(values.get("gracePeriodMinutes")),
        lostTicketFee: Number(values.get("lostTicketFee")),
        maxDailyRate: Number(values.get("maxDailyRate"))
    };
    try {
        await api(id ? `/api/admin/vehicle-types/${id}` : "/api/admin/vehicle-types", {
            method: id ? "PUT" : "POST",
            body: JSON.stringify(input)
        });
        document.querySelector("#tariff-dialog").close();
        showMessage("Tarif berhasil disimpan.");
        await loadTariffs();
    } catch (error) {
        showMessage(error.message, true);
    }
}

async function loadMembers() {
    const [members, vehicleTypes] = await Promise.all([
        api("/api/admin/members"),
        api("/api/admin/vehicle-types")
    ]);
    state.vehicleTypes = vehicleTypes;
    const rows = document.querySelector("#member-rows");
    rows.replaceChildren();
    document.querySelector("#member-empty").hidden = members.length > 0;
    for (const member of members) {
        const row = document.createElement("tr");
        appendCell(row, member.fullName, true);
        appendCell(row, member.licensePlate);
        appendCell(row, member.rfidUid);
        appendCell(row, member.vehicleTypeName || "- ");
        appendCell(row, new Intl.DateTimeFormat("id-ID", { dateStyle: "medium" }).format(new Date(`${member.expiredDate}T00:00:00`)));
        const statusCell = document.createElement("td");
        const select = document.createElement("select");
        select.className = "status-select";
        select.setAttribute("aria-label", `Status member ${member.fullName}`);
        for (const status of ["ACTIVE", "SUSPENDED", "EXPIRED"]) {
            const option = document.createElement("option");
            option.value = status;
            option.textContent = status;
            option.selected = member.status === status;
            select.append(option);
        }
        select.addEventListener("change", () => updateMemberStatus(member.id, select.value));
        statusCell.append(select);
        row.append(statusCell);
        rows.append(row);
    }
    clearMessage();
}

function openMemberDialog() {
    const form = document.querySelector("#member-form");
    form.reset();
    const select = form.elements.vehicleTypeId;
    select.replaceChildren();
    for (const type of state.vehicleTypes) {
        const option = document.createElement("option");
        option.value = type.id;
        option.textContent = type.typeName;
        select.append(option);
    }
    form.elements.expiredDate.value = new Date().toISOString().slice(0, 10);
    document.querySelector("#member-dialog").showModal();
}

async function saveMember(event) {
    event.preventDefault();
    const values = new FormData(event.currentTarget);
    const input = {
        fullName: values.get("fullName"),
        rfidUid: values.get("rfidUid"),
        licensePlate: values.get("licensePlate"),
        vehicleTypeId: values.get("vehicleTypeId") ? Number(values.get("vehicleTypeId")) : null,
        expiredDate: values.get("expiredDate")
    };
    try {
        await api("/api/admin/members", { method: "POST", body: JSON.stringify(input) });
        document.querySelector("#member-dialog").close();
        showMessage("Member berhasil didaftarkan.");
        await loadMembers();
    } catch (error) {
        showMessage(error.message, true);
    }
}

async function updateMemberStatus(id, status) {
    try {
        await api(`/api/admin/members/${id}/status`, {
            method: "PUT",
            body: JSON.stringify({ status })
        });
        showMessage("Status member diperbarui.");
    } catch (error) {
        showMessage(error.message, true);
        await loadMembers();
    }
}

function appendCell(row, value, emphasized = false) {
    const cell = document.createElement("td");
    if (emphasized) {
        const strong = document.createElement("strong");
        strong.textContent = value;
        cell.append(strong);
    } else {
        cell.textContent = value;
    }
    row.append(cell);
}

function showMessage(message, isError = false) {
    appMessage.textContent = message;
    appMessage.classList.toggle("error", isError);
    appMessage.hidden = false;
}

function clearMessage() {
    appMessage.hidden = true;
    appMessage.textContent = "";
    appMessage.classList.remove("error");
}

function localDate(date) {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, "0");
    const day = String(date.getDate()).padStart(2, "0");
    return `${year}-${month}-${day}`;
}

setInterval(() => {
    if (state.token && state.currentView === "dashboard") loadDashboard().catch(error => showMessage(error.message, true));
}, 30000);