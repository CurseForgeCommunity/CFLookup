// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.


(function () {
    let fileIdInput = document.querySelector('#fileSearchField');

    if (fileIdInput) {
        fileIdInput.addEventListener('wheel', function (evt) {
            evt.preventDefault();
        });
    }
})();

// Download-trend graph modal (embeds the public Grafana panel). Available on any page
// that renders a `.show-download-graph` button with data-project-id/data-project-name.
(function () {
    const modalEl = document.getElementById('downloadGraphModal');
    if (!modalEl) return;

    const frame = document.getElementById('downloadGraphFrame');
    const openLink = document.getElementById('downloadGraphOpenLink');
    const titleEl = document.getElementById('downloadGraphModalLabel');
    const modal = new bootstrap.Modal(modalEl);

    document.addEventListener('click', function (e) {
        const btn = e.target.closest('.show-download-graph');
        if (!btn) return;

        const projectId = btn.dataset.projectId;
        const projectName = btn.dataset.projectName || `Project ${projectId}`;
        const url = `https://grafana.cflookup.com/d-solo/cflookup-pub-embed/x?orgId=2&panelId=1&var-projectId=${encodeURIComponent(projectId)}&theme=system&from=now-90d&to=now&timezone=browser`;

        titleEl.textContent = `${projectName} — 90-day downloads`;
        frame.src = url;
        openLink.href = url;
        modal.show();
    });

    modalEl.addEventListener('hidden.bs.modal', function () {
        frame.src = '';
    });
})();

// Recent-changes quick-peek modal, fed from /api/ProjectSearch/History/{projectId}.
// Available on any page that renders a `.show-history` button with data-project-id/data-project-name.
(function () {
    const modalEl = document.getElementById('historyModal');
    if (!modalEl) return;

    const bodyEl = document.getElementById('historyModalBody');
    const fullLink = document.getElementById('historyFullLink');
    const titleEl = document.getElementById('historyModalLabel');
    const modal = new bootstrap.Modal(modalEl);

    function formatValue(v) {
        if (v === null || v === undefined) return '(none)';
        if (typeof v === 'object') return JSON.stringify(v);
        return String(v);
    }

    function renderHistory(entries) {
        if (!entries || entries.length === 0) {
            return '<p class="text-muted">No tracked changes for this project yet.</p>';
        }

        return '<ul class="list-unstyled mb-0">' + entries.map(entry => {
            const changed = new Date(entry.changedAt).toLocaleString();
            const fields = Object.entries(entry.changes || {}).map(([field, v]) =>
                `<li><strong>${field}</strong>: <span class="text-danger">${formatValue(v.old)}</span> &rarr; <span class="text-success">${formatValue(v.new)}</span></li>`
            ).join('');
            return `<li class="mb-3"><div class="text-muted small">${changed}</div><ul class="ps-3 mb-0">${fields}</ul></li>`;
        }).join('') + '</ul>';
    }

    document.addEventListener('click', async function (e) {
        const btn = e.target.closest('.show-history');
        if (!btn) return;

        const projectId = btn.dataset.projectId;
        const projectName = btn.dataset.projectName || `Project ${projectId}`;

        titleEl.textContent = `${projectName} — recent changes`;
        fullLink.href = `/project-history/${projectId}`;
        bodyEl.innerHTML = '<p class="text-muted">Loading...</p>';
        modal.show();

        try {
            const res = await fetch(`/api/ProjectSearch/History/${projectId}?limit=10`);
            const data = await res.json();
            bodyEl.innerHTML = renderHistory(data);
        } catch {
            bodyEl.innerHTML = '<p class="text-danger">Could not load history.</p>';
        }
    });
})();