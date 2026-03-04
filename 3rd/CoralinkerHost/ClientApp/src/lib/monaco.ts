/**
 * Custom Monaco Editor entry.
 * Only includes C#, JSON, XML, Markdown — avoids bundling all 80+ languages
 * and heavy TS/CSS/HTML workers (~8.7 MB savings).
 */

import 'monaco-editor/esm/vs/editor/editor.all.js'
import 'monaco-editor/esm/vs/basic-languages/csharp/csharp.contribution.js'
import 'monaco-editor/esm/vs/basic-languages/xml/xml.contribution.js'
import 'monaco-editor/esm/vs/basic-languages/markdown/markdown.contribution.js'
import 'monaco-editor/esm/vs/language/json/monaco.contribution.js'

export * from 'monaco-editor/esm/vs/editor/editor.api.js'
import * as monaco from 'monaco-editor/esm/vs/editor/editor.api.js'
export { monaco }
export default monaco
