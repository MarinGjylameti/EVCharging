import { redirect } from "next/navigation";

export default function DriverSettingsPage() {
  redirect("/driver/account#map-preferences");
}
