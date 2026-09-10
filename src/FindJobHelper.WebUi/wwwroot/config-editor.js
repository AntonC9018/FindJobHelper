/* Config editor (fjw-w4u.6): single inline editor on the Config tab.
 *
 * CodeMirror 6 + offline JSON schema (json5Schema) mounted in the application
 * detail panel: Validate / Save / Discard toolbar, server errors below the
 * editor, schema lint in the gutter, Ctrl+Space / typing for schema
 * completion, Tab accepts the selected completion, Ctrl+S saves.
 *
 * Editor bundle: vendored offline build (no CDN, no workers) in
 * codemirror-bundle.js, exposing window.ConfigEditorCM. Rebuild with esbuild:
 *   esbuild entry.js --bundle --minify --format=iife --outfile=codemirror-bundle.js
 * then rename the single window.ConfigEditorCM assignment if the global changes.
 * Backend: GET /api/config-schema (generated in-process from the pinned
 * Configuration.Json model), POST /api/applications/config/validate,
 * PUT /api/applications/config (server re-validates with the real loader,
 * then overwrites directly — no backup, history lives in source control),
 * POST /api/applications/file/open-in-vscode.
 */
(function () {
    "use strict";

    let schemaPromise = null;

    function getSchema() {
        if (!schemaPromise) {
            schemaPromise = fetch("/api/config-schema").then((response) => {
                if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
                return response.json();
            });
        }
        return schemaPromise;
    }

    let tagsPromise = null;

    // Every tag name in the experience database, for completing tag positions
    // (requiredTags names, skills, technologies). Missing DB => no tag items.
    function getTags() {
        if (!tagsPromise) {
            tagsPromise = fetch("/api/tags")
                .then((response) => (response.ok ? response.json() : { tags: [] }))
                .then((body) => body.tags || [])
                .catch(() => []);
        }
        return tagsPromise;
    }

    // Collects every string enum/const reachable at the given pointer path,
    // descending through anyOf/oneOf/allOf branches. Purely schema-driven:
    // new enums in the schema complete with no code changes.
    function collectEnums(node, segments, out) {
        if (!node || typeof node !== "object") return;
        for (const key of ["anyOf", "oneOf", "allOf"]) {
            const branches = node[key];
            if (Array.isArray(branches)) {
                for (const branch of branches) collectEnums(branch, segments, out);
            }
        }
        if (Array.isArray(node.enum)) {
            for (const value of node.enum) {
                if (typeof value === "string") out.push(value);
            }
        }
        if (typeof node.const === "string") out.push(node.const);
        if (!segments.length) return;
        const [head, ...rest] = segments;
        const properties = node.properties;
        if (properties && typeof properties === "object" && head in properties) {
            collectEnums(properties[head], rest, out);
        }
        if (/^\d+$/.test(head) && node.items) {
            if (Array.isArray(node.items)) {
                if (node.items[+head]) collectEnums(node.items[+head], rest, out);
            } else {
                collectEnums(node.items, rest, out);
            }
        }
    }

    const TAG_POINTER = /^\/(requiredTags\/\d+(\/name)?|skills\/\d+|technologies\/\d+)$/;

    // Value completion the bundled schema source misses: enum values hidden
    // behind anyOf (section names) plus tag names from /api/tags. Returns null
    // everywhere else so property completion stays with the schema source.
    function makeValueCompletion(schema, tagNames) {
        const CM = window.ConfigEditorCM;
        return (context) => valueCompletionInner(CM, schema, tagNames, context);
    }

    // Finds the active JSON string token on this line so completion replaces
    // the whole quoted value ("ASP.NET Co") instead of only the whitespace
    // word after the cursor. Returns null outside strings (word fallback).
    function stringTokenRange(lineText, offset) {
        const closed = [];
        for (const pattern of [/"[^"\r\n]*"/g, /'[^'\r\n]*'/g]) {
            let match;
            while ((match = pattern.exec(lineText)) !== null) {
                const start = match.index;
                const end = start + match[0].length;
                if (start + 1 <= offset && offset <= end - 1) {
                    return { from: start + 1, to: end - 1 };
                }
                closed.push({ start, end });
            }
        }
        const lastDouble = lineText.lastIndexOf('"', offset - 1);
        const lastSingle = lineText.lastIndexOf("'", offset - 1);
        const last = Math.max(lastDouble, lastSingle);
        if (last === -1) {
            return null;
        }
        for (const span of closed) {
            if (span.end - 1 === last) {
                return null;
            }
        }
        const quote = lineText[last];
        if (lineText.indexOf(quote, offset) !== -1) {
            return null;
        }
        return { from: last + 1, to: offset };
    }

    function valueCompletionInner(CM, schema, tagNames, context) {
            let pointer;
            try {
                pointer = CM.jsonPointerForPosition(context.state, context.pos, -1, CM.MODES.JSON5);
            } catch {
                return null;
            }
            if (!pointer) return null;
            let values = [];
            if (TAG_POINTER.test(pointer)) {
                values = tagNames;
            } else {
                const found = [];
                collectEnums(schema, pointer.split("/").slice(1).map(decodePointerSegment), found);
                values = [...new Set(found)];
            }
            if (!values.length) return null;
            const line = context.state.doc.lineAt(context.pos);
            const offset = context.pos - line.from;
            const stringRange = stringTokenRange(line.text, offset);
            if (stringRange) {
                return {
                    from: line.from + stringRange.from,
                    to: line.from + stringRange.to,
                    validFor: /^[\w .+#/()&-]*$/,
                    options: values.map((label) => ({
                        label,
                        apply: label,
                        type: "value",
                    })),
                };
            }
            const prefix = line.text.slice(0, offset);
            const word = /[^"'\s:{}\[\],]*$/.exec(prefix)[0];
            const from = context.pos - word.length;
            const quoted = from > line.from && /["']/.test(line.text[from - line.from - 1]);
            return {
                from,
                validFor: /^[\w .+#/()&-]*$/,
                options: values.map((label) => ({
                    label,
                    apply: quoted ? label : `"${label}"`,
                    type: "value",
                })),
            };
    }

    function decodePointerSegment(segment) {
        return segment.replace(/~1/g, "/").replace(/~0/g, "~");
    }

    async function postJson(path, payload) {
        const response = await fetch(path, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
        });
        const body = await response.json().catch(() => ({}));
        return { ok: response.ok, body };
    }

    async function putJson(path, payload) {
        const response = await fetch(path, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
        });
        const body = await response.json().catch(() => ({}));
        return { ok: response.ok, body };
    }

    function cacheKeyOf(applicationKey, fileName) {
        return `${applicationKey}::${fileName}`;
    }

    function renderErrorBox(box, errors) {
        box.innerHTML = "";
        if (!errors) return;
        box.classList.toggle("valid", errors.length === 0);
        const title = document.createElement("div");
        title.className = "err-title";
        title.textContent = errors.length === 0 ? "Valid — the loader accepts this config." : `Invalid (${errors.length}):`;
        box.append(title);
        if (errors.length > 0) {
            const list = document.createElement("ul");
            for (const message of errors) {
                const item = document.createElement("li");
                item.textContent = message;
                list.append(item);
            }
            box.append(list);
        }
    }

    async function mount(host, application, fileName, original, newLine) {
        const toolbar = document.createElement("div");
        toolbar.className = "config-editor-toolbar";

        const validate = document.createElement("button");
        validate.className = "btn subtle";
        validate.textContent = "Validate";
        validate.title = "Check with the real loader (no write)";
        const save = document.createElement("button");
        save.className = "btn primary";
        save.textContent = "Save";
        save.title = "Validate and write config.json (Ctrl+S)";
        const discard = document.createElement("button");
        discard.className = "btn subtle";
        discard.textContent = "Discard";
        discard.title = "Revert to the saved file";
        const vscode = document.createElement("button");
        vscode.className = "btn subtle icon-btn";
        vscode.title = "Open config.json in VS Code";
        vscode.setAttribute("aria-label", "Open config.json in VS Code");
        vscode.innerHTML = '<span class="icon"><svg viewBox="0 0 24 24" fill="#007acc"><path d="M23.15 2.587 18.21.21a1.494 1.494 0 0 0-1.705.29l-9.46 8.63-4.12-3.128a.999.999 0 0 0-1.276.057L.327 7.261A1 1 0 0 0 .326 8.74L3.899 12 .326 15.26a1 1 0 0 0 .001 1.479L1.65 17.94a.999.999 0 0 0 1.276.057l4.12-3.128 9.46 8.63a1.492 1.492 0 0 0 1.704.29l4.942-2.377A1.5 1.5 0 0 0 24 20.06V3.939a1.5 1.5 0 0 0-.85-1.352zm-5.146 14.861L10.826 12l7.178-5.448v10.896z"/></svg></span>';
        const status = document.createElement("span");
        status.className = "config-editor-status";
        status.textContent = "loading…";
        toolbar.append(validate, save, discard, vscode, status);
        host.append(toolbar);

        const wrap = document.createElement("div");
        wrap.className = "config-editor-wrap";
        host.append(wrap);
        const errorBox = document.createElement("div");
        errorBox.className = "config-editor-errors";
        host.append(errorBox);

        let savedContent = original;
        const getDoc = () => view.state.doc.toString();

        const CM = window.ConfigEditorCM;
        let schema = null;
        try {
            schema = await getSchema();
        } catch {
            renderErrorBox(errorBox, ["Schema endpoint unreachable — plain JSON mode."]);
        }
        const tagNames = await getTags();

        const extensions = [CM.basicSetup, CM.oneDark, CM.EditorView.lineWrapping];
        if (schema) {
            // json5Schema() already bundles the json5 language: do NOT add a
            // second json5() — a duplicate language breaks syntaxTree queries
            // and silently kills completion/hover/lint.
            extensions.push(CM.json5Schema(schema));
            extensions.push(CM.json5Language.data.of({
                autocomplete: makeValueCompletion(schema, tagNames),
            }));
        } else {
            extensions.push(CM.json());
        }
        extensions.push(CM.keymap.of([
            {
                key: "Tab",
                run: (target) => CM.acceptCompletion(target),
            },
            {
                key: "Ctrl-s",
                mac: "Cmd-s",
                run: () => {
                    onSave();
                    return true;
                },
            },
        ]));
        const view = new CM.EditorView({ doc: original, extensions, parent: wrap });
        host._cmView = view;

        function setStatus(text, kind) {
            status.textContent = text;
            status.className = `config-editor-status${kind ? ` ${kind}` : ""}`;
        }

        function refreshDirty() {
            const dirty = getDoc() !== savedContent;
            save.disabled = !dirty;
            discard.disabled = !dirty;
            if (dirty && !status.classList.contains("dirty")) {
                setStatus("modified — not saved", "dirty");
            }
            if (!dirty && status.textContent === "loading…") {
                setStatus("no changes", "");
            }
        }

        async function onValidate() {
            validate.disabled = true;
            setStatus("validating…");
            try {
                const { ok, body } = await postJson("/api/applications/config/validate", {
                    key: application.key,
                    content: getDoc(),
                });
                const errors = ok ? body.errors || [] : body.errors || ["Validation request failed."];
                renderErrorBox(errorBox, errors);
                if (!ok || errors.length > 0) setStatus("invalid", "bad");
                else setStatus("valid", "ok");
            } catch (error) {
                renderErrorBox(errorBox, [`Validation request failed: ${error.message}`]);
                setStatus("validation failed", "bad");
            } finally {
                validate.disabled = false;
                const verdict = status.textContent;
                const verdictKind = status.classList.contains("ok") ? "ok"
                    : status.classList.contains("bad") ? "bad" : "";
                refreshDirty();
                // Keep the validation verdict on screen instead of letting the
                // dirty poll replace it with "modified — not saved".
                if (verdictKind) setStatus(verdict, verdictKind);
            }
        }

        async function onSave() {
            validate.disabled = true;
            save.disabled = true;
            setStatus("saving…");
            try {
                // The editor works in LF; restore the file's original line
                // endings on the way out so saves don't reformat whole files.
                const content = newLine === "\r\n"
                    ? getDoc().replace(/\r?\n/g, "\r\n")
                    : getDoc();
                const { ok, body } = await putJson("/api/applications/config", {
                    key: application.key,
                    content,
                });
                if (!ok) {
                    renderErrorBox(errorBox, body.errors || [body.error || "Save rejected."]);
                    setStatus("save rejected — not written", "bad");
                } else {
                    savedContent = getDoc();
                    // Cache what was actually written (original endings
                    // restored), not the LF-normalized editor text, so the
                    // cache agrees with disk; savedContent stays normalized
                    // for the dirty comparison above.
                    app.fileCache[cacheKeyOf(application.key, fileName)] = content;
                    renderErrorBox(errorBox, []);
                    setStatus("saved", "ok");
                    toast("Config saved.", "success");
                }
            } catch (error) {
                renderErrorBox(errorBox, [`Save request failed: ${error.message}`]);
                setStatus("save failed", "bad");
            } finally {
                validate.disabled = false;
                refreshDirty();
            }
        }

        async function onOpenInVscode() {
            setStatus("opening in VS Code…");
            try {
                const { ok, body } = await postJson("/api/applications/file/open-in-vscode", {
                    key: application.key,
                    name: fileName,
                });
                if (!ok) {
                    toast(body.error || "Could not open in VS Code.", "error");
                    refreshDirty();
                } else {
                    setStatus(`opened in VS Code: ${body.opened}`, "ok");
                }
            } catch (error) {
                toast(`Could not open in VS Code: ${error.message}`, "error");
                refreshDirty();
            }
        }

        validate.addEventListener("click", onValidate);
        save.addEventListener("click", onSave);
        discard.addEventListener("click", () => {
            view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: savedContent } });
            renderErrorBox(errorBox, null);
            setStatus("reverted", "");
            refreshDirty();
        });
        vscode.addEventListener("click", onOpenInVscode);

        const poll = setInterval(() => {
            if (!view.dom.isConnected) {
                clearInterval(poll);
                return;
            }
            refreshDirty();
        }, 600);
        refreshDirty();
        // The editor mounts after the expand-time fit pass: cap it to the
        // viewport without re-scrolling (the user may have moved meanwhile).
        if (typeof fitDetailContent === "function") {
            fitDetailContent(application.key);
        }
    }

    async function takeover(root, application, selectedTab, fileContentNode) {
        if (!application.files || selectedTab !== application.files.config) return false;
        if (!window.ConfigEditorCM) {
            fileContentNode.textContent = "Editor bundle failed to load (codemirror-bundle.js).";
            return true;
        }
        const host = document.createElement("div");
        host.className = "config-editor-host";
        fileContentNode.style.display = "none";
        fileContentNode.after(host);
        let original;
        let newLine = "\n";
        try {
            const response = await fetch(fileUrl(application.key, selectedTab), { cache: "no-store" });
            if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
            const raw = await response.text();
            // CodeMirror normalizes CRLF to LF; compare against the normalized
            // form so a fresh load reads "saved", not "modified" — but remember
            // the original endings so saves don't reformat the file.
            newLine = raw.includes("\r\n") ? "\r\n" : "\n";
            original = raw.replace(/\r\n/g, "\n");
        } catch (error) {
            host.textContent = `Failed to load: ${error.message}`;
            return true;
        }
        await mount(host, application, selectedTab, original, newLine);
        return true;
    }

    window.ConfigEditor = { takeover };
})();
