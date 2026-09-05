const STATE_LABELS = {
    "added": "added",
    "generated": "generated",
    "sent": "sent",
    "followed-up": "followed-up",
    "n/a": "N/A",
    "other": "other",
};

const app = {
    status: null,
    applications: [],
    activeGenerations: [],
    jobs: {},
    filter: "all",
    query: "",
    expanded: new Set(),
    detailTabs: {},
    fileCache: {},
};

function el(id) {
    return document.getElementById(id);
}

async function api(path, options) {
    const response = await fetch(path, {
        headers: { "Content-Type": "application/json" },
        ...options,
    });
    const body = await response.json().catch(() => ({}));
    if (!response.ok) {
        throw new Error(body.error || `${response.status} ${response.statusText}`);
    }
    return body;
}

function toast(message, kind = "info") {
    const node = document.createElement("div");
    node.className = `toast ${kind}`;
    node.textContent = message;
    el("toasts").appendChild(node);
    setTimeout(() => node.remove(), kind === "error" ? 10000 : 4000);
}

async function loadStatus() {
    app.status = await api("/api/status");
    el("workspace-root").textContent = app.status.workspaceRoot;
    el("workspace-root").title = app.status.workspaceRoot;
    const db = el("db-status");
    db.classList.toggle("ok", app.status.database.exists);
    db.title = app.status.database.exists
        ? `${app.status.database.path}\nupdated ${app.status.database.lastWriteUtc || "(unknown)"}`
        : `missing: ${app.status.database.path}`;
    const jobsDb = app.status.jobsDb;
    if (jobsDb) {
        el("refresh").title =
            `sqlite: ${jobsDb.applicationCount} apps, ${jobsDb.recruiterCount} recruiters, ${jobsDb.eventCount} events\n${jobsDb.path}`;
    }
}

async function loadApplications() {
    const body = await api("/api/applications");
    app.applications = body.applications;
    app.activeGenerations = body.activeGenerations || [];
    for (const job of app.activeGenerations) {
        if (!app.jobs[job.applicationKey]) {
            app.jobs[job.applicationKey] = job;
        }
    }
    render();
}

/* --- rendering ---------------------------------------------------------- */

function stateClass(state) {
    return state.replace("/", "-");
}

function matchesFilter(application) {
    if (app.filter !== "all" && application.state !== app.filter) {
        return false;
    }
    if (!app.query) {
        return true;
    }
    const haystack = [
        application.nr,
        application.title,
        application.company,
        application.folderPath,
        application.recruiter && application.recruiter.name,
    ].filter(Boolean).join(" ").toLowerCase();
    return haystack.includes(app.query);
}

function sortApplications(list) {
    return [...list].sort((a, b) => {
        const nrA = parseInt(a.nr, 10) || -1;
        const nrB = parseInt(b.nr, 10) || -1;
        if (nrA !== nrB) return nrB - nrA;
        return (a.title || "").localeCompare(b.title || "");
    });
}

function renderStateFilters() {
    const counts = { all: app.applications.length };
    for (const application of app.applications) {
        counts[application.state] = (counts[application.state] || 0) + 1;
    }
    const container = el("state-filters");
    container.innerHTML = "";
    const states = ["all", "added", "generated", "sent", "followed-up", "n/a", "other"];
    for (const state of states) {
        if (!(state in counts)) continue;
        const chip = document.createElement("button");
        chip.className = `chip ${app.filter === state ? "active" : ""}`;
        chip.innerHTML = `${STATE_LABELS[state] || state}<span class="count">${counts[state]}</span>`;
        chip.addEventListener("click", () => {
            app.filter = state;
            render();
        });
        container.appendChild(chip);
    }
}

const FILE_INDICATORS = [
    { label: "pdf", file: application => application.files.pdf },
    { label: "config", file: application => application.files.config },
    { label: "job", file: application => application.files.jobDescription },
    { label: "research", file: application => application.files.companyResearch },
    { label: "cover", file: application => application.files.coverLetter },
];

function renderFiles(td, application) {
    const wrap = document.createElement("div");
    wrap.className = "file-indicators";
    if (!application.folderExists) {
        wrap.append("(no folder)");
        td.append(wrap);
        return;
    }
    if (!(application.files.allFiles || []).length) {
        wrap.append("(empty)");
    }
    for (const indicator of FILE_INDICATORS) {
        const file = indicator.file(application);
        const span = document.createElement("span");
        span.className = `file-indicator ${file ? "present" : "missing"}`;
        span.textContent = indicator.label;
        span.title = file || `${indicator.label}: not present`;
        wrap.append(span);
    }
    td.append(wrap);
}

function actionsCell(td, application) {
    const wrap = document.createElement("div");
    wrap.style.display = "flex";
    wrap.style.gap = "6px";
    wrap.style.flexWrap = "wrap";

    const openFolder = document.createElement("button");
    openFolder.className = "btn subtle icon-btn";
    openFolder.append(svgIcon("folder"));
    openFolder.title = "Open folder in file manager";
    openFolder.setAttribute("aria-label", "Open folder in file manager");
    openFolder.addEventListener("click", async () => {
        try {
            await api("/api/applications/open", { method: "POST", body: JSON.stringify({ key: application.key }) });
        } catch (error) {
            toast(error.message, "error");
        }
    });
    wrap.append(openFolder);

    if (application.files.pdf) {
        const view = document.createElement("a");
        view.className = "btn subtle icon-btn";
        view.append(svgIcon("pdf"));
        view.title = application.files.pdf;
        view.setAttribute("aria-label", `Open ${application.files.pdf}`);
        view.href = `/api/applications/file?key=${encodeURIComponent(application.key)}&name=${encodeURIComponent(application.files.pdf)}`;
        view.target = "_blank";
        view.rel = "noopener";
        wrap.append(view);
    }

    td.append(wrap);
}

const ICON_SVGS = {
    folder: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>',
    pdf: '<svg viewBox="0 0 24 24"><path fill="#e2574c" d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path fill="#b5362b" d="M14 2v6h6z"/><text x="12" y="17.5" text-anchor="middle" font-size="6.5" font-weight="bold" fill="#fff" font-family="Arial, Helvetica, sans-serif">PDF</text></svg>',
    vscode: '<svg viewBox="0 0 24 24" fill="#007acc"><path d="M23.15 2.587 18.21.21a1.494 1.494 0 0 0-1.705.29l-9.46 8.63-4.12-3.128a.999.999 0 0 0-1.276.057L.327 7.261A1 1 0 0 0 .326 8.74L3.899 12 .326 15.26a1 1 0 0 0 .001 1.479L1.65 17.94a.999.999 0 0 0 1.276.057l4.12-3.128 9.46 8.63a1.492 1.492 0 0 0 1.704.29l4.942-2.377A1.5 1.5 0 0 0 24 20.06V3.939a1.5 1.5 0 0 0-.85-1.352zm-5.146 14.861L10.826 12l7.178-5.448v10.896z"/></svg>',
};

function svgIcon(name) {
    const span = document.createElement("span");
    span.className = "icon";
    span.innerHTML = ICON_SVGS[name];
    return span;
}

function stateCell(td, application) {
    const badge = document.createElement("span");
    badge.className = `badge ${stateClass(application.state)}`;
    badge.textContent = STATE_LABELS[application.state] || application.state;
    td.append(badge);
    if (application.stateNote) {
        const note = document.createElement("div");
        note.className = "state-note";
        note.textContent = application.stateNote;
        note.title = application.stateNote;
        td.append(note);
    }
}

function renderEvents(td, application) {
    const wrap = document.createElement("div");
    wrap.className = "events-list";
    const events = application.events || [];
    if (!events.length) {
        wrap.textContent = application.createdAt || "";
        wrap.title = application.createdAt || "";
    }
    for (const event of events) {
        const line = document.createElement("div");
        line.className = "event-item";
        line.textContent = `${event.type} ${event.occurredAt}`;
        line.title = event.note || event.type;
        wrap.append(line);
    }
    td.append(wrap);
}

function alreadyTextedText(info) {
    return `Already texted: ${info.title} @ ${info.company} (${info.followedUpAt})`;
}

function renderAlreadyTexted(container, application) {
    const infos = application.alreadyTexted || [];
    for (const info of infos) {
        const banner = document.createElement("div");
        banner.className = "already-texted-inline";
        banner.textContent = alreadyTextedText(info);
        banner.title = alreadyTextedText(info);
        container.append(banner);
    }
}

function renderRecruiter(node, application) {
    const recruiter = application.recruiter;
    if (!recruiter || (!recruiter.name && !recruiter.profileUrl)) {
        node.textContent = "No recruiter recorded.";
        return;
    }
    node.innerHTML = "";
    if (recruiter.name) {
        const name = document.createElement("div");
        name.className = "recruiter-name";
        name.textContent = recruiter.name;
        node.append(name);
    }
    if (recruiter.title) {
        const title = document.createElement("div");
        title.className = "recruiter-title";
        title.textContent = recruiter.title;
        node.append(title);
    }
    if (recruiter.profileUrl) {
        const link = document.createElement("a");
        link.href = recruiter.profileUrl;
        link.target = "_blank";
        link.rel = "noopener";
        link.textContent = recruiter.profileUrl;
        node.append(link);
    }
    if (recruiter.location) {
        const location = document.createElement("div");
        location.className = "recruiter-location";
        location.textContent = recruiter.location;
        node.append(location);
    }
    if (recruiter.notes) {
        const notes = document.createElement("div");
        notes.className = "recruiter-notes";
        notes.textContent = recruiter.notes;
        node.append(notes);
    }
    if (application.companyUrl) {
        const company = document.createElement("div");
        company.className = "recruiter-company";
        const link = document.createElement("a");
        link.href = application.companyUrl;
        link.target = "_blank";
        link.rel = "noopener";
        link.textContent = application.companyUrl;
        company.append("Company: ", link);
        node.append(company);
    }
}

function renderDetailEvents(node, application) {
    node.innerHTML = "";
    const events = application.events || [];
    if (!events.length) {
        node.textContent = `created: ${application.createdAt}`;
        return;
    }
    for (const event of events) {
        const line = document.createElement("div");
        line.className = "event-item";
        line.textContent = event.note
            ? `${event.type} ${event.occurredAt} — ${event.note}`
            : `${event.type} ${event.occurredAt}`;
        line.title = event.note || event.type;
        node.append(line);
    }
}

function renderRows() {
    const tbody = el("apps-body");
    tbody.innerHTML = "";
    const visible = sortApplications(app.applications.filter(matchesFilter));
    el("empty-state").classList.toggle("hidden", visible.length > 0);

    for (const application of visible) {
        const row = document.createElement("tr");
        row.className = "app-row";
        if (app.expanded.has(application.key)) row.classList.add("expanded");
        row.dataset.key = application.key;
        row.title = app.expanded.has(application.key) ? "Click to collapse" : "Click to expand";
        row.addEventListener("click", event => {
            // Let links, buttons, and inputs inside the row do their own thing.
            if (event.target instanceof Element
                && event.target.closest("a, button, select, input, textarea")) {
                return;
            }
            toggleDetail(application.key);
        });

        const nr = document.createElement("td");
        nr.className = "col-nr";
        nr.textContent = application.nr || "—";
        row.append(nr);

        const title = document.createElement("td");
        title.className = "col-title";
        if (application.jobUrl) {
            const link = document.createElement("a");
            link.href = application.jobUrl;
            link.target = "_blank";
            link.rel = "noopener";
            link.textContent = application.title || "(untitled)";
            title.append(link);
        } else {
            title.textContent = application.title || "(untitled)";
        }
        const company = document.createElement("div");
        company.className = "job-company";
        company.textContent = application.company || "";
        title.append(company);
        renderAlreadyTexted(title, application);
        row.append(title);

        const events = document.createElement("td");
        events.className = "col-events";
        renderEvents(events, application);
        row.append(events);

        const state = document.createElement("td");
        state.className = "col-state";
        stateCell(state, application);
        row.append(state);

        const files = document.createElement("td");
        files.className = "col-files";
        renderFiles(files, application);
        row.append(files);

        const actions = document.createElement("td");
        actions.className = "col-actions";
        actionsCell(actions, application);
        row.append(actions);

        tbody.append(row);

        if (app.expanded.has(application.key)) {
            tbody.append(renderDetailRow(application));
        }
    }
}

function render() {
    renderStateFilters();
    renderRows();
}

/* --- detail panel ---------------------------------------------------------- */

function renderDetailRow(application) {
    const tr = document.createElement("tr");
    tr.className = "detail-row";
    const td = document.createElement("td");
    td.colSpan = 6;
    tr.append(td);

    const template = el("detail-template");
    const detail = template.content.cloneNode(true);
    const root = detail.querySelector(".detail");

    const tabsContainer = root.querySelector(".detail-tabs");
    tabsContainer.innerHTML = "";
    const tabs = detailTabsFor(application);
    const selectedTab = selectedDetailTab(application);
    const fileContent = root.querySelector("[data-role=file-content]");

    for (const tabInfo of tabs) {
        const tab = document.createElement("button");
        tab.className = `tab ${tabInfo.name === selectedTab ? "active" : ""}`;
        tab.textContent = tabInfo.label;
        tab.title = tabInfo.name;
        tab.addEventListener("click", () => {
            if (app.detailTabs[application.key] === tabInfo.name) return;
            app.detailTabs[application.key] = tabInfo.name;
            // Targeted refresh: a full renderRows() rebuilds the whole table
            // and flashes the panel for a frame.
            refreshDetailTab(application.key, tabInfo.name);
        });
        tabsContainer.append(tab);
    }

    loadDetailContent(root, application, selectedTab, fileContent);

    const select = root.querySelector("[data-role=state-select]");
    for (const [wire, label] of Object.entries(STATE_LABELS)) {
        const option = document.createElement("option");
        option.value = wire;
        option.textContent = label;
        if (wire === application.state) option.selected = true;
        select.append(option);
    }
    root.querySelector("[data-role=state-note]").value = application.stateNote || "";
    const createdInfo = root.querySelector("[data-role=created-info]");
    const eventCount = (application.events || []).length;
    createdInfo.textContent = `created: ${application.createdAt} • ${eventCount} events`;

    const alreadyBanner = root.querySelector("[data-role=already-texted]");
    const alreadyInfos = application.alreadyTexted || [];
    if (!alreadyInfos.length) {
        alreadyBanner.classList.add("hidden");
    } else {
        alreadyBanner.classList.remove("hidden");
        for (const info of alreadyInfos) {
            const line = document.createElement("div");
            line.textContent = alreadyTextedText(info);
            line.title = alreadyTextedText(info);
            alreadyBanner.append(line);
        }
    }

    const recruiterNode = root.querySelector("[data-role=recruiter]");
    renderRecruiter(recruiterNode, application);

    const eventsNode = root.querySelector("[data-role=events]");
    renderDetailEvents(eventsNode, application);

    root.querySelector("[data-role=state-save]").addEventListener("click", async event => {
        const button = event.currentTarget;
        button.disabled = true;
        try {
            await api("/api/applications/state", {
                method: "PUT",
                body: JSON.stringify({
                    key: application.key,
                    state: select.value,
                    note: root.querySelector("[data-role=state-note]").value,
                }),
            });
            toast("State updated.", "success");
            await loadApplications();
        } catch (error) {
            toast(error.message, "error");
        } finally {
            button.disabled = false;
        }
    });

    const links = root.querySelector("[data-role=links]");
    if (application.jobUrl) {
        links.append(makeLink("Job posting", application.jobUrl));
    }
    if (application.files.pdf) {
        links.append(makeLink(application.files.pdf, fileUrl(application.key, application.files.pdf)));
    }
    if (application.files.annotatedMarkdown) {
        links.append(makeLink(
            application.files.annotatedMarkdown,
            fileUrl(application.key, application.files.annotatedMarkdown)));
    }
    if (!links.children.length) {
        links.textContent = "No links yet.";
    }

    const progressWrap = root.querySelector("[data-role=generation-progress]");
    const progressLabel = root.querySelector("[data-role=progress-label]");
    const progressFill = root.querySelector("[data-role=progress-fill]");
    const errorNode = root.querySelector("[data-role=generation-error]");

    root.querySelector("[data-role=generate-pdf]").addEventListener("click", () => startGeneration(application.key, false));
    root.querySelector("[data-role=generate-md]").addEventListener("click", () => startGeneration(application.key, true));

    const job = findJob(application.key);
    if (job) {
        if (job.state === "Queued" || job.state === "Running") {
            progressWrap.classList.remove("hidden");
            progressFill.style.width = `${Math.round(job.overallPercent)}%`;
            progressLabel.textContent = `${job.state === "Queued" ? "Queued" : job.moduleDescription || "Working"} — ${Math.round(job.overallPercent)}%`;
        } else if (job.state === "Failed") {
            errorNode.textContent = job.error || "Generation failed.";
            errorNode.classList.remove("hidden");
        }
    }

    td.append(root);
    return tr;
}

function selectedDetailTab(application) {
    const tabs = detailTabsFor(application);
    const wanted = app.detailTabs[application.key];
    if (wanted && tabs.some(tabInfo => tabInfo.name === wanted)) return wanted;
    return tabs[0] && tabs[0].name;
}

// Switches the visible tab inside an already-expanded detail panel without
// rebuilding the table (renderRows() flashes the whole panel for a frame).
function refreshDetailTab(key, tabName) {
    const application = app.applications.find(candidate => candidate.key === key);
    const row = document.querySelector(`tr.app-row[data-key="${CSS.escape(key)}"]`);
    const detail = row && row.nextElementSibling;
    if (!application || !detail || !detail.classList.contains("detail-row")) {
        renderRows();
        return;
    }
    // Reserve the current panel size first: the incoming tab's content
    // (especially the async editor) must fill, never resize, the panel.
    reservePanel(detail);
    detail.querySelectorAll(".detail-tabs .tab").forEach(tab => {
        tab.classList.toggle("active", tab.title === tabName);
    });
    const root = detail.querySelector(".detail");
    loadDetailContent(root, application, tabName, root.querySelector("[data-role=file-content]"));
}

function loadDetailContent(root, application, selectedTab, fileContent) {
    // Drop any previous editor instance (destroyed so its listeners die too).
    for (const host of root.querySelectorAll(".config-editor-host")) {
        if (host._cmView) host._cmView.destroy();
        host.remove();
    }
    const editor = window.ConfigEditor;
    if (!selectedTab) {
        fileContent.style.display = "";
        fileContent.textContent = "No files in this folder yet.";
    } else if (editor) {
        // Config editor owns the Config tab; every other tab renders as
        // plain text below.
        fileContent.textContent = "Loading…";
        editor.takeover(root, application, selectedTab, fileContent).then(handled => {
            if (handled) return;
            fileContent.style.display = "";
            loadFileContent(application.key, selectedTab).then(content => {
                fileContent.textContent = content;
                // Cap only — the expand frame already scrolled; never scroll
                // on load or the panel visibly jumps a frame late.
                fitDetailContent(application.key);
            }).catch(error => {
                fileContent.textContent = `Failed to load: ${error.message}`;
            });
        }).catch(error => {
            fileContent.style.display = "";
            fileContent.textContent = `Failed to load: ${error.message}`;
        });
    } else {
        fileContent.style.display = "";
        fileContent.textContent = "Loading…";
        loadFileContent(application.key, selectedTab).then(content => {
            fileContent.textContent = content;
            fitDetailContent(application.key);
        }).catch(error => {
            fileContent.textContent = `Failed to load: ${error.message}`;
        });
    }
}

function makeLink(label, href) {
    const link = document.createElement("a");
    link.href = href;
    link.target = "_blank";
    link.rel = "noopener";
    link.textContent = label;
    return link;
}

function detailTabsFor(application) {
    const tabs = [];
    if (application.files.jobDescription) {
        tabs.push({ name: application.files.jobDescription, label: "Job description" });
    }
    if (application.files.companyResearch) {
        tabs.push({ name: application.files.companyResearch, label: "Company research" });
    }
    if (application.files.config) {
        tabs.push({ name: application.files.config, label: "Config" });
    }
    return tabs;
}

function fileUrl(key, name) {
    return `/api/applications/file?key=${encodeURIComponent(key)}&name=${encodeURIComponent(name)}`;
}

async function loadFileContent(key, name) {
    const cacheKey = `${key}::${name}`;
    if (app.fileCache[cacheKey]) return app.fileCache[cacheKey];
    const response = await fetch(fileUrl(key, name));
    if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
    const content = await response.text();
    app.fileCache[cacheKey] = content;
    return content;
}

function toggleDetail(key) {
    const expanding = !app.expanded.has(key);
    // Single-open accordion: expanding one row closes the others.
    app.expanded.clear();
    if (expanding) app.expanded.add(key);
    render();
    if (expanding) pinExpandedTop(key);
}

let resizeTimer = 0;
window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
        // Re-reserve only, never scroll: the panel tracks the viewport.
        if (app.expanded.size !== 1) return;
        fitDetailContent([...app.expanded][0]);
    }, 150);
});

// Pins the expanded row to the viewport top and reserves the full panel size
// in the SAME synchronous frame as the expand — before paint and before the
// file content (or editor) arrives. Later loads only fill the reserved space,
// so nothing moves and there is no blink. All geometry is measured live; the
// only fixed numbers are a breathing margin and a usability floor.
function pinExpandedTop(key) {
    const { appRow, detailRow } = findDetailRow(key);
    if (!appRow) return;
    const rowTop = appRow.getBoundingClientRect().top;
    reservePanel(detailRow, rowTop);
    window.scrollBy(0, rowTop);
}

function findDetailRow(key) {
    const appRow = document.querySelector(`tr.app-row[data-key="${CSS.escape(key)}"]`);
    const detailRow = appRow && appRow.nextElementSibling;
    if (!appRow || !detailRow || !detailRow.classList.contains("detail-row")) return {};
    return { appRow, detailRow };
}

// Reserves the panel at its viewport-fitted size: the scrollable content gets
// min+max height and the .detail div (a real block — min-height on the table
// row itself is ignored by browsers) gets the matching floor, so async mounts
// and text fills can't move the panel. Pass the pre-scroll row top when the
// caller is about to scroll: offsets within the block are scroll-invariant.
function reservePanel(detailRow, rowTop) {
    const main = detailRow.querySelector(".detail-main");
    const detail = detailRow.querySelector(".detail");
    const content = detailRow.querySelector(".config-editor-wrap .cm-editor")
        || detailRow.querySelector(".file-content");
    if (!main || !detail || !content) return;
    const contentTop = content.getBoundingClientRect().top;
    const base = rowTop === undefined ? contentTop : contentTop - rowTop;
    const cap = Math.max(Math.floor(window.innerHeight - base - 40), 200);
    content.style.minHeight = `${cap}px`;
    content.style.maxHeight = `${cap}px`;
    const chrome = Math.max(0, Math.floor(main.getBoundingClientRect().height - content.getBoundingClientRect().height));
    detail.style.minHeight = `${cap + chrome}px`;
}

// Re-reserve only, never scroll (loads, editor mounts, tab content swaps,
// resizes): the panel keeps its size while content fills in.
function fitDetailContent(key) {
    const { detailRow } = findDetailRow(typeof key === "string" ? key : key.dataset.key);
    if (!detailRow) return;
    reservePanel(detailRow);
}

/* --- generation --------------------------------------------------------------- */

function findJob(key) {
    return app.jobs[key]
        || app.activeGenerations.find(job => job.applicationKey === key)
        || null;
}

async function startGeneration(key, debug) {
    try {
        const job = await api("/api/generations", {
            method: "POST",
            body: JSON.stringify({ key, debug }),
        });
        app.jobs[key] = job;
        toast(`Generation started${debug ? " (debug markdown)" : ""}.`);
        renderRows();
        pollJobs();
    } catch (error) {
        toast(error.message, "error");
    }
}

let polling = false;

async function pollJobs() {
    if (polling) return;
    polling = true;
    try {
        for (const [key, job] of Object.entries(app.jobs)) {
            if (job.state === "Succeeded" || job.state === "Failed") continue;
            try {
                const snapshot = await api(`/api/generations/${job.id}`);
                app.jobs[key] = snapshot;
                if (snapshot.state === "Succeeded") {
                    toast(`CV generated for ${key}.`, "success");
                    delete app.jobs[key];
                    await loadApplications();
                    await loadStatus();
                } else if (snapshot.state === "Failed") {
                    toast(`Generation failed: ${snapshot.error}`, "error");
                    renderRows();
                } else {
                    updateProgressInPlace(key, snapshot);
                }
            } catch (error) {
                console.error(error);
            }
        }
    } finally {
        polling = false;
    }
}

function updateProgressInPlace(key, job) {
    const appRow = document.querySelector(`tr.app-row[data-key="${CSS.escape(key)}"]`);
    if (!appRow) return;
    const detailRow = appRow.nextElementSibling;
    if (!detailRow || !detailRow.classList.contains("detail-row")) return;
    const fill = detailRow.querySelector("[data-role=progress-fill]");
    const label = detailRow.querySelector("[data-role=progress-label]");
    const wrap = detailRow.querySelector("[data-role=generation-progress]");
    if (!fill || !label) return;
    wrap.classList.remove("hidden");
    fill.style.width = `${Math.round(job.overallPercent)}%`;
    const phase = job.state === "Queued" ? "Queued" : job.moduleDescription || "Working";
    label.textContent = `${phase} — ${Math.round(job.overallPercent)}%`;
}

setInterval(() => {
    if (Object.keys(app.jobs).length || app.activeGenerations.length) pollJobs();
}, 900);

/* --- database ------------------------------------------------------------------ */

el("rebuild-db").addEventListener("click", async event => {
    const button = event.currentTarget;
    button.disabled = true;
    button.textContent = "Rebuilding…";
    try {
        await api("/api/database/rebuild", { method: "POST" });
        toast("Experience database rebuilt.", "success");
        await loadStatus();
    } catch (error) {
        toast(error.message, "error");
    } finally {
        button.disabled = false;
        button.textContent = "Rebuild DB";
    }
});

/* --- toolbar wiring --------------------------------------------------------------- */

el("search").addEventListener("input", event => {
    app.query = event.target.value.trim().toLowerCase();
    render();
});

el("refresh").addEventListener("click", async () => {
    app.fileCache = {};
    try {
        const report = await api("/api/applications/refresh", { method: "POST" });
        await Promise.all([loadStatus(), loadApplications()]);
        toast(`Refreshed: ${report.added} added, ${report.updated} updated, ${report.eventsAppended} events.`, "success");
    } catch (error) {
        toast(error.message, "error");
    }
});

/* --- boot ------------------------------------------------------------------------ */

(async function boot() {
    try {
        await Promise.all([loadStatus(), loadApplications()]);
    } catch (error) {
        toast(`Failed to load applications: ${error.message}`, "error");
    }
})();
