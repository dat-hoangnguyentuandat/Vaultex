function submitDeleteForm(id) {
    if (!confirm('Bạn chắc muốn xóa?')) return;
    document.getElementById('delete-product-id').value = id;
    document.getElementById('delete-product-form').submit();
}

// Vaultex docs helpers
function vxCopy(btn) {
    var pre = btn.closest('.docs-codeblock').querySelector('pre');
    if (!pre) return;
    var text = pre.innerText;
    if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(function () {
            var orig = btn.innerText;
            btn.innerText = 'Copied';
            setTimeout(function () { btn.innerText = orig; }, 1400);
        });
    }
}

function vxTab(btn, panelId) {
    var tabs = btn.parentElement;
    var wrap = tabs.parentElement;
    Array.prototype.forEach.call(tabs.querySelectorAll('.docs-tab'), function (t) { t.classList.remove('active'); });
    Array.prototype.forEach.call(wrap.querySelectorAll('.docs-tab-panel'), function (p) { p.classList.remove('active'); });
    btn.classList.add('active');
    var panel = wrap.querySelector('#' + panelId);
    if (panel) panel.classList.add('active');
}

// Event delegation — Blazor InteractiveServer intercepts inline onclick,
// so we attach at document level to handle tabs and copy buttons reliably.
document.addEventListener('click', function (e) {
    var tab = e.target.closest && e.target.closest('.docs-tab');
    if (tab) {
        e.preventDefault();
        var panelId = tab.getAttribute('data-panel');
        if (panelId) vxTab(tab, panelId);
        return;
    }
    var copy = e.target.closest && e.target.closest('.docs-codeblock-copy');
    if (copy) {
        e.preventDefault();
        vxCopy(copy);
        return;
    }
}, true);

document.addEventListener('keydown', function (e) {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        var input = document.querySelector('.docs-search input');
        if (input) {
            e.preventDefault();
            input.focus();
            input.select();
        }
    }
});
