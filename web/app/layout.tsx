import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "PRAXIS — the Mastery Operating System",
  description:
    "PRAXIS will tell you when you are failing. Verified mastery, not completion.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  // The palette is committed to dark (color-scheme: dark in globals) rather than
  // following the OS, because the design *is* dark — the prototype has no light mode.
  return (
    <html lang="en" className="h-full">
      <body className="min-h-full">{children}</body>
    </html>
  );
}
