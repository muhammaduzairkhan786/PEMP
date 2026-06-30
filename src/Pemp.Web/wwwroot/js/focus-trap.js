// Modal focus management (WCAG 2.4.3 Focus Order, 2.1.2 No Keyboard Trap escape via Esc).
// activate(): remembers the element that had focus, traps Tab/Shift+Tab inside the dialog so
// it cycles first<->last instead of escaping to the page behind the scrim, and moves focus to
// the requested initial element. release(): removes the trap and returns focus to the trigger.
// Loaded as a JS module via import() so no <script> registration is required.

const FOCUSABLE =
    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
    'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

function focusables(dialog) {
    return Array.from(dialog.querySelectorAll(FOCUSABLE))
        // visible only (offsetParent is null for display:none / detached)
        .filter(el => el.offsetParent !== null || el === document.activeElement);
}

export function activate(dialog, initialSelector) {
    if (!dialog) return;

    // Remember what to restore focus to when the dialog closes (the trigger button).
    dialog.__prevFocus = document.activeElement;

    const handler = (e) => {
        if (e.key !== 'Tab') return;
        const els = focusables(dialog);
        if (els.length === 0) { e.preventDefault(); return; }
        const first = els[0];
        const last = els[els.length - 1];
        const active = document.activeElement;
        // Wrap around at both ends so focus never leaves the dialog.
        if (e.shiftKey && (active === first || !dialog.contains(active))) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && (active === last || !dialog.contains(active))) {
            e.preventDefault();
            first.focus();
        }
    };
    dialog.__trapHandler = handler;
    dialog.addEventListener('keydown', handler);

    const initial = initialSelector ? dialog.querySelector(initialSelector) : null;
    const target = initial || focusables(dialog)[0] || dialog;
    if (target && target.focus) target.focus();
}

export function release(dialog) {
    if (!dialog) return;
    if (dialog.__trapHandler) {
        dialog.removeEventListener('keydown', dialog.__trapHandler);
        dialog.__trapHandler = null;
    }
    const prev = dialog.__prevFocus;
    dialog.__prevFocus = null;
    if (prev && prev.focus) {
        try { prev.focus(); } catch { /* trigger gone (e.g. re-render) — ignore */ }
    }
}
