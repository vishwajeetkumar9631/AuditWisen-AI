import type { Metadata } from "next";
import Link from "next/link";
import { BarChart3, FileUp, ListChecks, ShieldCheck } from "lucide-react";
import "./globals.css";

export const metadata: Metadata = {
  title: "AuditWise",
  description: "Real-time compliance audit dashboard"
};

const navItems = [
  { href: "/", label: "Analytics", icon: BarChart3 },
  { href: "/audits", label: "Audits", icon: ListChecks },
  { href: "/upload", label: "Upload", icon: FileUp }
];

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>
        <div className="min-h-screen">
          <header className="sticky top-0 z-30 border-b border-white/50 bg-white/58 shadow-sm backdrop-blur-2xl">
            <div className="mx-auto flex max-w-7xl items-center justify-between px-4 py-3 sm:px-6 lg:px-8">
              <Link href="/" className="flex items-center gap-3">
                <span className="grid h-9 w-9 place-items-center rounded-md bg-azure text-white shadow-sm">
                  <ShieldCheck className="h-5 w-5" aria-hidden="true" />
                </span>
                <span>
                  <span className="block text-base font-semibold leading-5">AuditWise</span>
                  <span className="block text-xs text-graphite">Compliance command center</span>
                </span>
              </Link>
              <nav className="glass-control flex items-center gap-1 rounded-md p-1 shadow-sm">
                {navItems.map((item) => (
                  <Link
                    key={item.href}
                    href={item.href}
                    className="flex h-9 items-center gap-2 rounded px-3 text-sm font-medium text-graphite transition hover:bg-white/80 hover:text-azure"
                  >
                    <item.icon className="h-4 w-4" aria-hidden="true" />
                    <span className="hidden sm:inline">{item.label}</span>
                  </Link>
                ))}
              </nav>
            </div>
          </header>
          {children}
        </div>
      </body>
    </html>
  );
}
