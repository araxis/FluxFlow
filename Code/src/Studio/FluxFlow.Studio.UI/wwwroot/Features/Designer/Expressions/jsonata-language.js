// Presentation only: highlighting never validates, evaluates or rewrites an expression.
require(["vs/editor/editor.main"], function () {
    monaco.languages.register({ id: "jsonata", aliases: ["JSONata"] });
    monaco.languages.setLanguageConfiguration("jsonata", {
        comments: { blockComment: ["/*", "*/"] },
        brackets: [["{", "}"], ["[", "]"], ["(", ")"]],
        autoClosingPairs: [{ open: "{", close: "}" }, { open: "[", close: "]" }, { open: "(", close: ")" }]
    });
    // These are message bindings supplied by FluxFlow, not properties of the input JSON.
    const variables = [
        ["$headers", "Message headers"],
        ["$traceId", "Message trace identifier"],
        ["$messageId", "Message identifier"],
        ["$correlationId", "Message correlation identifier, or null"],
        ["$causationId", "Message causation identifier, or null"],
        ["$timestamp", "Message timestamp"],
        ["$message", "Complete message"],
        ["$input", "Alias for the original message value; root properties can be read directly"],
        ["$payload", "Alias for the original message value"],
        ["$value", "Alias for the original message value"]
    ].flatMap(([name, detail]) => [
        [name, detail],
        ["$" + name[1].toUpperCase() + name.slice(2), detail]
    ]);
    monaco.languages.registerCompletionItemProvider("jsonata", {
        triggerCharacters: ["$"],
        provideCompletionItems: function (model, position) {
            const line = model.getLineContent(position.lineNumber);
            const prefix = line.slice(0, position.column - 1).match(/\$[\w]*$/);
            if (!prefix) return { suggestions: [] };
            const startColumn = position.column - prefix[0].length;
            const suffix = line.slice(position.column - 1).match(/^\w*/)[0];
            const range = {
                startLineNumber: position.lineNumber, endLineNumber: position.lineNumber,
                startColumn: startColumn, endColumn: position.column + suffix.length
            };
            return { suggestions: variables.map(([label, detail]) => ({
                label, detail, insertText: label, range,
                kind: monaco.languages.CompletionItemKind.Variable
            })) };
        }
    });
    monaco.languages.setMonarchTokensProvider("jsonata", {
        tokenizer: {
            root: [
                [/\/\*/, "comment", "@comment"],
                [/"/, "string", "@doubleString"],
                [/'/, "string", "@singleString"],
                [/`[^`]*`/, "identifier"],
                [/\$[\w]*/, "variable"],
                [/\b(?:true|false|null|function|and|or|in)\b/, "keyword"],
                [/\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/, "number"],
                [/[{}\[\]()]/, "@brackets"],
                [/[?:=<>!+*%&|~^;.,/-]+/, "operator"],
                [/[a-zA-Z_][\w]*/, "identifier"]
            ],
            comment: [[/[^*]+/, "comment"], [/\*\//, "comment", "@pop"], [/\*/, "comment"]],
            doubleString: [[/\\./, "string.escape"], [/"/, "string", "@pop"], [/[^"\\]+/, "string"]],
            singleString: [[/\\./, "string.escape"], [/'/, "string", "@pop"], [/[^'\\]+/, "string"]]
        }
    });
});
