import type { Config } from "tailwindcss";

const config: Config = {
  content: [
    "./app/**/*.{js,ts,jsx,tsx,mdx}",
    "./components/**/*.{js,ts,jsx,tsx,mdx}",
    "./hooks/**/*.{js,ts,jsx,tsx,mdx}",
    "./lib/**/*.{js,ts,jsx,tsx,mdx}"
  ],
  theme: {
    extend: {
      colors: {
        ink: "#14213d",
        panel: "#f8fbff",
        line: "rgba(126, 164, 214, 0.34)",
        risk: "#c2413b",
        amber: "#b7791f",
        ok: "#16836a",
        teal: "#2563eb",
        graphite: "#475569",
        azure: "#0078d4",
        skyglass: "rgba(255, 255, 255, 0.66)"
      },
      boxShadow: {
        soft: "0 18px 48px rgba(30, 92, 160, 0.14)",
        glass: "0 22px 60px rgba(26, 79, 142, 0.18)"
      }
    }
  },
  plugins: []
};

export default config;
