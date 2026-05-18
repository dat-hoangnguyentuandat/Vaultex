(function () {
    var supported = ['en', 'vi', 'zh', 'ja'];

    function initControls() {
        var lang = getLanguage();
        if (!supported.includes(lang)) lang = 'vi';
        var picker = document.querySelector('.auth-lang-picker');
        if (picker) picker.value = lang;
    }

    async function applyI18n() {
        var lang = getLanguage();
        if (!supported.includes(lang)) lang = 'vi';
        if (lang === 'vi') return;

        try {
            var resp = await fetch('/i18n/' + lang + '.json');
            if (!resp.ok) return;
            var strings = await resp.json();

            document.querySelectorAll('[data-i18n]').forEach(function (el) {
                var key = el.getAttribute('data-i18n');
                if (strings[key] !== undefined) el.textContent = strings[key];
            });

            document.querySelectorAll('[data-i18n-placeholder]').forEach(function (el) {
                var key = el.getAttribute('data-i18n-placeholder');
                if (strings[key] !== undefined) el.placeholder = strings[key];
            });

            document.querySelectorAll('[data-i18n-title]').forEach(function (el) {
                var key = el.getAttribute('data-i18n-title');
                if (strings[key] !== undefined) el.title = strings[key];
            });
        } catch (e) { }
    }

    document.addEventListener('DOMContentLoaded', function () {
        initControls();
        applyI18n();
    });
})();
