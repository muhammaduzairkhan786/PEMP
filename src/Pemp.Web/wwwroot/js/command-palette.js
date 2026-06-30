// ⌘K command palette — global key listener + focus management ONLY (the search/render is in Blazor).
// Loaded as a JS module via import() from CommandPalette.razor (the focus-trap.js pattern), so no
// <script> registration is needed. Focus trapping reuses focus-trap.js so there is one trap impl.

import { activate, release } from './focus-trap.js';

let dotNet = null;
let keyHandler = null;

// Register the global ⌘K / Ctrl+K opener. The handler calls back into the mounted Blazor
// component, which owns all state + rendering. Idempotent: re-registering replaces the handler.
export function register(dotNetRef) {
    unregister();
    dotNet = dotNetRef;
    keyHandler = (e) => {
        // ⌘K (mac) / Ctrl+K (win/linux) toggles the palette open.
        if ((e.metaKey || e.ctrlKey) && (e.key === 'k' || e.key === 'K')) {
            e.preventDefault();
            if (dotNet) dotNet.invokeMethodAsync('OpenFromJs');
        }
    };
    window.addEventListener('keydown', keyHandler);
}

export function unregister() {
    if (keyHandler) window.removeEventListener('keydown', keyHandler);
    keyHandler = null;
    dotNet = null;
}

// Trap focus inside the dialog and move focus to the search box on open.
export function trap(dialog, initialSelector) { activate(dialog, initialSelector); }
// Release the trap; focus returns to whatever was focused when the palette opened.
export function untrap(dialog) { release(dialog); }
