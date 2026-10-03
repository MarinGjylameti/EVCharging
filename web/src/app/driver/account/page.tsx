import { redirect } from "next/navigation";
import { currentUser } from "@/lib/backend";
import { AccountForm } from "@/features/account/account-form";
import { NavigationSettings } from "@/features/driver/navigation-settings";

export default async function DriverAccountPage() {
  const user = await currentUser();
  if (!user) redirect("/");
  return <div className="driver-content"><span className="driver-eyebrow">{user.language === "sq" ? "LLOGARIA" : "ACCOUNT"}</span>
    <h1>{user.language === "sq" ? "Profili dhe makina" : "Profile and vehicle"}</h1>
    <AccountForm key={user.version} initial={user}>
      <NavigationSettings language={user.language}/>
    </AccountForm>
  </div>;
}
