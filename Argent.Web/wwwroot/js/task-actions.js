document.querySelectorAll('[data-submit-once]').forEach(form => {
    form.addEventListener('submit', event => {
        if (form.getAttribute('aria-busy') === 'true') {
            event.preventDefault();
            return;
        }
        const button = event.submitter;
        const confirmation = button?.dataset.confirm;
        if (confirmation && !window.confirm(confirmation)) {
            event.preventDefault();
            return;
        }
        // Disabled submitters are omitted from native form data. Preserve the routing key first.
        if (button?.name) {
            const action = document.createElement('input');
            action.type = 'hidden';
            action.name = button.name;
            action.value = button.value;
            form.append(action);
        }
        form.setAttribute('aria-busy', 'true');
        form.querySelectorAll('button').forEach(button => button.disabled = true);
    });
});
