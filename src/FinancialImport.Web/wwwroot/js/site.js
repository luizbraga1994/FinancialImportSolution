// Loading overlay functions
function showLoading(message = 'Processando...', cancelUrl = null) {
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
    overlay.innerHTML = `
        <div class="loading-content">
            <div class="loading-spinner"></div>
            <div class="loading-message">${message}</div>
            ${cancelHtml}
        </div>
    `;
    overlay.classList.remove('hidden');
    overlay.classList.add('active');
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
            showLoading(message, cancelUrl);
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