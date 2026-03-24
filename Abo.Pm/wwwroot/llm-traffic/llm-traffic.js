(function () {
        'use strict';

        // -- DOM refs --
        const entryListEl   = document.getElementById('entry-list');
        const entryCountEl  = document.getElementById('entry-count');
        const lastUpdatedEl = document.getElementById('last-updated');
        const statusDot     = document.getElementById('status-dot');
        const statusText    = document.getElementById('status-text');
        const filterType    = document.getElementById('filter-type');
        const filterSession = document.getElementById('filter-session');
        const limitInput    = document.getElementById('limit-input');

        let allEntries  = [];
        let expandedIds = new Set();

        // -- Helpers --

        function escapeHtml(str) {
            if (str == null) return '';
            return String(str)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        }

        function getEntryKey(entry) {
            return (entry.Timestamp || '') + '_' + (entry.SessionId || '') + '_' + (entry.Type || '');
        }

        function formatTimestamp(ts) {
            if (!ts) return '-';
            try {
                const d = new Date(ts);
                return d.toLocaleString('en-GB', {
                    year: 'numeric', month: '2-digit', day: '2-digit',
                    hour: '2-digit', minute: '2-digit', second: '2-digit',
                    timeZone: 'UTC', timeZoneName: 'short'
                });
            } catch (_) { return ts; }
        }

        function formatCost(cost) {
            if (cost == null || isNaN(cost)) return null;
            if (cost === 0) return '$0.00';
            if (cost < 0.0001) return '$' + cost.toExponential(2);
            return '$' + cost.toFixed(4);
        }

        function tryParseJson(str) {
            if (!str) return null;
            try { return JSON.parse(str); } catch (_) { return null; }
        }

        function prettyJson(obj) {
            try { return JSON.stringify(obj, null, 2); } catch (_) { return String(obj); }
        }

        function parsePossibleStringJson(val) {
            if (typeof val === 'string') {
                const parsed = tryParseJson(val);
                if (parsed !== null) return parsed;
            }
            return val;
        }

        // -- Entry-level type detection --

        function getDisplayType(entry) {
            const t = (entry.Type || '').toUpperCase();
            if (t === 'SUMMARY_REQUEST')  return 'SUMMARY';
            if (t === 'SUMMARY_RESPONSE') return 'SUMMARY';
            if (t === 'REQUEST')          return 'REQUEST';
            if (t === 'RESPONSE')         return 'RESPONSE';
            if (t === 'ERROR')            return 'ERROR';
            return t;
        }

        // -- Header preview & meta-pills --

        function buildHeaderMeta(entry) {
            const content = tryParseJson(entry.Content);
            const type    = (entry.Type || '').toUpperCase();
            const pills   = [];

            if (!content) return { preview: '', pillsHtml: '' };

            // Model
            const model = content.model
                || content.choices?.[0]?.model
                || null;
            if (model) {
                const short = model.includes('/') ? model.split('/').pop() : model;
                pills.push('<span class="meta-pill model" title="' + escapeHtml(model) + '">' + escapeHtml(short) + '</span>');
            }

            if (type === 'REQUEST' || type === 'SUMMARY_REQUEST') {
                const msgs      = content.messages || [];
                const toolCalls = msgs.filter(m => m.role === 'assistant' && m.tool_calls && m.tool_calls.length > 0)
                                      .reduce((acc, m) => acc + m.tool_calls.length, 0);
                pills.push('<span class="meta-pill msgs">' + msgs.length + ' msgs</span>');
                if (toolCalls > 0) {
                    pills.push('<span class="meta-pill tools">' + toolCalls + ' \uD83D\uDD27</span>');
                }

                // Preview: first user message
                const userMsg  = msgs.find(m => m.role === 'user');
                const preview  = typeof userMsg?.content === 'string'
                    ? userMsg.content
                    : (Array.isArray(userMsg?.content) ? userMsg.content.map(c => c.text || '').join('') : '');
                return { preview: preview.slice(0, 120), pillsHtml: pills.join('') };
            }

            if (type === 'RESPONSE' || type === 'SUMMARY_RESPONSE') {
                const choice      = content.choices?.[0];
                const finishReason = choice?.finish_reason || '';
                const usage       = content.usage;
                const cost        = usage?.cost ?? null;

                if (finishReason) {
                    const cls = finishReason === 'stop' ? 'finish-stop'
                              : finishReason === 'tool_calls' ? 'finish-tool_calls'
                              : 'finish-length';
                    pills.push('<span class="meta-pill ' + cls + '">' + escapeHtml(finishReason) + '</span>');
                }
                if (usage) {
                    pills.push('<span class="meta-pill msgs">\u2191' + (usage.prompt_tokens||0).toLocaleString() + ' \u2193' + (usage.completion_tokens||0).toLocaleString() + '</span>');
                }
                if (cost != null) {
                    pills.push('<span class="meta-pill cost">' + escapeHtml(formatCost(cost)) + '</span>');
                }

                // Preview: assistant content
                const msg     = choice?.message;
                let preview   = '';
                if (msg?.content) {
                    preview = typeof msg.content === 'string' ? msg.content : JSON.stringify(msg.content);
                } else if (msg?.tool_calls?.length) {
                    const names = msg.tool_calls.map(tc => tc.function?.name || '?').join(', ');
                    preview = '\u2699 tool_calls: ' + names;
                }
                return { preview: preview.slice(0, 120), pillsHtml: pills.join('') };
            }

            if (type === 'ERROR') {
                const preview = typeof entry.Content === 'string' ? entry.Content.slice(0, 120) : '';
                return { preview, pillsHtml: '' };
            }

            return { preview: '', pillsHtml: pills.join('') };
        }

        // -- Structured body renderer --

        function renderStructuredBody(entry) {
            const type    = (entry.Type || '').toUpperCase();
            const content = tryParseJson(entry.Content);

            if (!content) {
                return renderFallback(entry.Content);
            }

            if (type === 'ERROR') {
                return renderError(content, entry.Content);
            }

            if (type === 'REQUEST' || type === 'SUMMARY_REQUEST') {
                return renderRequest(content, entry.Content);
            }

            if (type === 'RESPONSE' || type === 'SUMMARY_RESPONSE') {
                return renderResponse(content, entry.Content);
            }

            return renderFallback(entry.Content);
        }

        // -- REQUEST renderer --

        function renderRequest(parsed, rawContent) {
            const parts = [];

            parts.push(renderMetaSection(parsed));

            const messages = parsed.messages || [];
            if (messages.length > 0) {
                parts.push(renderMessagesSection(messages));
            }

            const tools = parsed.tools || [];
            if (tools.length > 0) {
                parts.push(renderToolDefinitionsSection(tools));
            }

            parts.push(renderRawToggle(rawContent));
            return parts.join('');
        }

        // -- RESPONSE renderer --

        function renderResponse(parsed, rawContent) {
            const parts = [];

            parts.push(renderMetaSection(parsed));

            const choice = parsed.choices?.[0];
            if (choice) {
                const finishReason = choice.finish_reason || '';
                const msg = choice.message;

                if (finishReason) {
                    const cls = finishReason === 'stop' ? 'stop'
                              : finishReason === 'tool_calls' ? 'tool_calls'
                              : 'length';
                    const sectionHtml = '<div class="section-block"><div class="section-header">\uD83D\uDCCB Response</div><div class="section-content"><span class="finish-badge ' + cls + '">' + escapeHtml(finishReason) + '</span></div></div>';
                    parts.push(sectionHtml);
                }

                if (msg) {
                    const fakeMessages = [msg];
                    parts.push('<div class="section-block"><div class="section-header">\uD83D\uDCAC Assistant Message</div><div class="section-content">' + renderMessageRows(fakeMessages, {}) + '</div></div>');
                }
            }

            if (parsed.usage) {
                parts.push(renderUsageSection(parsed.usage));
            }

            parts.push(renderRawToggle(rawContent));
            return parts.join('');
        }

        // -- ERROR renderer --

        function renderError(content, rawContent) {
            const parts = [];
            parts.push('<div class="section-block error-body"><div class="section-header" style="color: #f87171;">\u274C Error</div><div class="section-content"><pre>' + escapeHtml(typeof content === 'string' ? content : prettyJson(content)) + '</pre></div></div>');
            parts.push(renderRawToggle(rawContent));
            return parts.join('');
        }

        // -- Fallback renderer --

        function renderFallback(rawContent) {
            const formatted = tryParseJson(rawContent);
            const display   = formatted ? prettyJson(formatted) : (rawContent || '(empty)');
            return '<pre style="margin:0;font-family:\'Consolas\',\'Courier New\',monospace;font-size:0.8rem;color:#e2e8f0;white-space:pre-wrap;word-break:break-word;max-height:600px;overflow-y:auto;background:rgba(0,0,0,0.3);padding:0.75rem;border-radius:0.25rem;border:1px solid var(--border-color);">' + escapeHtml(display) + '</pre>';
        }

        // -- Section renderers --

        function renderMetaSection(parsed) {
            const model    = parsed.model || null;
            const tools    = parsed.tools || [];
            const messages = parsed.messages || [];

            const items = [];
            if (model) {
                items.push('<div class="meta-item"><span class="meta-label">Model</span><span class="meta-value">' + escapeHtml(model) + '</span></div>');
            }
            if (messages.length > 0) {
                items.push('<div class="meta-item"><span class="meta-label">Messages</span><span class="meta-value">' + messages.length + '</span></div>');
            }
            if (tools.length > 0) {
                items.push('<div class="meta-item"><span class="meta-label">Tools available</span><span class="meta-value">' + tools.length + '</span></div>');
            }
            if (parsed.max_tokens != null) {
                items.push('<div class="meta-item"><span class="meta-label">Max tokens</span><span class="meta-value">' + parsed.max_tokens + '</span></div>');
            }
            if (parsed.temperature != null) {
                items.push('<div class="meta-item"><span class="meta-label">Temperature</span><span class="meta-value">' + parsed.temperature + '</span></div>');
            }

            if (items.length === 0) return '';

            return '<div class="section-block"><div class="section-header">\uD83D\uDCCB Metadata</div><div class="section-content"><div class="meta-grid">' + items.join('') + '</div></div></div>';
        }

        function renderMessagesSection(messages) {
            const toolCallMap = {};
            for (const msg of messages) {
                if (msg.role === 'assistant' && msg.tool_calls) {
                    for (const tc of msg.tool_calls) {
                        if (tc.id && tc.function?.name) {
                            toolCallMap[tc.id] = tc.function.name;
                        }
                    }
                }
            }

            const rows = renderMessageRows(messages, toolCallMap);

            return '<div class="section-block"><div class="section-header">\uD83D\uDCAC Messages (' + messages.length + ')</div><div class="section-content" style="padding: 0 0.75rem;">' + rows + '</div></div>';
        }

        function renderMessageRows(messages, toolCallMap) {
            return messages.map(msg => renderSingleMessage(msg, toolCallMap)).join('');
        }

        function renderSingleMessage(msg, toolCallMap) {
            const role   = (msg.role || 'unknown').toLowerCase();
            const badgeClass = ['system', 'user', 'assistant', 'tool'].includes(role) ? role : 'user';

            let contentHtml = '';

            if (role === 'system') {
                contentHtml = renderSystemContent(msg.content);
            } else if (role === 'tool') {
                contentHtml = renderToolResultContent(msg, toolCallMap);
            } else if (role === 'assistant') {
                contentHtml = renderAssistantContent(msg);
            } else {
                contentHtml = renderUserContent(msg.content);
            }

            return '<div class="msg-row"><span class="role-badge ' + badgeClass + '">' + escapeHtml(msg.role || 'unknown') + '</span><div class="msg-content">' + contentHtml + '</div></div>';
        }

        function renderSystemContent(content) {
            const text = contentToString(content);
            const PREVIEW_LEN = 200;
            const id = 'sys_' + Math.random().toString(36).slice(2, 9);

            if (text.length <= PREVIEW_LEN) {
                return '<span class="system-prompt-preview">' + escapeHtml(text) + '</span>';
            }

            return '<span class="system-prompt-preview" id="preview_' + id + '">' + escapeHtml(text.slice(0, PREVIEW_LEN)) + '...</span><span class="system-prompt-full" id="full_' + id + '">' + escapeHtml(text) + '</span><button class="expand-msg-btn" onclick="toggleSystemPrompt(\'' + id + '\', this)">\u25BC Show full system prompt</button>';
        }

        function renderUserContent(content) {
            const text = contentToString(content);
            return '<span style="white-space:pre-wrap;">' + escapeHtml(text) + '</span>';
        }

        function renderAssistantContent(msg) {
            const parts = [];

            if (msg.content) {
                const text = contentToString(msg.content);
                parts.push('<span style="white-space:pre-wrap;">' + escapeHtml(text) + '</span>');
            }

            if (msg.tool_calls && msg.tool_calls.length > 0) {
                const callBlocks = msg.tool_calls.map(tc => renderToolCallBlock(tc)).join('');
                parts.push('<div class="tool-calls-container">' + callBlocks + '</div>');
            }

            if (parts.length === 0) {
                parts.push('<span class="msg-content muted">(no content)</span>');
            }

            return parts.join('');
        }

        function renderToolCallBlock(tc) {
            const name = tc.function?.name || '?';
            const callId = tc.id || '';
            const id = 'tc_' + Math.random().toString(36).slice(2, 9);

            let argsObj = parsePossibleStringJson(tc.function?.arguments || '{}');
            const argsDisplay = prettyJson(argsObj);

            return '<div class="tool-call-block" id="' + id + '"><div class="tool-call-header" onclick="toggleCollapsible(\'' + id + '\', this)"><span>\uD83D\uDD27</span><span class="tool-call-name">' + escapeHtml(name) + '</span><span class="toggle-icon">\u25BC</span><span class="tool-call-id-label">' + escapeHtml(callId) + '</span></div><pre class="tool-call-args">' + escapeHtml(argsDisplay) + '</pre></div>';
        }

        function renderToolResultContent(msg, toolCallMap) {
            const callId   = msg.tool_call_id || '';
            const toolName = toolCallMap[callId] || null;
            const text     = contentToString(msg.content);
            const id       = 'tr_' + Math.random().toString(36).slice(2, 9);

            const headerLabel = toolName
                ? '\uD83D\uDCE4 Result of <span style="font-family:monospace;color:var(--role-tool-fg);">' + escapeHtml(toolName) + '</span>'
                : '\uD83D\uDCE4 Tool Result';

            return '<div class="tool-result-block" id="' + id + '"><div class="tool-result-header" onclick="toggleCollapsible(\'' + id + '\', this)">' + headerLabel + '<span class="toggle-icon">\u25BC</span><span class="tool-result-id">' + escapeHtml(callId) + '</span></div><div class="tool-result-content">' + escapeHtml(text) + '</div></div>';
        }

        function renderToolDefinitionsSection(tools) {
            const id = 'tools_def_' + Math.random().toString(36).slice(2, 9);
            const rows = tools.map(t => {
                const fn   = t.function || t;
                const name = fn.name || '?';
                const desc = fn.description || '';
                return '<div style="padding:0.3rem 0;border-bottom:1px solid rgba(255,255,255,0.05);"><span style="font-family:monospace;color:var(--role-tool-fg);font-weight:600;">' + escapeHtml(name) + '</span>' + (desc ? '<span style="color:var(--text-muted);font-size:0.78rem;margin-left:0.5rem;">' + escapeHtml(desc) + '</span>' : '') + '</div>';
            }).join('');

            return '<div class="section-block" id="' + id + '"><div class="section-header" style="cursor:pointer;" onclick="toggleSectionCollapse(\'' + id + '\', this)">\uD83D\uDEE0 Tools Defined (' + tools.length + ') <span class="toggle-icon" style="margin-left:auto;">\u25BC</span></div><div class="section-content" style="max-height:200px;overflow-y:auto;">' + rows + '</div></div>';
        }

        function renderUsageSection(usage) {
            const promptTokens     = usage.prompt_tokens     != null ? usage.prompt_tokens     : 0;
            const completionTokens = usage.completion_tokens != null ? usage.completion_tokens : 0;
            const totalTokens      = usage.total_tokens      != null ? usage.total_tokens      : (promptTokens + completionTokens);
            const cost             = usage.cost              != null ? usage.cost              : null;

            const costDisplay = cost != null ? formatCost(cost) : null;

            return '<div class="section-block"><div class="section-header">\uD83D\uDCCA Token Usage</div><div class="section-content"><div class="usage-grid"><div class="usage-stat"><span class="usage-label">Input</span><span class="usage-value tokens-in">' + promptTokens.toLocaleString() + '</span></div><div class="usage-stat"><span class="usage-label">Output</span><span class="usage-value tokens-out">' + completionTokens.toLocaleString() + '</span></div><div class="usage-stat"><span class="usage-label">Total</span><span class="usage-value tokens-total">' + totalTokens.toLocaleString() + '</span></div>' + (costDisplay ? '<div class="usage-stat"><span class="usage-label">Cost</span><span class="usage-value cost">' + escapeHtml(costDisplay) + '</span></div>' : '') + '</div></div></div>';
        }

        function renderRawToggle(rawContent) {
            const id = 'raw_' + Math.random().toString(36).slice(2, 9);
            const formatted = tryParseJson(rawContent);
            const display   = formatted ? prettyJson(formatted) : (rawContent || '(empty)');
            return '<div class="raw-toggle-area"><button class="raw-toggle-btn" onclick="toggleRawJson(\'' + id + '\', this)">\uD83D\uDCCB Show Raw JSON</button><div class="raw-json-container" id="' + id + '"><pre>' + escapeHtml(display) + '</pre></div></div>';
        }

        // -- Content helpers --

        function contentToString(content) {
            if (content == null) return '';
            if (typeof content === 'string') return content;
            if (Array.isArray(content)) {
                return content.map(c => {
                    if (typeof c === 'string') return c;
                    if (c.type === 'text') return c.text || '';
                    return JSON.stringify(c);
                }).join('\n');
            }
            return JSON.stringify(content);
        }

        // -- Interactive callbacks (global scope) --

        window.toggleSystemPrompt = function (id, btn) {
            const preview = document.getElementById('preview_' + id);
            const full    = document.getElementById('full_'    + id);
            if (!preview || !full) return;
            const isExpanded = full.style.display === 'block';
            preview.style.display = isExpanded ? 'inline' : 'none';
            full.style.display    = isExpanded ? 'none'   : 'block';
            btn.textContent = isExpanded ? '\u25BC Show full system prompt' : '\u25B2 Hide system prompt';
        };

        window.toggleCollapsible = function (id) {
            const block = document.getElementById(id);
            if (!block) return;
            block.classList.toggle('collapsed');
        };

        window.toggleSectionCollapse = function (id, headerEl) {
            const block = document.getElementById(id);
            if (!block) return;
            const content = block.querySelector('.section-content');
            if (!content) return;
            const hidden = content.style.display === 'none';
            content.style.display = hidden ? '' : 'none';
            const icon = headerEl.querySelector('.toggle-icon');
            if (icon) icon.style.transform = hidden ? '' : 'rotate(-90deg)';
        };

        window.toggleRawJson = function (id, btn) {
            const container = document.getElementById(id);
            if (!container) return;
            const isVisible = container.style.display === 'block';
            container.style.display = isVisible ? 'none' : 'block';
            btn.textContent = isVisible ? '\uD83D\uDCCB Show Raw JSON' : '\uD83D\uDCCB Hide Raw JSON';
        };

        // -----------------------------------------------------------------------
        // Smart incremental DOM update
        //
        // Instead of tearing down and rebuilding the entire list on every poll,
        // we reconcile the existing cards with the new filtered set:
        //   - Cards that are already in the DOM and unchanged are left in place.
        //   - New cards are inserted.
        //   - Cards that have disappeared from the filtered set are removed.
        //
        // This means the user's scroll position, expanded/collapsed state, and
        // any focus inside a card are all preserved automatically across updates.
        // -----------------------------------------------------------------------

        // Map from entry key -> { card (DOM node), entry (data), bodyRendered }
        const cardRegistry = new Map();

        function buildCard(entry) {
            const key         = getEntryKey(entry);
            const isExpanded  = expandedIds.has(key);
            const rawType     = (entry.Type || 'UNKNOWN').toUpperCase();
            const displayType = getDisplayType(entry);
            const isSummary   = rawType.startsWith('SUMMARY');

            const { preview, pillsHtml } = buildHeaderMeta(entry);

            const summaryBadge = isSummary
                ? '<span class="meta-pill" style="color:#fbbf24;border-color:rgba(251,191,36,0.5)">SUMMARY</span>'
                : '';

            const card = document.createElement('div');
            card.className = 'entry-card' + (isExpanded ? ' expanded' : '');
            card.dataset.key = key;

            card.innerHTML =
                '<div class="entry-header">' +
                    '<span class="type-badge ' + displayType + '">' + displayType + '</span>' +
                    '<span class="entry-timestamp">' + formatTimestamp(entry.Timestamp) + '</span>' +
                    '<span class="entry-session" title="' + escapeHtml(entry.SessionId || '') + '">' + escapeHtml(entry.SessionId || '-') + '</span>' +
                    '<div class="header-meta-badges">' + summaryBadge + pillsHtml + '</div>' +
                    '<span class="entry-preview" title="' + escapeHtml(preview) + '">' + escapeHtml(preview) + '</span>' +
                    '<span class="expand-icon">\u25BC</span>' +
                '</div>' +
                '<div class="entry-body"></div>';

            const header      = card.querySelector('.entry-header');
            const bodyEl      = card.querySelector('.entry-body');
            let   bodyRendered = false;

            function ensureBodyRendered() {
                if (!bodyRendered) {
                    bodyEl.innerHTML = '<div class="structured-body">' + renderStructuredBody(entry) + '</div>';
                    bodyRendered = true;
                }
            }

            header.addEventListener('click', () => {
                card.classList.toggle('expanded');
                if (card.classList.contains('expanded')) {
                    expandedIds.add(key);
                    ensureBodyRendered();
                } else {
                    expandedIds.delete(key);
                }
            });

            if (isExpanded) {
                ensureBodyRendered();
            }

            cardRegistry.set(key, { card, entry, bodyRendered: () => bodyRendered });
            return card;
        }

        // -- Filtering --

        function applyFilters() {
            const typeFilter    = filterType.value;
            const sessionFilter = filterSession.value.trim().toLowerCase();

            return allEntries.filter(entry => {
                if (typeFilter && (entry.Type || '') !== typeFilter) return false;
                if (sessionFilter && !(entry.SessionId || '').toLowerCase().includes(sessionFilter)) return false;
                return true;
            });
        }

        // -- Rendering (incremental reconciliation) --

        function renderEntries() {
            const filtered = applyFilters();
            entryCountEl.textContent = filtered.length + ' entr' + (filtered.length !== 1 ? 'ies' : 'y');

            if (filtered.length === 0) {
                entryListEl.innerHTML =
                    '<div class="empty-state"><div class="icon">\uD83D\uDD0D</div><p>No entries found.</p></div>';
                cardRegistry.clear();
                return;
            }

            // Build a set of keys that should currently be visible
            const desiredKeys = new Set(filtered.map(getEntryKey));

            // Remove cards no longer in the filtered set
            for (const [key, rec] of cardRegistry.entries()) {
                if (!desiredKeys.has(key)) {
                    if (rec.card.parentNode === entryListEl) {
                        entryListEl.removeChild(rec.card);
                    }
                    cardRegistry.delete(key);
                }
            }

            // Insert / reorder cards to match the desired order.
            // We walk through filtered entries in order and ensure each card is
            // in the correct position without touching cards that are already there.
            let referenceNode = null; // the node that should come *after* the current card

            for (let i = filtered.length - 1; i >= 0; i--) {
                const entry = filtered[i];
                const key   = getEntryKey(entry);

                let rec = cardRegistry.get(key);
                if (!rec) {
                    // New card - create it
                    const card = buildCard(entry);
                    rec = cardRegistry.get(key); // buildCard sets it
                    entryListEl.insertBefore(card, referenceNode);
                } else {
                    // Card already exists - check if it needs repositioning
                    const nextSibling = rec.card.nextSibling;
                    if (nextSibling !== referenceNode) {
                        entryListEl.insertBefore(rec.card, referenceNode);
                    }
                }

                referenceNode = rec.card;
            }
        }

        // -- Data fetching --

        async function fetchData() {
            const limit = parseInt(limitInput.value, 10) || 100;
            try {
                const res = await fetch('/api/llm-traffic?limit=' + limit);
                if (!res.ok) throw new Error('HTTP ' + res.status);
                allEntries = await res.json();

                statusDot.className = 'live';
                statusText.textContent = 'Live';

                renderEntries();

                lastUpdatedEl.textContent = 'Last updated: ' + new Date().toLocaleTimeString('en-GB');
            } catch (err) {
                statusDot.className = '';
                statusText.textContent = 'Error';
                console.error('Failed to load LLM traffic data:', err);
            }
        }

        filterType.addEventListener('change', renderEntries);
        filterSession.addEventListener('input',  renderEntries);
        limitInput.addEventListener('change', fetchData);

        fetchData();
        setInterval(fetchData, 5000);

    })();
