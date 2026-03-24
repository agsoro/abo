using System;
using System.IO;
using System.Text;

var filePath = Path.Combine("..", "Abo.Pm", "wwwroot", "llm-traffic", "index.html");
var content  = File.ReadAllText(filePath, Encoding.UTF8);
var original = content;

int patchCount = 0;

// ── Patch 1: Add data-key attribute to card element ─────────────────────────
{
    var oldText = "                card.className = `entry-card${isExpanded ? ' expanded' : ''}`;";
    var newText = "                card.className = `entry-card${isExpanded ? ' expanded' : ''}`;\n                card.dataset.key = key;";

    if (!content.Contains(oldText))
    {
        Console.Error.WriteLine("PATCH 1 FAILED: marker not found.");
        Console.Error.WriteLine("Looking for: " + oldText);
        Environment.Exit(1);
    }
    content = content.Replace(oldText, newText);
    patchCount++;
    Console.WriteLine("Patch 1 OK: data-key on card element.");
}

// ── Patch 2: Insert captureUiState + restoreUiState before renderEntries, ──────
//            and wire captureUiState call at the start of renderEntries.
{
    var oldText = "        // ── Rendering ─────────────────────────────────────────────────────────\n\n        function renderEntries() {\n            const filtered = applyFilters();";

    var newText = @"        // ── Rendering ─────────────────────────────────────────────────────────

        function captureUiState() {
            const state = {
                scrollTop:  entryListEl.scrollTop,
                focusedKey: null,
                bodies:     {}
            };

            // Identify which card holds focus (if any)
            const active = document.activeElement;
            if (active && active !== document.body) {
                const card = active.closest('[data-key]');
                if (card) state.focusedKey = card.dataset.key;
            }

            // Snapshot internal toggle state of every expanded card body
            entryListEl.querySelectorAll('.entry-card.expanded').forEach(card => {
                const key  = card.dataset.key;
                const body = card.querySelector('.entry-body');
                if (!key || !body) return;

                const bodyState = {
                    systemPromptExpanded: false,
                    rawJsonVisible:       false,
                    collapsedBlocks:      [],   // IDs of collapsed .tool-call-block / .tool-result-block
                    collapsedSections:    []    // IDs of .section-block with hidden .section-content
                };

                // System prompt: preview hidden => full is shown => expanded
                const sysPreview = body.querySelector('.system-prompt-preview');
                if (sysPreview) {
                    bodyState.systemPromptExpanded = (sysPreview.style.display === 'none');
                }

                // Raw JSON container visible?
                const rawContainer = body.querySelector('.raw-json-container');
                if (rawContainer) {
                    bodyState.rawJsonVisible = (rawContainer.style.display === 'block');
                }

                // Collapsed tool-call-block / tool-result-block
                body.querySelectorAll('.tool-call-block.collapsed, .tool-result-block.collapsed').forEach(el => {
                    if (el.id) bodyState.collapsedBlocks.push(el.id);
                });

                // Collapsed section-blocks (Tools Defined section etc.)
                body.querySelectorAll('.section-block').forEach(el => {
                    if (!el.id) return;
                    const sc = el.querySelector('.section-content');
                    if (sc && sc.style.display === 'none') {
                        bodyState.collapsedSections.push(el.id);
                    }
                });

                state.bodies[key] = bodyState;
            });

            return state;
        }

        function restoreUiState(state) {
            // 1. Restore scroll position
            entryListEl.scrollTop = state.scrollTop;

            // 2. Restore internal body toggle states for expanded cards
            entryListEl.querySelectorAll('.entry-card.expanded').forEach(card => {
                const key       = card.dataset.key;
                const bodyState = state.bodies[key];
                if (!bodyState) return;

                const body = card.querySelector('.entry-body');
                if (!body) return;

                // System prompt
                if (bodyState.systemPromptExpanded) {
                    const preview = body.querySelector('.system-prompt-preview');
                    const full    = body.querySelector('.system-prompt-full');
                    const btn     = body.querySelector('.expand-msg-btn');
                    if (preview && full) {
                        preview.style.display = 'none';
                        full.style.display    = 'block';
                        if (btn) btn.textContent = '\u25b2 Hide system prompt';
                    }
                }

                // Raw JSON
                if (bodyState.rawJsonVisible) {
                    const rawContainer = body.querySelector('.raw-json-container');
                    const btn          = body.querySelector('.raw-toggle-btn');
                    if (rawContainer) rawContainer.style.display = 'block';
                    if (btn)          btn.textContent = '\uD83D\uDCCB Hide Raw JSON';
                }

                // Re-collapse tool-call / tool-result blocks
                bodyState.collapsedBlocks.forEach(id => {
                    const block = document.getElementById(id);
                    if (block && !block.classList.contains('collapsed')) {
                        block.classList.add('collapsed');
                    }
                });

                // Re-collapse section-blocks
                bodyState.collapsedSections.forEach(id => {
                    const block = document.getElementById(id);
                    if (!block) return;
                    const sc   = block.querySelector('.section-content');
                    const icon = block.querySelector('.toggle-icon');
                    if (sc)   sc.style.display        = 'none';
                    if (icon) icon.style.transform    = 'rotate(-90deg)';
                });
            });

            // 3. Restore focus (best-effort: re-focus the card header)
            if (state.focusedKey) {
                const escapedKey = CSS.escape(state.focusedKey);
                const card = entryListEl.querySelector('[data-key="' + escapedKey + '"]');
                if (card) {
                    const focusTarget = card.querySelector('.entry-header') || card;
                    focusTarget.setAttribute('tabindex', '-1');
                    focusTarget.focus({ preventScroll: true });
                }
            }
        }

        function renderEntries() {
            const uiState  = captureUiState();
            const filtered = applyFilters();";

    if (!content.Contains(oldText))
    {
        Console.Error.WriteLine("PATCH 2 FAILED: renderEntries() start marker not found.");
        Environment.Exit(1);
    }
    content = content.Replace(oldText, newText);
    patchCount++;
    Console.WriteLine("Patch 2 OK: captureUiState/restoreUiState functions + renderEntries start updated.");
}

// ── Patch 3: Add restoreUiState() call at end of renderEntries ──────────────
{
    // The end of renderEntries has:
    //             entryListEl.innerHTML = '';
    //             entryListEl.appendChild(fragment);
    //         }
    //
    //         // ── Data fetching ──
    var oldText = "            entryListEl.innerHTML = '';\n            entryListEl.appendChild(fragment);\n        }\n\n        // ── Data fetching";
    var newText = "            entryListEl.innerHTML = '';\n            entryListEl.appendChild(fragment);\n            restoreUiState(uiState);\n        }\n\n        // ── Data fetching";

    if (!content.Contains(oldText))
    {
        Console.Error.WriteLine("PATCH 3 FAILED: fragment append marker not found.");
        // Show context around innerHTML = ''
        var idx = content.IndexOf("entryListEl.innerHTML = '';");
        if (idx >= 0)
        {
            Console.Error.WriteLine("Context:");
            Console.Error.WriteLine(content.Substring(Math.Max(0, idx - 20), Math.Min(300, content.Length - idx)));
        }
        Environment.Exit(1);
    }
    content = content.Replace(oldText, newText);
    patchCount++;
    Console.WriteLine("Patch 3 OK: restoreUiState() call appended at end of renderEntries.");
}

File.WriteAllText(filePath, content, Encoding.UTF8);
Console.WriteLine($"\nAll {patchCount} patches applied. File saved.");
Console.WriteLine($"Size: {new FileInfo(filePath).Length} bytes.");
