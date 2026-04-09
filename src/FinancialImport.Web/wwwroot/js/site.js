// Loading overlay functions
function showLoading(message = 'Processando...') {
    let overlay = document.getElementById('loadingOverlay');
    if (!overlay) {
        overlay = document.createElement('div');
        overlay.id = 'loadingOverlay';
        overlay.className = 'loading-overlay';
        overlay.innerHTML = `
            <div class="loading-content">
                <div class="loading-spinner"></div>
                <div class="loading-message">${message}</div>
            </div>
        `;
        document.body.appendChild(overlay);
    } else {
        const msgElement = overlay.querySelector('.loading-message');
        if (msgElement) msgElement.textContent = message;
        overlay.classList.remove('hidden');
    }
    overlay.classList.add('active');
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
            showLoading(message);
        });
    });

    // All buttons with data-loading attribute
    const buttons = document.querySelectorAll('button[data-loading], a[data-loading]');
    buttons.forEach(btn => {
        btn.addEventListener('click', function (e) {
            const message = this.getAttribute('data-loading-message') || 'Carregando...';
            showLoading(message);
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