// Type declarations for Monaco Editor ESM deep imports.
// The wildcard export "./*" in monaco-editor/package.json works at runtime
// but TypeScript can't resolve types through it.

declare module 'monaco-editor/esm/vs/editor/editor.all.js' {}
declare module 'monaco-editor/esm/vs/basic-languages/csharp/csharp.contribution.js' {}
declare module 'monaco-editor/esm/vs/basic-languages/xml/xml.contribution.js' {}
declare module 'monaco-editor/esm/vs/basic-languages/markdown/markdown.contribution.js' {}
declare module 'monaco-editor/esm/vs/language/json/monaco.contribution.js' {}

declare module 'monaco-editor/esm/vs/editor/editor.api.js' {
  export * from 'monaco-editor'
  import monaco from 'monaco-editor'
  export default monaco
}
