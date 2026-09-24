async function startPublicForm(host) {
    const formId = host.dataset.formId;
    const bootstrapUrl = host.dataset.bootstrapUrl ?? `/api/runtime/forms/${encodeURIComponent(formId)}/bootstrap`;
    const submitUrl = host.dataset.submitUrl ?? `/api/runtime/forms/${encodeURIComponent(formId)}/submit`;
    const successRedirect = host.dataset.successRedirect;
    const unavailableMessage = host.dataset.unavailableMessage;
    const accessLostMessage = host.dataset.accessLostMessage;
    const conflictMessage = host.dataset.conflictMessage;
    const form = host.querySelector('argent-form');
    const status = host.querySelector('[data-form-status]');

    const showStatus = (message, kind = 'status') => {
        status.textContent = message;
        status.setAttribute('role', kind === 'error' ? 'alert' : 'status');
        status.className = kind === 'error' ? 'text-red-700 mb-3' : 'text-gray-500 mb-3';
        status.hidden = !message;
    };

    try {
        await customElements.whenDefined('argent-form');
        const response = await fetch(bootstrapUrl, {
            headers: { Accept: 'application/json' },
            credentials: 'same-origin'
        });
        if (!response.ok) throw new Error(response.status === 404 ? 'This form is not published.' : `Unable to load form (${response.status}).`);

        const bootstrap = await response.json();
        const messages = bootstrap.messages ?? {};
        const submissionId = crypto.randomUUID();
        form.definition = bootstrap.definition;
        form.values = bootstrap.initialValues ?? {};
        form.actions = bootstrap.actions ?? [];
        form.messages = messages;
        showStatus('');

        let submitting = false;
        form.addEventListener('argent-submit', async event => {
            if (submitting) return;
            submitting = true;
            const submitButtons = [...form.querySelectorAll('button[type="submit"]')];
            submitButtons.forEach(button => button.disabled = true);
            showStatus(messages.submitting ?? 'Submitting…');
            form.setAttribute('aria-busy', 'true');
            try {
                const tokenResponse = await fetch('/api/antiforgery/token', { credentials: 'same-origin' });
                if (!tokenResponse.ok) throw new Error(messages.securityFailed ?? 'Unable to establish a secure submission session.');
                const { token } = await tokenResponse.json();

                const submitResponse = await fetch(submitUrl, {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: {
                        'Content-Type': 'application/json',
                        'X-CSRF-TOKEN': token
                    },
                    body: JSON.stringify({
                        protocolVersion: '2.0',
                        submissionId,
                        formVersionId: bootstrap.formVersionId,
                        action: event.detail.action,
                        values: event.detail.values
                    })
                });

                const contentType = submitResponse.headers.get('content-type') ?? '';
                const payload = contentType.includes('application/json') ? await submitResponse.json() : null;
                if (submitResponse.status === 422) {
                    form.setServerErrors?.(payload.errors ?? []);
                    showStatus(messages.errorSummary ?? 'Please correct the highlighted fields.', 'error');
                    return;
                }
                if (submitResponse.status === 404 && unavailableMessage) throw new Error(unavailableMessage);
                if (submitResponse.status === 403) throw new Error(accessLostMessage ?? 'You no longer have access to perform this action.');
                if (submitResponse.status === 409) throw new Error(payload?.error ?? conflictMessage ?? 'The task changed while it was open. Refresh and try again.');
                if (!submitResponse.ok) throw new Error(`Submission failed (${submitResponse.status}).`);

                form.hidden = true;
                showStatus(messages.submitted ?? 'Your form has been submitted successfully.');
                if (successRedirect) window.location.assign(successRedirect);
            } catch (error) {
                showStatus(error instanceof Error ? error.message : (messages.submissionFailed ?? 'Submission failed.'), 'error');
            } finally {
                submitting = false;
                submitButtons.forEach(button => button.disabled = false);
                form.removeAttribute('aria-busy');
            }
        });
    } catch (error) {
        form.hidden = true;
        showStatus(error instanceof Error ? error.message : 'Unable to load form.', 'error');
    }
}

document.querySelectorAll('[data-argent-public-form]').forEach(startPublicForm);
