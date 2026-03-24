
#!/usr/bin/env dotnet-script
// Patch script: adds UI state capture/restore to llm-traffic/index.html (Issue #2)

using System;
using System.IO;
using System.Text;

var filePath = @"Abo.Pm\wwwroot\llm-traffic\index.html";
var content  = File.ReadAllText(filePath, Encoding.UTF8);

// ── Patch 1: Add data-key attribute to card element ─────────────────────────
// Current:
//   card.className = `entry-card${isExpanded ? ' expanded' : ''}`;
// Target: also set card.dataset.key = key;

var oldCardClass = "                card.className = `entry-card${isExpanded ? ' expanded' : ''}`;";
var newCardClass  = "                card.className = `entry-card${isExpanded ? ' expanded' : ''}`;\n                card.dataset.key = key;";

if (!content.Contains(oldCardClass))
{
    Console.Error.WriteLine("PATCH 1 FAILED: Could not find card.className assignment.");
    Environment.Exit(1);
}
content = content.Replace(oldCardClass, newCardClass);
Console.WriteLine("Patch 1 applied: data-key on card element.");

// ── Patch 2: Replace renderEntries() to capture/restore UI state ──────────────
// Insert captureUiState + restoreUiState functions before renderEntries,
// and wire them into renderEntries.

var oldRenderEntries = @"        // ── Rendering ─────────────────────────────────────────────────────────

        function renderEntries() {
            const filtered = applyFilters();";

var newRenderEntries = @"        // ── Rendering ─────────────────────────────────────────────────────────

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

                // System prompt: preview hidden → full is shown → expanded
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
                    if (sc) sc.style.display = 'none';
                    if (icon) icon.style.transform = 'rotate(-90deg)';
                });
            });

            // 3. Restore focus (best-effort: re-focus the card header)
            if (state.focusedKey) {
                const escapedKey = CSS.escape(state.focusedKey);
                const card = entryListEl.querySelector(`[data-key=""${escapedKey}""]`);
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

if (!content.Contains(oldRenderEntries))
{
    Console.Error.WriteLine("PATCH 2 FAILED: Could not find renderEntries() start marker.");
    Environment.Exit(1);
}
content = content.Replace(oldRenderEntries, newRenderEntries);
Console.WriteLine("Patch 2 applied: captureUiState/restoreUiState + updated renderEntries start.");

// ── Patch 3: Add restoreUiState call at the end of renderEntries ─────────────
// Find the fragment append + closing of renderEntries and insert the call.
//
// Current:
//             entryListEl.innerHTML = '';
//             entryListEl.appendChild(fragment);
//         }
//
//         // ── Data fetching ───
//
// We need to add:
//             restoreUiState(uiState);
// after the appendChild(fragment) line.

var oldFragmentEnd = "            entryListEl.innerHTML = '';\n            entryListEl.appendChild(fragment);\n        }\n\n        // ── Data fetching";
var newFragmentEnd = "            entryListEl.innerHTML = '';\n            entryListEl.appendChild(fragment);\n            restoreUiState(uiState);\n        }\n\n        // ── Data fetching";

if (!content.Contains(oldFragmentEnd))
{
    Console.Error.WriteLine("PATCH 3 FAILED: Could not find fragment append + closing brace marker.");
    // Debug: print surrounding content
    var idx = content.IndexOf("entryListEl.innerHTML = '';");
    Console.Error.WriteLine($"  'entryListEl.innerHTML' found at index {idx}");
    Environment.Exit(1);
}
content = content.Replace(oldFragmentEnd, newFragmentEnd);
Console.WriteLine("Patch 3 applied: restoreUiState call at end of renderEntries.");

File.WriteAllText(filePath, content, Encoding.UTF8);
Console.WriteLine("Done. File written successfully.");
