// Loading overlay functions
var __progressPoller = null;
var __progressStartedAt = null; // timestamp when showLoading was called
var __progressSawProcessing = false; // true once we see status === 'Processing' at least once

function showLoading(message = 'Processando...', cancelUrl = null, progressUrl = null) {
    let overlay = document.getElementById('loadingOverlay');
    if (!overlay) {
        overlay = document.createElement('div');
        overlay.id = 'loadingOverlay';
        overlay.className = 'loading-overlay';
        document.body.appendChild(overlay);
    }

    var cancelHtml = '';
    if (cancelUrl) {
        cancelHtml = `<button class="btn btn-outline-danger btn-sm mt-3" onclick="cancelProcessing('${cancelUrl}')">
            <i class="bi bi-x-circle me-1"></i> Cancelar processamento
        </button>`;
    }

    var progressHtml = '';
    if (progressUrl) {
        progressHtml = `
            <div class="progress mt-3" style="width: 280px; height: 6px;">
                <div id="loadingProgressBar" class="progress-bar bg-primary" role="progressbar" style="width: 0%"></div>
            </div>
            <div id="loadingProgressText" class="loading-submessage mt-2 small text-muted" style="min-height: 40px;">
                Iniciando envio ao SAP...
            </div>`;
    }

    overlay.innerHTML = `
        <div class="loading-content">
            <div class="loading-spinner"></div>
            <div class="loading-message">${message}</div>
            ${progressHtml}
            ${cancelHtml}
        </div>
    `;
    overlay.classList.remove('hidden');
    overlay.classList.add('active');

    // Clear any previous poller to avoid leaks when reusing the overlay
    if (__progressPoller) { clearInterval(__progressPoller); __progressPoller = null; }
    __progressStartedAt = Date.now();
    __progressSawProcessing = false;

    if (progressUrl) {
        __progressPoller = setInterval(function () { pollProgress(progressUrl); }, 1500);
        // Wait 1s before the first poll so the server has time to start
        // processing. Polling too early sees the stale pre-run state and
        // the JS mistakes it for "finished".
        setTimeout(function () { pollProgress(progressUrl); }, 1000);
    }
}

function pollProgress(url) {
    fetch(url, { headers: { 'Accept': 'application/json' } })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (data) {
            if (!data) return;

            var bar = document.getElementById('loadingProgressBar');
            var text = document.getElementById('loadingProgressText');
            if (bar) bar.style.width = (data.percent || 0) + '%';

            if (text) {
                var parts = [];
                parts.push('<strong>' + (data.percent || 0) + '%</strong>');
                if (typeof data.totalGroups === 'number' && data.totalGroups > 0) {
                    parts.push((data.dispatched || 0) + ' de ' + data.totalGroups + ' lancamento(s) enviado(s)');
                }
                if (data.failed > 0) {
                    parts.push('<span class="text-danger">' + data.failed + ' com erro</span>');
                }
                var line1 = parts.join(' &middot; ');

                var line2 = '';
                if (data.currentGroup) {
                    line2 = 'Processando: <code>' + escapeHtml(data.currentGroup) + '</code>';
                }

                var line3 = '';
                if (typeof data.elapsedSeconds === 'number' && data.elapsedSeconds > 0) {
                    line3 = 'Tempo decorrido: ' + formatDuration(data.elapsedSeconds);
                }

                text.innerHTML = [line1, line2, line3].filter(Boolean).join('<br>');
            }

            // Remember if we ever saw the server in Processing state.
            // This prevents a race where the first poll happens before the
            // processor sets status=Processing and the JS would otherwise
            // treat the stale "Failed" state as terminal.
            if (data.isProcessing) {
                __progressSawProcessing = true;
            }

            if (data.isFinished) {
                // Only accept "finished" once we have either seen the server
                // transition into Processing (proof the run started) OR
                // waited long enough that we are sure the POST would have
                // kicked things off by now.
                var elapsedSinceStart = Date.now() - (__progressStartedAt || Date.now());
                var canTrustFinish = __progressSawProcessing || elapsedSinceStart > 15000;

                if (canTrustFinish) {
                    if (__progressPoller) { clearInterval(__progressPoller); __progressPoller = null; }
                    setTimeout(function () { window.location.reload(); }, 600);
                }
            }
        })
        .catch(function () { /* polling errors are non-fatal */ });
}

function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
        return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
}

function formatDuration(sec) {
    if (sec < 60) return sec + 's';
    var m = Math.floor(sec / 60);
    var s = sec % 60;
    return m + 'min ' + s + 's';
}

function cancelProcessing(url) {
    var btn = event.target.closest('button');
    btn.disabled = true;
    btn.innerHTML = '<i class="bi bi-hourglass-split me-1"></i> Cancelando...';
    fetch(url, { method: 'POST' })
        .then(function () {
            var msg = document.querySelector('.loading-message');
            if (msg) msg.textContent = 'Cancelamento solicitado. Aguarde...';
        })
        .catch(function () {
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-x-circle me-1"></i> Cancelar processamento';
        });
}

function hideLoading() {
    const overlay = document.getElementById('loadingOverlay');
    if (overlay) {
        overlay.classList.remove('active');
        overlay.classList.add('hidden');
    }
}

// Show loading on form submit
document.addEventListener('DOMContentLoaded', function () {
    // All forms with data-loading attribute
    const forms = document.querySelectorAll('form[data-loading], form.loading-form');
    forms.forEach(form => {
        form.addEventListener('submit', function () {
            const message = form.getAttribute('data-loading-message') || 'Enviando...';
            const cancelUrl = form.getAttribute('data-cancel-url') || null;
            const progressUrl = form.getAttribute('data-progress-url') || null;
            showLoading(message, cancelUrl, progressUrl);
        });
    });

    // All buttons with data-loading attribute
    const buttons = document.querySelectorAll('button[data-loading], a[data-loading]');
    buttons.forEach(btn => {
        btn.addEventListener('click', function (e) {
            const message = this.getAttribute('data-loading-message') || 'Carregando...';
            showLoading(message);

            // File-download links (data-download) never trigger a navigation
            // event, so the overlay would stay forever. Auto-dismiss after a
            // short delay — the browser handles the download in parallel.
            if (this.hasAttribute('data-download')) {
                setTimeout(hideLoading, 1500);
            }
        });
    });

    // File upload detection
    const fileInputs = document.querySelectorAll('input[type="file"]');
    fileInputs.forEach(input => {
        input.addEventListener('change', function () {
            if (this.files && this.files.length > 0) {
                const form = this.closest('form');
                if (form) {
                    form.setAttribute('data-loading', 'true');
                    form.setAttribute('data-loading-message', 'Enviando arquivo...');
                }
            }
        });
    });
});

// Helper for AJAX calls
function ajaxWithLoading(options) {
    showLoading(options.loadingMessage || 'Carregando...');

    const defaultOptions = {
        success: null,
        error: null,
        complete: function () { hideLoading(); }
    };

    const mergedOptions = { ...defaultOptions, ...options };
    const completeCallback = mergedOptions.complete;
    mergedOptions.complete = function (xhr, status) {
        if (completeCallback) completeCallback(xhr, status);
        hideLoading();
    };

    $.ajax(mergedOptions);
}