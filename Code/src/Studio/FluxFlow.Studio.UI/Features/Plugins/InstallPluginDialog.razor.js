// Blazor exposes file names but not their paths within a directory selection.
export function paths(input) {
    return Array.from(input.files, file => file.webkitRelativePath.split("/").slice(1).join("/"));
}
