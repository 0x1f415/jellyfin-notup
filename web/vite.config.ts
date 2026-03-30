import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { viteSingleFile } from 'vite-plugin-singlefile'
import { resolve } from 'path'

// Outputs a single self-contained HTML file to ../Configuration/configPage.html
// so it can be picked up by the C# embedded resource declaration in the .csproj.
export default defineConfig({
  plugins: [react(), viteSingleFile()],
  build: {
    outDir: '../Configuration',
    emptyOutDir: false,
    rollupOptions: {
      // Using configPage.html (not index.html) so Vite preserves the filename
      // in the output directory.
      input: resolve(__dirname, 'configPage.html'),
      output: {
        // IIFE format avoids <script type="module"> in the inlined output.
        // Jellyfin loads plugin pages by injecting HTML into the DOM; dynamically
        // inserted module scripts don't execute, but plain IIFE scripts do.
        format: 'iife',
        name: 'NextUpFilterConfig',
      },
    },
  },
})
