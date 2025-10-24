import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import svgr from 'vite-plugin-svgr' 

// https://vite.dev/config/
export default defineConfig({
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
  plugins: [
    react(),
    tailwindcss(), 
    svgr({
        svgrOptions: {
            // svgr options (e.g., ref: true to forward refs)
            icon: true, // Treat SVGs as icons, potentially applying optimizations
            // You might want to configure SVGO or other options here
        },
    }),
  ],
})
