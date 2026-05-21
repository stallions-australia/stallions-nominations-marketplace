// Focus trap for Modal — called via IJSRuntime when modal opens/closes

window.modalFocus = {
    // Trap focus inside element and return a cleanup handle
    trap: function (element) {
        if (!element) return;
        const focusable = element.querySelectorAll(
            'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
        );
        if (focusable.length === 0) return;
        focusable[0].focus();

        function handler(e) {
            if (e.key !== 'Tab') return;
            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (e.shiftKey) {
                if (document.activeElement === first) { e.preventDefault(); last.focus(); }
            } else {
                if (document.activeElement === last) { e.preventDefault(); first.focus(); }
            }
        }
        element.addEventListener('keydown', handler);
        return handler;  // return so caller can remove it (not used in Blazor pattern)
    },

    // Restore focus to a previously stored element
    restoreFocus: function (element) {
        if (element && element.focus) element.focus();
    }
};
