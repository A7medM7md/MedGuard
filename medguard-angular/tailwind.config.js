/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ["./src/**/*.{html,ts}"],
  darkMode: "class",
  theme: {
    extend: {
      fontFamily: {
        sans: ["Inter", "ui-sans-serif", "system-ui", "sans-serif"],
        mono: ["JetBrains Mono", "ui-monospace", "SFMono-Regular", "monospace"],
      },
      fontSize: {
        "2xs": "12px",
        xs: "13px",
        sm: "14px",
        base: "16px",
        lg: "20px",
        xl: "24px",
        "2xl": "32px",
        "3xl": "40px",
      },
      borderRadius: {
        sm: "4px",
        md: "6px",
        lg: "8px",
        xl: "12px",
        "2xl": "16px",
      },
      colors: {
        background: "var(--background)",
        foreground: "var(--foreground)",
        card: "var(--card)",
        "card-foreground": "var(--card-foreground)",
        popover: "var(--popover)",
        "popover-foreground": "var(--popover-foreground)",
        primary: {
          DEFAULT: "var(--primary)",
          foreground: "var(--primary-foreground)",
          900: "var(--primary-900)",
          700: "var(--primary-700)",
          600: "var(--primary-600)",
          100: "var(--primary-100)",
        },
        secondary: {
          DEFAULT: "var(--secondary)",
          foreground: "var(--secondary-foreground)",
        },
        muted: {
          DEFAULT: "var(--muted)",
          foreground: "var(--muted-foreground)",
        },
        accent: {
          DEFAULT: "var(--accent)",
          foreground: "var(--accent-foreground)",
        },
        destructive: {
          DEFAULT: "var(--critical)",
          foreground: "var(--critical-fg)",
        },
        border: "var(--border)",
        input: "var(--input)",
        ring: "var(--ring)",

        critical: "var(--critical)",
        "critical-bg": "var(--critical-bg)",
        "critical-border": "var(--critical-border)",
        warning: "var(--warning)",
        "warning-bg": "var(--warning-bg)",
        "warning-border": "var(--warning-border)",
        safe: "var(--safe)",
        "safe-bg": "var(--safe-bg)",
        "safe-border": "var(--safe-border)",
        transit: "var(--transit)",
        "transit-bg": "var(--transit-bg)",
        "transit-border": "var(--transit-border)",
        neutralst: "var(--neutralst)",
        "neutralst-bg": "var(--neutralst-bg)",
        "neutralst-border": "var(--neutralst-border)",

        "chart-temp": "var(--chart-temp)",
        "chart-humidity": "var(--chart-humidity)",

        sidebar: "var(--sidebar)",
        "sidebar-foreground": "var(--sidebar-foreground)",
        "sidebar-primary": "var(--sidebar-primary)",
        "sidebar-primary-foreground": "var(--sidebar-primary-foreground)",
        "sidebar-accent": "var(--sidebar-accent)",
        "sidebar-accent-foreground": "var(--sidebar-accent-foreground)",
        "sidebar-border": "var(--sidebar-border)",
        "sidebar-ring": "var(--sidebar-ring)",
      },
      boxShadow: {
        card: "0 1px 2px rgb(0 0 0 / 0.06)",
        overlay: "0 4px 16px rgb(0 0 0 / 0.1)",
      },
      animation: {
        "status-pulse": "status-pulse 1.8s ease-in-out infinite",
      },
      keyframes: {
        "status-pulse": {
          "0%, 100%": { opacity: 1 },
          "50%": { opacity: 0.55 },
        },
      },
    },
  },
  plugins: [],
};
