import { redirect } from "next/navigation";
import { currentUser } from "@/lib/backend";
import { NavigationSettings } from "@/features/driver/navigation-settings";

export default async function DriverSettingsPage() {
  const user = await currentUser();
  if (!user) redirect("/");
  return <NavigationSettings language={user.language}/>;
}
