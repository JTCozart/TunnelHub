// Reformats server-rendered UTC timestamps into the viewer's local timezone, display-only.
// Any element with a `data-utc` attribute (ISO-8601 UTC) has its text replaced with the
// browser-local rendering. A MutationObserver keeps this working across Blazor interactive
// re-renders and enhanced navigation. See the <LocalTime> Razor component.
(function () {
    function format(el) {
        var iso = el.getAttribute('data-utc');
        if (!iso) return;
        var d = new Date(iso);
        if (isNaN(d.getTime())) return;

        var text;
        switch (el.getAttribute('data-utc-mode')) {
            case 'date':
                text = d.toLocaleDateString();
                break;
            case 'time':
                text = d.toLocaleTimeString();
                break;
            case 'datetime-sec':
                text = d.toLocaleString(undefined, {
                    year: 'numeric', month: '2-digit', day: '2-digit',
                    hour: '2-digit', minute: '2-digit', second: '2-digit'
                });
                break;
            default:
                text = d.toLocaleString();
        }
        // Guard against redundant writes (and the childList mutation they'd cause).
        if (el.textContent !== text) el.textContent = text;
    }

    function formatAll(root) {
        if (!root) root = document;
        if (root.nodeType === 1 && root.hasAttribute && root.hasAttribute('data-utc')) format(root);
        if (root.querySelectorAll) root.querySelectorAll('[data-utc]').forEach(format);
    }

    // Exposed so interactive pages can force a pass if ever needed.
    window.ztprFormatLocalTimes = formatAll;

    function init() {
        formatAll(document);
        var observer = new MutationObserver(function (mutations) {
            for (var i = 0; i < mutations.length; i++) {
                var m = mutations[i];
                if (m.type === 'attributes') {
                    if (m.target.hasAttribute && m.target.hasAttribute('data-utc')) format(m.target);
                    continue;
                }
                for (var j = 0; j < m.addedNodes.length; j++) {
                    if (m.addedNodes[j].nodeType === 1) formatAll(m.addedNodes[j]);
                }
            }
        });
        observer.observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['data-utc']
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
