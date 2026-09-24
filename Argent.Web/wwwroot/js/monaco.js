window.ArgentMonaco = {
    editors: {},
    completionProviders: {},
    monacoLoadPromise: null,

    loadMonaco: function () {
        if (window.monaco && window.monaco.editor) {
            return Promise.resolve();
        }

        if (this.monacoLoadPromise) {
            return this.monacoLoadPromise;
        }

        this.monacoLoadPromise = new Promise(function (resolve, reject) {
            var script = document.createElement('script');
            script.src = 'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.0/min/vs/loader.js';
            script.onload = function () {
                require.config({ paths: { vs: 'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.0/min/vs' } });
                require(['vs/editor/editor.main'], function () {
                    resolve();
                });
            };
            script.onerror = reject;
            document.head.appendChild(script);
        });

        return this.monacoLoadPromise;
    },

    init: async function (containerId, value, language, dotNetRef, readOnly, placeholder, enablePlatformApi) {
        await this.loadMonaco();

        if (this.editors[containerId]) {
            this.editors[containerId].dispose();
        }

        var container = document.getElementById(containerId);
        if (!container) return;

        var editor = monaco.editor.create(container, {
            value: value || '',
            language: language,
            minimap: { enabled: false },
            scrollBeyondLastLine: false,
            lineNumbers: 'off',
            fontSize: 13,
            readOnly: readOnly || false,
            automaticLayout: true,
            wordWrap: 'on',
            renderWhitespace: 'selection',
            tabSize: 2,
            suggestOnTriggerCharacters: true,
            quickSuggestions: true,
            folding: false,
            glyphMargin: false,
            lineDecorationsWidth: 0,
            lineNumbersMinChars: 0,
            overviewRulerLanes: 0,
            hideCursorInOverviewRuler: true,
            overviewRulerBorder: false,
            scrollbar: {
                vertical: 'hidden',
                horizontal: 'hidden'
            }
        });

        this.editors[containerId] = editor;

        if (language === 'javascript' || language === 'ncalc') {
            this.registerCompletionProvider(language, enablePlatformApi === true);
        }

        // HTML intellisense
        if (language === 'html') {
            monaco.languages.registerCompletionItemProvider('html', {
                provideCompletionItems: function (model, position) {
                    var word = model.getWordUntilPosition(position);
                    var range = {
                        startLineNumber: position.lineNumber,
                        endLineNumber: position.lineNumber,
                        startColumn: word.startColumn,
                        endColumn: word.endColumn
                    };

                    var suggestions = [
                        { label: 'form-field', kind: monaco.languages.CompletionItemKind.Snippet, insertText: '<span class="form-field">${1:fieldName}</span>', insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet, detail: 'Insert form field reference' },
                        { label: 'user-name', kind: monaco.languages.CompletionItemKind.Snippet, insertText: '<span class="user-name">{{user.name}}</span>', detail: 'Insert user name' },
                        { label: 'user-email', kind: monaco.languages.CompletionItemKind.Snippet, insertText: '<span class="user-email">{{user.email}}</span>', detail: 'Insert user email' },
                        { label: 'task-id', kind: monaco.languages.CompletionItemKind.Snippet, insertText: '<span class="task-id">{{task.id}}</span>', detail: 'Insert task ID' },
                        { label: 'task-name', kind: monaco.languages.CompletionItemKind.Snippet, insertText: '<span class="task-name">{{task.name}}</span>', detail: 'Insert task name' },
                        { label: 'if-block', kind: monaco.languages.CompletionItemKind.Snippet, insertText: '{{#if ${1:condition}}}\n  ${2:content}\n{{/if}}', insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet, detail: 'Insert conditional block' },
                        { label: 'each-block', kind: monaco.languages.CompletionItemKind.Snippet, insertText: '{{#each ${1:items}}}\n  ${2:content}\n{{/each}}', insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet, detail: 'Insert each loop' },
                    ];

                    return { suggestions: suggestions };
                }
            });
        }

        // Listen for changes
        editor.onDidChangeModelContent(function () {
            var value = editor.getValue();
            dotNetRef.invokeMethodAsync('OnValueChanged', value);
        });

        // Set placeholder
        if (placeholder && !value) {
            editor.setValue('// ' + placeholder);
        }
    },

    registerCompletionProvider: function (language, enablePlatformApi) {
        var providerKey = language + (enablePlatformApi ? ':jint' : '');
        if (this.completionProviders[providerKey]) return;

        this.completionProviders[providerKey] = monaco.languages.registerCompletionItemProvider(language, {
            provideCompletionItems: function (model, position) {
                var word = model.getWordUntilPosition(position);
                var range = {
                    startLineNumber: position.lineNumber,
                    endLineNumber: position.lineNumber,
                    startColumn: word.startColumn,
                    endColumn: word.endColumn
                };

                var suggestions = [
                    { label: 'fieldValue', kind: monaco.languages.CompletionItemKind.Variable, insertText: 'fieldValue', detail: 'Current field value' },
                    { label: 'formData', kind: monaco.languages.CompletionItemKind.Variable, insertText: 'formData', detail: 'All form data' },
                    { label: 'user', kind: monaco.languages.CompletionItemKind.Variable, insertText: 'user', detail: 'Current user context' },
                    { label: 'user.roles', kind: monaco.languages.CompletionItemKind.Property, insertText: 'user.roles', detail: 'User roles array' },
                    { label: 'user.name', kind: monaco.languages.CompletionItemKind.Property, insertText: 'user.name', detail: 'User display name' },
                    { label: 'user.email', kind: monaco.languages.CompletionItemKind.Property, insertText: 'user.email', detail: 'User email' },
                    { label: 'task', kind: monaco.languages.CompletionItemKind.Variable, insertText: 'task', detail: 'Current task context' },
                    { label: 'task.id', kind: monaco.languages.CompletionItemKind.Property, insertText: 'task.id', detail: 'Task ID' },
                    { label: 'task.name', kind: monaco.languages.CompletionItemKind.Property, insertText: 'task.name', detail: 'Task name' },
                    { label: 'environment', kind: monaco.languages.CompletionItemKind.Variable, insertText: 'environment', detail: 'Environment variables' },
                    { label: 'true', kind: monaco.languages.CompletionItemKind.Keyword, insertText: 'true', detail: 'Boolean true' },
                    { label: 'false', kind: monaco.languages.CompletionItemKind.Keyword, insertText: 'false', detail: 'Boolean false' },
                    { label: 'null', kind: monaco.languages.CompletionItemKind.Keyword, insertText: 'null', detail: 'Null value' },
                    { label: 'contains', kind: monaco.languages.CompletionItemKind.Function, insertText: 'CONTAINS', detail: 'String contains check' },
                    { label: 'startsWith', kind: monaco.languages.CompletionItemKind.Function, insertText: 'STARTSWITH', detail: 'String starts with' },
                    { label: 'endsWith', kind: monaco.languages.CompletionItemKind.Function, insertText: 'ENDSWITH', detail: 'String ends with' },
                    { label: 'length', kind: monaco.languages.CompletionItemKind.Function, insertText: 'LENGTH(', detail: 'String length' },
                    { label: 'trim', kind: monaco.languages.CompletionItemKind.Function, insertText: 'TRIM(', detail: 'Trim whitespace' },
                    { label: 'upper', kind: monaco.languages.CompletionItemKind.Function, insertText: 'UPPER(', detail: 'To uppercase' },
                    { label: 'lower', kind: monaco.languages.CompletionItemKind.Function, insertText: 'LOWER(', detail: 'To lowercase' },
                    { label: 'if', kind: monaco.languages.CompletionItemKind.Keyword, insertText: 'IF(', detail: 'Conditional expression' },
                    { label: 'and', kind: monaco.languages.CompletionItemKind.Operator, insertText: '&&', detail: 'Logical AND' },
                    { label: 'or', kind: monaco.languages.CompletionItemKind.Operator, insertText: '||', detail: 'Logical OR' },
                    { label: 'not', kind: monaco.languages.CompletionItemKind.Operator, insertText: '!', detail: 'Logical NOT' }
                ];

                if (enablePlatformApi) {
                    suggestions.push(
                        {
                            label: 'argent',
                            kind: monaco.languages.CompletionItemKind.Variable,
                            insertText: 'argent',
                            detail: 'Argent platform API for the current workflow execution',
                            documentation: 'Access the context-bound Argent API from Jint.'
                        },
                        {
                            label: 'GetInstanceValue',
                            kind: monaco.languages.CompletionItemKind.Method,
                            insertText: 'GetInstanceValue(${1:name})',
                            insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                            detail: 'GetInstanceValue(name: string): any',
                            documentation: 'Read a process variable available to the current workflow execution.'
                        },
                        {
                            label: 'SetInstanceValue',
                            kind: monaco.languages.CompletionItemKind.Method,
                            insertText: 'SetInstanceValue(${1:name}, ${2:value})',
                            insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                            detail: 'SetInstanceValue(name: string, value: any): void',
                            documentation: 'Set a process variable. The update is persisted when the Jint activity completes successfully.'
                        },
                        {
                            label: 'GetFormValue',
                            kind: monaco.languages.CompletionItemKind.Method,
                            insertText: 'GetFormValue(${1:name})',
                            insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                            detail: 'GetFormValue(name: string): any',
                            documentation: 'Read a custom or domain field from the current workflow form/record context.'
                        },
                        {
                            label: 'SetFormValue',
                            kind: monaco.languages.CompletionItemKind.Method,
                            insertText: 'SetFormValue(${1:name}, ${2:value})',
                            insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                            detail: 'SetFormValue(name: string, value: any): void',
                            documentation: 'Update an existing current-form or domain-record field. The field must exist in the published schema.'
                        },
                        {
                            label: 'QueryDomainRecords',
                            kind: monaco.languages.CompletionItemKind.Method,
                            insertText: 'QueryDomainRecords(${1:take})',
                            insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                            detail: 'QueryDomainRecords(take: number): Array<Record>',
                            documentation: 'Query the current workflow domain object. Results are capped at 25 records.'
                        }
                    );
                }

                return { suggestions: suggestions };
            }
        });
    },

    setValue: function (containerId, value) {
        var editor = this.editors[containerId];
        if (editor) {
            editor.setValue(value || '');
        }
    },

    dispose: function (containerId) {
        var editor = this.editors[containerId];
        if (editor) {
            editor.dispose();
            delete this.editors[containerId];
        }
    }
};
