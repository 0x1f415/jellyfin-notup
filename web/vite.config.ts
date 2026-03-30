import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import { viteSingleFile } from 'vite-plugin-singlefile'
import { resolve } from 'path'

// vite-plugin-singlefile inlines JS but preserves type="module" on the script
// tag. Jellyfin injects plugin pages into the DOM dynamically; inline module
// scripts execute fine when statically parsed, but Jellyfin re-evaluates
// script tags via createElement('script'), which does NOT run type="module"
// scripts. This plugin strips that attribute after inlining is complete.
function stripModuleType(): Plugin {
  return {
    name: 'strip-module-type',
    enforce: 'post',
    transformIndexHtml(html: string) {
      return html.replace(/<script\s+type="module">/g, '<script>')
    },
  }
}

// Outputs a single self-contained HTML file to ../Configuration/configPage.html
// so it can be picked up by the C# embedded resource declaration in the .csproj.
export default defineConfig({
  plugins: [react(), viteSingleFile(), stripModuleType()],
  build: {
    outDir: '../Configuration',
    emptyOutDir: false,
    rollupOptions: {
      input: resolve(__dirname, 'configPage.html'),
      output: {
        format: 'iife',
        name: 'NextUpFilterConfig',
      },
    },
  },
})
