import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = { title: "EVCharging · Operations", description: "Menaxhimi i rrjetit të karikimit elektrik", robots: { index: false, follow: false } };
export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="sq"><body>{children}</body></html>;
}
