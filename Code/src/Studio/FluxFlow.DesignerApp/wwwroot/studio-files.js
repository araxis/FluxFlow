window.studioFiles = {
    open(extension, image = false, verbatim = false) {
        return new Promise((resolve, reject) => {
            const input = document.createElement("input");
            input.type = "file";
            if (extension) input.accept = image ? extension : extension + ",application/json";
            input.hidden = true;
            input.addEventListener("cancel", () => { input.remove(); resolve(null); }, { once: true });
            input.addEventListener("change", async () => {
                try {
                    const file = input.files[0];
                    if (!file) { resolve(null); return; }
                    if (image) {
                        if (file.size > 2 * 1024 * 1024) throw new Error("Images must be 2 MB or smaller.");
                        const dataUrl = await new Promise((read, fail) => {
                            const reader = new FileReader();
                            reader.onload = () => read(reader.result);
                            reader.onerror = () => fail(reader.error);
                            reader.onabort = () => fail(new Error("Image reading was cancelled."));
                            reader.readAsDataURL(file);
                        });
                        const mediaType = file.type || (/\.png$/i.test(file.name) ? "image/png" : "image/jpeg");
                        resolve({ name: file.name, mediaType, base64: dataUrl.substring(dataUrl.indexOf(",") + 1) });
                    } else {
                        if (file.size > 10 * 1024 * 1024) throw new Error("Files must be 10 MB or smaller.");
                        const content = verbatim
                            ? new TextDecoder("utf-8", { ignoreBOM: true, fatal: true }).decode(await file.arrayBuffer())
                            : await file.text();
                        resolve({ name: file.name, content });
                    }
                } catch (error) { reject(error); }
                finally { input.remove(); }
            }, { once: true });
            document.body.appendChild(input);
            try { input.click(); }
            catch (error) { input.remove(); reject(error); }
        });
    },
    save(name, content) {
        window.studioFiles.download(name, new Blob([content], { type: "application/json;charset=utf-8" }));
    },
    async downloadApplication(name, configuration, endpoint, fields) {
        const headers = { "X-FluxFlow-Run": "1" };
        let body = configuration;
        const inputs = fields?.querySelectorAll("input[type=file]") ?? [];
        if (inputs.length) {
            body = new FormData();
            // A multipart text field would normalize the export's line endings.
            body.append("configuration", new Blob([configuration], { type: "application/json" }), "application.json");
            for (const input of inputs) {
                if (!input.files.length) return `Select the Source file for '${input.name}'.`;
                body.append("source:" + encodeURIComponent(input.name), input.files[0], "source");
            }
        } else {
            headers["Content-Type"] = "application/json";
        }
        const response = await fetch(endpoint, {
            method: "POST",
            headers,
            body
        });
        if (!response.ok) return await response.text() || `Download failed (HTTP ${response.status}).`;
        window.studioFiles.download(name, await response.blob());
        return null;
    },
    download(name, blob) {
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = name;
        document.body.appendChild(link);
        try { link.click(); }
        finally {
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        }
    }
};
