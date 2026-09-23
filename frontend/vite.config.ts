import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";
import { VitePWA } from "vite-plugin-pwa";

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    // ADR-004: PWA instalable para el despacho. El service worker solo guarda la
    // aplicación (HTML/JS/CSS/WASM). La API es siempre NetworkOnly: sin conexión no se
    // valida ni se registra nada, y ningún dato personal ni token queda en caché (H-07).
    VitePWA({
      registerType: "autoUpdate",
      includeAssets: ["icon.svg", "apple-touch-icon.png"],
      manifest: {
        name: "TanQR · INTEC",
        short_name: "TanQR",
        description:
          "Tickets digitales de combustible de INTEC: despacho con QR firmado.",
        lang: "es-DO",
        start_url: "/",
        scope: "/",
        display: "standalone",
        orientation: "portrait",
        theme_color: "#162d41",
        background_color: "#f5f6f8",
        icons: [
          { src: "icon-192.png", sizes: "192x192", type: "image/png" },
          { src: "icon-512.png", sizes: "512x512", type: "image/png" },
          {
            src: "icon-maskable-512.png",
            sizes: "512x512",
            type: "image/png",
            purpose: "maskable",
          },
        ],
      },
      workbox: {
        globPatterns: ["**/*.{js,css,html,svg,png,wasm}"],
        // El lector de QR (ZXing en WebAssembly) supera el límite por defecto de 2 MB.
        maximumFileSizeToCacheInBytes: 6 * 1024 * 1024,
        navigateFallback: "/index.html",
        navigateFallbackDenylist: [/^\/api\//, /^\/health/, /^\/connect\//],
        runtimeCaching: [
          {
            urlPattern: ({ url }) =>
              url.pathname.startsWith("/api/") ||
              url.pathname.startsWith("/connect/") ||
              url.pathname.startsWith("/health"),
            handler: "NetworkOnly",
          },
        ],
      },
    }),
  ],
  server: {
    host: "127.0.0.1",
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": "http://127.0.0.1:5080",
      "/health": "http://127.0.0.1:5080",
    },
  },
});
